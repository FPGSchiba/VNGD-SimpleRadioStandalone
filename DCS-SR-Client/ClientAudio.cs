using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudio
    {
        public byte[] PCMAudio { get; set; }
        public string ClientGUID { get; set; }
        public long ReceiveTime { get; set; }

    }
}
