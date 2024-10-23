using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
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

        // Event to notify UI of state changes
        public event EventHandler<StateChangedEventArgs> StateChanged;

        public void UpdateState(Condition newCondition)
        {
            StateManager.SetCondition(newCondition);
            StateChanged?.Invoke(this, new StateChangedEventArgs(newCondition));
        }

        public void UpdateComponentState(Component component, string state)
        {
            StateManager.UpdateComponentState(component, state);
            // Could add component-specific event here if needed
        }
    }

    public class StateChangedEventArgs : EventArgs
    {
        public Condition NewCondition { get; }

        public StateChangedEventArgs(Condition condition)
        {
            NewCondition = condition;
        }
    }
}