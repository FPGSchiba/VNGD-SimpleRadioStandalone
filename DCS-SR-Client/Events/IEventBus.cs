using System;

namespace Vanguard.VCS.Client.Events
{
    public interface IEventBus
    {
        void Publish<T>(T message) where T : class;
        IDisposable Subscribe<T>(Action<T> handler) where T : class;
    }
}
