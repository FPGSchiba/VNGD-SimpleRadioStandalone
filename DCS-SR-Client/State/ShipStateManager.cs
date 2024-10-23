using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public class ShipStateManager
    {
        private ShipCondition currentCondition;
        private Dictionary<ShipComponent, string> componentStates;

        public event EventHandler<StateChangedEventArgs> StateChanged;

        public ShipCondition CurrentCondition => currentCondition;

        public ShipStateManager()
        {
            currentCondition = ShipCondition.Normal;
            componentStates = new Dictionary<ShipComponent, string>();
            foreach (ShipComponent component in Enum.GetValues(typeof(ShipComponent)))
            {
                componentStates[component] = "Nominal";
            }
        }

        public string GetComponentState(ShipComponent component)
        {
            if (componentStates.TryGetValue(component, out string state))
            {
                return state;
            }
            return "Unknown";
        }

        public void SetCondition(ShipCondition newCondition)
        {
            if (currentCondition != newCondition)
            {
                Console.WriteLine($"Transitioning from {currentCondition} to {newCondition}");
                currentCondition = newCondition;
                UpdateComponentStates();
                OnStateChanged(newCondition);
            }
        }

        public void UpdateComponentState(ShipComponent component, string state)
        {
            string oldState = GetComponentState(component);
            if (oldState != state)
            {
                componentStates[component] = state;
                Console.WriteLine($"Updated {component} state to: {state}");
            }
        }

        private void UpdateComponentStates()
        {
            switch (currentCondition)
            {
                case ShipCondition.Normal:
                    foreach (ShipComponent component in Enum.GetValues(typeof(ShipComponent)))
                    {
                        componentStates[component] = "Nominal";
                    }
                    break;

                case ShipCondition.LowPower:
                    componentStates[ShipComponent.PP] = "Reduced Output";
                    componentStates[ShipComponent.EG] = "Low Power";
                    componentStates[ShipComponent.QD] = "Standby";
                    componentStates[ShipComponent.WP] = "Offline";
                    break;

                case ShipCondition.Combat:
                    componentStates[ShipComponent.PP] = "Maximum Output";
                    componentStates[ShipComponent.WP] = "Combat Ready";
                    componentStates[ShipComponent.SH] = "Maximum";
                    break;

                case ShipCondition.CombatEnded:
                    foreach (ShipComponent component in Enum.GetValues(typeof(ShipComponent)))
                    {
                        componentStates[component] = "Needs Inspection";
                    }
                    break;
            }
        }

        protected virtual void OnStateChanged(ShipCondition newCondition)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(newCondition));
        }
    }
}