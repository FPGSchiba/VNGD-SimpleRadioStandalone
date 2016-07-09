using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NGuava
{
    /// <summary>
    ///     A IHandlerFindingStrategy for collecting all event handler  methods marked with Subscribe atrribute.
    /// </summary>
    public class AttributeHandlerFinder : IHandlerFindingStrategy
    {
        /// <summary>
        ///     This getting an object and extract all the methods marked with Subscribe attribute.
        /// </summary>
        /// <param name="subscriber">The object whose methods are desired to be event handlers</param>
        /// <returns>
        ///     MultiMap between a key which is event type and a value which is the event handler contains a target object and
        ///     methodInfo
        /// </returns>
        public IMultiMap<Type, EventHandler> FindAllHandlers(object subscriber)
        {
            IMultiMap<Type, EventHandler> methodsInSubscriber = HashMultiMap<Type, EventHandler>.Create();
            foreach (var method in GetMarkedMethods(subscriber))
            {
                var parmetersTypes = method.GetParameters();
                var eventType = parmetersTypes[0].ParameterType;
                var handler = new EventHandler(subscriber, method);
                methodsInSubscriber.Add(eventType, handler);
            }
            return methodsInSubscriber;
        }

        /// <summary>
        ///     This method doing the actual extraction of methods (MethodInfo) marked with Subscribe attribute.
        /// </summary>
        /// <param name="clazz">The object which contains the methods to be extracted.</param>
        /// <returns>A list of MethodInfo , where each MethodInfo matches a method that was marked with Subscribe atrribute</returns>
        private IEnumerable<MethodInfo> GetMarkedMethods(object clazz)
        {
            var typeOfClass = clazz.GetType();
            return typeOfClass.GetMethods().Where(method =>
            {
                var attribute = method.GetCustomAttribute(typeof(Subscribe));
                return attribute == null ? false : true;
            });
        }
    }
}