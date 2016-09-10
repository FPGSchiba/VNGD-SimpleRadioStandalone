using System;
using System.Diagnostics;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Dsp;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.DSP
{
    public class RadioFilter : ISampleProvider
    {
        private readonly BiQuadFilter _highPassFilter;
        private readonly BiQuadFilter _lowPassFilter;
        private readonly Settings _settings;
        private readonly ISampleProvider _source;
        private Stopwatch _stopwatch;

        public RadioFilter(ISampleProvider sampleProvider)
        {
            _source = sampleProvider;

            _highPassFilter = BiQuadFilter.HighPassFilter(sampleProvider.WaveFormat.SampleRate, 520, 0.97f);
            _lowPassFilter = BiQuadFilter.LowPassFilter(sampleProvider.WaveFormat.SampleRate, 4130, 2.0f);

            _settings = Settings.Instance;
            _stopwatch= new Stopwatch();
            _stopwatch.Start();
        }

        public WaveFormat WaveFormat
        {
            get { return _source.WaveFormat; }
        }

        public int Read(float[] buffer, int offset, int sampleCount)
        {
            var samplesRead = _source.Read(buffer, offset, sampleCount);

            if (_settings.UserSettings[(int) SettingType.RadioEffects] == "ON" && samplesRead > 0)
            {
                for (var n = 0; n < sampleCount; n++)
                {
                    var audio = buffer[offset + n];
                    if (audio != 0)
                        // because we have silence in one channel (if a user picks radio left or right ear) we don't want to transform it or it'll play in both
                    {
                        audio = _highPassFilter.Transform(audio);

                        if (float.IsNaN(audio))
                            audio = _lowPassFilter.Transform(buffer[offset + n]);
                        else
                            audio = _lowPassFilter.Transform(audio);

                        if (!float.IsNaN(audio))
                        {
                            buffer[offset + n] = audio;
                        }
                    }
                }
            }

           
            Console.WriteLine("Read:"+samplesRead+" Time - " + _stopwatch.ElapsedMilliseconds);
            _stopwatch.Restart();

            return samplesRead;
        }
    }
}