using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class RadioAudioProvider
    {
        protected readonly Settings _settings;

        public RadioAudioProvider(int sampleRate)
        {
            BufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 2));

            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            //   BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 5); //2 seconds buffer

            var pcm = new Pcm16BitToSampleProvider(BufferedWaveProvider);

            VolumeSampleProvider = new VolumeSampleProvider(pcm);

            _settings = Settings.Instance;
        }

        public VolumeSampleProvider VolumeSampleProvider { get; }
        public BufferedWaveProvider BufferedWaveProvider { get; }

        public void AddAudioSamples(byte[] pcmAudio, int radioId, bool isStereo = false)
        {
            if (isStereo)
            {
                BufferedWaveProvider.AddSamples(pcmAudio, 0, pcmAudio.Length);
            }
            else
            {
                var settingType = SettingType.Radio1Channel;

                if (radioId == 0)
                {
                    settingType = SettingType.IntercomChannel;
                }
                else if (radioId == 1)
                {
                    settingType = SettingType.Radio1Channel;
                }
                else if (radioId == 2)
                {
                    settingType = SettingType.Radio2Channel;
                }
                else if (radioId == 3)
                {
                    settingType = SettingType.Radio3Channel;
                }
                else
                {
                    var stereo = CreateStereoMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);

                    return;
                }

                var setting = _settings.UserSettings[(int) settingType];

                if (setting == "Left")
                {
                    var stereo = CreateLeftMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);
                }
                else if (setting == "Right")
                {
                    var stereo = CreateRightMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);
                }
                else
                {
                    var stereo = CreateStereoMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);
                }
            }
        }

        public static byte[] CreateLeftMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length/2; i++)
            {
                stereoMix[i*4] = pcmAudio[i*2];
                stereoMix[i*4 + 1] = pcmAudio[i*2 + 1];

                stereoMix[i*4 + 2] = 0;
                stereoMix[i*4 + 3] = 0;
            }
            return stereoMix;
        }

        public static byte[] CreateRightMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length/2; i++)
            {
                stereoMix[i*4] = 0;
                stereoMix[i*4 + 1] = 0;

                stereoMix[i*4 + 2] = pcmAudio[i*2];
                stereoMix[i*4 + 3] = pcmAudio[i*2 + 1];
            }
            return stereoMix;
        }

        public static byte[] CreateStereoMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length/2; i++)
            {
                stereoMix[i*4] = pcmAudio[i*2];
                stereoMix[i*4 + 1] = pcmAudio[i*2 + 1];

                stereoMix[i*4 + 2] = pcmAudio[i*2];
                stereoMix[i*4 + 3] = pcmAudio[i*2 + 1];
            }
            return stereoMix;
        }

//
    }
}