using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class RadioAudioProvider
    {
        private readonly Settings _settings;

        public RadioAudioProvider()
        {
            BufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(AudioManager.SAMPLE_RATE, 16, 2));

            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

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
                    var stereo = JitterBuffer.CreateStereoMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);

                    return;
                }

                var setting = _settings.UserSettings[(int) settingType];

                if (setting == "Left")
                {
                    var stereo = JitterBuffer.CreateLeftMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);

                }
                else if (setting == "Right")
                {
                    var stereo = JitterBuffer.CreateRightMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);
                }
                else
                {
                    var stereo = JitterBuffer.CreateStereoMix(pcmAudio);
                    BufferedWaveProvider.AddSamples(stereo, 0, stereo.Length);
                }
            }
        }
    }
}