using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Tests
{
    [TestClass()]
    public class UDPVoicePacketTests
    {
        [TestMethod()]
        public void EncodeInitialVoicePacket()
        {
            var udpVoicePacket = new UDPVoicePacket
            {
                GuidBytes = Encoding.ASCII.GetBytes("ufYS_WlLVkmFPjqCgxz6GA"),
                AudioPart1Bytes = new byte[] { 0, 1, 2, 3, 4, 5 },
                AudioPart1Length = (ushort)6,
                Frequencies = new double[] { 100 },
                UnitId = 1,
                Encryptions = new byte[] { 0 },
                Modulations = new byte[] { 4 },
                PacketNumber = 1
            };

            var encodedUdpVoicePacket = udpVoicePacket.EncodePacket();

            Assert.AreEqual(60, udpVoicePacket.PacketLength);
            Assert.AreEqual(60, encodedUdpVoicePacket.Length);

            var expectedEncodedUdpVoicePacket = new byte[60] {
                60, 0, 6, 0, 10, 0, 0, 1, 2, 3,
                4, 5, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 89, 64, 4, 0, 1, 0, 0, 0,
                1, 0, 0, 0, 0, 0, 0, 0, 117, 102,
                89, 83, 95, 87, 108, 76, 86, 107, 109, 70,
                80, 106, 113, 67, 103, 120, 122, 54, 71, 65 };

            CollectionAssert.AreEqual(expectedEncodedUdpVoicePacket, encodedUdpVoicePacket);
        }

        [TestMethod()]
        public void DecodeInitialVoicePacket()
        {
            var encodedUdpVoicePacket = new byte[60] {
                60, 0, 6, 0, 10, 0, 0, 1, 2, 3,
                4, 5, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 89, 64, 4, 0, 1, 0, 0, 0,
                1, 0, 0, 0, 0, 0, 0, 0, 117, 102,
                89, 83, 95, 87, 108, 76, 86, 107, 109, 70,
                80, 106, 113, 67, 103, 120, 122, 54, 71, 65 };

            var udpVoicePacket = UDPVoicePacket.DecodeVoicePacket(encodedUdpVoicePacket);

            Assert.AreEqual("ufYS_WlLVkmFPjqCgxz6GA", udpVoicePacket.Guid);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5 }, udpVoicePacket.AudioPart1Bytes);
            Assert.AreEqual(6, udpVoicePacket.AudioPart1Length);
            CollectionAssert.AreEqual(new double[] { 100 }, udpVoicePacket.Frequencies);
            Assert.AreEqual((uint)1, udpVoicePacket.UnitId);
            CollectionAssert.AreEqual(new byte[] { 0 }, udpVoicePacket.Encryptions);
            CollectionAssert.AreEqual(new byte[] { 4 }, udpVoicePacket.Modulations);
            Assert.AreEqual((ulong)1, udpVoicePacket.PacketNumber);
            Assert.AreEqual((ushort)60, udpVoicePacket.PacketLength);
        }

        [TestMethod()]
        public void EncodeMultipleFrequencyVoicePacket()
        {
            var udpVoicePacket = new UDPVoicePacket
            {
                GuidBytes = Encoding.ASCII.GetBytes("ufYS_WlLVkmFPjqCgxz6GA"),
                AudioPart1Bytes = new byte[] { 0, 1, 2, 3, 4, 5 },
                AudioPart1Length = (ushort)6,
                Frequencies = new double[] { 251000000, 30000000, 251000000 },
                UnitId = 1,
                Encryptions = new byte[] { 0, 0, 1 },
                Modulations = new byte[] { 0, 1, 0 },
                PacketNumber = 1
            };

            var encodedUdpVoicePacket = udpVoicePacket.EncodePacket();

            Assert.AreEqual(80, encodedUdpVoicePacket.Length);

            var expectedEncodedUdpVoicePacket = new byte[80] {
                80, 0, 6, 0, 30, 0, 0, 1, 2, 3,
                4, 5, 0, 0, 0, 0, 0, 0, 0, 128,
                233, 235, 173, 65, 0, 0, 0, 0, 0, 0,
                56, 156, 124, 65, 1, 0, 0, 0, 0, 128,
                233, 235, 173, 65, 0, 1, 1, 0, 0, 0,
                1, 0, 0, 0, 0, 0, 0, 0, 117, 102,
                89, 83, 95, 87, 108, 76, 86, 107, 109, 70,
                80, 106, 113, 67, 103, 120, 122, 54, 71, 65 };

            CollectionAssert.AreEqual(expectedEncodedUdpVoicePacket, encodedUdpVoicePacket);
        }

        [TestMethod()]
        public void DecodeMultipleFrequencyVoicePacket()
        {
            var encodedUdpVoicePacket = new byte[80] {
                80, 0, 6, 0, 30, 0, 0, 1, 2, 3,
                4, 5, 0, 0, 0, 0, 0, 0, 0, 128,
                233, 235, 173, 65, 0, 0, 0, 0, 0, 0,
                56, 156, 124, 65, 1, 0, 0, 0, 0, 128,
                233, 235, 173, 65, 0, 1, 1, 0, 0, 0,
                1, 0, 0, 0, 0, 0, 0, 0, 117, 102,
                89, 83, 95, 87, 108, 76, 86, 107, 109, 70,
                80, 106, 113, 67, 103, 120, 122, 54, 71, 65 };

            var udpVoicePacket = UDPVoicePacket.DecodeVoicePacket(encodedUdpVoicePacket);

            Assert.AreEqual("ufYS_WlLVkmFPjqCgxz6GA", udpVoicePacket.Guid);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5 }, udpVoicePacket.AudioPart1Bytes);
            Assert.AreEqual(6, udpVoicePacket.AudioPart1Length);
            CollectionAssert.AreEqual(new double[] { 251000000, 30000000, 251000000 }, udpVoicePacket.Frequencies);
            Assert.AreEqual((uint)1, udpVoicePacket.UnitId);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 1 }, udpVoicePacket.Encryptions);
            CollectionAssert.AreEqual(new byte[] { 0, 1, 0 }, udpVoicePacket.Modulations);
            Assert.AreEqual((ulong)1, udpVoicePacket.PacketNumber);
            Assert.AreEqual((ushort)80, udpVoicePacket.PacketLength);
        }
    }
}