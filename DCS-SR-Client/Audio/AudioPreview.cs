using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    internal class AudioPreview
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private BufferedWaveProvider _playBuffer;
        private WaveIn _waveIn;
        private WaveOut _waveOut;

        public SampleChannel Volume { get; private set; }


        public void StartPreview(int mic, int speakers)
        {
            try
            {
                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback())
                {
                    BufferMilliseconds = 20, // buffer of 20ms gives data every 40ms - double latency gives tick rate
                    DeviceNumber = mic,
                    WaveFormat = new WaveFormat(8000, 16, 1)
                };

                var pcm = new TestWaveInProvider(_waveIn);

                Volume = new SampleChannel(pcm);

                var filter = new RadioFilter(Volume);

                var downsample = new WdlResamplingSampleProvider(filter, 44100);

                _waveOut = new WaveOut
                {
                    DesiredLatency = 80, // buffer of 40ms gives data every 40ms - so half latency gives tick rate
                    DeviceNumber = speakers
                };
              

                _waveOut.Init(downsample);

                _waveIn.StartRecording();
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Quitting!");

                Environment.Exit(1);
            }
        }

        public void StopEncoding()
        {
            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_playBuffer != null)
            {
                _playBuffer.ClearBuffer();
                _playBuffer = null;
            }
        }
    }
}