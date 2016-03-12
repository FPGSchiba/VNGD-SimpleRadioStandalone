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
            RADIO_UPDATE,
            PING,
            SYNC,
        }

        public string ClientGuid { get; set; }

        public MessageType MsgType { get; set; }

        public DCSRadios ClientRadioUpdate { get; set; }

        public List<SRClient> Clients { get; set; }


    }



}
