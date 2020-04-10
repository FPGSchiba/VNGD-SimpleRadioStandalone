using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Network.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Settings;
using Newtonsoft.Json;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    // State object for reading client data asynchronously

    internal class ServerSync : IHandle<ServerSettingsChangedMessage>
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Thread signal.
        public static ManualResetEvent _allDone = new ManualResetEvent(false);

        private readonly HashSet<IPAddress> _bannedIps;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();
        private readonly IEventAggregator _eventAggregator;

        private readonly ServerSettingsStore _serverSettings;

        private Socket _listener;

        private readonly BlockingCollection<OutgoingTCPMessage> _outGoing = new BlockingCollection<OutgoingTCPMessage>();
        private readonly CancellationTokenSource _outgoingCancellationToken = new CancellationTokenSource();

        private volatile bool _stop = false;

        public ServerSync(ConcurrentDictionary<string, SRClient> connectedClients, HashSet<IPAddress> _bannedIps,
            IEventAggregator eventAggregator)
        {
            _clients = connectedClients;
            this._bannedIps = _bannedIps;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
            _serverSettings = ServerSettingsStore.Instance;
        }

        public void Handle(ServerSettingsChangedMessage message)
        {
                //send server settings
                var replyMessage = new NetworkMessage
                {
                    MsgType = NetworkMessage.MessageType.SERVER_SETTINGS,
                    ServerSettings = _serverSettings.ToDictionary()
                };

                Multicast(replyMessage);
        }

        public void StartListening()
        {
            var ipAddress = new IPAddress(0);
            var port = _serverSettings.GetServerPort();
            var localEndPoint = new IPEndPoint(ipAddress, port);

            // Create a TCP/IP socket.
            _listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            _listener.NoDelay = true;

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                _listener.Bind(localEndPoint);
                _listener.Listen(100);
                _listener.NoDelay = true;


                //outgoing packets
                new Thread(SendPendingPackets).Start();

                while (true)
                {
                    // Set the event to nonsignaled state.
                    _allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    _logger.Info($"Waiting for a connection on {port}...");
                    _listener.BeginAccept(
                        AcceptCallback,
                        _listener);

                    // Wait until a connection is made before continuing.
                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Server Listen error: " + e.Message);
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            _allDone.Set();

            try
            {
                // Get the socket that handles the client request.
                var listener = (Socket) ar.AsyncState;
                var socket = listener.EndAccept(ar);
                socket.NoDelay = true;

                // Create the state object.
                var state = new ConnectionStateObject {workSocket = socket};

                socket.SendTimeout = 2000;
                socket.SendBufferSize = 8192 * 2; //trying large buffer

                socket.BeginReceive(state.buffer, 0, ConnectionStateObject.BufferSize, 0,
                    ReadCallback, state);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error accepting socket");
            }
        }

        public void HandleDisconnect(ConnectionStateObject connectionState)
        {
            _logger.Info("Disconnecting Client");

            if ((connectionState != null) && (connectionState.guid != null))
            {
                connectionState.sb.Clear();
                //removed
                SRClient client;
                _clients.TryRemove(connectionState.guid, out client);

                if (client != null)
                {
                    HandleClientDisconnect(client);

                    _logger.Info("Removed Client " + connectionState.guid);
                }

                try
                {
                    _eventAggregator.PublishOnUIThread(
                        new ServerStateMessage(true, new List<SRClient>(_clients.Values)));
                }
                catch (Exception ex)
                {
                    _logger.Info(ex, "Exception Publishing Client Update After Disconnect");
                }
            }

            try
            {
                connectionState.workSocket.Shutdown(SocketShutdown.Both);
                connectionState.workSocket.Close();
            }
            catch (Exception ex)
            {
                _logger.Info(ex, "Exception closing socket after disconnect");
            }
        }

        public void ReadCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            var state = (ConnectionStateObject) ar.AsyncState;
            var handler = state.workSocket;

            try
            {
                // Read data from the client socket.
                var bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There  might be more data, so store the data received so far.
                    state.sb.Append(Encoding.UTF8.GetString(
                        state.buffer, 0, bytesRead));

                    List<string> messages = GetNetworkMessage(state.sb);

                    if (messages.Count > 0)
                    {
                        foreach (var content in messages)
                        {
                            //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                            //   content.Length, content);

                            try
                            {
                                var message = JsonConvert.DeserializeObject<NetworkMessage>(content);

                                HandleMessage(state, message);
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Server - Client Exception reading");
                            }
                        }
                    }

                    //continue receiving more
                    handler.BeginReceive(state.buffer, 0, ConnectionStateObject.BufferSize, 0,
                        ReadCallback, state);
                }
                else
                {
                    _logger.Error("Received 0 bytes - Disconnecting Client");
                    HandleDisconnect(state);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading from socket. Disconnecting ");

                HandleDisconnect(state);
            }
        }

        private List<string> GetNetworkMessage(StringBuilder buf)
        {
            List<string> messages = new List<string>();
            //search for a \n, extract up to that \n and then remove from buffer
            var content = buf.ToString();
            while (content.Length > 2 && content.Contains("\n"))
            {
                //extract message
                var message = content.Substring(0, content.IndexOf("\n") + 1);

                //now clear from buffer
                buf.Remove(0, message.Length);

                //trim the received part
                messages.Add(message.Trim());
      
                //load in next part
                content = buf.ToString();
            }

            return messages;
        }

        public void HandleMessage(ConnectionStateObject connectionState, NetworkMessage message)
        {
            try
            {
                var clientIp = (IPEndPoint) connectionState.workSocket.RemoteEndPoint;

                if (_bannedIps.Contains(clientIp.Address))
                { 
                    connectionState.workSocket.Shutdown(SocketShutdown.Both);
                    connectionState.workSocket.Disconnect(true);

                    _logger.Warn("Disconnecting Banned Client -  " + clientIp.Address + " " + clientIp.Port);
                    return;
                }

                //  logger.Info("Received From " + clientIp.Address + " " + clientIp.Port);
                // logger.Info("Recevied: " + message.MsgType);

                switch (message.MsgType)
                {
                    case NetworkMessage.MessageType.PING:
                        // Do nothing for now
                        break;
                    case NetworkMessage.MessageType.UPDATE:
                        HandleClientMetaDataUpdate(message,true);
                        break;
                    case NetworkMessage.MessageType.RADIO_UPDATE:
                        bool showTuned = _serverSettings.GetGeneralSetting(ServerSettingsKeys.SHOW_TUNED_COUNT)
                            .BoolValue;
                        HandleClientMetaDataUpdate(message,!showTuned);
                        HandleClientRadioUpdate(message,showTuned);
                        break;
                    case NetworkMessage.MessageType.SYNC:

                        var srClient = message.Client;
                        if (!_clients.ContainsKey(srClient.ClientGuid))
                        {
                            if (message.Version == null)
                            {
                                _logger.Warn("Disconnecting Unversioned Client -  " + clientIp.Address + " " +
                                             clientIp.Port);
                                connectionState.workSocket.Shutdown(SocketShutdown.Both);
                                connectionState.workSocket.Disconnect(true);
                                return;
                            }

                            var clientVersion = Version.Parse(message.Version);
                            var protocolVersion = Version.Parse(UpdaterChecker.MINIMUM_PROTOCOL_VERSION);

                            if (clientVersion < protocolVersion)
                            {
                                _logger.Warn(
                                    $"Disconnecting Unsupported  Client Version - Version {clientVersion} IP {clientIp.Address} Port {clientIp.Port}");
                                HandleVersionMismatch(connectionState.workSocket);

                                connectionState.workSocket.Shutdown(SocketShutdown.Both);
                                //close socket after
                                connectionState.workSocket.Disconnect(true);

                                return;
                            }

                            srClient.ClientSocket = connectionState.workSocket;

                            //add to proper list
                            _clients[srClient.ClientGuid] = srClient;

                            connectionState.guid = srClient.ClientGuid;

                            _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                                new List<SRClient>(_clients.Values)));
                        }

                        HandleRadioClientsSync(clientIp, connectionState.workSocket, message);

                        break;
                    case NetworkMessage.MessageType.SERVER_SETTINGS:
                        //HandleServerSettingsMessage(state.workSocket);
                        break;
                    case NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_PASSWORD:
                        HandleExternalAWACSModePassword(connectionState.workSocket, message.ExternalAWACSModePassword, message.Client);
                        break;
                    case NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_DISCONNECT:
                        HandleExternalAWACSModeDisconnect(message.Client);
                        break;
                    default:
                        _logger.Warn("Recevied unknown message type");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception Handling Message " + ex.Message);
            }
        }

        private void HandleVersionMismatch(Socket socket)
        {
            //send server settings
            var replyMessage = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.VERSION_MISMATCH,
            };
            Send(socket, replyMessage);
        }

        private void HandleClientMetaDataUpdate(NetworkMessage message, bool send)
        {
            if (_clients.ContainsKey(message.Client.ClientGuid))
            {
                var client = _clients[message.Client.ClientGuid];

                if (client != null)
                {
                    bool redrawClientAdminList = client.Name != message.Client.Name || client.Coalition != message.Client.Coalition;

                    //copy the data we need
                    client.LastUpdate = DateTime.Now.Ticks;
                    client.Name = message.Client.Name;
                    client.Coalition = message.Client.Coalition;
                    client.LatLngPosition = message.Client.LatLngPosition;

                    //send update to everyone
                    //Remove Client Radio Info
                    var replyMessage = new NetworkMessage
                    {
                        MsgType = NetworkMessage.MessageType.UPDATE,
                        ServerSettings = _serverSettings.ToDictionary(),
                        Client = new SRClient
                        {
                            ClientGuid = client.ClientGuid,
                            Coalition = client.Coalition,
                            Name = client.Name,
                            LastUpdate = client.LastUpdate,
                            LatLngPosition = client.LatLngPosition
                        }
                    };

                    foreach (var clientToSent in _clients)
                    {
                        Send(clientToSent.Value.ClientSocket, replyMessage);
                    }
                    
                    // Only redraw client admin UI of server if really needed
                    if (redrawClientAdminList)
                    {
                        _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                            new List<SRClient>(_clients.Values)));
                    }
                }
            }
        }

        private void HandleClientDisconnect(SRClient client)
        {
            var message = new NetworkMessage()
            {
                Client = client,
                MsgType = NetworkMessage.MessageType.CLIENT_DISCONNECT
            };

            Multicast(message,client.ClientGuid);
        }

        private void HandleClientRadioUpdate(NetworkMessage message, bool send)
        {
            if (_clients.ContainsKey(message.Client.ClientGuid))
            {
                var client = _clients[message.Client.ClientGuid];

                if (client != null)
                {
                    //shouldnt be the case but just incase...
                    if (message.Client.RadioInfo == null)
                    {
                        message.Client.RadioInfo = new DCSPlayerRadioInfo();
                    }
                    //update to local ticks
                    message.Client.RadioInfo.LastUpdate = DateTime.Now.Ticks;

                    var changed = false;

                    if (client.RadioInfo == null)
                    {
                        client.RadioInfo = message.Client.RadioInfo;
                        changed = true;
                    }
                    else
                    {
                       changed = !client.RadioInfo.Equals(message.Client.RadioInfo);
                    }

                    client.LastUpdate = DateTime.Now.Ticks;
                    client.Name = message.Client.Name;
                    client.Coalition = message.Client.Coalition;
                    client.RadioInfo = message.Client.RadioInfo;
                    client.LatLngPosition = message.Client.LatLngPosition;

                    TimeSpan lastSent = new TimeSpan(DateTime.Now.Ticks - client.LastRadioUpdateSent);

                    //send update to everyone
                    //Remove Client Radio Info
                    if (send)
                    {
                        NetworkMessage replyMessage;
                        if ((changed || lastSent.TotalSeconds > 60))
                        {
                            client.LastRadioUpdateSent = DateTime.Now.Ticks;
                            replyMessage = new NetworkMessage
                            {
                                MsgType = NetworkMessage.MessageType.RADIO_UPDATE,
                                ServerSettings = _serverSettings.ToDictionary(),
                                Client = new SRClient
                                {
                                    ClientGuid = client.ClientGuid,
                                    Coalition = client.Coalition,
                                    Name = client.Name,
                                    LastUpdate = client.LastUpdate,
                                    LatLngPosition = client.LatLngPosition,
                                    RadioInfo = client.RadioInfo //send radio info
                                }
                            };
                           
                        }
                        else
                        {
                            replyMessage = new NetworkMessage
                            {
                                MsgType = NetworkMessage.MessageType.RADIO_UPDATE,
                                ServerSettings = _serverSettings.ToDictionary(),
                                Client = new SRClient
                                {
                                    ClientGuid = client.ClientGuid,
                                    Coalition = client.Coalition,
                                    Name = client.Name,
                                    LastUpdate = client.LastUpdate,
                                    LatLngPosition = client.LatLngPosition,
                                    RadioInfo = null //send radio update will null indicating no change
                                }
                            };
                        }

                        Multicast(replyMessage, message.Client.ClientGuid);
                    }
                }
            }
        }

        private void HandleRadioClientsSync(IPEndPoint clientIp, Socket clientSocket, NetworkMessage message)
        {
            //store new client
            var replyMessage = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.SYNC,
                Clients = new List<SRClient>(_clients.Values),
                ServerSettings = _serverSettings.ToDictionary(),
                Version = UpdaterChecker.VERSION
            };

            Send(clientSocket,replyMessage);
        
            //send update to everyone bar who just synced
            //Remove Client Radio Info
            var update = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.UPDATE,
                ServerSettings = _serverSettings.ToDictionary(),
                Client = new SRClient
                {
                    ClientGuid = message.Client.ClientGuid,
                }
            };

            Multicast(update, message.Client.ClientGuid);

        }

        private void HandleExternalAWACSModePassword(Socket clientSocket, string password, SRClient client)
        {
            // Response of clientCoalition = 0 indicates authentication success (or external AWACS mode disabled)
            int clientCoalition = 0;
            if (_serverSettings.GetGeneralSetting(ServerSettingsKeys.EXTERNAL_AWACS_MODE).BoolValue
                && !string.IsNullOrWhiteSpace(password))
            {
                if (_serverSettings.GetExternalAWACSModeSetting(ServerSettingsKeys.EXTERNAL_AWACS_MODE_BLUE_PASSWORD).StringValue == password)
                {
                    clientCoalition = 2;
                }
                else if (_serverSettings.GetExternalAWACSModeSetting(ServerSettingsKeys.EXTERNAL_AWACS_MODE_RED_PASSWORD).StringValue == password)
                {
                    clientCoalition = 1;
                }
            }

            if (_clients.ContainsKey(client.ClientGuid))
            {
                _clients[client.ClientGuid].Coalition = clientCoalition;
                _clients[client.ClientGuid].Name = client.Name;

                _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                    new List<SRClient>(_clients.Values)));
            }

            var replyMessage = new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = clientCoalition
                },
                MsgType = NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_PASSWORD,
            };
            Send(clientSocket, replyMessage);

            var message = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.UPDATE,
                ServerSettings = _serverSettings.ToDictionary(),
                Client = new SRClient
                {
                    ClientGuid = client.ClientGuid,
                    Coalition = clientCoalition,
                    Name = client.Name,
                    LastUpdate = client.LastUpdate,
                    LatLngPosition = client.LatLngPosition
                }
            };

            Multicast(message);
          
        }

        private void Send(Socket socket, NetworkMessage message)
        {
            _outGoing.Add(new OutgoingTCPMessage() { NetworkMessage = message, SocketList = new List<Socket> {socket} });
        }

        private void HandleExternalAWACSModeDisconnect(SRClient client)
        {
            if (_clients.ContainsKey(client.ClientGuid))
            {
                _clients[client.ClientGuid].Coalition = 0;
                _clients[client.ClientGuid].Name = "";

                _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                    new List<SRClient>(_clients.Values)));

                var message = new NetworkMessage
                {
                    MsgType = NetworkMessage.MessageType.UPDATE,
                    ServerSettings = _serverSettings.ToDictionary(),
                    Client = new SRClient
                    {
                        ClientGuid = client.ClientGuid,
                        Coalition = client.Coalition,
                        Name = client.Name,
                        LastUpdate = client.LastUpdate,
                        LatLngPosition = client.LatLngPosition
                    }
                };

                Multicast(message);
            }
        }


        private void Multicast(NetworkMessage message, string guid = "")
        {
            List<Socket> sockets = new List<Socket>();
            foreach (var clientToSent in _clients)
            {
                if (!guid.Equals(clientToSent.Key))
                {
                    sockets.Add(clientToSent.Value.ClientSocket);
                }
            }

            if (sockets.Count> 0)
            {
                _outGoing.Add(new OutgoingTCPMessage() { NetworkMessage = message, SocketList = sockets });
            }
           
        }


        private
            void SendPendingPackets()
        {
            //_listener.Send(bytes, bytes.Length, ip);
            while (!_stop)
                try
                {
                    OutgoingTCPMessage udpPacket = null;
                    _outGoing.TryTake(out udpPacket, 100000, _outgoingCancellationToken.Token);

                    if (udpPacket != null)
                    {
                        var byteData = Encoding.UTF8.GetBytes(udpPacket.NetworkMessage.Encode());

                        foreach (var outgoingEndPoint in udpPacket.SocketList)
                        {
                            try
                            {
                                if (!outgoingEndPoint.Connected) continue;
                                
                                int sent = outgoingEndPoint.Send(byteData, 0);

                                if (sent != byteData.Length)
                                {
                                    _logger.Error($"Packet not fully sent : Sent {sent} out of {byteData.Length} ");
                                }

                            }
                            catch (Exception ex)
                            {
                                _logger.Error("Error Sending packet - closing : " + ex.Message);
                                outgoingEndPoint.Close();
                                   
                            }
                        }
                        
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("Error processing Sending Queue TCP Packet: " + ex.Message);
                }
        }

        public void RequestStop()
        {
            _stop = true;
            _outgoingCancellationToken.Cancel();
            try
            {
                foreach (var client in _clients)
                {
                    try
                    { 
                        client.Value.ClientSocket.Shutdown(SocketShutdown.Both);
                        client.Value.ClientSocket.Close();
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }
                _listener.Shutdown(SocketShutdown.Both);
                _listener.Close();

                _clients.Clear();
            }
            catch (Exception)
            {
                // ignored
            }
        }
    }
}