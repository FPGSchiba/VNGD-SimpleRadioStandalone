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
        private long firstPacketTime;

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
                firstPacketTime = long.MaxValue;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal byte[] MixDown()
        {
            for (int i = 0; i < 5; i++)
            {
                clientAudioBuffer[i].Clear();
            }

            return new byte[0];
        }
    }
}
