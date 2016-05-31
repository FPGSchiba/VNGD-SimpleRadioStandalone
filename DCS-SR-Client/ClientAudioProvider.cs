using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider
    {
        public VolumeSampleProvider VolumeSampleProvider { get; }
        public BufferedWaveProvider BufferedWaveProvider { get; }
        public long lastUpdate;

        public ClientAudioProvider()
        {
            BufferedWaveProvider = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(24000, 16, 1));
            //    
            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

            Pcm16BitToSampleProvider pcm = new Pcm16BitToSampleProvider(BufferedWaveProvider);
            VolumeSampleProvider = new VolumeSampleProvider(pcm);
        }
    }
}
