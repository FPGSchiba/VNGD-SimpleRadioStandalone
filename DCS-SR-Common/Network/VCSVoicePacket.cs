using System;
using System.Text;
using NLog;

namespace Vanguard.VCS.Common.Network
{
    /// <summary>
    /// VCS Protocol Packet Types
    /// </summary>
    public enum VcsVoicePacketType : byte
    {
        Voice = 0,
        Hello = 1,
        HelloAck = 2,
        Keepalive = 3,
        Bye = 4
    }

    /// <summary>
    /// VCS Protocol UDP Packet Implementation
    /// 
    /// HEADER LAYOUT (27 bytes):
    /// - Magic: 3 bytes (VCS in ASCII)
    /// - Version/Type: 1 byte (4 bits version, 4 bits type)
    /// - Flags: 1 byte (1 bit PTT, 7 bits reserved)
    /// - Sequence: 3 bytes (24-bit sequence number)
    /// - Frequency: 3 bytes (24-bit kHz integer)
    /// - Session ID: 16 bytes (UUIDv4)
    /// - Payload: variable (Opus frame or control data)
    /// </summary>
    public class VcsVoicePacket
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Constants
        public static readonly int HeaderSize = 27;
        public static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("VCS");
        public static readonly string MagicString = "VCS";

        // Flags
        private const byte PTT_FLAG = 0x01;
        private const byte INTERCOM_FLAG = 0x02;

        // HEADER FIELDS
        public byte[] Magic { get; set; } = new byte[3];
        public byte Version { get; set; } = 1;
        public VcsVoicePacketType Type { get; set; }
        public byte Flags { get; set; }
        public uint Sequence { get; set; } // 24-bit, stored as uint32
        public uint Frequency { get; set; } // 24-bit kHz, stored as uint32
        public Guid ClientId { get; set; }
        public byte[] Payload { get; set; } = new byte[0];

        /// <summary>
        /// Gets or sets the PTT (Push-To-Talk) flag
        /// </summary>
        public bool IsPttActive
        {
            get => (Flags & PTT_FLAG) != 0;
            set
            {
                if (value)
                    Flags |= PTT_FLAG;
                else
                    Flags &= unchecked((byte)~PTT_FLAG);
            }
        }
        
        /// <summary>
        /// Gets or sets the Intercom flag
        /// </summary>
        public bool IsIntercom
        {
            get => (Flags & INTERCOM_FLAG) != 0;
            set
            {
                if (value)
                    Flags |= INTERCOM_FLAG;
                else
                    Flags &= unchecked((byte)~INTERCOM_FLAG);
            }
        }

        /// <summary>
        /// Gets the frequency in MHz as a double
        /// </summary>
        public double FrequencyMHz => Frequency / 1000.0;

        /// <summary>
        /// Sets the frequency from MHz (converts to kHz internally)
        /// </summary>
        /// <param name="freqMHz">Frequency in MHz</param>
        public void SetFrequencyHz(double freqHz)
        {
            Frequency = (uint)(freqHz / 1000.0);
        }

        /// <summary>
        /// Checks if the packet frequency matches a given frequency in MHz
        /// </summary>
        /// <param name="freqMHz">Frequency in MHz to compare</param>
        /// <param name="toleranceKHz">Tolerance in kHz (default 0.5)</param>
        /// <returns>True if frequencies match within tolerance</returns>
        public bool MatchesFrequency(double freqMHz, double toleranceKHz = 0.5)
        {
            var freqKHz = freqMHz * 1000;
            return Math.Abs(Frequency - freqKHz) < toleranceKHz;
        }

        /// <summary>
        /// Checks if the packet frequency exactly matches a given frequency in MHz
        /// </summary>
        /// <param name="freqMHz">Frequency in MHz to compare</param>
        /// <returns>True if frequencies match exactly</returns>
        public bool MatchesFrequencyExact(double freqMHz)
        {
            return Frequency == (uint)(freqMHz * 1000);
        }

        /// <summary>
        /// Encodes the packet to a byte array
        /// </summary>
        /// <returns>Encoded packet as byte array</returns>
        public byte[] EncodePacket()
        {
            try
            {
                var totalLength = HeaderSize + (Payload?.Length ?? 0);
                var packet = new byte[totalLength];

                // Magic (3 bytes)
                Array.Copy(MagicBytes, 0, packet, 0, 3);

                // Version/Type (1 byte)
                packet[3] = (byte)((Version << 4) | (byte)Type);

                // Flags (1 byte)
                packet[4] = Flags;

                // Sequence (3 bytes, big-endian)
                packet[5] = (byte)(Sequence >> 16);
                packet[6] = (byte)(Sequence >> 8);
                packet[7] = (byte)Sequence;

                // Frequency (3 bytes, big-endian)
                packet[8] = (byte)(Frequency >> 16);
                packet[9] = (byte)(Frequency >> 8);
                packet[10] = (byte)Frequency;

                // Session ID (16 bytes, RFC 4122/Golang compatible)
                var sessionBytes = ClientId.ToByteArray();
                if (BitConverter.IsLittleEndian)
                {
                    // Convert .NET Guid to RFC 4122 (big-endian) for Go compatibility
                    // .NET Guid: [uint32][uint16][uint16][8 bytes]
                    // RFC 4122: 4-2-2-8, but first three fields are big-endian
                    byte[] rfcBytes = new byte[16];
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt32(sessionBytes, 0))), 0, rfcBytes, 0, 4);
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt16(sessionBytes, 4))), 0, rfcBytes, 4, 2);
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(BitConverter.ToInt16(sessionBytes, 6))), 0, rfcBytes, 6, 2);
                    Array.Copy(sessionBytes, 8, rfcBytes, 8, 8);
                    sessionBytes = rfcBytes;
                }
                Array.Copy(sessionBytes, 0, packet, 11, 16);
                // Payload (variable)
                if (Payload != null && Payload.Length > 0)
                {
                    Array.Copy(Payload, 0, packet, HeaderSize, Payload.Length);
                }

                return packet;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to encode VCS packet");
                return null;
            }
        }

        /// <summary>
        /// Decodes a byte array into a VCSPacket
        /// </summary>
        /// <param name="data">Raw packet data</param>
        /// <returns>Decoded VCSPacket or null if invalid</returns>
        public static VcsVoicePacket DecodePacket(byte[] data)
        {
            try
            {
                if (data == null || data.Length < HeaderSize)
                {
                    Logger.Warn("Packet too short or null");
                    return null;
                }

                var packet = new VcsVoicePacket();

                // Magic (3 bytes)
                Array.Copy(data, 0, packet.Magic, 0, 3);
                var magicString = Encoding.ASCII.GetString(packet.Magic);
                if (magicString != MagicString)
                {
                    Logger.Warn($"Invalid magic: expected {MagicString}, got {magicString}");
                    return null;
                }

                // Version/Type (1 byte)
                var versionType = data[3];
                packet.Version = (byte)((versionType >> 4) & 0x0F);
                packet.Type = (VcsVoicePacketType)(versionType & 0x0F);

                // Flags (1 byte)
                packet.Flags = data[4];

                // Sequence (3 bytes, big-endian)
                packet.Sequence = ((uint)data[5] << 16) | ((uint)data[6] << 8) | data[7];

                // Frequency (3 bytes, big-endian)
                packet.Frequency = ((uint)data[8] << 16) | ((uint)data[9] << 8) | data[10];

                // Session ID (16 bytes)
                var sessionBytes = new byte[16];
                Array.Copy(data, 11, sessionBytes, 0, 16);
                if (BitConverter.IsLittleEndian)
                {
                    // Convert RFC 4122 (big-endian) to .NET Guid (little-endian)
                    byte[] netBytes = new byte[16];
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt32(sessionBytes, 0))), 0, netBytes, 0, 4);
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt16(sessionBytes, 4))), 0, netBytes, 4, 2);
                    Array.Copy(BitConverter.GetBytes(System.Net.IPAddress.NetworkToHostOrder(BitConverter.ToInt16(sessionBytes, 6))), 0, netBytes, 6, 2);
                    Array.Copy(sessionBytes, 8, netBytes, 8, 8);
                    sessionBytes = netBytes;
                }
                packet.ClientId = new Guid(sessionBytes);

                // Payload (remaining bytes)
                if (data.Length > HeaderSize)
                {
                    packet.Payload = new byte[data.Length - HeaderSize];
                    Array.Copy(data, HeaderSize, packet.Payload, 0, packet.Payload.Length);
                }

                return packet;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to decode VCS packet");
                return null;
            }
        }

        /// <summary>
        /// Creates a HELLO packet with listening frequencies
        /// </summary>
        /// <param name="sessionId">Client session ID</param>
        /// <param name="listeningFrequencies">Array of frequencies in MHz</param>
        /// <param name="sequence">Sequence number</param>
        /// <returns>HELLO packet</returns>
        public static VcsVoicePacket CreateHelloPacket(Guid sessionId, uint sequence = 1)
        {
            return new VcsVoicePacket
            {
                Type = VcsVoicePacketType.Hello,
                ClientId = sessionId,
                Sequence = sequence,
            };
        }

        /// <summary>
        /// Creates a VOICE packet
        /// </summary>
        /// <param name="sessionId">Client session ID</param>
        /// <param name="frequencyHz">Transmission frequency in Hz</param>
        /// <param name="opusFrame">Opus audio frame</param>
        /// <param name="sequence">Sequence number</param>
        /// <param name="pttActive">PTT state</param>
        /// <returns>VOICE packet</returns>
        public static VcsVoicePacket CreateVoicePacket(Guid sessionId, double frequencyHz, byte[] opusFrame, uint sequence, bool pttActive = true)
        {
            var packet = new VcsVoicePacket
            {
                Type = VcsVoicePacketType.Voice,
                ClientId = sessionId,
                Sequence = sequence,
                Payload = opusFrame ?? new byte[0],
                IsPttActive = pttActive,
            };
            packet.SetFrequencyHz(frequencyHz);
            return packet;
        }

        /// <summary>
        /// Creates a BYE packet for graceful disconnection
        /// </summary>
        /// <param name="sessionId">Client session ID</param>
        /// <param name="sequence">Sequence number</param>
        /// <returns>BYE packet</returns>
        public static VcsVoicePacket CreateByePacket(Guid sessionId, uint sequence)
        {
            return new VcsVoicePacket
            {
                Type = VcsVoicePacketType.Bye,
                ClientId = sessionId,
                Sequence = sequence
            };
        }
        
        /// <summary>
        /// Creates a BYE packet for graceful disconnection
        /// </summary>
        /// <param name="sessionId">Client session ID</param>
        /// <param name="sequence">Sequence number</param>
        /// <returns>BYE packet</returns>
        public static VcsVoicePacket CreateKeepalivePacket(Guid sessionId)
        {
            return new VcsVoicePacket
            {
                ClientId = sessionId,
                Type = VcsVoicePacketType.Keepalive,
            };
        }

        /// <summary>
        /// Extracts listening frequencies from a HELLO packet payload
        /// </summary>
        /// <returns>Array of frequencies in MHz</returns>
        public double[] GetListeningFrequencies()
        {
            if (Type != VcsVoicePacketType.Hello || Payload == null || Payload.Length < 4)
                return new double[0];

            try
            {
                var count = BitConverter.ToInt32(Payload, 0);
                if (Payload.Length < 4 + (count * 8))
                    return new double[0];

                var frequencies = new double[count];
                for (int i = 0; i < count; i++)
                {
                    frequencies[i] = BitConverter.ToDouble(Payload, 4 + (i * 8));
                }
                return frequencies;
            }
            catch
            {
                return new double[0];
            }
        }

        public override string ToString()
        {
            return $"VCSPacket{{Magic: {Encoding.ASCII.GetString(Magic)}, Version: {Version}, Type: {Type}, PTT: {IsPttActive}, Seq: {Sequence}, Freq: {FrequencyMHz:F3} MHz, SessionID: {ClientId}, PayloadLen: {Payload?.Length ?? 0}}}";
        }
    }
}
