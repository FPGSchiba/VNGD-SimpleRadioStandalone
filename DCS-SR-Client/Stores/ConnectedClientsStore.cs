using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ConnectedClientsStore : IDisposable
    {
        public sealed record ClientEntry(ClientInfo Info, RadioInfo Radios);

        private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
        private readonly IDisposable _joinSub;
        private readonly IDisposable _leftSub;
        private readonly IDisposable _radioSub;
        private readonly IDisposable _infoSub;

        public IReadOnlyDictionary<string, ClientEntry> Clients => _clients;

        public ConnectedClientsStore(IEventBus bus)
        {
            ArgumentNullException.ThrowIfNull(bus);

            _joinSub = bus.Subscribe<ClientJoinedEvent>(e =>
                _clients[e.Guid] = new ClientEntry(e.Info, e.Radios));

            _leftSub = bus.Subscribe<ClientLeftEvent>(e =>
                _clients.TryRemove(e.Guid, out _));

            _radioSub = bus.Subscribe<ClientRadioUpdatedEvent>(e =>
            {
                if (_clients.TryGetValue(e.Guid, out var existing))
                    _clients[e.Guid] = existing with { Radios = e.Radios };
            });

            _infoSub = bus.Subscribe<ClientInfoUpdatedEvent>(e =>
            {
                if (_clients.TryGetValue(e.Guid, out var existing))
                    _clients[e.Guid] = existing with { Info = e.Info };
            });
        }

        public void Dispose()
        {
            _joinSub.Dispose();
            _leftSub.Dispose();
            _radioSub.Dispose();
            _infoSub.Dispose();
        }
    }
}
