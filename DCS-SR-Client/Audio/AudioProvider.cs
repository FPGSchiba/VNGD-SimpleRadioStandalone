using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public abstract class AudioProvider
    {
        protected readonly Settings _settings;

        public AudioProvider()
        {
            _settings = Settings.Instance;
        }


        public byte[] SeperateAudio(byte[] pcmAudio, int radioId)
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
            else if (radioId == 4)
            {
                settingType = SettingType.Radio4Channel;
            }
            else if (radioId == 5)
            {
                settingType = SettingType.Radio5Channel;
            }
            else if (radioId == 6)
            {
                settingType = SettingType.Radio6Channel;
            }
            else if (radioId == 7)
            {
                settingType = SettingType.Radio7Channel;
            }
            else if (radioId == 8)
            {
                settingType = SettingType.Radio8Channel;
            }
            else if (radioId == 9)
            {
                settingType = SettingType.Radio9Channel;
            }
            else if (radioId == 10)
            {
                settingType = SettingType.Radio10Channel;
            }
            else
            {
                return CreateStereoMix(pcmAudio);
            }

            var setting = _settings.UserSettings[(int) settingType];

            if (setting == "Left")
            {
                return CreateLeftMix(pcmAudio);
            }
            if (setting == "Right")
            {
                return CreateRightMix(pcmAudio);
            }
            return CreateStereoMix(pcmAudio);
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
    }
}