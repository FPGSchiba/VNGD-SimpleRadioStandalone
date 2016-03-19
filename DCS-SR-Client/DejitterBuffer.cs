using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class DejitterBuffer
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        //    private List<List<ClientAudio>> clientAudioBuffer = new List<List<ClientAudio>>(5);
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private long firstPacketTime;

        private Dictionary<string, List<byte>> clientBuffers = new Dictionary<string, List<byte>>();



        private long bufferLength = 100; //in ms
        public DejitterBuffer()
        {
            for (int i = 0; i < 5; i++)
            {
                //      clientAudioBuffer.Add(new List<ClientAudio>());
            }

            firstPacketTime = long.MaxValue;  //stops audio buffer playing
        }



        public void AddAudio(ClientAudio audio)
        {
            if (firstPacketTime > GetTickCount64())
            {

                //      logger.Info("Start");
                firstPacketTime = audio.ReceiveTime;
                clientBuffers.Clear();
                clientBuffers[audio.ClientGUID] = new List<byte>(1920 * 5); //asumes 5 sets worth of 20ms PCM audio
                clientBuffers[audio.ClientGUID].AddRange(audio.PCMAudio);


            }
            else
            {
                //work out which buffer
                var diff = audio.ReceiveTime - firstPacketTime;

                if (diff < 0 || diff > bufferLength)
                {
                    //drop too early or to late
                    //TODO tune the too early? an old packet would knacker the queue
                    logger.Warn("Dropping Packet - Diff: " + diff);
                }
                else
                {
                    if (!clientBuffers.ContainsKey(audio.ClientGUID))
                    {
                        clientBuffers[audio.ClientGUID] = new List<byte>();
                        clientBuffers[audio.ClientGUID].AddRange(audio.PCMAudio);


                    }
                    else
                    {
                        //   logger.Info("adding");
                        clientBuffers[audio.ClientGUID].AddRange(audio.PCMAudio);

                    }

                }

            }
        }

        internal bool IsReady()
        {
            var diff = GetTickCount64() - firstPacketTime;

            //TODO check this? maybe tune 
            if (diff >= bufferLength)
            {
                firstPacketTime = int.MaxValue;
                return true;
            }
            else
            {
                return false;
            }
        }


        internal byte[] MixDown()
        {
     

            firstPacketTime = long.MaxValue;

            int mixDownSize = 0;
            int largestIndex = 0;

            if (clientBuffers.Count() > 1)
            {
                var clientBytesArray = clientBuffers.Values.ToList();


                for (int i =0;i< clientBytesArray.Count();i++)
                {
                    var client = clientBytesArray[i];
                    if (client.Count() > mixDownSize)
                    {
                        mixDownSize = client.Count();
                        largestIndex = i;
                    }
                }

                //    return  MixBytes_16Bit(clientBytesArray, mixDownSize).ToArray();

                //copy the longest element from the array out to mix down to start the process off


                var mixDownByteArray = clientBytesArray[largestIndex].ToArray();

                //removed already merged array from list
                clientBytesArray.RemoveAt(largestIndex);
                try
                {
                    for (int i = 0; i < clientBytesArray.Count(); i++)
                    {
                        var speaker1Bytes = clientBytesArray[i].ToArray();
                        //     var speaker2Bytes = clientBytesArray[i+1].ToArray();

                        var limit = speaker1Bytes.Count();


                        limit = limit / 2;

                        for (int j = 0, offset = 0; j < limit; j++, offset += 2)
                        {
                            var speaker1Short = BitConverter.ToInt16(speaker1Bytes, offset);
                            var speaker2Short = BitConverter.ToInt16(mixDownByteArray, offset);

                            var mixdown = BitConverter.GetBytes(MixSpeakers(speaker1Short, speaker2Short));

                            mixDownByteArray[offset] = mixdown[0];
                            mixDownByteArray[offset + 1] = mixdown[1];

                        }
                    }

                }
                catch (Exception Ex)
                {
                    logger.Warn(Ex,"Error processing audio mixdown ");
                }

                clientBuffers.Clear();
                return mixDownByteArray;


            }
            else if(clientBuffers.Count() == 1)
            {
                clientBuffers.Clear();
                return clientBuffers.Values.First().ToArray();
            }

            return new byte[0];
        }

   

        //FROM: http://stackoverflow.com/a/25102339

        private short MixSpeakers(int speaker1, int speaker2)
        {

           return (short)(speaker1 + speaker2 - ((speaker1 * speaker2) / 65535));

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

            //method 4
            //int m; // mixed result will go here

            //// Make both samples unsigned (0..65535)
            //speaker1 += 32768;
            //speaker2 += 32768;

            //// Pick the equation
            //if ((speaker1 < 32768) || (speaker2 < 32768))
            //{
            //    // Viktor's first equation when both sources are "quiet"
            //    // (i.e. less than middle of the dynamic range)
            //    m = speaker1 * speaker2 / 32768;
            //}
            //else {
            //    // Viktor's second equation when one or both sources are loud
            //    m = 2 * (speaker1 + speaker2) - (speaker1 * speaker2) / 32768 - 65536;
            //}

            //// Output is unsigned (0..65536) so convert back to signed (-32768..32767)
            //if (m == 65536)
            //    m = 65535;

            //m -= 32768;

            //return (short)m;

        }
    }
}
