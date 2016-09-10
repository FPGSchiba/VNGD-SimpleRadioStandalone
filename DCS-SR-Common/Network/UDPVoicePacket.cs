using System;
using System.Text;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    /**
       * UDP PACKET LAYOUT
       * UInt16 AudioPart1 Length - 2 bytes
       * UInt16 AudioPart2 Length - 2 bytes
       * Bytes AudioPart1 - variable bytes
       * Bytes AudioPart2 - variable bytes
       * Double Frequency Length - 8 bytes
       * byte Modulation Length - 1 byte
       * byte Encryption Length - 1 byte
       * UInt UnitId Length - 4 bytes
       * Byte[] / Ascii String GUID length - 22
       */

    public class UDPVoicePacket
    {
        public static readonly int GuidLength = 22;

        public static readonly int FixedPacketLength =
            sizeof(ushort)
            + sizeof(double)
            + sizeof(int)
            + sizeof(byte)
            + sizeof(byte)
            + sizeof(uint) + GuidLength;

        public ushort AudioPart1Length { get; set; }
        public byte[] AudioPart1Bytes { get; set; }

        public double Frequency { get; set; }
        public byte Modulation { get; set; } //0 - AM, 1 - FM, 2- Intercom, 3 - disabled
        public byte Encryption { get; set; }
        public uint UnitId { get; set; }
        public byte[] GuidBytes { get; set; }
        public string Guid { get; set; }


        public byte[] EncodePacket()
        {
            //1 * int16 at the start giving the segment
            //
            var combinedLength = AudioPart1Length + 2;
            //calculate first part of packet length + 4 for 2* int16
            var combinedBytes = new byte[combinedLength + FixedPacketLength];

            var part1Size = BitConverter.GetBytes(Convert.ToUInt16(AudioPart1Bytes.Length));
            combinedBytes[0] = part1Size[0];
            combinedBytes[1] = part1Size[1];

            //copy audio segments after we've added the length header
            Buffer.BlockCopy(AudioPart1Bytes, 0, combinedBytes, 2, AudioPart1Bytes.Length); // copy audio

            var freq = BitConverter.GetBytes(Frequency); //8 bytes

            combinedBytes[combinedLength] = freq[0];
            combinedBytes[combinedLength + 1] = freq[1];
            combinedBytes[combinedLength + 2] = freq[2];
            combinedBytes[combinedLength + 3] = freq[3];
            combinedBytes[combinedLength + 4] = freq[4];
            combinedBytes[combinedLength + 5] = freq[5];
            combinedBytes[combinedLength + 6] = freq[6];
            combinedBytes[combinedLength + 7] = freq[7];

            //modulation
            combinedBytes[combinedLength + 8] = Modulation; //1 byte;

            //encryption
            combinedBytes[combinedLength + 9] = Encryption; //1 byte;

            //unit Id
            var unitId = BitConverter.GetBytes(UnitId); //4 bytes
            combinedBytes[combinedLength + 10] = unitId[0];
            combinedBytes[combinedLength + 11] = unitId[1];
            combinedBytes[combinedLength + 12] = unitId[2];
            combinedBytes[combinedLength + 13] = unitId[3];

            Buffer.BlockCopy(GuidBytes, 0, combinedBytes, combinedLength + FixedPacketLength - GuidLength, GuidLength);
            // copy short guid

            return combinedBytes;
        }

        public static UDPVoicePacket DecodeVoicePacket(byte[] encodedOpusAudio, bool decode = true)
        {
            //last 22 bytes are guid!
            var recievingGuid = Encoding.ASCII.GetString(
                encodedOpusAudio, encodedOpusAudio.Length - GuidLength, GuidLength);

            var ecnAudio1 = BitConverter.ToUInt16(encodedOpusAudio, 0);

            byte[] part1 = null;

            if (decode)
            {
                part1 = new byte[ecnAudio1];
                Buffer.BlockCopy(encodedOpusAudio, 2, part1, 0, ecnAudio1);
            }

            var frequency = BitConverter.ToDouble(encodedOpusAudio,
                ecnAudio1 + 2);

            //after frequency and audio
            var modulation = encodedOpusAudio[ecnAudio1 + 2 + 8];

            var encryption = encodedOpusAudio[ecnAudio1 + 2 + 8 + 1];

            var unitId = BitConverter.ToUInt32(encodedOpusAudio, ecnAudio1 + 2 + 8 + 1 + 1);

            return new UDPVoicePacket
            {
                Guid = recievingGuid,
                AudioPart1Bytes = part1,
                AudioPart1Length = ecnAudio1,
                Frequency = frequency,
                UnitId = unitId,
                Encryption = encryption,
                Modulation = modulation
            };
        }
    }
}