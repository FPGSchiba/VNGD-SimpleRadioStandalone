using Vanguard.VCS.Client.Network.Models;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Network.DCS.Models
{
    public struct CombinedRadioState
    {
        public DCSPlayerRadioInfo RadioInfo;

        public RadioSendingState RadioSendingState;

        public RadioReceivingState[] RadioReceivingState;

        public int ClientCountConnected;

        public int[] TunedClients;
    }
}