using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ClientStateStoreTests
    {
        private IEventBus _bus;
        private ClientStateStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ClientStateStore(_bus);
        }

        [TestCleanup]
        public void Cleanup() => _store.Dispose();

        [TestMethod]
        public void ConnectionStateChanged_Connected_IsConnectedTrue()
        {
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
            Assert.IsTrue(_store.IsConnected);
            Assert.AreEqual(ConnectionState.Connected, _store.ConnectionState);
        }

        [TestMethod]
        public void ConnectionStateChanged_Disconnected_IsConnectedFalse()
        {
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Disconnected));
            Assert.IsFalse(_store.IsConnected);
        }

        [TestMethod]
        public void AuthenticationCompleted_SetsPlayerInfo()
        {
            _bus.Publish(new AuthenticationCompletedEvent("Pilot1", "Blue", "unit-42", VcsRole.Member));
            Assert.AreEqual("Pilot1", _store.PlayerName);
            Assert.AreEqual("Blue", _store.Coalition);
            Assert.AreEqual("unit-42", _store.UnitId);
            Assert.AreEqual(VcsRole.Member, _store.Role);
        }

        [TestMethod]
        public void AfterDispose_EventsNoLongerUpdateState()
        {
            _store.Dispose();
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
            Assert.IsFalse(_store.IsConnected);
        }
    }
}
