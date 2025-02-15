using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.VAICOM;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Easy.MessageHub;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class SrsClientSyncHandler
    {
        public delegate void ConnectCallback(bool result, bool connectionError, string connection);
        public delegate void ExternalAWACSModeConnectCallback(bool result, int coalition, bool error = false);
        public delegate void UpdateUICallback();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private volatile bool _stop = false;

        public static string ServerVersion = "Unknown";
        private readonly string _guid;
        private ConnectCallback _callback;
        private ExternalAWACSModeConnectCallback _externalAWACSModeCallback;
        private UpdateUICallback _updateUICallback;
        private IPEndPoint _serverEndpoint;
        private TcpClient _tcpClient;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;


        private DCSRadioSyncManager _radioDCSSync = null;

        private static readonly int MAX_DECODE_ERRORS = 5;
        private VAICOMSyncHandler _vaicomSync;

        private long _lastSent = -1;
        private DispatcherTimer _idleTimeout;

        public SrsClientSyncHandler(UpdateUICallback uiCallback)
        {
            _updateUICallback = uiCallback;

            _idleTimeout = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            _idleTimeout.Tick += CheckIfIdleTimeOut;
            _idleTimeout.Interval = TimeSpan.FromSeconds(10);
        }

        private void CheckIfIdleTimeOut(object sender, EventArgs e)
        {
            var timeout = GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.IdleTimeOut).IntValue;
            if (_lastSent != -1 && TimeSpan.FromTicks(DateTime.Now.Ticks - _lastSent).TotalSeconds > timeout)
            {
                Logger.Warn("Disconnecting - Idle Time out");
                Disconnect();
            }

        }


        public void TryConnect(IPEndPoint endpoint, string password, string playerName, ConnectCallback callback, ExternalAWACSModeConnectCallback externalAwacsModeConnectCallback)
        {
            _callback = callback;
            _serverEndpoint = endpoint;
            _externalAWACSModeCallback = externalAwacsModeConnectCallback;

            var tcpThread = new Thread(() => Connect(password, playerName));
            tcpThread.Start();
        }

        public void DisconnectExternalAWACSMode()
        {
            if (!_clientStateSingleton.ExternalAWACSModelSelected || _radioDCSSync == null)
            {
                return;
            }
            
            SendToServer(new NetworkMessage
            {
                MsgType = NetworkMessage.MessageType.CLIENT_DISCONNECT
            });

            _radioDCSSync.StopExternalAWACSModeLoop();

            CallExternalAWACSModeOnMain(false, 0);
        }

        private void Connect(string password, string playerName)
        {
            _lastSent = DateTime.Now.Ticks;
            _idleTimeout.Start();

            if (_radioDCSSync != null)
            {
                _radioDCSSync.Stop();
                _radioDCSSync = null;
            }
            if (_vaicomSync != null)
            {
                _vaicomSync.Stop();
                _vaicomSync = null;
            }

            bool connectionError = false;

            _radioDCSSync = new DCSRadioSyncManager(ClientRadioUpdated, ClientCoalitionUpdate);
            _vaicomSync = new VAICOMSyncHandler();

            using (_tcpClient = new TcpClient())
            {
                try
                {
                    _tcpClient.SendTimeout = 90000;
                    _tcpClient.NoDelay = true;

                    // Wait for 10 seconds before aborting connection attempt - no SRS server running/port opened in that case
                    _tcpClient.ConnectAsync(_serverEndpoint.Address, _serverEndpoint.Port).Wait(TimeSpan.FromSeconds(10));

                    if (_tcpClient.Connected)
                    {
                        _radioDCSSync.Start();
                        _vaicomSync.Start();

                        _tcpClient.NoDelay = true;

                        CallOnMain(true);
                        ClientSyncLoop(password, playerName);
                    }
                    else
                    {
                        Logger.Error($"Failed to connect to server @ {_serverEndpoint.ToString()}");

                        // Signal disconnect including an error
                        connectionError = true;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Could not connect to server");
                    connectionError = true;
                }
            }

            _radioDCSSync.Stop();
            _vaicomSync.Stop();
            _idleTimeout?.Stop();

            //disconnect callback
            CallOnMain(false, connectionError);
        }

        private void ClientRadioUpdated()
        {
            //disconnect AWACS
            if (_clientStateSingleton.ExternalAWACSModelSelected && _clientStateSingleton.IsGameConnected)
            {
                Logger.Debug("Disconnect AWACS Mode as Game Detected");
                DisconnectExternalAWACSMode();
            }

            Logger.Debug("Sending Radio Update to Server");
            var sideInfo = _clientStateSingleton.PlayerCoaltionLocationMetadata;

            var message = new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name,
                    ClientGuid = _guid,
                    RadioInfo = _clientStateSingleton.DcsPlayerRadioInfo,
                    AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                },
                MsgType = NetworkMessage.MessageType.RADIO_UPDATE
            };

            foreach (var radio in message.Client.RadioInfo.radios)
            {
                Logger.Trace(radio.name + ": " + radio.modulation);
            }

            var needValidPosition = _serverSettings.GetSettingAsBool(ServerSettingsKeys.DISTANCE_ENABLED) || _serverSettings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED);

            SendToServer(message);
        }

        private void ClientCoalitionUpdate()
        {
            var sideInfo = _clientStateSingleton.PlayerCoaltionLocationMetadata;

            var message = new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name,
                    ClientGuid = _guid,
                    AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                },
                MsgType = NetworkMessage.MessageType.UPDATE
            };

            SendToServer(message);
        }

        private void CallOnMain(bool result, bool connectionError = false)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                    new ThreadStart(delegate { _callback(result, connectionError, _serverEndpoint.ToString()); }));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to update UI after connection callback (result {result}, connectionError {connectionError})", result, connectionError);
            }
        }

        private void CallExternalAWACSModeOnMain(bool result, int coalition, bool error = false)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                    new ThreadStart(delegate { _externalAWACSModeCallback(result, coalition, error); }));
            }
            catch (Exception)
            {
            }
        }

        private void CallUpdateUIOnMain()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                    new ThreadStart(delegate { _updateUICallback(); }));
            }
            catch (Exception)
            {
                //IGNORE
            }
        }
        
        private static string ToHex(byte[] bytes, bool upperCase)
        {
            StringBuilder result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                result.Append(bytes[i].ToString(upperCase ? "X2" : "x2"));
            return result.ToString();
        }

        private static string SHA256HexHashString(string StringIn)
        {
            string hashString;
            using (var sha256 = SHA256Managed.Create())
            {
                var hash = sha256.ComputeHash(Encoding.Default.GetBytes(StringIn));
                hashString = ToHex(hash, false);
            }

            return hashString;
        }

        private void ClientSyncLoop(string password, string playerName)
        {
            //clear the clients list
            _clients.Clear();
            int decodeErrors = 0; //if the JSON is unreadable - new version likely

            using (var reader = new StreamReader(_tcpClient.GetStream(), Encoding.UTF8))
            {
                try
                {
                    var sideInfo = _clientStateSingleton.PlayerCoaltionLocationMetadata;
                    // Start off by sending login information 
                    SendToServer(new NetworkMessage
                    {
                        Client = new SRClient
                        {
                            Coalition = sideInfo.side,
                            Name = playerName,
                            AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                        },
                        Password = SHA256HexHashString(password),
                        MsgType = NetworkMessage.MessageType.LOGIN
                    });

                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            var serverMessage = JsonConvert.DeserializeObject<NetworkMessage>(line);
                            decodeErrors = 0; //reset counter
                            if (serverMessage != null)
                            {
                                //Logger.Debug("Received "+serverMessage.MsgType);
                                switch (serverMessage.MsgType)
                                {
                                    case NetworkMessage.MessageType.PING:
                                        // Do nothing for now
                                        break;
                                    case NetworkMessage.MessageType.RADIO_UPDATE:
                                    case NetworkMessage.MessageType.UPDATE:

                                        if (serverMessage.ServerSettings != null)
                                        {
                                            _serverSettings.Decode(serverMessage.ServerSettings);
                                        }

                                        if (_clients.ContainsKey(serverMessage.Client.ClientGuid))
                                        {
                                            var srClient = _clients[serverMessage.Client.ClientGuid];
                                            var updatedSrClient = serverMessage.Client;
                                            if (srClient != null)
                                            {
                                                srClient.LastUpdate = DateTime.Now.Ticks;
                                                srClient.Name = updatedSrClient.Name;
                                                srClient.Coalition = updatedSrClient.Coalition;

                                                if (updatedSrClient.RadioInfo != null)
                                                {
                                                    srClient.RadioInfo = updatedSrClient.RadioInfo;
                                                    srClient.RadioInfo.LastUpdate = DateTime.Now.Ticks;
                                                }
                                                else
                                                {
                                                    //radio update but null RadioInfo means no change
                                                    if (serverMessage.MsgType ==
                                                        NetworkMessage.MessageType.RADIO_UPDATE &&
                                                        srClient.RadioInfo != null)
                                                    {
                                                        srClient.RadioInfo.LastUpdate = DateTime.Now.Ticks;
                                                    }
                                                }

                                                // Logger.Debug("Received Update Client: " + NetworkMessage.MessageType.UPDATE + " From: " +
                                                //             srClient.Name + " Coalition: " +
                                                //             srClient.Coalition + " Pos: " + srClient.LatLngPosition);
                                            }
                                        }
                                        else
                                        {
                                            var connectedClient = serverMessage.Client;
                                            connectedClient.LastUpdate = DateTime.Now.Ticks;

                                            _clients[serverMessage.Client.ClientGuid] = connectedClient;

                                            // Logger.Debug("Received New Client: " + NetworkMessage.MessageType.UPDATE +
                                            //             " From: " +
                                            //             serverMessage.Client.Name + " Coalition: " +
                                            //             serverMessage.Client.Coalition);
                                        }

                                        if (_clientStateSingleton.ExternalAWACSModelSelected &&
                                            !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE))
                                        {
                                            Logger.Debug("Closing AWACS Mode after Update message...");
                                            Logger.Debug($"Mode selected: {_clientStateSingleton.ExternalAWACSModelSelected}");
                                            Logger.Debug($"Server AWACS Settings: {_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE)}");
                                            DisconnectExternalAWACSMode();
                                        }

                                        CallUpdateUIOnMain();

                                        break;
                                    case NetworkMessage.MessageType.SYNC:
                                        if (serverMessage.Clients != null)
                                        {
                                            foreach (var client in serverMessage.Clients)
                                            {
                                                client.LastUpdate = DateTime.Now.Ticks;
                                                _clients[client.ClientGuid] = client;
                                            }
                                        }
                                        //add server settings
                                        _serverSettings.Decode(serverMessage.ServerSettings);

                                        if (_clientStateSingleton.ExternalAWACSModelSelected &&
                                            !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE))
                                        {
                                            Logger.Debug("Closing AWACS Mode after SYNC message...");
                                            Logger.Debug($"Mode selected: {_clientStateSingleton.ExternalAWACSModelSelected}");
                                            Logger.Debug($"Server AWACS Settings: {_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE)}");
                                            DisconnectExternalAWACSMode();
                                        }

                                        CallUpdateUIOnMain();

                                        break;

                                    case NetworkMessage.MessageType.SERVER_SETTINGS:
                                        _serverSettings.Decode(serverMessage.ServerSettings);
                                        ServerVersion = serverMessage.Version;

                                        if (_clientStateSingleton.ExternalAWACSModelSelected &&
                                            !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE))
                                        {
                                            Logger.Debug("Closing AWACS Mode after Server Settings message...");
                                            Logger.Debug($"Mode selected: {_clientStateSingleton.ExternalAWACSModelSelected}");
                                            Logger.Debug($"Server AWACS Settings: {_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE)}");
                                            DisconnectExternalAWACSMode();
                                        }

                                        CallUpdateUIOnMain();

                                        break;
                                    case NetworkMessage.MessageType.CLIENT_DISCONNECT:
                                        SRClient outClient;
                                        _clients.TryRemove(serverMessage.Client.ClientGuid, out outClient);

                                        if (outClient != null)
                                        {
                                            MessageHub.Instance.Publish(outClient);
                                        }

                                        break;
                                    case NetworkMessage.MessageType.VERSION_MISMATCH:
                                        Logger.Error($"Version Mismatch Between Client ({UpdaterChecker.VERSION}) & Server ({serverMessage.Version}) - Disconnecting");
                                        ShowVersionMistmatchWarning(serverMessage.Version);
                                        Disconnect();
                                        break;
                                    case NetworkMessage.MessageType.LOGIN_SUCCESS:
                                        Logger.Info("External AWACS mode authentication succeeded, coalition {0}", serverMessage.Client.Coalition == 1 ? "red" : "blue");
                                        // Set the ID for the client
                                        Logger.Info("Registering Client with GUID: " + serverMessage.Client.ClientGuid);
                                        _clientStateSingleton.ShortGUID = serverMessage.Client.ClientGuid;
                                        CallExternalAWACSModeOnMain(true, serverMessage.Client.Coalition);
                                        _radioDCSSync.StartExternalAWACSModeLoop();
                                        break;
                                    case NetworkMessage.MessageType.LOGIN_FAILED:
                                        Logger.Info("External AWACS mode authentication failed");
                                        CallExternalAWACSModeOnMain(false, 0, true);
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
                            decodeErrors++;
                            if (!_stop)
                            {
                                Logger.Error(ex, "Client exception reading from socket ");
                            }

                            if (decodeErrors > MAX_DECODE_ERRORS)
                            {
                                ShowVersionMistmatchWarning("unknown");
                                Disconnect();
                                break;
                            }
                        }

                        // do something with line
                    }
                }
                catch (Exception ex)
                {
                    if (!_stop)
                    {
                        Logger.Error(ex, "Client exception reading - Disconnecting ");
                    }
                }
            }

            //disconnected - reset DCS Info
            ClientStateSingleton.Instance.DcsPlayerRadioInfo.LastUpdate = 0;

            //clear the clients list
            _clients.Clear();

            Disconnect();
        }

        private void ShowVersionMistmatchWarning(string serverVersion)
        {
            MessageBox.Show($"The SRS server you're connecting to is incompatible with this Client. " +
                            $"\n\nMake sure to always run the latest version of the SRS Server & Client" +
                            $"\n\nServer Version: {serverVersion}" +
                            $"\nClient Version: {UpdaterChecker.VERSION}",
                            "SRS Server Incompatible",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }

        private void SendToServer(NetworkMessage message)
        {
            try
            {
                _lastSent = DateTime.Now.Ticks;
                message.Version = UpdaterChecker.VERSION;

                var json = message.Encode();

                if (message.MsgType == NetworkMessage.MessageType.RADIO_UPDATE)
                {
                    Logger.Debug("Sending Radio Update To Server: " + (json));
                }

                var bytes = Encoding.UTF8.GetBytes(json);
                _tcpClient.GetStream().Write(bytes, 0, bytes.Length);
                //Need to flush?
            }
            catch (Exception ex)
            {
                if (!_stop)
                {
                    Logger.Error(ex, "Client exception sending to server");
                }

                Disconnect();
            }
        }

        //implement IDispose? To close stuff properly?
        public void Disconnect()
        {
            _stop = true;

            _lastSent = DateTime.Now.Ticks;
            _idleTimeout?.Stop();

            DisconnectExternalAWACSMode();

            try
            {
                if (_tcpClient != null)
                {
                    _tcpClient.Close(); // this'll stop the socket blocking
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to close TCP Client correctly");
            }

            Logger.Info("Disconnecting from server");
            ClientStateSingleton.Instance.IsConnected = false;

            //CallOnMain(false);
        }
    }
}