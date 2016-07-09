namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudio
    {
        public byte[] PcmAudio { get; set; }
        public string ClientGuid { get; set; }
        public long ReceiveTime { get; set; }
        public int ReceivedRadio { get; set; }
        public double Frequency { get; internal set; }
        public short Modulation { get; internal set; }
        public float Volume { get; internal set; }
    }
}