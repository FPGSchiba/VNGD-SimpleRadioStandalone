using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models
{
    public class TransmittedAudio
    {
        public short[] PcmAudioShort { get; set; }
        public double Frequency { get; internal set; }
        public short Modulation { get; internal set; }
    }
}
