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
    
    public class VcsClientSyncHandler : SRSService.SRSServiceClient
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

        public async void ConnectVcs(IPEndPoint endpoint, UserLogin userLogin)
        {
            Logger.Info("Starting gRPC connection to VCS server");
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 10 * 1024 * 1024, // 10 MB
                MaxSendMessageSize = 10 * 1024 * 1024, // 10 MB
                Credentials = ChannelCredentials.Create(ChannelCredentials.SecureSsl, CallCredentials.FromInterceptor((
                    (context, metadata) =>
                    {
                        if (!string.IsNullOrEmpty(userLogin.Username) && !string.IsNullOrEmpty(userLogin.Password))
                        {
                            metadata.Add("username", userLogin.Username);
                            metadata.Add("unitId", "DEV"); // Example unit ID, replace with actual logic if needed
                            metadata.Add("password", HashPassword(userLogin.Password)); // Defines the coalition for the user
                        }
                        return System.Threading.Tasks.Task.CompletedTask;
                    })))
            };
            var channel = GrpcChannel.ForAddress($"https://{endpoint.Address}:{endpoint.Port}", channelOptions);
            _client = new SRSService.SRSServiceClient(channel);

            var connectRequest = new ClientConnectRequest()
            {
                Version = "0.1.0",
            };
            
            Connect(connectRequest);
        }
    }
}