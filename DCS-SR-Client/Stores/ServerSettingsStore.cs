using System;
using System.Collections.Generic;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ServerSettingsStore : IDisposable
    {
        private readonly IDisposable _settingsSub;

        public IReadOnlyList<float> TestFrequencies { get; private set; } = Array.Empty<float>();
        public IReadOnlyList<float> GlobalFrequencies { get; private set; } = Array.Empty<float>();
        public IReadOnlyList<Coalition> Coalitions { get; private set; } = Array.Empty<Coalition>();
        public int MaxRadiosPerClient { get; private set; }

        public ServerSettingsStore(IEventBus bus)
        {
            ArgumentNullException.ThrowIfNull(bus);
            _settingsSub = bus.Subscribe<ServerSettingsChangedEvent>(e => Apply(e.Settings));
        }

        private void Apply(ServerSettings s)
        {
            TestFrequencies = new List<float>(s.TestFrequencies).AsReadOnly();
            GlobalFrequencies = new List<float>(s.GlobalFrequencies).AsReadOnly();
            Coalitions = new List<Coalition>(s.Coalitions).AsReadOnly();
            MaxRadiosPerClient = s.GeneralSettings?.MaxRadiosPerClient ?? 0;
        }

        public void Dispose() => _settingsSub.Dispose();
    }
}
