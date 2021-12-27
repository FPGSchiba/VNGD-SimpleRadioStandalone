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

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models
{
    internal class PerRadioRecordingWriter : IAudioRecordingWriter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly WaveFormat _waveFormat;

        private readonly Dictionary<int, string> _filePaths;
        private readonly LameMP3FileWriter[] _mp3FileWriters;
        private readonly int _sampleRate;

        private Dictionary<int, short[]> _sampleRemainders;
        private long _lastWrite;

        public PerRadioRecordingWriter(int sampleRate)
        {
            _sampleRate = sampleRate;
            _sampleRemainders = new Dictionary<int, short[]>();
            _waveFormat = new WaveFormat(sampleRate, 1);
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

        public void ProcessAudio(ConcurrentQueue<ClientAudio>[] queues)
        {
            for (int i = 0; i < 11; i++)
            {
                short[] shortArray = new short[_sampleRate * 2];

                if (_sampleRemainders.ContainsKey(i))
                {
                    bool hasRemainder = _sampleRemainders.TryGetValue(i, out short[] remainder);

                    if (hasRemainder)
                    {
                        remainder.CopyTo(shortArray, 0);
                    }

                    _sampleRemainders.Remove(i);
                }

                while (queues[i].Count > 0)
                {
                    queues[i].TryPeek(out ClientAudio firstInQueue);
                    int indexPosition = AudioManipulationHelper.CalculateSamplesStart(_lastWrite, firstInQueue.ReceiveTime, _sampleRate);
                    // Prevent audio sample exceeding two second time
                    if (indexPosition + firstInQueue.PcmAudioShort.Length < _sampleRate * 2 )
                    {
                        queues[i].TryDequeue(out ClientAudio dequeued);

                        var mixedDown = AudioManipulationHelper.MixSamples(shortArray, dequeued.PcmAudioShort, indexPosition);
                        mixedDown.CopyTo(shortArray, indexPosition);
                    }
                    else if (indexPosition < _sampleRate * 2)
                    {
                        queues[i].TryDequeue(out ClientAudio dequeued);
                        (short[], short[]) splitArrays = AudioManipulationHelper.SplitSampleByTime(_sampleRate * 2 - indexPosition, dequeued.PcmAudioShort);
                        var mixedDown = AudioManipulationHelper.MixSamples(shortArray, splitArrays.Item1, indexPosition);
                        _sampleRemainders.Add(i, splitArrays.Item2);
                        mixedDown.CopyTo(shortArray, indexPosition);

                        break;
                    }
                    else
                    {
                        break;
                    }
                }
                byte[] byteArray = ConversionHelpers.ShortArrayToByteArray(shortArray);
                OutputToFile(i, byteArray);
            }
            _lastWrite += _sampleRate * 2 * (long)208.33;
        }

        public void Start()
        {
            if (!Directory.Exists("Recordings"))
            {
                Directory.CreateDirectory("Recordings");
            }

            _lastWrite = DateTime.Now.Ticks;
            var lamePreset = (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue);
            for (int i = 0; i < 11; i++)
            {
                string sanitisedDate = String.Join("-", DateTime.Now.ToShortDateString().Split(Path.GetInvalidFileNameChars()));
                string sanitisedTime = String.Join("-", DateTime.Now.ToLongTimeString().Split(Path.GetInvalidFileNameChars()));
                _filePaths.Add(i, $"C:\\Recordings\\{sanitisedDate}-{sanitisedTime}-Radio{i}.mp3");

                _mp3FileWriters[i] = new LameMP3FileWriter(_filePaths[i], _waveFormat, lamePreset);
            }
        }

        public void Stop()
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
