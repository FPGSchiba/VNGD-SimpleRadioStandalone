using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public abstract class AudioProvider
    {
        protected readonly Settings.ProfileSettingsStore globalSettings;

        public AudioProvider()
        {
            globalSettings = Settings.GlobalSettingsStore.Instance.ProfileSettingsStore;
        }


        public byte[] SeperateAudio(byte[] pcmAudio, int radioId)
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
                return CreateStereoMix(pcmAudio);
            }

            var setting = globalSettings.GetClientSetting(settingType);

            if (setting.StringValue == "Left")
            {
                return CreateLeftMix(pcmAudio);
            }
            if (setting.StringValue == "Right")
            {
                return CreateRightMix(pcmAudio);
            }
            return CreateStereoMix(pcmAudio);
        }

        public static byte[] CreateLeftMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length * 2];
            for (var i = 0; i < pcmAudio.Length / 2; i++)
            {
                stereoMix[i * 4] = pcmAudio[i * 2];
                stereoMix[i * 4 + 1] = pcmAudio[i * 2 + 1];

                stereoMix[i * 4 + 2] = 0;
                stereoMix[i * 4 + 3] = 0;
            }
            return stereoMix;
        }

        public static byte[] CreateRightMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length * 2];
            for (var i = 0; i < pcmAudio.Length / 2; i++)
            {
                stereoMix[i * 4] = 0;
                stereoMix[i * 4 + 1] = 0;

                stereoMix[i * 4 + 2] = pcmAudio[i * 2];
                stereoMix[i * 4 + 3] = pcmAudio[i * 2 + 1];
            }
            return stereoMix;
        }

        public static byte[] CreateStereoMix(byte[] pcmAudio)
        {
            var stereoMix = new byte[pcmAudio.Length * 2];
            for (var i = 0; i < pcmAudio.Length / 2; i++)
            {
                short audio = ConversionHelpers.ToShort(pcmAudio[i * 2], pcmAudio[i * 2 + 1]);

                //half audio to keep loudness the same
                if (audio != 0)
                {
                    audio = (short) (audio / 2);
                }

                byte byte1;
                byte byte2;
                ConversionHelpers.FromShort(audio, out byte1, out byte2);

                stereoMix[i * 4] = byte1;
                stereoMix[i * 4 + 1] = byte2;

                stereoMix[i * 4 + 2] = byte1;
                stereoMix[i * 4 + 3] = byte2;
            }
            return stereoMix;
        }
    }
}