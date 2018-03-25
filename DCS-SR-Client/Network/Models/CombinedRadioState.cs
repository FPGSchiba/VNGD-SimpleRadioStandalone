using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.Models
{
    public struct CombinedRadioState
    {
        public DCSPlayerRadioInfo RadioInfo;

        public RadioSendingState RadioSendingState;

        public RadioReceivingState[] RadioReceivingState;
    }
}