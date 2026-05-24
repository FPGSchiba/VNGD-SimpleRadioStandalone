using System;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Audio.Models
{
    public class ClientAudio
    {
        public byte[] EncodedAudio { get; set; }
        public float[] PcmAudioFloat { get; set; }
        public Guid ClientGuid { get; set; }
        public long ReceiveTime { get; set; }
        public int ReceivedRadio { get; set; }
        public double Frequency { get; internal set; }
        public short Modulation { get; internal set; }
        public float Volume { get; internal set; }
        public RadioReceivingState RadioReceivingState { get; set; }
        public ulong Sequence { get; set; }
        public bool IsSecondary { get; set; }
        public bool NoAudioEffects { get; set; }
    }
}