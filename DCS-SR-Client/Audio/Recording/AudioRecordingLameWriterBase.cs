using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal abstract class AudioRecordingLameWriterBase
    {
        protected static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        protected readonly WaveFormat _waveFormat;
        protected readonly int _sampleRate;

        protected AudioRecordingLameWriterBase(int sampleRate)
        {
            _sampleRate = sampleRate;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate,1);
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

        public abstract void ProcessAudio(List<CircularFloatBuffer> perRadioClientAudio);
        public abstract void Start();
        public abstract void Stop();
    }
}