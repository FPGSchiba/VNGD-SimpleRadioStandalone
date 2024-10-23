using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.State
{
    public static class ShipStateManagerExtensions
    {
        private static readonly Dictionary<ShipComponent, string> DefaultStates =
            new Dictionary<ShipComponent, string>
            {
                { ShipComponent.PP, "Nominal" },
                { ShipComponent.EG, "Nominal" },
                { ShipComponent.QD, "Nominal" },
                { ShipComponent.WP, "Nominal" },
                { ShipComponent.SH, "Nominal" },
                { ShipComponent.CL, "Nominal" },
                { ShipComponent.CR, "Nominal" }
            };

        public static void UpdateFromNetworkMessage(this ShipStateManager manager, SRClient client)
        {
            if (client == null) return;

            manager.SetCondition(client.ShipCondition);

            if (client.ShipComponentStates != null)
            {
                foreach (var kvp in client.ShipComponentStates)
                {
                    manager.UpdateComponentState(kvp.Key, kvp.Value);
                }
            }
            else
            {
                // Set defaults if no states provided
                foreach (var kvp in DefaultStates)
                {
                    manager.UpdateComponentState(kvp.Key, kvp.Value);
                }
            }
        }

        public static void PopulateNetworkMessage(this ShipStateManager manager, SRClient client)
        {
            if (client == null) return;

            client.ShipCondition = manager.CurrentCondition;
            client.ShipComponentStates = new Dictionary<ShipComponent, string>();

            foreach (ShipComponent component in Enum.GetValues(typeof(ShipComponent)))
            {
                client.ShipComponentStates[component] = manager.GetComponentState(component);
            }
        }
    }
}