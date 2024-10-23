using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public class StateChangedEventArgs : EventArgs
    {
        public ShipCondition NewCondition { get; }

        public StateChangedEventArgs(ShipCondition condition)
        {
            NewCondition = condition;
        }
    }
}