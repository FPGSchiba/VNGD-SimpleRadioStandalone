using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using NAudio.Lame;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class MixDownLameRecordingWriter : AudioRecordingLameWriterBase
    {
        private LameMP3FileWriter _mp3FileWriter;
        private List<short[]> _processedAudio;

        public MixDownLameRecordingWriter(int sampleRate) : base(sampleRate)
        {
        }

        private void OutputToFile(byte[] byteArray)
        {
            try
            {
                if (_mp3FileWriter != null)
                {
                    _mp3FileWriter.Write(byteArray, 0, byteArray.Length);
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

                _processedAudio.Add(shortArray);
            }
            short[] finalShortArray = AudioManipulationHelper.MixSamplesWithHeadroom(_processedAudio, _sampleRate * 2);
            _lastWrite += TimeSpan.TicksPerSecond * 2;
            OutputToFile(ConversionHelpers.ShortArrayToByteArray(finalShortArray));
            _processedAudio.Clear();
        }

        public override void Start()
        {
            string partialFilePath = base.CreateFilePath();

            _lastWrite = DateTime.Now.Ticks;

            string _filePath = $"{partialFilePath}.mp3";

            _mp3FileWriter = new LameMP3FileWriter(_filePath, _waveFormat,
                    (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue));
            _processedAudio = new List<short[]>();
        }

        public override void Stop()
        {
            _mp3FileWriter.Dispose();
            _mp3FileWriter = null;          
        }
    }
}