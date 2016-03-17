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

        private List<List<ClientAudio>> clientAudioBuffer = new List<List<ClientAudio>>(5);
        private static Logger logger = LogManager.GetCurrentClassLogger();
        private  long  firstPacketTime;

        public DejitterBuffer()
        {
            for(int i =0;i< 5;i++)
            {
                clientAudioBuffer.Add(new List<ClientAudio>());
            }

            firstPacketTime = long.MaxValue;  //stops audio buffer playing
        }

        public void AddAudio(ClientAudio audio)
        {
            if(clientAudioBuffer[0].Count == 0)
            {
                firstPacketTime = audio.ReceiveTime;
                clientAudioBuffer[0].Add(audio);
            }
            else
            {
                //work out which buffer
                var diff = audio.ReceiveTime - firstPacketTime;

                if(diff < 0 || diff > 100)
                {
                    //drop too early or to late
                    //TODO tune the too early? an old packet would knacker the queue
                    logger.Warn("Dropping Packet - Diff: " + diff);
                }
                else
                {
                    int pos = (int)diff / 20;

                    clientAudioBuffer[pos].Add(audio);

                }
               
            }
        }

        internal bool IsReady()
        {
            var diff = GetTickCount64() - firstPacketTime;

            //TODO check this? maybe tune 
            if(diff > 95)
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
            List<byte> mixDownBytes = new List<byte>();

            for (int i = 0; i < 5; i++)
            {
                if(clientAudioBuffer[i].Count() == 1)
                {
                    mixDownBytes.AddRange(clientAudioBuffer[i][0].PCMAudio);
                }
                else
                {
                    //dont need to check for length 0, handled by this for equality
                    for (int j = 0; j < clientAudioBuffer[i].Count() - 1; j++)
                    {
                        short[] speaker1 = ConvertByteArrayToShortArray(clientAudioBuffer[i][j].PCMAudio);
                        short[] speaker2 = ConvertByteArrayToShortArray(clientAudioBuffer[i][j+1].PCMAudio);

                        byte[] mixDownSpeakerBytes = new byte[speaker1.Length * 2];

                        for (int k = 0; k < speaker1.Length; k++)
                        {
                            mixDownBytes.AddRange(BitConverter.GetBytes(MixSpeakers(speaker1[k], speaker2[k])));
                        }
                    }
                }
               
                clientAudioBuffer[i].Clear();
            }

            return mixDownBytes.ToArray();
        }

        //FROM: http://stackoverflow.com/a/25102339
    
        private short MixSpeakers(int speaker1,int speaker2)
        {
        
            int m; // mixed result will go here

            // Make both samples unsigned (0..65535)
            speaker1 += 32768;
            speaker2 += 32768;

            // Pick the equation
            if ((speaker1 < 32768) || (speaker2 < 32768))
            {
                // Viktor's first equation when both sources are "quiet"
                // (i.e. less than middle of the dynamic range)
                m = speaker1 * speaker2 / 32768;
            }
            else {
                // Viktor's second equation when one or both sources are loud
                m = 2 * (speaker1 + speaker2) - (speaker1 * speaker2) / 32768 - 65536;
            }

            // Output is unsigned (0..65536) so convert back to signed (-32768..32767)
            if (m == 65536)
                m = 65535;

            m -= 32768;

            return (short)m;

        }


        short[] ConvertByteArrayToShortArray(byte[] source)
        {
            short[] destination = new short[source.Length / 2];
            Buffer.BlockCopy(source, 0, destination, 0, destination.Length);
            return destination;
        }
    }
}
