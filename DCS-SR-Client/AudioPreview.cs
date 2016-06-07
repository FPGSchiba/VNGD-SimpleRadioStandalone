using Ciribob.DCS.SimpleRadio.Standalone.Client;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class AudioPreview
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        WaveIn _waveIn;
        WaveOut _waveOut;
        BufferedWaveProvider _playBuffer;

        public VolumeWaveProvider16 Volume { get; private set; }

        public void StartPreview(int mic, int speakers)
        {
            try
            {
                 _playBuffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(24000, 16, 1));

                _waveOut = new WaveOut();
                _waveOut.DesiredLatency = 100; //75ms latency in output buffer
                _waveOut.DeviceNumber = speakers;

                Volume = new VolumeWaveProvider16(_playBuffer);
                Volume.Volume = 1.0f; // seems a good max 4.5f 

                _waveOut.Init(Volume);
                _waveOut.Play();

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback());
                _waveIn.BufferMilliseconds = 60;
                _waveIn.DeviceNumber = mic;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(24000, 16, 1);
              
                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error starting audio Quitting!");

                Environment.Exit(1);
            }

        }



        void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {

            _playBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);

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
