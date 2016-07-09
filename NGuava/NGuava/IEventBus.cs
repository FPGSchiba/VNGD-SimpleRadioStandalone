namespace NGuava
{
    /// <summary>
    ///     Interface api for Event Bus.
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        ///     Register an object as subscriber.
        /// </summary>
        /// <param name="object">Subscriber</param>
        void Register(object @object);

        /// <summary>
        ///     Unregister an instance to events.
        /// </summary>
        /// <param name="object">Object instance to be unregistered to events.</param>
        void UnRegister(object @object);

        /// <summary>
        ///     Post an event for subscribers to that event.
        /// </summary>
        /// <param name="event">Event to be post to subscribers</param>
        void Post(object @event);
    }
}