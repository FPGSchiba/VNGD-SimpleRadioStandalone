using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioReceivingState
    {
        public double LastReceviedAt { get; set; }

        public bool IsReceiving()
        {
            return (Environment.TickCount - LastReceviedAt) < 600;
        }
        public int ReceivedOn { get; set; }
        public bool IsSecondary { get; set; }
    }
}
