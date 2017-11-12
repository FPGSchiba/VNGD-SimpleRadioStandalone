using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    public class OutgoingVoice
    {
        public List<Socket> OutgoingEndPoints { get; set; }
        public byte[] ReceivedPacket { get; set; }
    }
}