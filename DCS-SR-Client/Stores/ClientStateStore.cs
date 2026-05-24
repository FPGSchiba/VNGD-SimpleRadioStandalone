using System;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ClientStateStore : IDisposable
    {
        private readonly IDisposable _connectionSub;
        private readonly IDisposable _authSub;

        public string PlayerName { get; private set; } = string.Empty;
        public string Coalition { get; private set; } = string.Empty;
        public string UnitId { get; private set; } = string.Empty;
        public VcsRole Role { get; private set; } = VcsRole.Guest;
        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
        public bool IsConnected => ConnectionState == ConnectionState.Connected;

        public ClientStateStore(IEventBus bus)
        {
            ArgumentNullException.ThrowIfNull(bus);
            _connectionSub = bus.Subscribe<ConnectionStateChangedEvent>(e => ConnectionState = e.State);
            _authSub = bus.Subscribe<AuthenticationCompletedEvent>(e =>
            {
                PlayerName = e.PlayerName;
                Coalition = e.Coalition;
                UnitId = e.UnitId;
                Role = e.Role;
            });
        }

        public void Dispose()
        {
            _connectionSub.Dispose();
            _authSub.Dispose();
        }
    }
}
