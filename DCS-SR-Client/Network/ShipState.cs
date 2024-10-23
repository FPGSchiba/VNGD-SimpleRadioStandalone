using System;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network
{
    [Serializable]
    public class ShipState
    {
        public string CurrentCondition { get; set; }
        public Dictionary<string, string> ComponentStates { get; set; }

        public ShipState()
        {
            CurrentCondition = "Normal";
            ComponentStates = new Dictionary<string, string>();
        }
    }
}
