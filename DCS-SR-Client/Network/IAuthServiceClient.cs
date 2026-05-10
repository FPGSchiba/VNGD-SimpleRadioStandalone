using Grpc.Core;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Network
{
    public interface IAuthServiceClient
    {
        AuthInitResponse InitAuth(AuthInitRequest request, CallOptions options);
        FlowDiscoveryResponse DiscoverAuthenticationFlows(FlowDiscoveryRequest request, CallOptions options);
        GuestLoginResponse GuestLogin(GuestLoginRequest request, CallOptions options);
        AuthStepResponse StartAuth(StartAuthRequest request, CallOptions options);
        AuthStepResponse ContinueAuth(ContinueAuthRequest request, CallOptions options);
        UnitSelectResponse UnitSelect(UnitSelectRequest request, CallOptions options);
    }

    public sealed class AuthServiceClientAdapter : IAuthServiceClient
    {
        private readonly AuthService.AuthServiceClient _inner;

        public AuthServiceClientAdapter(AuthService.AuthServiceClient inner)
        {
            _inner = inner;
        }

        public AuthInitResponse InitAuth(AuthInitRequest request, CallOptions options)
            => _inner.InitAuth(request, options);

        public FlowDiscoveryResponse DiscoverAuthenticationFlows(FlowDiscoveryRequest request, CallOptions options)
            => _inner.DiscoverAuthenticationFlows(request, options);

        public GuestLoginResponse GuestLogin(GuestLoginRequest request, CallOptions options)
            => _inner.GuestLogin(request, options);

        public AuthStepResponse StartAuth(StartAuthRequest request, CallOptions options)
            => _inner.StartAuth(request, options);

        public AuthStepResponse ContinueAuth(ContinueAuthRequest request, CallOptions options)
            => _inner.ContinueAuth(request, options);

        public UnitSelectResponse UnitSelect(UnitSelectRequest request, CallOptions options)
            => _inner.UnitSelect(request, options);
    }
}
