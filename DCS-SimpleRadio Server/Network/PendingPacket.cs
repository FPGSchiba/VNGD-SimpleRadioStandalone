using System.Net;
using System.Net.Sockets;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    public class PendingPacket
    {
        public string ReceivedFrom { get; set; }
        public byte[] RawBytes { get; set; }
        public Socket ConnectedClient { get; set; }
    }
}