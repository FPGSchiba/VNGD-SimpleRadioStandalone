using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ServerSettingsStoreTests
    {
        private IEventBus _bus;
        private ServerSettingsStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ServerSettingsStore(_bus);
        }

        [TestCleanup]
        public void Cleanup() => _store.Dispose();

        [TestMethod]
        public void ServerSettingsChanged_UpdatesTestFrequencies()
        {
            var settings = new ServerSettings();
            settings.TestFrequencies.Add(121.5f);
            settings.TestFrequencies.Add(243.0f);

            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(2, _store.TestFrequencies.Count);
            Assert.AreEqual(121.5f, _store.TestFrequencies[0]);
            Assert.AreEqual(243.0f, _store.TestFrequencies[1]);
        }

        [TestMethod]
        public void ServerSettingsChanged_UpdatesGlobalFrequencies()
        {
            var settings = new ServerSettings();
            settings.GlobalFrequencies.Add(135.0f);

            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(1, _store.GlobalFrequencies.Count);
            Assert.AreEqual(135.0f, _store.GlobalFrequencies[0]);
        }

        [TestMethod]
        public void ServerSettingsChanged_UpdatesMaxRadiosPerClient()
        {
            var settings = new ServerSettings
            {
                GeneralSettings = new GeneralServerSettings { MaxRadiosPerClient = 5 }
            };

            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(5, _store.MaxRadiosPerClient);
        }

        [TestMethod]
        public void ServerSettingsChanged_NullGeneralSettings_MaxRadiosIsZero()
        {
            var settings = new ServerSettings();

            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(0, _store.MaxRadiosPerClient);
        }

        [TestMethod]
        public void ServerSettingsChanged_UpdatesCoalitions()
        {
            var coalition = new Coalition { Name = "Blue" };
            var settings = new ServerSettings();
            settings.Coalitions.Add(coalition);

            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(1, _store.Coalitions.Count);
            Assert.AreEqual("Blue", _store.Coalitions[0].Name);
        }

        [TestMethod]
        public void AfterDispose_EventsNoLongerUpdateState()
        {
            _store.Dispose();

            var settings = new ServerSettings();
            settings.TestFrequencies.Add(121.5f);
            _bus.Publish(new ServerSettingsChangedEvent(settings));

            Assert.AreEqual(0, _store.TestFrequencies.Count);
        }

        [TestMethod]
        public void InitialState_IsEmpty()
        {
            Assert.AreEqual(0, _store.TestFrequencies.Count);
            Assert.AreEqual(0, _store.GlobalFrequencies.Count);
            Assert.AreEqual(0, _store.Coalitions.Count);
            Assert.AreEqual(0, _store.MaxRadiosPerClient);
        }
    }
}
