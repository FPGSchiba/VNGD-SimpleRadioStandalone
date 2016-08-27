using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class RadioAudioProvider
    {
        private readonly Settings _settings;

        public VolumeSampleProvider VolumeSampleProvider { get; }
        public BufferedWaveProvider BufferedWaveProvider { get; }

        private Random _random = new Random();

        public RadioAudioProvider(bool radioFilter)
        {
            BufferedWaveProvider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(AudioManager.SAMPLE_RATE, 2));

            BufferedWaveProvider.DiscardOnBufferOverflow = true;
            BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

            if (radioFilter)
            {
                var filter = new RadioFilter(BufferedWaveProvider.ToSampleProvider());
                VolumeSampleProvider = new VolumeSampleProvider(filter);
            }
            else
            {
                VolumeSampleProvider = new VolumeSampleProvider(BufferedWaveProvider.ToSampleProvider());
            }
            _settings = Settings.Instance;
        }


        public void AddSamples(byte[] floatAudio, bool decrytable, short encryptionKey, int receiveRadio)
        {
            //convert to Stereo Mix
            var settingType = SettingType.Radio1Channel;
            byte[] stereoMix;
            if (receiveRadio == 0)
            {
                settingType = SettingType.IntercomChannel;
            }
            else if (receiveRadio == 1)
            {
                settingType = SettingType.Radio1Channel;
            }
            else if (receiveRadio == 2)
            {
                settingType = SettingType.Radio2Channel;
            }
            else if (receiveRadio == 3)
            {
                settingType = SettingType.Radio3Channel;
            }
            else
            {
                //different radio
                stereoMix = CreateBothMix(floatAudio, decrytable,  encryptionKey);

             

                BufferedWaveProvider.AddSamples((stereoMix), 0, stereoMix.Length);

                return;
            }

            var setting = _settings.UserSettings[(int) settingType];

            if (setting == "Left")
            {
                stereoMix = CreateBothMix(floatAudio, decrytable, encryptionKey);
                ; //CreateLeftMix(floatAudio, decrytable, encryptionKey);
            }
            else if (setting == "Right")
            {
                stereoMix = CreateBothMix(floatAudio, decrytable, encryptionKey);
                ; //CreateRightMix(floatAudio, decrytable, encryptionKey);
            }
            else
            {
                stereoMix = CreateBothMix(floatAudio, decrytable, encryptionKey);
            }

            BufferedWaveProvider.AddSamples((stereoMix), 0, stereoMix.Length);
        }

        private float[] CreateLeftMix(float[] pcmAudio, bool decrytable, short encryptionKey)
        {
            var stereoMix = new float[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length/2; i++)
            {
                stereoMix[i * 2 + 1] = 0;

                if (decrytable || encryptionKey == 0)
                {
                    stereoMix[i * 2 ] = pcmAudio[i];
                }
                else
                {
                    stereoMix[i * 2] = (1.0f - _random.Next(1000) / 1000.0f) * 0.6f;
                }
            }
            return stereoMix;
        }

        private float[] CreateRightMix(float[] pcmAudio, bool decrytable, short encryptionKey)
        {
            var stereoMix = new float[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length/2; i++)
            {
                stereoMix[i*2] = 0;
               
                if (decrytable || encryptionKey == 0)
                {
                    stereoMix[i * 2 + 1] = pcmAudio[i];
                }
                else
                {
                    stereoMix[i * 2 + 1] = (1.0f - _random.Next(1000) / 1000.0f) * 0.6f;
                }

            }
            return stereoMix;
        }

        private float[] CreateBothMix(float[] pcmAudio, bool decrytable, short encryptionKey)
        {
            var stereoMix = new float[pcmAudio.Length*2];
            for (var i = 0; i < pcmAudio.Length; i++)
            {
                if (decrytable || encryptionKey == 0)
                {
                    stereoMix[i*2] = pcmAudio[i];
                    stereoMix[i*2 + 1] = pcmAudio[i];
                }
                else
                {
                    stereoMix[i*2] = (1.0f - _random.Next(1000) / 1000.0f) * 0.6f;
                    stereoMix[i*2 + 1] = (1.0f - _random.Next(1000) / 1000.0f) * 0.6f;
                }

            }
            return stereoMix;
        }

        private byte[] CreateBothMix(byte[] pcmAudio, bool decrytable, short encryptionKey)
        {
            var stereoMix = new byte[pcmAudio.Length * 2];
            for (var i = 0; i < pcmAudio.Length / 2; i++)
            {
                if (decrytable || encryptionKey == 0)
                {
                    stereoMix[i * 4] = pcmAudio[i * 2];
                    stereoMix[i * 4 + 1] = pcmAudio[i * 2 + 1];

                    stereoMix[i * 4 + 2] = pcmAudio[i * 2];
                    stereoMix[i * 4 + 3] = pcmAudio[i * 2 + 1];
                }
                else
                {
                    stereoMix[i * 4] = (byte)_random.Next(16);
                    stereoMix[i * 4 + 1] = (byte)_random.Next(16);

                    stereoMix[i * 4 + 2] = (byte)_random.Next(16);
                    stereoMix[i * 4 + 3] = (byte)_random.Next(16);
                }

            }
            return stereoMix;
        }

        private byte[] ConvertToByteArray(float[] stereoMix)
        {
            byte[] inputBytes = new byte[stereoMix.Length * 2];

            var destWaveBuffer = new WaveBuffer(inputBytes);

            int destOffset = 0;
            for (int sample = 0; sample < stereoMix.Length; sample++)
            {
                // adjust volume
                float sample32 = stereoMix[sample];// * volume;
                                                   // clip
                if (sample32 > 1.0f)
                    sample32 = 1.0f;
                if (sample32 < -1.0f)
                    sample32 = -1.0f;
                destWaveBuffer.ShortBuffer[sample] = (short)(sample32 * 32767);
            }

            return destWaveBuffer;
        }
      
    }
}
