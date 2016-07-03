using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Dsp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.DSP
{
    public class RadioFilter: NAudio.Wave.ISampleProvider
    {
        private readonly NAudio.Wave.ISampleProvider _sampleProvider;
        public NAudio.Wave.WaveFormat WaveFormat { get; private set; }
        private BiQuadFilter _highPassFilter = BiQuadFilter.HighPassFilter(24000, 520, 0.97f);
        private BiQuadFilter _lowPassFilter = BiQuadFilter.LowPassFilter(24000, 4130, 2.0f);
        private Settings settings;

        public RadioFilter(NAudio.Wave.ISampleProvider sampleProvider)
        {
            this._sampleProvider = sampleProvider;
            settings = Settings.Instance;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            if(settings.UserSettings[(int)SettingType.RADIO_EFFECTS] == "ON")
            {
                for (int i = offset; i < count; i++)
                {
                    buffer[i] = _highPassFilter.Transform(buffer[i]);
                    buffer[i] = _lowPassFilter.Transform(buffer[i]);
                }
            }
         
         
            return this._sampleProvider.Read(buffer, offset, count);
        }
    }
}
