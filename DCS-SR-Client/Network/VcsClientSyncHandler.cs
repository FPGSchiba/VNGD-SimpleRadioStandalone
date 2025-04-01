using System;
using System.Collections.Generic;
using System.Net;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using NLog;
using SocketIO.Core;
using SocketIOClient;
using Wpf.Ui.Controls;
using Xamarin.Forms.Internals;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class UserLogin
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
    
    public enum VcsUiUpdateType
    {
        ConnectionError,
        ConnectionSuccess,
        ConnectionLost,
        ConnectionRestored,
        AuthenticationSuccess,
        AuthenticationFailed,
    }
    
    public class VcsClientSyncHandler
    {
        public delegate void UpdateUiCallback(VcsUiUpdateType updateType, string message);
        
        private UpdateUiCallback _callback;
        private DateTime _connectedAt;
        private SocketIOClient.SocketIO _client;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        
        private DCSRadioSyncManager _radioDCSSync = null;

        public VcsClientSyncHandler(UpdateUiCallback uiCallback)
        {
            _callback = uiCallback;
        }
        
        private static string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(password);
                var hash = sha256.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
            }
        }

        public async void Connect(IPEndPoint endpoint, UserLogin userLogin)
        {
            Logger.Info("Starting Socket.IO connection to VCS server");
            var authData = new UserLogin
            {
                Username = userLogin.Username,
                Password = HashPassword(userLogin.Password)
            };
            
            _client = new SocketIOClient.SocketIO($"http://{endpoint.Address}:{endpoint.Port}/", new SocketIOOptions
            {
                Auth = authData,
                ReconnectionAttempts = 10,
                ConnectionTimeout = TimeSpan.FromSeconds(10),
                EIO = EngineIO.V4,
                Transport = SocketIOClient.Transport.TransportProtocol.WebSocket
            });
            _client.OnConnected += OnConnected;
            _client.OnDisconnected += OnDisconnected;
            
            _client.On("auth-success", response =>
            {
                Logger.Info("Socket connected & Authenticated to VCS server");
            });
            
            await _client.ConnectAsync();
        }
        
        private async void OnDisconnected(object sender, string e)
        {
            Logger.Info("Disconnected from VCS server");
        }
        
        private async void OnConnected(object sender, EventArgs e)
        {
            Logger.Info("Connected to VCS server");
        }
    }
}