using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.DSP
{
    public class RadioFilter: NAudio.Wave.ISampleProvider
    {
        private readonly NAudio.Wave.ISampleProvider source;
        private BiQuadFilter _highPassFilter = BiQuadFilter.HighPassFilter(24000, 520, 0.97f);
        private BiQuadFilter _lowPassFilter = BiQuadFilter.LowPassFilter(24000, 4130, 2.0f);
        private Settings settings;

        public RadioFilter(NAudio.Wave.ISampleProvider sampleProvider)
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
            int samplesRead = source.Read(buffer, offset, sampleCount);

            if(settings.UserSettings[(int)SettingType.RADIO_EFFECTS] == "ON")
            {
                for (int n = 0; n < sampleCount; n++)
                {
                    buffer[offset + n] = _highPassFilter.Transform(buffer[offset + n]);
                    buffer[offset + n] = _lowPassFilter.Transform(buffer[offset + n]);
                }
            }

            return samplesRead;
        }
    }
}
