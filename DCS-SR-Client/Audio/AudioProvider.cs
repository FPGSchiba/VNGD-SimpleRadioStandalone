using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            else
            {
                return AudioProvider.CreateStereoMix(pcmAudio);
            }

            var setting = _settings.UserSettings[(int)settingType];

            if (setting == "Left")
            {
               return AudioProvider.CreateLeftMix(pcmAudio);
            }
            else if (setting == "Right")
            {
                return AudioProvider.CreateRightMix(pcmAudio);
            }
            else
            {
               return AudioProvider.CreateStereoMix(pcmAudio);
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
    }
}
