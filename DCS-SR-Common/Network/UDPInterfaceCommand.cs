using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network
{
    public class UDPInterfaceCommand
    {
        public enum UDPCommandType
        {
            FREQUENCY = 0,
            ACTIVE_RADIO = 1,
            TOGGLE_GUARD = 2,
            CHANNEL_UP = 3,
            CHANNEL_DOWN = 4,
            SET_VOLUME = 5,
        }

        public int RadioId { get; set; }
        public double Frequency { get; set; }
        public UDPCommandType Command { get; set; }
        public float Volume { get; set; }
    }
}