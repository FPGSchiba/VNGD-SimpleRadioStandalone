using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ConnectedClientsStoreTests
    {
        private IEventBus _bus;
        private ConnectedClientsStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ConnectedClientsStore(_bus);
        }

        [TestCleanup]
        public void Cleanup() => _store.Dispose();

        [TestMethod]
        public void ClientJoined_AddsClient()
        {
            var info = new ClientInfo { Name = "Pilot1", Coalition = "Blue" };
            var radios = new RadioInfo();
            _bus.Publish(new ClientJoinedEvent("guid-1", info, radios));
            Assert.AreEqual(1, _store.Clients.Count);
            Assert.IsTrue(_store.Clients.ContainsKey("guid-1"));
        }

        [TestMethod]
        public void ClientLeft_RemovesClient()
        {
            var info = new ClientInfo { Name = "Pilot1" };
            _bus.Publish(new ClientJoinedEvent("guid-1", info, new RadioInfo()));
            _bus.Publish(new ClientLeftEvent("guid-1"));
            Assert.AreEqual(0, _store.Clients.Count);
        }

        [TestMethod]
        public void ClientRadioUpdated_UpdatesRadios()
        {
            _bus.Publish(new ClientJoinedEvent("guid-1", new ClientInfo { Name = "Pilot1" }, new RadioInfo()));
            var newRadios = new RadioInfo { Muted = true };
            _bus.Publish(new ClientRadioUpdatedEvent("guid-1", newRadios));
            Assert.IsTrue(_store.Clients["guid-1"].Radios.Muted);
        }

        [TestMethod]
        public void ClientInfoUpdated_UpdatesInfo()
        {
            _bus.Publish(new ClientJoinedEvent("guid-1", new ClientInfo { Name = "Pilot1" }, new RadioInfo()));
            var newInfo = new ClientInfo { Name = "Pilot2", Coalition = "Red" };
            _bus.Publish(new ClientInfoUpdatedEvent("guid-1", newInfo));
            Assert.AreEqual("Pilot2", _store.Clients["guid-1"].Info.Name);
        }

        [TestMethod]
        public void ClientLeft_UnknownGuid_DoesNotThrow()
        {
            _bus.Publish(new ClientLeftEvent("unknown-guid"));
            Assert.AreEqual(0, _store.Clients.Count);
        }
    }
}
