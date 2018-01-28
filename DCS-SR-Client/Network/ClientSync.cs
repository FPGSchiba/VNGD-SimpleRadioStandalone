using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using Easy.MessageHub;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class ClientSync
    {
        public delegate void ConnectCallback(bool result);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static bool[] ServerSettings = new bool[Enum.GetValues(typeof(ServerSettingType)).Length];
        public static string ServerVersion = "Unknown";
        private readonly ConcurrentDictionary<string, SRClient> _clients;
        private readonly string _guid;
        private ConnectCallback _callback;
        private IPEndPoint _serverEndpoint;
        private TcpClient _tcpClient;

        private ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        public ClientSync(ConcurrentDictionary<string, SRClient> clients, string guid)
        {
            _clients = clients;
            _guid = guid;
        }


        public void TryConnect(IPEndPoint endpoint, ConnectCallback callback)
        {
            _callback = callback;
            _serverEndpoint = endpoint;

            var tcpThread = new Thread(Connect);
            tcpThread.Start();
        }

        private void Connect()
        {
            var radioSync = new RadioDCSSyncServer(ClientRadioUpdated, ClientCoalitionUpdate, _clients, _guid);
            using (_tcpClient = new TcpClient())
            {
                _tcpClient.SendTimeout = 10;
                try
                {
                    _tcpClient.NoDelay = true;

                    _tcpClient.Connect(_serverEndpoint);

                    if (_tcpClient.Connected)
                    {
                        radioSync.Listen();

                        _tcpClient.NoDelay = true;

                        CallOnMain(true);
                        ClientSyncLoop();
                    }
                }
                catch (SocketException ex)
                {
                    Logger.Error(ex, "error connecting to server");
                }
            }

            radioSync.Stop();

            //disconnect callback
            CallOnMain(false);
        }

        private void ClientRadioUpdated()
        {
            var sideInfo = _clientStateSingleton.DcsPlayerSideInfo;
            SendToServer(new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name,
                    ClientGuid = _guid,
                    RadioInfo = _clientStateSingleton.DcsPlayerRadioInfo,
                    Position = sideInfo.Position
                },
                MsgType = NetworkMessage.MessageType.RADIO_UPDATE
            });
        }

        private void ClientCoalitionUpdate()
        {
            var sideInfo = _clientStateSingleton.DcsPlayerSideInfo;
            SendToServer(new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name,
                    Position = sideInfo.Position,
                    ClientGuid = _guid
                },
                MsgType = NetworkMessage.MessageType.UPDATE
            });
        }

        private void CallOnMain(bool result)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                    new ThreadStart(delegate
                    {
                        
                        _callback(result); 
                        
                    }));
            }
            catch (Exception ex)
            {
            }
        }

        private void ClientSyncLoop()
        {
            //clear the clietns list
            _clients.Clear();

            using (var reader = new StreamReader(_tcpClient.GetStream(), Encoding.UTF8))
            {
                try
                {
                    var sideInfo = _clientStateSingleton.DcsPlayerSideInfo;
                    //start the loop off by sending a SYNC Request
                    SendToServer(new NetworkMessage
                    {
                        Client = new SRClient
                        {
                            Coalition = sideInfo.side,
                            Name = sideInfo.name,
                            Position = sideInfo.Position,
                            ClientGuid = _guid
                        },
                        MsgType = NetworkMessage.MessageType.SYNC
                    });

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            var serverMessage = JsonConvert.DeserializeObject<NetworkMessage>(line);
                            if (serverMessage != null)
                            {
                                switch (serverMessage.MsgType)
                                {
                                    case NetworkMessage.MessageType.PING:
                                        // Do nothing for now
                                        break;
                                    case NetworkMessage.MessageType.UPDATE:

                                        ServerSettings = serverMessage.ServerSettings;

                                        if (_clients.ContainsKey(serverMessage.Client.ClientGuid))
                                        {
                                            var srClient = _clients[serverMessage.Client.ClientGuid];
                                            var updatedSrClient = serverMessage.Client;
                                            if (srClient != null)
                                            {
                                                srClient.LastUpdate = Environment.TickCount;
                                                srClient.Name = updatedSrClient.Name;
                                                srClient.Coalition = updatedSrClient.Coalition;
                                                srClient.Position = updatedSrClient.Position;

//                                                Logger.Info("Recevied Update Client: " + NetworkMessage.MessageType.UPDATE + " From: " +
//                                                            srClient.Name + " Coalition: " +
//                                                            srClient.Coalition + " Pos: " + srClient.Position);
                                            }
                                        }
                                        else
                                        {
                                            var connectedClient = serverMessage.Client;
                                            connectedClient.LastUpdate = Environment.TickCount;

                                            //init with LOS true so you can hear them incase of bad DCS install where
                                            //LOS isnt working
                                            connectedClient.LineOfSightLoss = 0.0f;
                                            //0.0 is NO LOSS therefore full Line of sight

                                            _clients[serverMessage.Client.ClientGuid] = connectedClient;

//                                            Logger.Info("Recevied New Client: " + NetworkMessage.MessageType.UPDATE +
//                                                        " From: " +
//                                                        serverMessage.Client.Name + " Coalition: " +
//                                                        serverMessage.Client.Coalition);
                                        }
                                        break;
                                    case NetworkMessage.MessageType.SYNC:
                                        // Logger.Info("Recevied: " + NetworkMessage.MessageType.SYNC);

                                        //check server version
                                        if (serverMessage.Version == null)
                                        {
                                          
                                            Logger.Error("Disconnecting Unversioned Server");
                                            Disconnect();
                                            break;
                                        }

                                        var serverVersion = Version.Parse(serverMessage.Version);
                                        var protocolVersion = Version.Parse(UpdaterChecker.MINIMUM_PROTOCOL_VERSION);

                                        ServerVersion = serverMessage.Version;

                                        if (serverVersion < protocolVersion)
                                        {
                                            Logger.Warn($"Disconnecting From Unsupported Server Version - Version {serverMessage.Version}");
                                            Disconnect();
                                            break;
                                        }

                                        if (serverMessage.Clients != null)
                                        {
                                            foreach (var client in serverMessage.Clients)
                                            {
                                                client.LastUpdate = Environment.TickCount;
                                                //init with LOS true so you can hear them incase of bad DCS install where
                                                //LOS isnt working
                                                client.LineOfSightLoss = 0.0f;
                                                //0.0 is NO LOSS therefore full Line of sight
                                                _clients[client.ClientGuid] = client;
                                            }
                                        }
                                        //add server settings
                                        ServerSettings = serverMessage.ServerSettings;

                                        break;

                                    case NetworkMessage.MessageType.SERVER_SETTINGS:

                                        //  Logger.Info("Recevied: " + NetworkMessage.MessageType.SERVER_SETTINGS);
                                        ServerSettings = serverMessage.ServerSettings;
                                        ServerVersion = serverMessage.Version;

                                        break;
                                    case NetworkMessage.MessageType.CLIENT_DISCONNECT:
                                        //   Logger.Info("Recevied: " + NetworkMessage.MessageType.CLIENT_DISCONNECT);

                                        SRClient outClient;
                                        _clients.TryRemove(serverMessage.Client.ClientGuid, out outClient);

                                        if (outClient != null)
                                        {
                                            MessageHub.Instance.Publish(outClient);
                                        }

                                        break;
                                    case NetworkMessage.MessageType.VERSION_MISMATCH:
                                        Logger.Error("Version Mismatch Between Client & Server - Disconnecting");
                                        Disconnect();
                                        break;

                                    default:
                                        Logger.Error("Recevied unknown " + line);
                                        break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Client exception reading from socket ");
                        }

                        // do something with line
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Client exception reading - Disconnecting ");
                }
            }

            //disconnected - reset DCS Info
            ClientStateSingleton.Instance.DcsPlayerRadioInfo.LastUpdate = 0;

            //clear the clietns list
            _clients.Clear();

            //disconnect callback
            CallOnMain(false);
        }

        private void SendToServer(NetworkMessage message)
        {
            try
            {
                message.Version = UpdaterChecker.VERSION;

                var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message) + "\n" );

                _tcpClient.GetStream().Write(json, 0, json.Length);
                //Need to flush?
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Client exception sending so server");

                Disconnect();
            }
        }

        //implement IDispose? To close stuff properly?
        public void Disconnect()
        {
            try
            {
                if (_tcpClient != null)
                {
                    _tcpClient.Close(); // this'll stop the socket blocking
                }
            }
            catch (Exception ex)
            {
            }

            Logger.Error("Disconnecting from server");

            //CallOnMain(false);
        }
    }
}