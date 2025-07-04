using System.Net;
using System.Net.Sockets;

namespace Vanguard.VCS.Server.Network.Models
{
    public class PendingPacket
    {
        public IPEndPoint ReceivedFrom { get; set; }
        public byte[] RawBytes { get; set; }
    }
}