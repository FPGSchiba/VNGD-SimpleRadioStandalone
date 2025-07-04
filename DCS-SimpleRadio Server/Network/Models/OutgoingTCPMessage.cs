using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Vanguard.VCS.Common.Network;

namespace Vanguard.VCS.Server.Network.Models
{
    public class OutgoingTCPMessage
    {
        public NetworkMessage NetworkMessage {
            get;
            set;
        }

        public List<Socket> SocketList { get; set; }

    }
}
