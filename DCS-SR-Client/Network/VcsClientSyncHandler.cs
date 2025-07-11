using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Google.Protobuf.Collections;
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
        public LoginRequestType LoginType { get; set; } // "guest" or "internal"
        
        public string UnitId { get; set; } // Optional, used for guest login
    }

    public class InternalLoginResult
    {
        public RepeatedField<CoalitionSelection> AvailableCoalitions { get; set; }
        public RepeatedField<UnitSelection> AvailableUnits { get; set; }
        public RepeatedField<RoleSelection> AvailableRoles { get; set; }
    }
    
    public struct InitializationResult
    {
        public string ClientGuid { get; set; }
        public bool IsVanguardLoginAvailable { get; set; }
        public bool IsGuestLoginAvailable { get; set; }
    }
    
    public enum LoginRequestType
    {
        Guest,
        Internal
    }
    
    public enum VcsRole
    {
        Guest,
        Member,
        Officer,
        Administrator
    }
    
    public enum VcsUiUpdateType
    {
        ConnectionError,
        InitializationError,
        InitializationSuccess,
        GuestLoginSuccess,
        GuestLoginError,
        InternalLoginSuccess,
        InternalLoginError,
        InternalUnitSelectionSuccess,
        InternalUnitSelectionError,
        ConnectionLost,
        ConnectionRestored,
    }
    
    public class VcsClientSyncHandler
    {
        public delegate void UpdateUiCallback(VcsUiUpdateType updateType, object message);
        
        private UpdateUiCallback _callback;
        private DateTime _connectedAt;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        
        private DCSRadioSyncManager _radioDCSSync = null;
        private SRSService.SRSServiceClient _srsServiceClient;
        private AuthService.AuthServiceClient _authServiceClient;
        private static readonly string _vcsVersion = "0.1.0";
        private string _clientGuid = string.Empty;
        private DistributionMode _serverDistributionMode = DistributionMode.Standalone; // Default to standalone mode
        private List<string> _serverAuthPlugins = new List<string>();
        private string _token = string.Empty;
        private string _tempSecret = string.Empty;

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

        public void ConnectVcs(IPEndPoint endpoint)
        {
            Logger.Info("Starting gRPC connection to VCS server");
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 10 * 1024 * 1024, // 10 MB
                MaxSendMessageSize = 10 * 1024 * 1024, // 10 MB
                Credentials = ChannelCredentials.Insecure, // Use insecure credentials for local development
            };
            var channel = GrpcChannel.ForAddress($"http://{endpoint.Address}:{endpoint.Port}", channelOptions);
            _srsServiceClient = new SRSService.SRSServiceClient(channel);
            _authServiceClient = new AuthService.AuthServiceClient(channel);
            
            InitializeRadioSync();
        }

        public void VcsLogin(UserLogin userLogin)
        {
            switch (userLogin.LoginType)
            {
                case LoginRequestType.Guest:
                    GuestLogin(userLogin);
                    break;
                case LoginRequestType.Internal:
                    // InternalLogin(userLogin);
                    Logger.Warn("Internal login is not yet implemented. Please use guest login for now.");
                    break;
                default:
                    Logger.Error("Invalid login type specified: {0}", userLogin.LoginType);
                    _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Invalid login type specified.");
                    break;
            }
        }
        
        private void InitializeRadioSync()
        {
            var initRequest = new ClientAuthInitRequest()
            {
                Capabilities = new ClientCapabilities()
                {
                    SupportedDistributionModes = { DistributionMode.Standalone }, // This Client only supports standalone mode
                    Version = _vcsVersion,
                },
            };
            var initResponse = _authServiceClient.InitAuth(initRequest);
            if (!initResponse.Success)
            {
                Logger.Error("Failed to initialize radio sync: {0}", initResponse.ErrorMessage);
                _callback?.Invoke(VcsUiUpdateType.InitializationError, initResponse.ErrorMessage);
                return;
            }
            _clientGuid = initResponse.Result.ClientGuid;
            _serverDistributionMode = initResponse.Result.DistributionMode;
            _serverAuthPlugins = new List<string>(initResponse.Result.AvailablePlugins);
            _callback?.Invoke(VcsUiUpdateType.InitializationSuccess, new InitializationResult
            {
                ClientGuid = _clientGuid,
                IsVanguardLoginAvailable = _serverAuthPlugins.Contains("profile-vanguard"),
                IsGuestLoginAvailable = initResponse.Result.HasGuestLogin,
            });
        }

        private void GuestLogin(UserLogin userLogin)
        {
            var connectRequest = new ClientGuestLoginRequest()
            {
                ClientGuid = _clientGuid,
                Name = userLogin.Username,
                Password = HashPassword(userLogin.Password),
                UnitId = userLogin.UnitId
            };
            
            var response = _authServiceClient.GuestLogin(connectRequest);
            if (!response.Success)
            {
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
            }
            else
            {
                _token = response.Result.Token;
                _connectedAt = DateTime.Now;
                // _clientStateSingleton.RegisterClientGuid(response.Result.ClientGuid);
                _callback?.Invoke(VcsUiUpdateType.GuestLoginSuccess, "");
            }
        }
        /*
        private void InternalLogin(UserLogin userLogin)
        {
            Logger.Info("Beginning internal login process for user: {0}", userLogin.Username);
            var loginRequest = new ClientVanguardLoginRequest()
            {
                Capabilities = new ClientCapabilities()
                {
                    SupportedFeatures = { ClientFeature.Standalone }, // This Client only supports standalone mode
                    Version = _vcsVersion,
                },
                Email = userLogin.Username,
                Password = userLogin.Password, // Password cannot be hashed here, as the Website expects the plain text password
            };
            
            var response = _authServiceClient.VanguardLogin(loginRequest);
            if (!response.Success)
            {
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
                return;
            }
            else
            {
                _tempSecret = response.Result.Secret;
                _clientStateSingleton.RegisterClientGuid(response.Result.ClientGuid);
                _connectedAt = DateTime.Now;
                _callback?.Invoke(VcsUiUpdateType.InternalLoginSuccess, new InternalLoginResult()
                {
                    AvailableCoalitions = response.Result.AvailableCoalitions,
                    AvailableUnits = response.Result.AvailableUnits,
                    AvailableRoles = response.Result.AvailableRoles
                });
            }
            Logger.Info($"Vanguard login response: {response}");
        }*/
    }
}