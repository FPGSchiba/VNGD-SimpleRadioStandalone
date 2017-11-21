using System;
using System.Text;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    /**
       * UDP PACKET LAYOUT
       * UInt16 Packet Length - 2 bytes
       * UInt16 AudioPart1 Length - 2 bytes
       * Bytes AudioPart1 - variable bytes
       * Double Frequency Length - 8 bytes
       * byte Modulation Length - 1 byte
       * byte Encryption Length - 1 byte
       * UInt UnitId Length - 4 bytes
       * UInt PacketId Length - 4 bytes
       * Byte[] / Ascii String GUID length - 22
       */

    public class UDPVoicePacket
    {
        public static readonly int GuidLength = 22;

        public static readonly int FixedPacketLength =
            sizeof(ushort)
            + sizeof(ushort)
            + sizeof(double)
            + sizeof(int)
            + sizeof(byte)
            + sizeof(byte)
            + sizeof(uint) 
            + sizeof(uint)
            + GuidLength;

        public ushort AudioPart1Length { get; set; }
        public byte[] AudioPart1Bytes { get; set; }

        public double Frequency { get; set; }
        public byte Modulation { get; set; } //0 - AM, 1 - FM, 2- Intercom, 3 - disabled
        public byte Encryption { get; set; }
        public uint UnitId { get; set; }
        public byte[] GuidBytes { get; set; }
        public string Guid { get; set; }
        public uint PacketNumber { get; set; }

        public ushort PacketLength { get; set; }

        public byte[] EncodePacket()
        {
            //1 * int16 at the start giving the segment
            //
            var combinedLength = AudioPart1Length + 2;
            //calculate first part of packet length + 2 for 1* int16
            var combinedBytes = new byte[combinedLength + FixedPacketLength];

            //calculate full packet length including 2 bytes containing the length
            var packetLength = BitConverter.GetBytes(Convert.ToUInt16(combinedBytes.Length));
            combinedBytes[0] = packetLength[0];
            combinedBytes[1] = packetLength[1];

            var part1Size = BitConverter.GetBytes(Convert.ToUInt16(AudioPart1Bytes.Length));
            combinedBytes[2] = part1Size[0];
            combinedBytes[3] = part1Size[1];

            //copy audio segments after we've added the length header
            Buffer.BlockCopy(AudioPart1Bytes, 0, combinedBytes, 4, AudioPart1Bytes.Length); // copy audio

            var freq = BitConverter.GetBytes(Frequency); //8 bytes

            combinedBytes[combinedLength +2] = freq[0];
            combinedBytes[combinedLength + 1 +2] = freq[1];
            combinedBytes[combinedLength + 2 +2] = freq[2];
            combinedBytes[combinedLength + 3 +2] = freq[3];
            combinedBytes[combinedLength + 4 +2] = freq[4];
            combinedBytes[combinedLength + 5 +2] = freq[5];
            combinedBytes[combinedLength + 6 +2] = freq[6];
            combinedBytes[combinedLength + 7 +2] = freq[7];

            //modulation
            combinedBytes[combinedLength + 8+ 2] = Modulation; //1 byte;

            //encryption
            combinedBytes[combinedLength + 9 +2] = Encryption; //1 byte;

            //unit Id
            var unitId = BitConverter.GetBytes(UnitId); //4 bytes
            combinedBytes[combinedLength + 10+2] = unitId[0];
            combinedBytes[combinedLength + 11+2] = unitId[1];
            combinedBytes[combinedLength + 12+2] = unitId[2];
            combinedBytes[combinedLength + 13+2] = unitId[3];

            //Packet Id
            var packetNumber = BitConverter.GetBytes(PacketNumber); //4 bytes
            combinedBytes[combinedLength + 14+2] = packetNumber[0];
            combinedBytes[combinedLength + 15+2] = packetNumber[1];
            combinedBytes[combinedLength + 16+2] = packetNumber[2];
            combinedBytes[combinedLength + 17+2] = packetNumber[3];

            Buffer.BlockCopy(GuidBytes, 0, combinedBytes, combinedLength + FixedPacketLength - GuidLength, GuidLength);
            // copy short guid

            return combinedBytes;
        }

        public static UDPVoicePacket DecodeVoicePacket(byte[] encodedOpusAudio, bool decode = true)
        {
            //last 22 bytes are guid!
            var recievingGuid = Encoding.ASCII.GetString(
                encodedOpusAudio, encodedOpusAudio.Length - GuidLength, GuidLength);

            var packetLength = BitConverter.ToUInt16(encodedOpusAudio, 0);

            var ecnAudio1 = BitConverter.ToUInt16(encodedOpusAudio, 2);

            byte[] part1 = null;

            if (decode)
            {
                part1 = new byte[ecnAudio1];
                Buffer.BlockCopy(encodedOpusAudio, 4, part1, 0, ecnAudio1);
            }

            var frequency = BitConverter.ToDouble(encodedOpusAudio,
                ecnAudio1 + 2 +2);

            //after frequency and audio
            var modulation = encodedOpusAudio[ecnAudio1 + 2 + 8 +2];

            var encryption = encodedOpusAudio[ecnAudio1 + 2 + 8 + 1 +2];

            var unitId = BitConverter.ToUInt32(encodedOpusAudio, ecnAudio1 + 2 + 8 + 1 + 1 +2);

            var packetNumber = BitConverter.ToUInt32(encodedOpusAudio, ecnAudio1 + 2 + 8 + 1 + 1 + 4 +2);

            return new UDPVoicePacket
            {
                Guid = recievingGuid,
                AudioPart1Bytes = part1,
                AudioPart1Length = ecnAudio1,
                Frequency = frequency,
                UnitId = unitId,
                Encryption = encryption,
                Modulation = modulation,
                PacketNumber = packetNumber,
                PacketLength = packetLength
            };
        }
    }
}