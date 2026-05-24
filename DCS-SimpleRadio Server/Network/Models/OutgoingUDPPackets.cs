using System.Collections.Generic;
using System.Net;

namespace Vanguard.VCS.Server.Network.Models
{
    public class OutgoingUDPPackets
    {
        public List<IPEndPoint> OutgoingEndPoints { get; set; }
        public byte[] ReceivedPacket { get; set; }
    }
}