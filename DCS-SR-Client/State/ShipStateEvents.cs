// State/ShipStateEvents.cs
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public static class ShipStateEvents
    {
        public static event EventHandler StateChanged;

        public static void NotifyStateChanged()
        {
            StateChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}