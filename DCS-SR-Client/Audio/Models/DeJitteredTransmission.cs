using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models
{
    //TODO profile if its better as class or struct
    public struct DeJitteredTransmission
    {
        public int ReceivedRadio { get; set; }

        public RadioInformation.Modulation Modulation { get; internal set; }

        public bool Decryptable { get; internal set; }

        public float Volume { get; internal set; }
        public bool IsSecondary { get; set; }

        public double Frequency { get; set; }

        public float[] pcmAudio { get; set; }

        public int count { get; set; }
        public bool NoAudioEffects { get; set; }
    }
}
