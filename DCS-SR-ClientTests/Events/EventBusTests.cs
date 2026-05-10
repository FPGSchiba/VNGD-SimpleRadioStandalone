using System;
using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;

namespace Vanguard.VCS.Client.Tests.Events
{
    [TestClass]
    public class EventBusTests
    {
        private IEventBus _bus;

        [TestInitialize]
        public void Setup() => _bus = new EventBus(new MessageHub());

        [TestMethod]
        public void Subscribe_Publish_HandlerReceivesMessage()
        {
            string received = null;
            _bus.Subscribe<string>(msg => received = msg);
            _bus.Publish("hello");
            Assert.AreEqual("hello", received);
        }

        [TestMethod]
        public void Subscribe_Dispose_HandlerNoLongerReceives()
        {
            string received = null;
            var subscription = _bus.Subscribe<string>(msg => received = msg);
            subscription.Dispose();
            _bus.Publish("hello");
            Assert.IsNull(received);
        }

        [TestMethod]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            _bus.Publish("orphan");
        }
    }
}
