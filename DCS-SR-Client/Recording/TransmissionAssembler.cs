using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class TransmissionAssembler
    {
        private Dictionary<string, short[]> _transmissionPerClient;
        private Dictionary<string, bool> _zeroEndingPcm;
        private short[] _sampleRemainders;

        public TransmissionAssembler()
        {
            _transmissionPerClient = new Dictionary<string, short[]>();
            _sampleRemainders = new short[48000 * 2];
            _zeroEndingPcm = new Dictionary<string, bool>();
        }

        private int FindNextEmpty(int indexPosition, short[] pcmAudio)
        {
            for (int i = indexPosition; i < pcmAudio.Length; i++)
            {
                if (pcmAudio[i] == 0)
                {
                    return i;
                }
            }

            return pcmAudio.Length;
        }

        private short[] SplitRemainder(short[] sample, int indexPosition)
        {
            (short[], short[]) splitArrays = AudioManipulationHelper.SplitSampleByTime(48000 * 2 - indexPosition, sample);
            short[] fullLengthRemainder = new short[48000 * 2];
            splitArrays.Item2.CopyTo(fullLengthRemainder, 0);
            _sampleRemainders = AudioManipulationHelper.MixSamplesClipped(_sampleRemainders, fullLengthRemainder, 48000 * 2);
            _zeroEndingPcm["remainder"] = CheckEndingIsZero(splitArrays.Item2);

            return splitArrays.Item1;
        }

        private bool CheckEndingIsZero(short[] pcmAudio)
        {
            return pcmAudio[pcmAudio.Length - 1] == 0;
        }

        private void ProcessNewPcmSegment(int indexPosition, short[] newPcmAudio, string guid)
        {
            short[] existingPcmAudio = _transmissionPerClient[guid];
            bool zeroEnding = _zeroEndingPcm[guid];

            if (existingPcmAudio[indexPosition] != 0)
            {
                indexPosition = FindNextEmpty(indexPosition, existingPcmAudio);
                indexPosition += !zeroEnding ? 1 : 0;
            }

            if (indexPosition >= 48000 * 2)
            {
                short[] fullLengthRemainder = new short[48000 * 2];
                newPcmAudio.CopyTo(fullLengthRemainder, 0);
                _sampleRemainders = AudioManipulationHelper.MixSamplesClipped(_sampleRemainders, fullLengthRemainder, 48000 * 2);
                _zeroEndingPcm["remainder"] = CheckEndingIsZero(newPcmAudio);
            }
            else if (indexPosition + newPcmAudio.Length >= 48000 * 2)
            {
                short[] split = SplitRemainder(newPcmAudio, indexPosition);
                split.CopyTo(existingPcmAudio, indexPosition);
                _zeroEndingPcm[guid] = CheckEndingIsZero(split);
            }
            else
            {
                newPcmAudio.CopyTo(existingPcmAudio, indexPosition);
                _zeroEndingPcm[guid] = CheckEndingIsZero(newPcmAudio);
            }
        }

        public void AddTransmission(ClientAudio audio, int indexPosition)
        {
            string guid = audio.OriginalClientGuid;
            if (_transmissionPerClient.Count == 0)
            {
                _transmissionPerClient.Add("remainder", _sampleRemainders);
                _sampleRemainders = new short[48000 * 2];
            }

            if (!_transmissionPerClient.ContainsKey(guid))
            {
                _transmissionPerClient.Add(guid, new short[48000 * 2]);
                _zeroEndingPcm[guid] = false;
            }

            ProcessNewPcmSegment(indexPosition, audio.PcmAudioShort, guid);
        }

        public short[] GetFinalSample()
        {
            short[] finalShortArray = new short[48000 * 2];
            foreach (short[] sample in _transmissionPerClient.Values)
            {
                finalShortArray = AudioManipulationHelper.MixSamplesClipped(finalShortArray, sample, 48000 * 2);
            }

            _transmissionPerClient.Clear();
            _zeroEndingPcm.Clear();

            return finalShortArray;
        }
    }
}
