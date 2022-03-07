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
        protected TransmissionAssembler _assembler;

        protected AudioRecordingLameWriterBase(int sampleRate)
        {
            _sampleRate = sampleRate;
            _waveFormat = new WaveFormat(sampleRate, 1);
            _assembler = new TransmissionAssembler();
        }

        protected short[] ProcessRadioAudio(ConcurrentQueue<ClientAudio>[] queues, int radio)
        {
            while (queues[radio].Count > 0)
            {
                queues[radio].TryPeek(out ClientAudio firstInQueue);
                int indexPosition = AudioManipulationHelper.CalculateSamplesStart(_lastWrite, firstInQueue.ReceiveTime, _sampleRate);

                if (indexPosition < _sampleRate * 2)
                {
                    queues[radio].TryDequeue(out ClientAudio dequeued);
                    _assembler.AddTransmission(dequeued);
                }
                else
                {
                    break;
                }
            }

            return _assembler.GetAssembledSample();
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