using System;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Audio.Models
{
    //TODO profile if its better as class or struct
    public struct DeJitteredTransmission
    {
        public int ReceivedRadio { get; set; }

        public RadioInformation.Modulation Modulation { get; internal set; }

        public bool Decryptable { get; internal set; }
        public short Encryption { get; internal set; }

        public float Volume { get; internal set; }
        public bool IsSecondary { get; set; }

        public double Frequency { get; set; }

        public float[] PCMMonoAudio { get; set; }

        public int PCMAudioLength { get; set; }
        public bool NoAudioEffects { get; set; }

        public Guid Guid { get; set; }

        public Guid OriginalClientGuid { get; set; }

        // Diagnostic: when the original packet was received into the jitter buffer (Utc ticks)
        public long ReceivedAtTicks { get; set; }
    }
}
