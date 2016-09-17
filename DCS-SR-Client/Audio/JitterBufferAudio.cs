using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public class JitterBufferAudio
    {
        public byte[] Audio { get; set; }

        public uint PacketNumber { get; set; }
    }
}
