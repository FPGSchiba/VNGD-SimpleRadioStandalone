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
            BufferedWaveProvider = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(24000, 16, 2));
            //    
            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

            Pcm16BitToSampleProvider pcm = new Pcm16BitToSampleProvider(BufferedWaveProvider);
            VolumeSampleProvider = new VolumeSampleProvider(pcm);
        }


        public void AddSamples(ClientAudio audio)
        {
            //convert to Stereo Mix
            // if radio 0 - Left channel only
            // if radio 1 - Right channel only
            // if radio 2 - Both channels

            byte[] stereoMix = new byte[audio.PCMAudio.Length *2];
            if(audio.ReceivedRadio == 0)
            {
                for (int i = 0; i < audio.PCMAudio.Length / 2; i++)
                {
                    stereoMix[i * 4] = audio.PCMAudio[i * 2]; ;
                    stereoMix[i * 4 + 1] = audio.PCMAudio[i * 2 +1];

                    stereoMix[i * 4 + 2] = 0;
                    stereoMix[i * 4 + 3] = 0;
                }
            }
            else if (audio.ReceivedRadio == 1)
            {
                for (int i = 0; i < audio.PCMAudio.Length / 2; i++)
                {
                    stereoMix[i * 4] = 0;
                    stereoMix[i * 4 + 1] = 0;

                    stereoMix[i * 4 + 2] = audio.PCMAudio[i * 2];
                    stereoMix[i * 4 + 3] = audio.PCMAudio[i * 2 + 1];
                }
            }
            else
            {

                for(int i =0;i<audio.PCMAudio.Length/2;i++)
                {
                    stereoMix[i*4] = audio.PCMAudio[i*2];
                    stereoMix[i*4 + 1] = audio.PCMAudio[i*2+1];

                    stereoMix[i *4 + 2] = audio.PCMAudio[i * 2];
                    stereoMix[i*4 + 3] = audio.PCMAudio[i * 2 + 1];
                }

            }

            BufferedWaveProvider.AddSamples(stereoMix, 0, stereoMix.Length);
        }
    }
}
