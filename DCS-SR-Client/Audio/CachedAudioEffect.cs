using System.IO;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Properties;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class CachedAudioEffect
    {
        public enum AudioEffectTypes
        {
            RADIO_TX = 0,
            RADIO_RX = 1,
            KY_58_TX = 2,
            KY_58_RX = 3
        }

        public CachedAudioEffect(AudioEffectTypes audioEffect)
        {
            AudioEffectType = audioEffect;

            var file = GetFile();

            using (var reader = new WaveFileReader(file))
            {
                //    Assert.AreEqual(16, reader.WaveFormat.BitsPerSample, "Only works with 16 bit audio");
                AudioEffectBytes = new byte[reader.Length];
                var read = reader.Read(AudioEffectBytes, 0, AudioEffectBytes.Length);
            }
        }

        public AudioEffectTypes AudioEffectType { get; }

        public byte[] AudioEffectBytes { get; }

        private UnmanagedMemoryStream GetFile()
        {
            switch (AudioEffectType)
            {
                case AudioEffectTypes.KY_58_RX:
                    return Resources.KY_58_RX_PREAMBLE_SHORT_16_1600;
                case AudioEffectTypes.KY_58_TX:
                    return Resources.KY_58_TX_PREAMBLE_SHORT_16_1600;
                case AudioEffectTypes.RADIO_RX:
                    return Resources.mic_click_on_16_1600;
                case AudioEffectTypes.RADIO_TX:
                    return Resources.mic_click_on_16_1600;
                default:
                    return Resources.mic_click_on_16_1600;
            }
        }
    }
}