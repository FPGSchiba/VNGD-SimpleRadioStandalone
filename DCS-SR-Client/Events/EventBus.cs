using System;
using Easy.MessageHub;

namespace Vanguard.VCS.Client.Events
{
    public sealed class EventBus : IEventBus
    {
        private readonly IMessageHub _hub;

        public EventBus(IMessageHub hub)
        {
            ArgumentNullException.ThrowIfNull(hub);
            _hub = hub;
        }

        public void Publish<T>(T message) where T : class => _hub.Publish(message);

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            var token = _hub.Subscribe(handler);
            return new SubscriptionToken(() => _hub.Unsubscribe(token));
        }

        private sealed class SubscriptionToken : IDisposable
        {
            private readonly Action _unsubscribe;
            public SubscriptionToken(Action unsubscribe) => _unsubscribe = unsubscribe;
            public void Dispose() => _unsubscribe();
        }
    }
}
