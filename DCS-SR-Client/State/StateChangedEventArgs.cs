using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public class StateChangedEventArgs : EventArgs
    {
        public Condition NewCondition { get; }
        public StateChangedEventArgs(Condition condition)
        {
            NewCondition = condition;
        }
    }
}