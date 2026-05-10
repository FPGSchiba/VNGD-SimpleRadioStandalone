using System;
using System.Threading;
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

        public void Publish<T>(T message) where T : class
        {
            ArgumentNullException.ThrowIfNull(message);
            _hub.Publish(message);
        }

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            ArgumentNullException.ThrowIfNull(handler);
            var token = _hub.Subscribe(handler);
            return new SubscriptionToken(() => _hub.Unsubscribe(token));
        }

        private sealed class SubscriptionToken : IDisposable
        {
            private Action _unsubscribe;

            public SubscriptionToken(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose()
            {
                Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
            }
        }
    }
}
