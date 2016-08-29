using System;
using System.Collections.Generic;
using System.Linq;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public class JitterBuffer
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly Settings _settings;

        private readonly Dictionary<string, List<ClientAudio>>[] _clientRadioBuffers = new Dictionary<string, List<
            ClientAudio>>[4];

        private Random _random = new Random();

        private Object _lock = new Object();

        public JitterBuffer()
        {
            for (var i = 0; i < _clientRadioBuffers.Length; i++)
            {
                _clientRadioBuffers[i] = new Dictionary<string, List<ClientAudio>>();
            }

            _settings = Settings.Instance;
        }

        public void AddAudio(ClientAudio audio)
        {
            lock (_lock)
            {
                var radioBuffer = _clientRadioBuffers[audio.ReceivedRadio];

                if (!radioBuffer.ContainsKey(audio.ClientGuid))
                {
                    radioBuffer[audio.ClientGuid] = new List<ClientAudio> { audio };
                }
                else
                {
                    //   logger.Info("adding");
                    radioBuffer[audio.ClientGuid].Add(audio);
                }
            }
        }


        public byte[][] MixDown()
        {
            long start = Environment.TickCount;
            lock (_lock)
            {
                byte[][] mixDownBytes = new byte[_clientRadioBuffers.Length][];

                for (var i = 0; i < _clientRadioBuffers.Length; i++)
                {
                    //TODO create stereo mix
                    mixDownBytes[i] = ConversionHelpers.ShortArrayToByteArray(RadioMixDown(_clientRadioBuffers[i]));

                    var settingType = SettingType.Radio1Channel;

                    if (i == 0)
                    {
                        settingType = SettingType.IntercomChannel;
                    }
                    else if (i == 1)
                    {
                        settingType = SettingType.Radio1Channel;
                    }
                    else if (i == 2)
                    {
                        settingType = SettingType.Radio2Channel;
                    }
                    else if (i == 3)
                    {
                        settingType = SettingType.Radio3Channel;
                    }
                    else
                    {
                        //different radio
                        mixDownBytes[i] = CreateBothMix(mixDownBytes[i]);

                        continue; //back to the top
                    }

                    var setting = _settings.UserSettings[(int) settingType];

                    if (setting == "Left")
                    {
                        mixDownBytes[i] = CreateLeftMix(mixDownBytes[i]);
                    }
                    else if (setting == "Right")
                    {
                        mixDownBytes[i] = CreateRightMix(mixDownBytes[i]);
                    }
                    else
                    {
                        mixDownBytes[i] = CreateBothMix(mixDownBytes[i]);
                    }
                }

                Clear();

                Logger.Debug("Mixdown took: "+(Environment.TickCount - start));

                return mixDownBytes;
            }
        }

        private short[] RadioMixDown(Dictionary<string, List<ClientAudio>> radioData)
        {
            //now process all the clientAudio and add to the final mixdown

            //init mixdown with the longest audio from a client
            // this means the mixing works properly rather than mixing
            //with silence initially which will cause issues
            var mixDown = InitRadioMixDown(radioData);

            foreach (var clientAudioList in radioData.Values)
            {
                //perclient audio
                foreach (var clientAudio in clientAudioList)
                {
                    var clientAudioBytesArray = clientAudio.PcmAudioShort;
                    var decrytable = (clientAudio.Decryptable) || clientAudio.Encryption == 0;

                    for (var i = 0; i < clientAudioBytesArray.Count(); i++)
                    {
                        short speaker1Short = 0;
                        if (decrytable)
                        {
                            speaker1Short = clientAudioBytesArray[i];
                        }
                        else
                        {
                            speaker1Short = RandomShort();
                        }

                        var speaker2Short = mixDown[i];


                        mixDown[i] = MixSpeakers(speaker1Short, speaker2Short);
                    }
                }
            }


            return mixDown;
        }

        private short[] InitRadioMixDown(Dictionary<string, List<ClientAudio>> radioData)
        {
            //TODO this return the longest client array and be used to init the MixDown array with data

            string longestGuid = null;
            int longestCount = 0;


            foreach (var clientAudioList in radioData.Values)
            {
                //perclient audio
                int clientCount = 0;
                string clientGuid = null;
                foreach (var clientAudio in clientAudioList)
                {
                    clientCount += clientAudio.PcmAudioShort.Length;

                    if (clientGuid == null)
                        clientGuid = clientAudio.ClientGuid;
                }

                if (clientCount > longestCount)
                {
                    longestCount = clientCount;
                    longestGuid = clientGuid;
                }
            }

            if (longestGuid != null)
            {
                var clientAudio = radioData[longestGuid];

                var mixDownInit = new List<short>();

                bool decryptable = true;
                foreach (var audio in clientAudio)
                {
                    decryptable = (audio.Decryptable) || audio.Encryption == 0;
                    mixDownInit.AddRange(audio.PcmAudioShort);
                }

                //remove now we've processed it
                radioData.Remove(longestGuid);

                var initArray = mixDownInit.ToArray();

                //now randomise if not decrytable
                if (!decryptable)
                {
                    for (int i = 0; i < initArray.Length; i++)
                    {
                        initArray[i] = RandomShort();
                    }
                }

                return initArray;
            }
            else
            {
                return new short[0];
            }
        }


        private short MixSpeakers(int speaker1, int speaker2)
        {
            //method 1
            //  return (short) (speaker1 + speaker2 - speaker1 * speaker2 / 65535);

            //method 2
            //int tmp = speaker1 + speaker2;
            //return (short)(tmp / 2);


            //method 3
            //float samplef1 = speaker1 / 32768.0f;
            //float samplef2 = speaker2 / 32768.0f;
            //float mixed = samplef1 + samplef2;
            //// reduce the volume a bit:
            //mixed *= 0.8f;
            //// hard clipping
            //if (mixed > 1.0f) mixed = 1.0f;
            //if (mixed < -1.0f) mixed = -1.0f;
            //short outputSample = (short)(mixed * 32768.0f);
            //return outputSample;

            //method 4 Vicktor Toth - http://www.vttoth.com/CMS/index.php/technical-notes/68
            int m; // mixed result will go here

            // Make both samples unsigned (0..65535)
            speaker1 += 32768;
            speaker2 += 32768;

            // Pick the equation
            if ((speaker1 < 32768) || (speaker2 < 32768))
            {
                // Viktor's first equation when both sources are "quiet"
                // (i.e. less than middle of the dynamic range)
                m = speaker1*speaker2/32768;
            }
            else
            {
                // Viktor's second equation when one or both sources are loud
                m = 2*(speaker1 + speaker2) - (speaker1*speaker2)/32768 - 65536;
            }

            // Output is unsigned (0..65536) so convert back to signed (-32768..32767)
            if (m == 65536)
            {
                m = 65535;
            }

            m -= 32768;

            return (short) m;
        }

        private short RandomShort()
        {
            //random short at max volume at eights
            return (short) _random.Next(-32768/8, 32768/8);
        }

        private byte[] CreateLeftMix(byte[] pcmAudio)
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

        private byte[] CreateRightMix(byte[] pcmAudio)
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

        private byte[] CreateBothMix(byte[] pcmAudio)
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

        public void Clear()
        {
            //clear buffer
            lock (_lock)
            {
                foreach (var perRadio in _clientRadioBuffers)
                {
                    perRadio.Clear();
                }
            }
        }
    }
}