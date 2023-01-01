using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Lame;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class PerRadioLameRecordingWriter : AudioRecordingLameWriterBase
    {
        private readonly Dictionary<int, string> _filePaths;
        private readonly List<LameMP3FileWriter> _mp3FileWriters;

        public PerRadioLameRecordingWriter(int sampleRate) : base(sampleRate)
        {
            _filePaths = new Dictionary<int, string>();
            _mp3FileWriters = new List<LameMP3FileWriter>();
        }

        private void OutputToFile(int radio, float[] floatArray)
        {
            try
            {
                if (_mp3FileWriters[radio] != null)
                {
                    // create a byte array and copy the floats into it...
                    var byteArray = new byte[floatArray.Length * 4];
                    Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);

                    _mp3FileWriters[radio].Write(byteArray, 0, byteArray.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to write audio samples to output file: {ex.Message}");
            }
        }

        public override void ProcessAudio(List<CircularFloatBuffer> perRadioClientAudio)
        {
            if (_mp3FileWriters.Count == 0)
            {
                Start();
            }
            
            for (int i = 0; i < perRadioClientAudio.Count; i++)
            {
                if (perRadioClientAudio[i].Count > 0)
                {
                    float[] floatArrray = new float[perRadioClientAudio[i].Count];

                    perRadioClientAudio[i].Read(floatArrray, 0, floatArrray.Length);
                    OutputToFile(i, floatArrray);
                }
            }
        }

        protected override void Start()
        {
            string partialFilePath = base.CreateFilePath();

            var lamePreset = (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue);
            for (int i = 0; i < 11; i++)
            {
                _filePaths.Add(i, $"{partialFilePath}-Radio{i}.mp3");

                _mp3FileWriters.Add(new LameMP3FileWriter(_filePaths[i], _waveFormat, lamePreset));
            }
        }

        public override void Stop()
        {
            _filePaths.Clear();
            foreach (var writer in _mp3FileWriters)
            {
                writer.Dispose();
            }
        }
    }
}