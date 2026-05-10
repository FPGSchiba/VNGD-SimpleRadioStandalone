using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Google.Protobuf.Collections;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Grpc.Core;
using Grpc.Net.Client;
using MathNet.Numerics.Distributions;
using NLog;
using Vanguard.VCS.Client.Network.DCS;
using Vanguard.VCS.Client.UI.ClientWindow;
using Vanguard.VCS.Common.DCSState;
using DevOne.Security.Cryptography.BCrypt;

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
        public string PlayerName { get; set; }
    }
    
    public struct InitializationResult
    {
        public Guid ClientGuid { get; set; }
        public bool IsVanguardLoginAvailable { get; set; }
        public bool IsGuestLoginAvailable { get; set; }
    }

    public struct UnitSelectionResult
    {
        public VcsRole SelectedRole { get; set; }
        public string SelectedUnitId { get; set; }
        public string SelectedCoalition { get; set; }
        public string PlayerName { get; set; }
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
        RadioSyncSuccess,
        RadioSyncError,
        RadioSyncUpdate,
        ClientSyncSuccess,
        ClientSyncError,
        ClientSyncUpdate,
        ConnectionLost,
        ConnectionRestored,
    }
    
    public class VcsClientSyncHandler
    {
        public delegate void UpdateUiCallback(VcsUiUpdateType updateType, object message);
        
        private readonly UpdateUiCallback _callback;
        private DateTime _connectedAt;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        
        private DCSRadioSyncManager _radioDcsSync = null;
        private SRSService.SRSServiceClient _srsServiceClient;
        private AuthService.AuthServiceClient _authServiceClient;
        private GrpcChannel _channel;
        private static readonly string VcsVersion = "0.1.0";
        private Guid _clientGuid;
        private Metadata _authenticationMetadata = new Metadata();
        private string _tempSecret = string.Empty;
        public string ServerVersion { get; private set; } = "0.0.0"; // Default version

        public VcsClientSyncHandler(UpdateUiCallback uiCallback)
        {
            _callback = uiCallback;
        }

        private static string HashPassword(string password)
        {
            return BCryptHelper.HashPassword(password,BCryptHelper.GenerateSalt(12));
        }

        private static CallOptions DefaultCallOptions(int timeoutSeconds = 10)
            => new CallOptions(deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds));

        private CallOptions AuthCallOptions(int timeoutSeconds = 10)
            => new CallOptions(headers: _authenticationMetadata, deadline: DateTime.UtcNow.AddSeconds(timeoutSeconds));

        public void ConnectVcs(IPEndPoint endpoint)
        {
            Logger.Info("Starting gRPC connection to VCS server");
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 10 * 1024 * 1024, // 10 MB
                MaxSendMessageSize = 10 * 1024 * 1024, // 10 MB
                Credentials = ChannelCredentials.Insecure, // Use insecure credentials for local development
            };
            _channel = GrpcChannel.ForAddress($"http://{endpoint.Address}:{endpoint.Port}", channelOptions);
            _srsServiceClient = new SRSService.SRSServiceClient(_channel);
            _authServiceClient = new AuthService.AuthServiceClient(_channel);
            
            InitializeConnection();
        }

        public void VcsLogin(UserLogin userLogin)
        {
            switch (userLogin.LoginType)
            {
                case LoginRequestType.Guest:
                    GuestLogin(userLogin);
                    break;
                case LoginRequestType.Internal:
                    InternalLogin(userLogin);
                    break;
                default:
                    Logger.Error("Invalid login type specified: {0}", userLogin.LoginType);
                    _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Invalid login type specified.");
                    break;
            }
        }
        
        private void InitializeConnection()
        {
            var initRequest = new AuthInitRequest()
            {
                Capabilities = new ClientCapabilities()
                {
                    SupportedDistributionModes = { DistributionMode.Standalone },
                    Version = VcsVersion,
                },
            };

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var callOptions = DefaultCallOptions(10);
                    var initResponse = _authServiceClient.InitAuth(initRequest, callOptions);
                    if (!initResponse.Success)
                    {
                        Logger.Error("Failed to initialize radio sync: {0}", initResponse.ErrorMessage);
                        _callback?.Invoke(VcsUiUpdateType.InitializationError, initResponse.ErrorMessage);
                        return;
                    }
                    _clientGuid = Guid.Parse(initResponse.Result.ClientGuid);
                    _clientStateSingleton.RegisterClientGuid(_clientGuid);
                    _callback?.Invoke(VcsUiUpdateType.InitializationSuccess, new InitializationResult
                    {
                        ClientGuid = _clientGuid,
                        IsVanguardLoginAvailable = initResponse.Result.AvailablePlugins.Contains("profile-vanguard"),
                        IsGuestLoginAvailable = initResponse.Result.HasGuestLogin,
                    });
                    _radioDcsSync = new DCSRadioSyncManager(UpdateRadioInformation, ClientCoalitionUpdate);
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded || ex.StatusCode == StatusCode.Unavailable)
                {
                    Logger.Warn(ex, $"Init attempt {attempt}/{maxAttempts} failed ({ex.StatusCode})");
                    if (attempt == maxAttempts)
                    {
                        _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Connection timed out — server unreachable.");
                        return;
                    }
                    Thread.Sleep(attempt * 1000);
                }
                catch (RpcException ex)
                {
                    Logger.Error(ex, "gRPC error during initialization");
                    _callback?.Invoke(VcsUiUpdateType.ConnectionError, ex.Status.Detail);
                    return;
                }
            }
        }

        private void GuestLogin(UserLogin userLogin)
        {
            var connectRequest = new GuestLoginRequest()
            {
                ClientGuid = _clientGuid.ToString(),
                Name = userLogin.Username,
                Password = HashPassword(userLogin.Password),
                UnitId = userLogin.UnitId
            };

            try
            {
                var response = _authServiceClient.GuestLogin(connectRequest, DefaultCallOptions(15));
                if (!response.Success)
                {
                    _callback?.Invoke(VcsUiUpdateType.GuestLoginError, response.ErrorMessage);
                }
                else
                {
                    _authenticationMetadata = new Metadata
                    {
                        { "authorization", $"Bearer {response.Result.Token}" },
                    };
                    _connectedAt = DateTime.Now;
                    _callback?.Invoke(VcsUiUpdateType.GuestLoginSuccess, null);
                    InitializeRadioSync();
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "Guest login timed out");
                _callback?.Invoke(VcsUiUpdateType.GuestLoginError, "Login timed out — server did not respond.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during guest login");
                _callback?.Invoke(VcsUiUpdateType.GuestLoginError, ex.Status.Detail);
            }
        }
        
        private void InternalLogin(UserLogin userLogin)
        {
            Logger.Info("Beginning internal login process for user: {0}", userLogin.Username);
            var loginRequest = new StartAuthRequest()
            {
                ClientGuid = _clientGuid.ToString(),
                AuthenticationPlugin = "profile-vanguard",
                FlowId = "vanguard_email_password",
                FirstStepInput = { { "email", userLogin.Username }, { "password", userLogin.Password } }
            };

            try
            {
                var response = _authServiceClient.StartAuth(loginRequest, DefaultCallOptions(15));
                if (!response.Success)
                {
                    _callback?.Invoke(VcsUiUpdateType.InternalLoginError, response.ErrorMessage);
                    return;
                }

                _tempSecret = response.Complete.Secret;
                _clientStateSingleton.LastSeenName = response.Complete.PlayerName;
                _callback?.Invoke(VcsUiUpdateType.InternalLoginSuccess, new InternalLoginResult()
                {
                    AvailableCoalitions = response.Complete.AvailableCoalitions,
                    AvailableUnits = response.Complete.AvailableUnits,
                    AvailableRoles = response.Complete.AvailableRoles,
                    PlayerName = response.Complete.PlayerName,
                });
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "Internal login timed out");
                _callback?.Invoke(VcsUiUpdateType.InternalLoginError, "Login timed out — server did not respond.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during internal login");
                _callback?.Invoke(VcsUiUpdateType.InternalLoginError, ex.Status.Detail);
            }
        }

        public void SelectUnit(string unitId, string coalition, uint roleId)
        {
            if (string.IsNullOrEmpty(_tempSecret))
            {
                Logger.Error("Cannot select unit without a valid temp secret.");
                _callback?.Invoke(VcsUiUpdateType.InternalUnitSelectionError, "No valid session found.");
                return;
            }

            var selectionRequest = new UnitSelectRequest()
            {
                ClientGuid = _clientGuid.ToString(),
                Secret = _tempSecret,
                UnitId = unitId,
                Coalition = coalition,
                Role = roleId
            };

            try
            {
                var response = _authServiceClient.UnitSelect(selectionRequest, DefaultCallOptions(15));
                if (!response.Success)
                {
                    _callback?.Invoke(VcsUiUpdateType.InternalUnitSelectionError, response.ErrorMessage);
                    return;
                }

                _connectedAt = DateTime.Now;
                _authenticationMetadata = new Metadata
                {
                    { "authorization", $"Bearer {response.Token}" },
                };
                _callback?.Invoke(VcsUiUpdateType.InternalUnitSelectionSuccess, new UnitSelectionResult()
                {
                    SelectedCoalition = coalition,
                    SelectedUnitId = unitId,
                    SelectedRole = (VcsRole)roleId + 1
                });
                InitializeRadioSync();
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "Unit selection timed out");
                _callback?.Invoke(VcsUiUpdateType.InternalUnitSelectionError, "Selection timed out — server did not respond.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during unit selection");
                _callback?.Invoke(VcsUiUpdateType.InternalUnitSelectionError, ex.Status.Detail);
            }
        }

        private void InitializeRadioSync()
        {
            _radioDcsSync.Start();
            _radioDcsSync.StartExternalAWACSModeLoop(); // The radio information will be updated here so we need to call this first
            SyncClient();
        }

        private void ClientCoalitionUpdate()
        {
            // TODO: implement this better to really choose the coalition not just red and blue
            Logger.Info("Client coalition update triggered");
        }
        
        private void SyncClient()
        {
            var syncRequest = new Empty();
            try
            {
                var syncResponse = _srsServiceClient.SyncClient(syncRequest, AuthCallOptions(10));
                if (syncResponse.Success)
                {
                    Logger.Info("Client sync successful.");
                    _serverSettings.DecodeVcs(syncResponse.Data.Settings);
                    _clients.DecodeVcs(syncResponse.Data.Clients, syncResponse.Data.Radios);
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncSuccess, null);
                }
                else
                {
                    Logger.Error("Client sync failed: {0}", syncResponse.ErrorMessage);
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncError, syncResponse.ErrorMessage);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "Client sync timed out");
                _callback?.Invoke(VcsUiUpdateType.ClientSyncError, "Sync timed out — server did not respond.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during client sync");
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, ex.Message);
            }
        }

        public void UpdateRadioInformation()
        {
            try
            {
                var response = _srsServiceClient.UpdateRadioInfo(GetRadioInfoFromState(), AuthCallOptions(5));
                if (response.Success)
                {
                    Logger.Info("Radio information updated successfully.");
                    _callback?.Invoke(VcsUiUpdateType.RadioSyncSuccess, null);
                }
                else
                {
                    Logger.Error("Failed to update radio information: {0}", response.ErrorMessage);
                    _callback?.Invoke(VcsUiUpdateType.RadioSyncError, response.ErrorMessage);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Warn(ex, "Radio update timed out");
                _callback?.Invoke(VcsUiUpdateType.RadioSyncError, "Radio update timed out.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during radio update");
                _callback?.Invoke(VcsUiUpdateType.RadioSyncError, ex.Status.Detail);
            }
        }
        
        public void Disconnect()
        {
            Logger.Info("Disconnecting from VCS server");
            try
            {
                var request = new Empty();
                _srsServiceClient.Disconnect(request, AuthCallOptions(5));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error sending Disconnect RPC (proceeding with local cleanup)");
            }

            try
            {
                _channel?.ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
                _channel?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error shutting down gRPC channel");
            }

            try
            {
                _radioDcsSync?.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error stopping DCS radio sync");
            }

            _channel = null;
            _srsServiceClient = null;
            _authServiceClient = null;
            _radioDcsSync = null;

            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, null);
        }

        private RadioInfo GetRadioInfoFromState()
        {
            var radios = _clientStateSingleton.DcsPlayerRadioInfo.radios.Select((radio, i) => new Radio()
                {
                    Id = (uint)i,
                    Name = radio.name,
                    Frequency = (float)(radio.freq / 1000000.0), // Convert to MHz
                    Enabled = radio.modulation == RadioInformation.Modulation.AM,
                    IsIntercom = radio.modulation == RadioInformation.Modulation.INTERCOM,
                })
                .ToList();
            
            Logger.Info($"Preparing radios for sync: {radios.Count} radios found. Radio details: {string.Join(", ", radios.Select(r => $"{r.Name} ({r.Frequency} kHz)"))}");

            return new RadioInfo
            {
                Radios = { radios },
                Muted = false,
            };
        }
    }
}