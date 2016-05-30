using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class NetworkMessage
    {
        public enum MessageType
        {
         
            PING,
            SYNC,
        }

        public string ClientGuid { get; set; }

        public MessageType MsgType { get; set; }

        public List<SRClient> Clients { get; set; }


    }



}
