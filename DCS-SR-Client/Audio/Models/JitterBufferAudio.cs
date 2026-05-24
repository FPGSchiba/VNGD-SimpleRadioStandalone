using System;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Audio.Models
{
    public class JitterBufferAudio
    {
        public float[] Audio { get; set; }

        public ulong PacketNumber { get; set; }

        public int ReceivedRadio { get; set; }

        public RadioInformation.Modulation Modulation { get; internal set; }

        public float Volume { get; internal set; }
        public bool IsSecondary { get; set; }

        public double Frequency { get; set; }
        public bool NoAudioEffects { get; set; }

        public Guid Guid { get; set; }

        // Timestamp (Utc ticks) when this packet was added to the jitter buffer (diagnostic only)
        public long ReceivedAtTicks { get; set; }
    }
}