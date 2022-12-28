using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class ClientTransmissionBuffer
    {
        private List<LinkedList<ClientAudio>> _clientAudioSamples;
        private long lastAccess;
        readonly int _sampleRate = 48000;

        public ClientTransmissionBuffer()
        {
            _clientAudioSamples = new List<LinkedList<ClientAudio>>();
        }

        public void AddSample(ClientAudio clientAudio)
        {
            if (clientAudio.ReceiveTime > lastAccess + TimeSpan.FromMilliseconds(400).Ticks || _clientAudioSamples.Count == 0)
            {
                _clientAudioSamples.Add(new LinkedList<ClientAudio>());
                _clientAudioSamples[_clientAudioSamples.Count - 1].AddFirst(clientAudio);
            }
            else
            {
                // TODO: Duplicate of logic in JitterBufferProviderInterface
                LinkedList<ClientAudio> currentLinkedList = _clientAudioSamples[_clientAudioSamples.Count - 1];
                for (var it = currentLinkedList.First; it != null;)
                {
                    var next = it.Next;

                    if (it.Value.PacketNumber == clientAudio.PacketNumber)
                    {
                        return;
                    }

                    if (clientAudio.PacketNumber < it.Value.PacketNumber)
                    {
                        currentLinkedList.AddBefore(it, clientAudio);
                        return;
                    }

                    if ((clientAudio.PacketNumber > it.Value.PacketNumber) &&
                        ((next == null) || (clientAudio.PacketNumber < next.Value.PacketNumber)))
                    {
                        currentLinkedList.AddAfter(it, clientAudio);
                        return;
                    }

                    it = next;
                }
            }
        }

        public short[] OutputPCM()
        {
            List<short[]> assembledOut = new List<short[]>();

            foreach(var transmission in _clientAudioSamples)
            {
                if (lastAccess > 0)
                {
                    long timeBetween = (transmission.First.Value.ReceiveTime - lastAccess);

                    // Discard multiple intervals of silence
                    if (timeBetween > TimeSpan.TicksPerSecond * 2)
                    {
                        timeBetween = timeBetween % (TimeSpan.TicksPerSecond * 2);
                    }
                    // assume all gaps smaller than 45ms aren't actually gaps, is this necessary?
                    if (timeBetween / TimeSpan.TicksPerMillisecond > 45)
                    {
                        //Console.WriteLine($"Big Gap - {DateTime.Now.Second}");
                        assembledOut.Add(new short[(timeBetween / TimeSpan.TicksPerSecond) * _sampleRate]);
                    }
                }

                //TODO FIX
           //     lastAccess = transmission.Last.Value.ReceiveTime + (transmission.Last.Value.PcmAudioShort.LongLength / _sampleRate) * TimeSpan.TicksPerSecond;

              //  var fulltransmission = transmission.SelectMany(x => x.PcmAudioShort).ToArray();
                //assembledOut.Add(fulltransmission);
            }

            _clientAudioSamples.Clear();
            short[] completeOutput = assembledOut.SelectMany(x => x).ToArray();

            return completeOutput;
        }
    }
}
