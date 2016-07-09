using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class NetworkMessage
    {
        public enum MessageType
        {
            UPDATE,
            PING,
            SYNC
        }

        public SRClient Client { get; set; }

        public MessageType MsgType { get; set; }

        public List<SRClient> Clients { get; set; }
    }
}