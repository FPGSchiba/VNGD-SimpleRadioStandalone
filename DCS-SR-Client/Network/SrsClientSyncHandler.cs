using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

        public static string ServerVersion { get; private set; }
        private readonly string _guid;
        private ConnectCallback _callback;
        private ExternalAWACSModeConnectCallback _externalAWACSModeCallback;
        private readonly UpdateUICallback _updateUICallback;
        private IPEndPoint _serverEndpoint;
        private TcpClient _tcpClient;

        private DateTime _connectedAt;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;


        private DCSRadioSyncManager _radioDCSSync = null;

        private static readonly int MAX_DECODE_ERRORS = 5;
        private VAICOMSyncHandler _vaicomSync;

        private long _lastSent = -1;
        private readonly DispatcherTimer _idleTimeout;

        public SrsClientSyncHandler(string guid, UpdateUICallback uiCallback)
        {
            _guid = guid;
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


        public void TryConnect(IPEndPoint endpoint, ConnectCallback callback)
        {
            _callback = callback;
            _serverEndpoint = endpoint;

            var tcpThread = new Thread(Connect);
            tcpThread.Start();
        }

        public void ConnectExternalAWACSMode(string password, ExternalAWACSModeConnectCallback callback)
        {
            if (_clientStateSingleton.ExternalAWACSModelSelected)
            {
                return;
            }

            _externalAWACSModeCallback = callback;

            var sideInfo = _clientStateSingleton.PlayerCoaltionLocationMetadata;
            sideInfo.name = _clientStateSingleton.LastSeenName;
            SendToServer(new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name,
                    LatLngPosition = sideInfo.LngLngPosition,
                    ClientGuid = _guid,
                    AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                },
                ExternalAWACSModePassword = password,
                MsgType = NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_PASSWORD
            });
        }

        public void DisconnectExternalAWACSMode()
        {
            if (!_clientStateSingleton.ExternalAWACSModelSelected || _radioDCSSync == null)
            {
                return;
            }

            _radioDCSSync.StopExternalAWACSModeLoop();

            CallExternalAWACSModeOnMain(false, 0);
        }

        private void Connect()
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
                        _connectedAt = DateTime.Now;
                        CallOnMain(true);
                        ClientSyncLoop();
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
                    Seat = sideInfo.seat,
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

            if (needValidPosition)
            {
                message.Client.LatLngPosition = sideInfo.LngLngPosition;
            }
            else
            {
                message.Client.LatLngPosition = new DCSLatLngPosition();
            }

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
                    Seat = sideInfo.seat,
                    ClientGuid = _guid,
                    AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                },
                MsgType = NetworkMessage.MessageType.UPDATE
            };

            var needValidPosition = _serverSettings.GetSettingAsBool(ServerSettingsKeys.DISTANCE_ENABLED) || _serverSettings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED);

            if (needValidPosition)
            {
                message.Client.LatLngPosition = sideInfo.LngLngPosition;
            }
            else
            {
                message.Client.LatLngPosition = new DCSLatLngPosition();
            }

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
            catch (Exception e)
            {
                Logger.Error(e, "Failed to update UI after external AWACS mode callback (result {result}, coalition {coalition}, error {error})", result, coalition, error);
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
        
        private static bool IsValidLine(string line)
        {
            // Add your validation logic here
            return !string.IsNullOrEmpty(line) && line.Length < 4096; // Example validation (4 MB max)
        }

        private void ClientSyncLoop()
        {
            _clients.Clear();
            int decodeErrors = 0;
        
            using (var reader = new StreamReader(_tcpClient.GetStream(), Encoding.UTF8))
            {
                try
                {
                    SendInitialSyncRequest();
                    
                    string line;
                    while ((line = reader.ReadLine()) != null && IsValidLine(line))
                    {
                        try
                        {
                            ProcessServerMessage(line, ref decodeErrors);
                        }
                        catch (Exception ex)
                        {
                            HandleDecodeError(ex, ref decodeErrors);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandleReadException(ex);
                }
            }
        
            ResetClientState();
            Disconnect();
        }
        
        private void SendInitialSyncRequest()
        {
            var sideInfo = _clientStateSingleton.PlayerCoaltionLocationMetadata;
            SendToServer(new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = sideInfo.side,
                    Name = sideInfo.name.Length > 0 ? sideInfo.name : _clientStateSingleton.LastSeenName,
                    LatLngPosition = sideInfo.LngLngPosition,
                    ClientGuid = _guid,
                    RadioInfo = _clientStateSingleton.DcsPlayerRadioInfo,
                    AllowRecord = GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowRecording)
                },
                MsgType = NetworkMessage.MessageType.SYNC,
            });
        }
        
        private void ProcessServerMessage(string line, ref int decodeErrors)
        {
            var serverMessage = JsonConvert.DeserializeObject<NetworkMessage>(line);
            decodeErrors = 0;
        
            if (serverMessage != null)
            {
                switch (serverMessage.MsgType)
                {
                    case NetworkMessage.MessageType.PING:
                        break;
                    case NetworkMessage.MessageType.RADIO_UPDATE:
                    case NetworkMessage.MessageType.UPDATE:
                        ProcessUpdateMessage(serverMessage);
                        break;
                    case NetworkMessage.MessageType.SYNC:
                        ProcessSyncMessage(serverMessage);
                        break;
                    case NetworkMessage.MessageType.SERVER_SETTINGS:
                        ProcessServerSettingsMessage(serverMessage);
                        break;
                    case NetworkMessage.MessageType.CLIENT_DISCONNECT:
                        ProcessClientDisconnectMessage(serverMessage);
                        break;
                    case NetworkMessage.MessageType.VERSION_MISMATCH:
                        ProcessVersionMismatchMessage(serverMessage);
                        break;
                    case NetworkMessage.MessageType.EXTERNAL_AWACS_MODE_PASSWORD:
                        ProcessExternalAWACSModePasswordMessage(serverMessage);
                        break;
                    default:
                        Logger.Error("Received unknown Packet Type: " + serverMessage.MsgType);
                        break;
                }
            }
        }
        
        private void ProcessUpdateMessage(NetworkMessage serverMessage)
        {
            if (serverMessage.ServerSettings != null)
            {
                _serverSettings.Decode(serverMessage.ServerSettings);
            }
        
            if (_clients.ContainsKey(serverMessage.Client.ClientGuid))
            {
                UpdateExistingClient(serverMessage);
            }
            else
            {
                AddNewClient(serverMessage);
            }
        
            if (_clientStateSingleton.ExternalAWACSModelSelected && !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE) && _connectedAt.AddSeconds(2) < DateTime.Now)
            {
                Logger.Debug("Closing AWACS Mode after Update message...");
                DisconnectExternalAWACSMode();
            }
        
            CallUpdateUIOnMain();
        }
        
        private void UpdateExistingClient(NetworkMessage serverMessage)
        {
            var srClient = _clients[serverMessage.Client.ClientGuid];
            var updatedSrClient = serverMessage.Client;
        
            if (srClient != null)
            {
                srClient.LastUpdate = DateTime.Now.Ticks;
                srClient.Name = updatedSrClient.Name;
                srClient.Coalition = updatedSrClient.Coalition;
                srClient.LatLngPosition = updatedSrClient.LatLngPosition;
        
                if (updatedSrClient.RadioInfo != null)
                {
                    srClient.RadioInfo = updatedSrClient.RadioInfo;
                    srClient.RadioInfo.LastUpdate = DateTime.Now.Ticks;
                }
                else if (serverMessage.MsgType == NetworkMessage.MessageType.RADIO_UPDATE && srClient.RadioInfo != null)
                {
                    srClient.RadioInfo.LastUpdate = DateTime.Now.Ticks;
                }
            }
        }
        
        private void AddNewClient(NetworkMessage serverMessage)
        {
            var connectedClient = serverMessage.Client;
            connectedClient.LastUpdate = DateTime.Now.Ticks;
            connectedClient.LineOfSightLoss = 0.0f;
            _clients[serverMessage.Client.ClientGuid] = connectedClient;
        }
        
        private void ProcessSyncMessage(NetworkMessage serverMessage)
        {
            if (serverMessage.Version == null)
            {
                Logger.Error("Disconnecting Unversioned Server");
                Disconnect();
                return;
            }
        
            var serverVersion = Version.Parse(serverMessage.Version);
            var protocolVersion = Version.Parse(UpdaterChecker.MINIMUM_PROTOCOL_VERSION);
        
            ServerVersion = serverMessage.Version;
        
            if (serverVersion < protocolVersion)
            {
                Logger.Error($"Server version ({serverMessage.Version}) older than minimum protocol version ({UpdaterChecker.MINIMUM_PROTOCOL_VERSION}) - disconnecting");
                ShowVersionMismatchWarning(serverMessage.Version);
                Disconnect();
                return;
            }
        
            if (serverMessage.Clients != null)
            {
                foreach (var client in serverMessage.Clients)
                {
                    client.LastUpdate = DateTime.Now.Ticks;
                    client.LineOfSightLoss = 0.0f;
                    _clients[client.ClientGuid] = client;
                }
            }
        
            _serverSettings.Decode(serverMessage.ServerSettings);
        
            if (_clientStateSingleton.ExternalAWACSModelSelected && !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE))
            {
                Logger.Debug("Closing AWACS Mode after SYNC message...");
                DisconnectExternalAWACSMode();
            }
        
            CallUpdateUIOnMain();
        }
        
        private void ProcessServerSettingsMessage(NetworkMessage serverMessage)
        {
            _serverSettings.Decode(serverMessage.ServerSettings);
            ServerVersion = serverMessage.Version;
        
            if (_clientStateSingleton.ExternalAWACSModelSelected && !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE))
            {
                Logger.Debug("Closing AWACS Mode after Server Settings message...");
                DisconnectExternalAWACSMode();
            }
        
            CallUpdateUIOnMain();
        }
        
        private void ProcessClientDisconnectMessage(NetworkMessage serverMessage)
        {
            SRClient outClient;
            _clients.TryRemove(serverMessage.Client.ClientGuid, out outClient);
        
            if (outClient != null)
            {
                MessageHub.Instance.Publish(outClient);
            }
        }
        
        private void ProcessVersionMismatchMessage(NetworkMessage serverMessage)
        {
            Logger.Error($"Version Mismatch Between Client ({UpdaterChecker.VERSION}) & Server ({serverMessage.Version}) - Disconnecting");
            ShowVersionMismatchWarning(serverMessage.Version);
            Disconnect();
        }
        
        private void ProcessExternalAWACSModePasswordMessage(NetworkMessage serverMessage)
        {
            if (serverMessage.Client.Coalition == 0)
            {
                Logger.Info("External AWACS mode authentication failed");
                CallExternalAWACSModeOnMain(false, 0, true);
            }
            else if (_radioDCSSync != null && _radioDCSSync.IsListening)
            {
                Logger.Info("External AWACS mode authentication succeeded, coalition {0}", serverMessage.Client.Coalition == 1 ? "red" : "blue");
                CallExternalAWACSModeOnMain(true, serverMessage.Client.Coalition);
                _radioDCSSync.StartExternalAWACSModeLoop();
            }
        }
        
        private void HandleDecodeError(Exception ex, ref int decodeErrors)
        {
            decodeErrors++;
            if (!_stop)
            {
                Logger.Error(ex, "Client exception reading from socket ");
            }
        
            if (decodeErrors > MAX_DECODE_ERRORS)
            {
                ShowVersionMismatchWarning("unknown");
                Disconnect();
            }
        }
        
        private void HandleReadException(Exception ex)
        {
            if (!_stop)
            {
                Logger.Error(ex, "Client exception reading - Disconnecting ");
            }
        }
        
        private void ResetClientState()
        {
            ClientStateSingleton.Instance.DcsPlayerRadioInfo.LastUpdate = 0;
            _clients.Clear();
        }

        private static void ShowVersionMismatchWarning(string serverVersion)
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
                Logger.Error(e, "Error closing TCP Client");
            }

            Logger.Info("Disconnecting from server");
            ClientStateSingleton.Instance.IsConnected = false;
        }
    }
}