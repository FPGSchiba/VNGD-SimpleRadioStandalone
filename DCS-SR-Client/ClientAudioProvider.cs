using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider
    {
        public long lastUpdate;
        private readonly Settings settings;


        public ClientAudioProvider()
        {
            BufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(24000, 16, 2));
            //    
            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

            var pcm = new Pcm16BitToSampleProvider(BufferedWaveProvider);

            var filter = new RadioFilter(pcm);

            VolumeSampleProvider = new VolumeSampleProvider(filter);

            settings = Settings.Instance;
        }

        public VolumeSampleProvider VolumeSampleProvider { get; }
        public BufferedWaveProvider BufferedWaveProvider { get; }


        public void AddSamples(ClientAudio audio)
        {
            //convert to Stereo Mix
            var settingType = SettingType.RADIO1_CHANNEL;

            if (audio.ReceivedRadio == 0)
            {
                settingType = SettingType.RADIO1_CHANNEL;
            }
            else if (audio.ReceivedRadio == 1)
            {
                settingType = SettingType.RADIO2_CHANNEL;
            }
            else
            {
                settingType = SettingType.RADIO3_CHANNEL;
            }

            var setting = settings.UserSettings[(int) settingType];

            var stereoMix = new byte[0];

            if (setting == "Left")
            {
                stereoMix = createLeftMix(audio);
            }
            else if (setting == "Right")
            {
                stereoMix = createRightMix(audio);
            }
            else
            {
                stereoMix = createBothMix(audio);
            }

            BufferedWaveProvider.AddSamples(stereoMix, 0, stereoMix.Length);
        }

        private byte[] createLeftMix(ClientAudio audio)
        {
            var stereoMix = new byte[audio.PCMAudio.Length*2];
            for (var i = 0; i < audio.PCMAudio.Length/2; i++)
            {
                stereoMix[i*4] = audio.PCMAudio[i*2];
                ;
                stereoMix[i*4 + 1] = audio.PCMAudio[i*2 + 1];

                stereoMix[i*4 + 2] = 0;
                stereoMix[i*4 + 3] = 0;
            }
            return stereoMix;
        }

        private byte[] createRightMix(ClientAudio audio)
        {
            var stereoMix = new byte[audio.PCMAudio.Length*2];
            for (var i = 0; i < audio.PCMAudio.Length/2; i++)
            {
                stereoMix[i*4] = 0;
                stereoMix[i*4 + 1] = 0;

                stereoMix[i*4 + 2] = audio.PCMAudio[i*2];
                stereoMix[i*4 + 3] = audio.PCMAudio[i*2 + 1];
            }
            return stereoMix;
        }

        private byte[] createBothMix(ClientAudio audio)
        {
            var stereoMix = new byte[audio.PCMAudio.Length*2];
            for (var i = 0; i < audio.PCMAudio.Length/2; i++)
            {
                stereoMix[i*4] = audio.PCMAudio[i*2];
                stereoMix[i*4 + 1] = audio.PCMAudio[i*2 + 1];

                stereoMix[i*4 + 2] = audio.PCMAudio[i*2];
                stereoMix[i*4 + 3] = audio.PCMAudio[i*2 + 1];
            }
            return stereoMix;
        }
    }
}