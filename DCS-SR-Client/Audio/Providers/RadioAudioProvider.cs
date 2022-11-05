using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class RadioAudioProvider : AudioProvider
    {
        private readonly Settings.ProfileSettingsStore profileSettings = Settings.GlobalSettingsStore.Instance.ProfileSettingsStore;
        public RadioAudioProvider(int sampleRate)
        {
            BufferedWaveProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(sampleRate,2));
            BufferedWaveProvider.ReadFully = false;
            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            VolumeSampleProvider = new VolumeSampleProvider(BufferedWaveProvider.ToSampleProvider());
        }

        public VolumeSampleProvider VolumeSampleProvider { get; }
        private BufferedWaveProvider BufferedWaveProvider { get; }

        public void AddAudioSamples(float[] pcm32Audio, int radioId)
        {
            if(pcm32Audio== null)
                return;
            // create a byte array and copy the floats into it...
           
            var seperatedAudio = SeparateAudio(pcm32Audio, radioId, 0,new float[pcm32Audio.Length*2],0,radioId);

            var byteArray = new byte[seperatedAudio.Length * 4];
            Buffer.BlockCopy(seperatedAudio, 0, byteArray, 0, byteArray.Length);
            BufferedWaveProvider.AddSamples(byteArray, 0, byteArray.Length);
        }


        public float[] SeparateAudio(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat, int dstOffset, int radioId)
        {
            var settingType = ProfileSettingsKeys.Radio1Channel;

            if (radioId == 0)
            {
                settingType = ProfileSettingsKeys.IntercomChannel;
            }
            else if (radioId == 1)
            {
                settingType = ProfileSettingsKeys.Radio1Channel;
            }
            else if (radioId == 2)
            {
                settingType = ProfileSettingsKeys.Radio2Channel;
            }
            else if (radioId == 3)
            {
                settingType = ProfileSettingsKeys.Radio3Channel;
            }
            else if (radioId == 4)
            {
                settingType = ProfileSettingsKeys.Radio4Channel;
            }
            else if (radioId == 5)
            {
                settingType = ProfileSettingsKeys.Radio5Channel;
            }
            else if (radioId == 6)
            {
                settingType = ProfileSettingsKeys.Radio6Channel;
            }
            else if (radioId == 7)
            {
                settingType = ProfileSettingsKeys.Radio7Channel;
            }
            else if (radioId == 8)
            {
                settingType = ProfileSettingsKeys.Radio8Channel;
            }
            else if (radioId == 9)
            {
                settingType = ProfileSettingsKeys.Radio9Channel;
            }
            else if (radioId == 10)
            {
                settingType = ProfileSettingsKeys.Radio10Channel;
            }
            else
            {
                return CreateBalancedMix(srcFloat, srcCount, srcOffset, dstFloat, dstOffset, 0);
            }

            float balance = 0;
            try
            {
                //TODO cache this
                balance = profileSettings.GetClientSettingFloat(settingType);
            }
            catch (Exception)
            {
                //ignore
            }

            return CreateBalancedMix(srcFloat, srcCount, srcOffset, dstFloat, dstOffset, balance);
        }

        public static float[] CreateBalancedMix(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat, int dstOffset, float balance)
        {
            float left = (1.0f - balance) / 2.0f;
            float right = 1.0f - left;

            //temp set of mono floats
            int monoBufferPosition = 0;
            for (int i = 0; i < srcCount * 2; i += 2)
            {
                dstFloat[i + dstOffset] = srcFloat[monoBufferPosition + srcOffset] * left;
                dstFloat[i + dstOffset + 1] = srcFloat[monoBufferPosition + srcOffset] * right;
                monoBufferPosition++;
            }

            return dstFloat;
        }
    }
}