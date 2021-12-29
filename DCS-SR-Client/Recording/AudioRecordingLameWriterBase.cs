using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal abstract class AudioRecordingLameWriterBase
    {
        protected static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected readonly WaveFormat _waveFormat;
        protected readonly int _sampleRate;

        protected long _lastWrite;
        protected Dictionary<int, short[]> _sampleRemainders;

        protected AudioRecordingLameWriterBase(int sampleRate)
        {
            _sampleRate = sampleRate;
            _waveFormat = new WaveFormat(sampleRate, 1);
            _sampleRemainders = new Dictionary<int, short[]>();
        }

        protected short[] SplitRemainder(int radio, short[] sample, int indexPosition)
        {
            (short[], short[]) splitArrays = AudioManipulationHelper.SplitSampleByTime(_sampleRate * 2 - indexPosition, sample);
            _sampleRemainders.Add(radio, splitArrays.Item2);

            return splitArrays.Item1;
        }

        protected short[] ProcessRadioAudio(ConcurrentQueue<ClientAudio>[] queues, int radio)
        {
            short[] shortArray = new short[_sampleRate * 2];

            if (_sampleRemainders.TryGetValue(radio, out short[] remainder))
            {
                remainder.CopyTo(shortArray, 0);
                _sampleRemainders.Remove(radio);
            }

            while (queues[radio].Count > 0)
            {
                queues[radio].TryPeek(out ClientAudio firstInQueue);
                int indexPosition = AudioManipulationHelper.CalculateSamplesStart(_lastWrite, firstInQueue.ReceiveTime, _sampleRate);
                // Prevent audio sample exceeding two second time
                if (indexPosition + firstInQueue.PcmAudioShort.Length < _sampleRate * 2)
                {
                    queues[radio].TryDequeue(out ClientAudio dequeued);
                    var mixedDown = AudioManipulationHelper.MixSamples(shortArray, dequeued.PcmAudioShort, indexPosition);
                    mixedDown.CopyTo(shortArray, indexPosition);
                }

                else if (indexPosition < _sampleRate * 2)
                {
                    queues[radio].TryDequeue(out ClientAudio dequeued);
                    short[] finalAudio = SplitRemainder(radio, dequeued.PcmAudioShort, indexPosition);
                    var mixedDown = AudioManipulationHelper.MixSamples(shortArray, finalAudio, indexPosition);
                    mixedDown.CopyTo(shortArray, indexPosition);
                    break;
                }
                else
                {
                    break;
                }
            }

            return shortArray;
        }

        protected string CreateFilePath()
        {
            if (!Directory.Exists("Recordings"))
            {
                _logger.Info("Recordings directory missing, creating Directory");
                Directory.CreateDirectory("Recordings");
            }

            string sanitisedDate = String.Join("-", DateTime.Now.ToShortDateString().Split(Path.GetInvalidFileNameChars()));
            string sanitisedTime = String.Join("-", DateTime.Now.ToLongTimeString().Split(Path.GetInvalidFileNameChars()));
            return $"Recordings\\{sanitisedDate}-{sanitisedTime}";
        }

        public abstract void ProcessAudio(ConcurrentQueue<ClientAudio>[] queues);
        public abstract void Start();
        public abstract void Stop();
    }
}