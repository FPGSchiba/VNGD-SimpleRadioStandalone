using System;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudio
    {
        public short[] PcmAudioShort { get; set; }
        public string ClientGuid { get; set; }
        public long ReceiveTime { get; set; }
        public int ReceivedRadio { get; set; }
        public double Frequency { get; internal set; }
        public short Modulation { get; internal set; }
        public float Volume { get; internal set; }
        public UInt32 UnitId { get;  set; }
        public short Encryption { get; internal set; }
        public bool Decryptable { get; internal set; }
        public RadioReceivingState RadioReceivingState { get; set; }


        //From NAUDIO but modded

        public static float[] ConvertPCMtoFloats(byte[] sourceBuffer)
        {
            float[] buffer = new float[sourceBuffer.Length/2];

            int newIndex = 0;
            int startIndex = 0;
            while (startIndex < sourceBuffer.Length)
            {
                buffer[newIndex++] = (float)BitConverter.ToInt16(sourceBuffer, startIndex) / 32768f;
                startIndex += 2;
            }
            return buffer;
        }
    }
}