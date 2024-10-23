using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class ShipStateUpdateHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ShipStateManager _stateManager;
        private readonly string _clientGuid;

        public ShipStateUpdateHandler(ShipStateManager stateManager, string clientGuid)
        {
            _stateManager = stateManager;
            _clientGuid = clientGuid;
        }

        public void HandleStateUpdate(NetworkMessage message)
        {
            if (message?.Client == null)
            {
                Logger.Debug("Received null message or client in ship state update");
                return;
            }

            // Only process updates from other clients
            if (message.Client.ClientGuid == _clientGuid)
            {
                Logger.Trace("Ignoring own ship state update");
                return;
            }

            switch (message.MsgType)
            {
                case NetworkMessage.MessageType.SHIP_STATE_UPDATE:
                    Logger.Debug($"Processing ship state update from client {message.Client.ClientGuid}");
                    ProcessStateUpdate(message.Client);
                    break;

                case NetworkMessage.MessageType.SHIP_STATE_SYNC:
                    Logger.Debug("Processing authoritative ship state sync from server");
                    _stateManager.UpdateFromNetworkMessage(message.Client);
                    break;

                default:
                    Logger.Warn($"Unexpected message type in ship state handler: {message.MsgType}");
                    break;
            }
        }

        private void ProcessStateUpdate(SRClient client)
        {
            try
            {
                // Update condition if different
                if (client.ShipCondition != _stateManager.CurrentCondition)
                {
                    Logger.Debug($"Updating ship condition from {_stateManager.CurrentCondition} to {client.ShipCondition}");
                    _stateManager.SetCondition(client.ShipCondition);
                }

                // Update component states
                if (client.ShipComponentStates != null)
                {
                    foreach (var kvp in client.ShipComponentStates)
                    {
                        var currentState = _stateManager.GetComponentState(kvp.Key);
                        if (currentState != kvp.Value)
                        {
                            Logger.Debug($"Updating component {kvp.Key} state from {currentState} to {kvp.Value}");
                            _stateManager.UpdateComponentState(kvp.Key, kvp.Value);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, "Error processing ship state update");
            }
        }
    }
}