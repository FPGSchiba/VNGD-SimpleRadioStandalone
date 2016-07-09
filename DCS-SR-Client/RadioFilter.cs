using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Dsp;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.DSP
{
    public class RadioFilter : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly BiQuadFilter _highPassFilter = BiQuadFilter.HighPassFilter(24000, 520, 0.97f);
        private readonly BiQuadFilter _lowPassFilter = BiQuadFilter.LowPassFilter(24000, 4130, 2.0f);
        private readonly Settings settings;

        public RadioFilter(ISampleProvider sampleProvider)
        {
            source = sampleProvider;

            settings = Settings.Instance;
        }

        public WaveFormat WaveFormat
        {
            get { return source.WaveFormat; }
        }

        public int Read(float[] buffer, int offset, int sampleCount)
        {
            var samplesRead = source.Read(buffer, offset, sampleCount);

            if (settings.UserSettings[(int) SettingType.RADIO_EFFECTS] == "ON")
            {
                for (var n = 0; n < sampleCount; n++)
                {
                    var audio = buffer[offset + n];
                    if (audio != 0)
                        // because we have silence in one channel (if a user picks radio left or right ear) we don't want to transform it or it'll play in both
                    {
                        audio = _highPassFilter.Transform(audio);
                        audio = _lowPassFilter.Transform(audio);
                        buffer[offset + n] = audio;
                    }
                }
            }

            return samplesRead;
        }
    }
}