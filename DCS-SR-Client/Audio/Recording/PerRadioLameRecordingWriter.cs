using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Lame;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class PerRadioLameRecordingWriter : AudioRecordingLameWriterBase
    {
        private readonly Dictionary<int, string> _filePaths;
        private readonly LameMP3FileWriter[] _mp3FileWriters;

        public PerRadioLameRecordingWriter(int sampleRate) : base(sampleRate)
        {
            _filePaths = new Dictionary<int, string>();
            _mp3FileWriters = new LameMP3FileWriter[11];
        }

        private void OutputToFile(int radio, byte[] byteArray)
        {
            try
            {
                if (_mp3FileWriters[radio] != null)
                {
                    _mp3FileWriters[radio].Write(byteArray, 0, byteArray.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to write audio samples to output file: {ex.Message}");
            }
        }

        public override void ProcessAudio(ConcurrentQueue<ClientAudio>[] queues)
        {
            for (int i = 0; i < 11; i++)
            {
                short[] shortArray = base.ProcessRadioAudio(queues, i);

                byte[] byteArray = ConversionHelpers.ShortArrayToByteArray(shortArray);
                OutputToFile(i, byteArray);
            }
            _lastWrite += TimeSpan.TicksPerSecond * 2;
        }

        public override void Start()
        {
            string partialFilePath = base.CreateFilePath();

            _lastWrite = DateTime.Now.Ticks;
            var lamePreset = (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue);
            for (int i = 0; i < 11; i++)
            {
                _filePaths.Add(i, $"{partialFilePath}-Radio{i}.mp3");

                _mp3FileWriters[i] = new LameMP3FileWriter(_filePaths[i], _waveFormat, lamePreset);
            }
        }

        public override void Stop()
        {
            _filePaths.Clear();
            for (int i = 0; i < 11; i++)
            {
                _mp3FileWriters[i].Dispose();
                _mp3FileWriters[i] = null;
            }
        }
    }
}