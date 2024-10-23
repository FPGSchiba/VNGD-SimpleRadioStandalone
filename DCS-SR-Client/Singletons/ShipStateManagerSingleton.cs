using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons
{
    public class ShipStateManagerSingleton
    {
        private static readonly Lazy<ShipStateManagerSingleton> lazy =
            new Lazy<ShipStateManagerSingleton>(() => new ShipStateManagerSingleton());

        public static ShipStateManagerSingleton Instance => lazy.Value;
        public ShipStateManager StateManager { get; private set; }

        private ShipStateManagerSingleton()
        {
            StateManager = new ShipStateManager();
        }

        public event EventHandler<StateChangedEventArgs> StateChanged;

        public void UpdateState(ShipCondition newCondition)
        {
            StateManager.SetCondition(newCondition);
            StateChanged?.Invoke(this, new StateChangedEventArgs(newCondition));
        }
        public object ClientSyncHandler { get; set; }

        public void UpdateComponentState(ShipComponent component, string state)
        {
            StateManager.UpdateComponentState(component, state);
        }
    }
}