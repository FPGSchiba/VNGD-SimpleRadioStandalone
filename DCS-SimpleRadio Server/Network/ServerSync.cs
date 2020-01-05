using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Settings;
using NetCoreServer;
using Newtonsoft.Json;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    // State object for reading client data asynchronously
    public class StateObject
    {
        // Size of receive buffer.
        public const int BufferSize = 1024;

        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];

        public string guid;

        // Received data string.
        public StringBuilder sb = new StringBuilder();

        // Client  socket.
        public Socket workSocket;
    }

    public class ServerSync : TcpServer, IHandle<ServerSettingsChangedMessage>
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // Thread signal.
        public static ManualResetEvent _allDone = new ManualResetEvent(false);

        private readonly HashSet<IPAddress> _bannedIps;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();
        private readonly IEventAggregator _eventAggregator;

        private readonly ServerSettingsStore _serverSettings;

        private Socket listener;

        public ServerSync(ConcurrentDictionary<string, SRClient> connectedClients, HashSet<IPAddress> _bannedIps,
            IEventAggregator eventAggregator) : base(IPAddress.Any, ServerSettingsStore.Instance.GetServerPort())
        {
            _clients = connectedClients;
            this._bannedIps = _bannedIps;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
            _serverSettings = ServerSettingsStore.Instance;

        }

        public void Handle(ServerSettingsChangedMessage message)
        {
            foreach (var clientToSent in _clients)
            {
                try
                {
                    HandleServerSettingsMessage();
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Exception Sending Server Settings ");
                }
            }
        }

        protected override TcpSession CreateSession() { return new SRSClientSession(this,_clients,_bannedIps); }

        protected override void OnError(SocketError error)
        {
            _logger.Error($"TCP SERVER ERROR: {error} ");
        }

        public void StartListening()
        {
            OptionKeepAlive = true;
            Start();
        }

        public void HandleDisconnect(SRSClientSession state)
        {
            _logger.Info("Disconnecting Client");

            if ((state != null) && (state.SRSGuid != null))
            {
                
                //removed
                SRClient client;
                _clients.TryRemove(state.SRSGuid, out client);

                if (client != null)
                {
                    _logger.Info("Removed Disconnected Client " + state.SRSGuid);

                    //Dont tell others for now as a test
                    HandleClientDisconnect(state,client);
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
            else
            {
                _logger.Info("Removed Disconnected Unknown Client");
            }

        }


      
        public void HandleMessage(SRSClientSession state, NetworkMessage message)
        {
            try
            {
                //  logger.Info("Received From " + clientIp.Address + " " + clientIp.Port);
                // logger.Info("Recevied: " + message.MsgType);

                switch (message.MsgType)
                {
                    case NetworkMessage.MessageType.PING:
                        // Do nothing for now
                        break;
                    case NetworkMessage.MessageType.UPDATE:
                        HandleClientMetaDataUpdate(state,message,true);
                        break;
                    case NetworkMessage.MessageType.RADIO_UPDATE:
                        bool showTuned = _serverSettings.GetGeneralSetting(ServerSettingsKeys.SHOW_TUNED_COUNT)
                            .BoolValue;
                        HandleClientMetaDataUpdate(state,message,!showTuned);
                        HandleClientRadioUpdate(state,message,showTuned);
                        break;
                    case NetworkMessage.MessageType.SYNC:

                        var srClient = message.Client;
                        if (!_clients.ContainsKey(srClient.ClientGuid))
                        {
                            var clientIp = (IPEndPoint) state.Socket.RemoteEndPoint;
                            if (message.Version == null)
                            {
                                _logger.Warn("Disconnecting Unversioned Client -  " + clientIp.Address + " " +
                                             clientIp.Port);
                                state.Disconnect();
                                return;
                            }

                            var clientVersion = Version.Parse(message.Version);
                            var protocolVersion = Version.Parse(UpdaterChecker.MINIMUM_PROTOCOL_VERSION);

                            if (clientVersion < protocolVersion)
                            {
                                _logger.Warn(
                                    $"Disconnecting Unsupported  Client Version - Version {clientVersion} IP {clientIp.Address} Port {clientIp.Port}");
                                HandleVersionMismatch(state);

                                //close socket after
                                state.Disconnect();

                                return;
                            }

                            srClient.ClientSession =state;

                            //add to proper list
                            _clients[srClient.ClientGuid] = srClient;

                            state.SRSGuid = srClient.ClientGuid;

                            _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                                new List<SRClient>(_clients.Values)));
                        }

                        HandleRadioClientsSync(state, message,srClient);

                        break;
                    case NetworkMessage.MessageType.SERVER_SETTINGS:
                        HandleServerSettingsMessage();
                        break;
                    case NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_PASSWORD:
                        HandleExternalAWACSModePassword(state, message.ExternalAWACSModePassword, message.Client);
                        break;
                    case NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_DISCONNECT:
                        HandleExternalAWACSModeDisconnect(state,message.Client);
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

        private void HandleServerSettingsMessage()
        {
            //send server settings
            var replyMessage = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.SERVER_SETTINGS,
                ServerSettings = _serverSettings.ToDictionary()
            };

            Multicast(replyMessage.Encode());
            
        }

        private void HandleVersionMismatch(SRSClientSession session)
        {
            //send server settings
            var replyMessage = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.VERSION_MISMATCH,
            };
            session.Send(replyMessage.Encode());
        }

        private void HandleClientMetaDataUpdate(SRSClientSession session,NetworkMessage message, bool send)
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
                    client.Position = message.Client.Position;
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
                            Position = client.Position,
                            LatLngPosition = client.LatLngPosition
                        }
                    };

                    if(send) 
                        Multicast(replyMessage.Encode());
                    
                    // Only redraw client admin UI of server if really needed
                    if (redrawClientAdminList)
                    {
                        _eventAggregator.PublishOnUIThread(new ServerStateMessage(true,
                            new List<SRClient>(_clients.Values)));
                    }
                }
            }
        }

        private void HandleClientDisconnect(SRSClientSession srsSession,SRClient client)
        {
            var message = new NetworkMessage()
            {
                Client = client,
                MsgType = NetworkMessage.MessageType.CLIENT_DISCONNECT
            };

            MulticastAllExeceptOne(message.Encode(),srsSession.Id);
        }

        private void HandleClientRadioUpdate(SRSClientSession session,NetworkMessage message, bool send)
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
                    client.Position = message.Client.Position;
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
                                    Position = client.Position,
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
                                    Position = client.Position,
                                    LatLngPosition = client.LatLngPosition,
                                    RadioInfo = null //send radio update will null indicating no change
                                }
                            };
                        }

                        MulticastAllExeceptOne(replyMessage.Encode(), session.Id);
                    }
                }
            }
        }

        private void HandleRadioClientsSync(SRSClientSession session,NetworkMessage message, SRClient client)
        {
            //store new client
            var replyMessage = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.SYNC,
                Clients = new List<SRClient>(_clients.Values),
                ServerSettings = _serverSettings.ToDictionary(),
                Version = UpdaterChecker.VERSION
            };

            session.Send(replyMessage.Encode());

            //send update to everyone
            //Remove Client Radio Info
            var update = new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.UPDATE,
                ServerSettings = _serverSettings.ToDictionary(),
                Client = new SRClient
                {
                    ClientGuid = client.ClientGuid,
                }
            };

            Multicast(update.Encode());

        }

        private void HandleExternalAWACSModePassword(SRSClientSession session, string password, SRClient client)
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

            session.Send(replyMessage.Encode());

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
                    Position = client.Position,
                    LatLngPosition = client.LatLngPosition
                }
            };

            Multicast(message.Encode());
        }

        private void HandleExternalAWACSModeDisconnect(SRSClientSession session,SRClient client)
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
                        Position = client.Position,
                        LatLngPosition = client.LatLngPosition
                    }
                };

                Multicast(message.Encode());
            }
        }

        public void RequestStop()
        {
           
            try
            {
                DisconnectAll();
                Stop();
                _clients.Clear();
            }
            catch (Exception ex)
            {
            }
        }
    }
}