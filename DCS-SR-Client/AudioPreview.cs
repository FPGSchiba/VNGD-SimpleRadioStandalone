using Ciribob.DCS.SimpleRadio.Standalone.Client;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class AudioPreview
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        WaveIn _waveIn;
        WaveOut _waveOut;
        BufferedWaveProvider _playBuffer;

        public SampleChannel Volume { get; private set; }

        public void StartPreview(int mic, int speakers)
        {
            try
            {
                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback());
                _waveIn.BufferMilliseconds = 60;
                _waveIn.DeviceNumber = mic;
                _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(24000, 16, 1);

                var pcm = new WaveInProvider(_waveIn);

                Volume = new SampleChannel(pcm);

                var filter = new RadioFilter(Volume);

                _waveOut = new WaveOut();
                _waveOut.DesiredLatency = 100; //75ms latency in output buffer
                _waveOut.DeviceNumber = speakers;

                _waveOut.Init(filter);

                _waveIn.StartRecording();
                _waveOut.Play();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error starting audio Quitting!");

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