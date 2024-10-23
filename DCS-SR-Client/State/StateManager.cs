using System;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public class ShipStateManager
    {
        private Condition currentCondition;
        private Dictionary<Component, string> componentStates;

        public event EventHandler<StateChangedEventArgs> StateChanged;

        public Condition CurrentCondition => currentCondition;

        public ShipStateManager()
        {
            currentCondition = Condition.Normal;
            componentStates = new Dictionary<Component, string>();
            foreach (Component component in Enum.GetValues(typeof(Component)))
            {
                componentStates[component] = "Nominal";
            }
        }

        public string GetComponentState(Component component)
        {
            if (componentStates.TryGetValue(component, out string state))
            {
                return state;
            }
            return "Unknown";
        }

        public void SetCondition(Condition newCondition)
        {
            if (currentCondition != newCondition)
            {
                Console.WriteLine($"Transitioning from {currentCondition} to {newCondition}");
                currentCondition = newCondition;
                UpdateComponentStates();
                OnStateChanged(newCondition);
            }
        }

        public void UpdateComponentState(Component component, string state)
        {
            string oldState = GetComponentState(component);
            if (oldState != state)
            {
                componentStates[component] = state;
                Console.WriteLine($"Updated {component} state to: {state}");
            }
        }

        public void ReportStatus()
        {
            Console.WriteLine($"Current Condition: {currentCondition}");
            Console.WriteLine("Component States:");
            foreach (var kvp in componentStates)
            {
                Console.WriteLine($"{kvp.Key}: {kvp.Value}");
            }
        }

        private void UpdateComponentStates()
        {
            switch (currentCondition)
            {
                case Condition.Normal:
                    foreach (Component component in Enum.GetValues(typeof(Component)))
                    {
                        componentStates[component] = "Nominal";
                    }
                    break;

                case Condition.LowPower:
                    componentStates[Component.PP] = "Reduced Output";
                    componentStates[Component.EG] = "Low Power";
                    componentStates[Component.QD] = "Standby";
                    componentStates[Component.WP] = "Offline";
                    break;

                case Condition.Combat:
                    componentStates[Component.PP] = "Maximum Output";
                    componentStates[Component.WP] = "Combat Ready";
                    componentStates[Component.SH] = "Maximum";
                    break;

                case Condition.CombatEnded:
                    foreach (Component component in Enum.GetValues(typeof(Component)))
                    {
                        componentStates[component] = "Needs Inspection";
                    }
                    break;
            }
        }

        protected virtual void OnStateChanged(Condition newCondition)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(newCondition));
        }
    }
}