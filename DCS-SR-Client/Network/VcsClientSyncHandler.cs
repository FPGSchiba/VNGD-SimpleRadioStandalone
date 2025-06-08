using System;
using System.Net;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Grpc.Core;
using Grpc.Net.Client;
using NLog;
using Vanguard.VCS.Client.Network.DCS;

namespace Vanguard.VCS.Client.Network
{
    public class UserLogin
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string LoginType { get; set; } // "guest" or "internal"
    }
    
    public enum LoginType
    {
        Guest,
        Member,
        Officer,
        Administrator
    }
    
    public enum VcsUiUpdateType
    {
        ConnectionError,
        GuestLoginSuccess,
        InternalLoginSuccess,
        InternalUnitSelectionSuccess,
        InternalUnitSelectionError,
        ConnectionLost,
        ConnectionRestored,
    }
    
    public class VcsClientSyncHandler
    {
        public delegate void UpdateUiCallback(VcsUiUpdateType updateType, string message);
        
        private UpdateUiCallback _callback;
        private DateTime _connectedAt;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        
        private DCSRadioSyncManager _radioDCSSync = null;
        private SRSService.SRSServiceClient _client;
        private static readonly string _vcsVersion = "0.1.0";
        private string _token = string.Empty;

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

        public void ConnectVcs(IPEndPoint endpoint, UserLogin userLogin)
        {
            Logger.Info("Starting gRPC connection to VCS server");
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 10 * 1024 * 1024, // 10 MB
                MaxSendMessageSize = 10 * 1024 * 1024, // 10 MB
                Credentials = ChannelCredentials.Insecure,
            };
            var channel = GrpcChannel.ForAddress($"http://{endpoint.Address}:{endpoint.Port}", channelOptions);
            _client = new SRSService.SRSServiceClient(channel);

            if (userLogin.LoginType == "guest")
            {
                GuestLogin(userLogin);
            }
            else if (userLogin.LoginType == "internal")
            {
                InternalLogin(userLogin);
            }
            else
            {
                Logger.Error("Invalid login type specified: {0}", userLogin.LoginType);
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Invalid login type specified.");
            }
        }

        private void GuestLogin(UserLogin userLogin)
        {
            var connectRequest = new ClientGuestLoginRequest()
            {
                Version = _vcsVersion,
                Name = userLogin.Username,
                Password = HashPassword(userLogin.Password),
                UnitId = "DEV"
            };
            
            var response = _client.GuestLogin(connectRequest);
            if (!response.Success)
            {
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
            }
            else
            {
                _token = response.Result.Token;
                _connectedAt = DateTime.Now;
                _callback?.Invoke(VcsUiUpdateType.GuestLoginSuccess, "");
            }
        }

        private void InternalLogin(UserLogin userLogin)
        {
            var loginRequest = new ClientVanguardLoginRequest()
            {
                Version = _vcsVersion,
                Email = userLogin.Username,
                Password = HashPassword(userLogin.Password),
            };
            
            var response = _client.VanguardLogin(loginRequest);
            if (!response.Success)
            {
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
                return;
            }
            Logger.Info($"Vanguard login response: {response}");
        }
    }
}