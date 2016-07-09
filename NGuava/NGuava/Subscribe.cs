using System;

namespace NGuava
{
    /// <summary>
    ///     Subscribers that would like to declare a method as handler for a specific message, should add
    ///     this attribute to the method decleration.
    ///     <example>
    ///         public class ConsoleLogger
    ///         {
    ///         [Subscribe]
    ///         public void OnPurchase(IPurchaseEvent purchase)
    ///         {
    ///         Console.WriteLine(purchase.GetAmount());
    ///         }
    ///         }
    ///     </example>
    ///     Please notice the following:
    ///     a.The return type of the subscribed method is irellevant and generally should be void.
    ///     b.The method must have one and only one parameter, and its type determines
    ///     the event type.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class Subscribe : Attribute
    {
    }
}