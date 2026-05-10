using System;
using Grpc.Core;

namespace Vanguard.VCS.Client.Network
{
    public interface ISrsServiceClient
    {
        SyncResponse SyncClient(Empty request, CallOptions options);
        ServerResponse UpdateClientInfo(ClientInfo request, CallOptions options);
        ServerResponse UpdateRadioInfo(RadioInfo request, CallOptions options);
        ServerResponse Disconnect(Empty request, CallOptions options);
        ServerSettings GetServerSettings(Empty request, CallOptions options);
        AsyncServerStreamingCall<ServerUpdate> SubscribeToUpdates(Empty request, CallOptions options);
    }

    public sealed class SrsServiceClientAdapter : ISrsServiceClient
    {
        private readonly SRSService.SRSServiceClient _inner;

        public SrsServiceClientAdapter(SRSService.SRSServiceClient inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        public SyncResponse SyncClient(Empty request, CallOptions options)
            => _inner.SyncClient(request, options);

        public ServerResponse UpdateClientInfo(ClientInfo request, CallOptions options)
            => _inner.UpdateClientInfo(request, options);

        public ServerResponse UpdateRadioInfo(RadioInfo request, CallOptions options)
            => _inner.UpdateRadioInfo(request, options);

        public ServerResponse Disconnect(Empty request, CallOptions options)
            => _inner.Disconnect(request, options);

        public ServerSettings GetServerSettings(Empty request, CallOptions options)
            => _inner.GetServerSettings(request, options);

        public AsyncServerStreamingCall<ServerUpdate> SubscribeToUpdates(Empty request, CallOptions options)
            => _inner.SubscribeToUpdates(request, options);
    }
}
