using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class RadioSendingState
    {
        public double LastSentAt { get; set; }

        public bool IsSending { get; set; }

        public int SendingOn { get; set; }
    }
}
