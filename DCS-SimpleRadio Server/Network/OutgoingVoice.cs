using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI.Network
{
    public class OutgoingVoice
    {
      
        public List<IPEndPoint> OutgoingEndPoints { get; set; }
        public byte[] ReceivedPacket { get; set; }

    }
}
