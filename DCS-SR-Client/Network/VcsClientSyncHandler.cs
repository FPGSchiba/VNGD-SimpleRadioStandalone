using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.Collections;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Network;
using Grpc.Core;
using Grpc.Net.Client;
using MathNet.Numerics.Distributions;
using NLog;
using Vanguard.VCS.Client.UI.ClientWindow;
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
        ServerSettingsFetched,
        ServerSettingsError,
    }
    
    public class VcsClientSyncHandler
    {
        public delegate void UpdateUiCallback(VcsUiUpdateType updateType, object message);
        
        private readonly UpdateUiCallback _callback;
        private readonly IEventBus _eventBus;
        private DateTime _connectedAt;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        
        private RadioStateManager _radioStateManager;
        private ISrsServiceClient _srsServiceClient;
        private IAuthServiceClient _authServiceClient;
        private GrpcChannel _channel;
        private static readonly string VcsVersion = "0.1.0";
        private Guid _clientGuid;
        private Metadata _authenticationMetadata = new Metadata();
        private string _tempSecret = string.Empty;
        public string ServerVersion { get; private set; } = "0.0.0"; // Default version
        private CancellationTokenSource _streamCts;
        private Task _subscriptionTask;

        public VcsClientSyncHandler(UpdateUiCallback uiCallback, IEventBus eventBus)
        {
            _callback = uiCallback;
            _eventBus = eventBus;
        }

        // For unit testing only: skips channel and radio-sync initialisation.
        internal VcsClientSyncHandler(
            UpdateUiCallback uiCallback,
            IAuthServiceClient authClient,
            ISrsServiceClient srsClient)
        {
            _callback = uiCallback;
            _authServiceClient = authClient;
            _srsServiceClient = srsClient;
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
            _srsServiceClient = new SrsServiceClientAdapter(new SRSService.SRSServiceClient(_channel));
            _authServiceClient = new AuthServiceClientAdapter(new AuthService.AuthServiceClient(_channel));
            
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
                    _eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Connecting));
                    _radioStateManager = new RadioStateManager(UpdateRadioInformation, _eventBus);
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
                    _eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
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

        public AuthStepResponse ContinueAuth(string sessionId, System.Collections.Generic.Dictionary<string, string> stepData)
        {
            var request = new ContinueAuthRequest
            {
                SessionId = sessionId,
                ClientGuid = _clientGuid.ToString()
            };
            if (stepData != null)
                foreach (var kv in stepData)
                    request.StepData[kv.Key] = kv.Value;

            try
            {
                var response = _authServiceClient.ContinueAuth(request, DefaultCallOptions(15));
                if (!response.Success)
                {
                    _callback?.Invoke(VcsUiUpdateType.InternalLoginError, response.ErrorMessage);
                    return null;
                }

                if (response.ResultCase == AuthStepResponse.ResultOneofCase.Complete)
                {
                    _tempSecret = response.Complete.Secret;
                    _clientStateSingleton.LastSeenName = response.Complete.PlayerName;
                    _callback?.Invoke(VcsUiUpdateType.InternalLoginSuccess, new InternalLoginResult
                    {
                        AvailableCoalitions = response.Complete.AvailableCoalitions,
                        AvailableUnits = response.Complete.AvailableUnits,
                        AvailableRoles = response.Complete.AvailableRoles,
                        PlayerName = response.Complete.PlayerName,
                    });
                }
                return response;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "ContinueAuth timed out");
                _callback?.Invoke(VcsUiUpdateType.InternalLoginError, "Login timed out — server did not respond.");
                return null;
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during ContinueAuth");
                _callback?.Invoke(VcsUiUpdateType.InternalLoginError, ex.Status.Detail);
                return null;
            }
        }

        public FlowDiscoveryResult DiscoverAuthenticationFlows(string pluginName)
        {
            try
            {
                var response = _authServiceClient.DiscoverAuthenticationFlows(
                    new FlowDiscoveryRequest { AuthenticationPlugin = pluginName },
                    DefaultCallOptions(10));

                if (!response.Success)
                {
                    Logger.Error("Flow discovery failed: {0}", response.ErrorMessage);
                    _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
                    return null;
                }
                return response.Result;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Error(ex, "Flow discovery timed out");
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Flow discovery timed out.");
                return null;
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during flow discovery");
                _callback?.Invoke(VcsUiUpdateType.ConnectionError, ex.Status.Detail);
                return null;
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
                _eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
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
            _radioStateManager.Start();
            _clientStateSingleton.CurrentRadioState = _radioStateManager.CurrentState;
            SyncClient();
            StartSubscription();
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

        public bool UpdateClientInfo(string name, string coalition, string unitId, uint roleId)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Logger.Warn("UpdateClientInfo called with null or empty name");
                return false;
            }

            var info = new ClientInfo
            {
                Name = name,
                Coalition = coalition,
                UnitId = unitId,
                RoleId = roleId,
            };
            try
            {
                var response = _srsServiceClient.UpdateClientInfo(info, AuthCallOptions(5));
                if (!response.Success)
                {
                    Logger.Error("UpdateClientInfo failed: {0}", response.ErrorMessage);
                    return false;
                }
                Logger.Info("Client info updated successfully.");
                return true;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Warn(ex, "UpdateClientInfo timed out");
                return false;
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during UpdateClientInfo");
                return false;
            }
        }

        public void UpdateRadioInformation()
        {
            try
            {
                _clientStateSingleton.CurrentRadioState = _radioStateManager.CurrentState;
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
        
        public void FetchServerSettings()
        {
            try
            {
                var settings = _srsServiceClient.GetServerSettings(new Empty(), AuthCallOptions(10));
                _serverSettings.DecodeVcs(settings);
                _callback?.Invoke(VcsUiUpdateType.ServerSettingsFetched, settings);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
            {
                Logger.Warn(ex, "GetServerSettings timed out");
                _callback?.Invoke(VcsUiUpdateType.ServerSettingsError, "Settings fetch timed out.");
            }
            catch (RpcException ex)
            {
                Logger.Error(ex, "gRPC error during GetServerSettings");
                _callback?.Invoke(VcsUiUpdateType.ServerSettingsError, ex.Status.Detail);
            }
            catch (Exception ex) when (ex is not RpcException)
            {
                Logger.Error(ex, "Unexpected error processing server settings");
                _callback?.Invoke(VcsUiUpdateType.ServerSettingsError, "Failed to apply server settings.");
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
                _streamCts?.Cancel();
                _streamCts?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error cancelling subscription stream");
            }
            _streamCts = null;
            _subscriptionTask = null;

            try
            {
                _ = _channel?.ShutdownAsync();
                _channel?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error shutting down gRPC channel");
            }

            try
            {
                _radioStateManager?.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error stopping radio state manager");
            }

            _channel = null;
            _srsServiceClient = null;
            _authServiceClient = null;
            _radioStateManager = null;

            _eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Disconnected));
            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, null);
        }

        internal void ProcessServerUpdate(ServerUpdate update)
        {
            switch (update.Type)
            {
                case ServerUpdate.Types.UpdateType.ClientJoined:
                    ApplyClientUpdate(update.ClientUpdate);
                    _eventBus?.Publish(new ClientJoinedEvent(
                        update.ClientUpdate.ClientGuid,
                        update.ClientUpdate.ClientInfo,
                        update.ClientUpdate.RadioInfo));
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
                    break;

                case ServerUpdate.Types.UpdateType.ClientRadioUpdate:
                    ApplyClientUpdate(update.ClientUpdate);
                    _eventBus?.Publish(new ClientRadioUpdatedEvent(
                        update.ClientUpdate.ClientGuid,
                        update.ClientUpdate.RadioInfo));
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
                    break;

                case ServerUpdate.Types.UpdateType.ClientInfoUpdate:
                    ApplyClientUpdate(update.ClientUpdate);
                    _eventBus?.Publish(new ClientInfoUpdatedEvent(
                        update.ClientUpdate.ClientGuid,
                        update.ClientUpdate.ClientInfo));
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
                    break;

                case ServerUpdate.Types.UpdateType.ClientLeft:
                    if (update.ClientUpdate != null && Guid.TryParse(update.ClientUpdate.ClientGuid, out var leftGuid))
                    {
                        _clients.TryRemove(leftGuid, out _);
                    }
                    _eventBus?.Publish(new ClientLeftEvent(update.ClientUpdate?.ClientGuid ?? string.Empty));
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
                    break;

                case ServerUpdate.Types.UpdateType.ServerSettingsChanged:
                    if (update.SettingsUpdate != null)
                    {
                        _serverSettings.DecodeVcs(update.SettingsUpdate);
                    }
                    _eventBus?.Publish(new ServerSettingsChangedEvent(update.SettingsUpdate));
                    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
                    break;

                case ServerUpdate.Types.UpdateType.ServerAction:
                    if (update.ServerAction != null)
                    {
                        HandleServerAction(update.ServerAction);
                    }
                    break;

                default:
                    Logger.Warn("Received unknown ServerUpdate type: {0}", update.Type);
                    break;
            }
        }

        private void ApplyClientUpdate(ClientUpdate clientUpdate)
        {
            if (clientUpdate == null || !Guid.TryParse(clientUpdate.ClientGuid, out var clientGuid))
            {
                return;
            }

            var clientInfo = clientUpdate.ClientInfo;
            var radioInfo = clientUpdate.RadioInfo;

            var srClient = new SRClient
            {
                ClientGuid = clientGuid,
                Name = clientInfo?.Name ?? "---",
                Coalition = 0,
                AllowRecord = true,
                Muted = radioInfo?.Muted ?? false,
                LastUpdate = clientInfo?.LastUpdate ?? 0,
                Seat = 0,
                RadioInfo = radioInfo != null
                    ? new DCSPlayerRadioInfo
                    {
                        radios = radioInfo.Radios.Select(r => new RadioInformation
                        {
                            freq = r.Frequency,
                            modulation = r.Enabled
                                ? RadioInformation.Modulation.DISABLED
                                : r.IsIntercom
                                    ? RadioInformation.Modulation.INTERCOM
                                    : RadioInformation.Modulation.AM,
                            name = r.Name,
                            enc = false,
                            freqMax = 9999999999,
                            freqMin = 1,
                        }).ToArray()
                    }
                    : null,
            };

            _clients[clientGuid] = srClient;
        }

        private void HandleServerAction(ServerAction action)
        {
            switch (action.Type)
            {
                case ServerAction.Types.ActionType.Kick:
                    Logger.Warn("Kicked from server. Reason: {0}", action.Reason);
                    _callback?.Invoke(VcsUiUpdateType.ConnectionLost, action.Reason);
                    break;

                case ServerAction.Types.ActionType.Ban:
                    Logger.Warn("Banned from server. Reason: {0}", action.Reason);
                    _callback?.Invoke(VcsUiUpdateType.ConnectionLost, action.Reason);
                    break;

                case ServerAction.Types.ActionType.Mute:
                    Logger.Info("Muted by server.");
                    break;

                case ServerAction.Types.ActionType.Unmute:
                    Logger.Info("Unmuted by server.");
                    break;

                default:
                    Logger.Warn("Received unknown ServerAction type: {0}", action.Type);
                    break;
            }
        }

        private void StartSubscription()
        {
            _streamCts = new CancellationTokenSource();
            _subscriptionTask = Task.Factory.StartNew(
                () => RunSubscriptionLoop(_streamCts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            _subscriptionTask.ContinueWith(
                t => Logger.Error(t.Exception, "Subscription loop faulted unexpectedly"),
                TaskContinuationOptions.OnlyOnFaulted);
        }

        private void RunSubscriptionLoop(CancellationToken cancellationToken)
        {
            var backoffSeconds = 1;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var callOptions = new CallOptions(
                        headers: _authenticationMetadata,
                        cancellationToken: cancellationToken);

                    using var call = _srsServiceClient.SubscribeToUpdates(new Empty(), callOptions);
                    backoffSeconds = 1;
                    Logger.Info("SubscribeToUpdates stream connected");

                    var stream = call.ResponseStream;
                    while (stream.MoveNext(cancellationToken).GetAwaiter().GetResult())
                    {
                        ProcessServerUpdate(stream.Current);
                    }

                    Logger.Info("SubscribeToUpdates stream ended cleanly");
                }
                catch (OperationCanceledException)
                {
                    Logger.Info("SubscribeToUpdates cancelled");
                    return;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                {
                    Logger.Info("SubscribeToUpdates RPC cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    Logger.Warn(ex, $"SubscribeToUpdates stream dropped, reconnecting in {backoffSeconds}s");
                    _callback?.Invoke(VcsUiUpdateType.ConnectionLost, null);
                    Thread.Sleep(backoffSeconds * 1000);
                    backoffSeconds = Math.Min(backoffSeconds * 2, 30);
                }
            }
        }

        private RadioInfo GetRadioInfoFromState()
        {
            var radios = _radioStateManager.CurrentState.Radios
                .Select((radio, i) => new Radio
                {
                    Id = (uint)i,
                    Name = radio.Name,
                    Frequency = (float)(radio.FrequencyHz / 1_000_000.0),
                    Enabled = radio.Enabled,
                    IsIntercom = radio.IsIntercom,
                })
                .ToList();

            Logger.Info($"Preparing radios for sync: {radios.Count} radios. Details: {string.Join(", ", radios.Select(r => $"{r.Name} ({r.Frequency} MHz)"))}");

            return new RadioInfo
            {
                Radios = { radios },
                Muted = false,
            };
        }
    }
}