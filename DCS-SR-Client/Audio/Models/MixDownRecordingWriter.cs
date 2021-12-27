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
    internal class MixDownRecordingWriter : IAudioRecordingWriter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly WaveFormat _waveFormat;
        private readonly int _sampleRate;

        private LameMP3FileWriter _mp3FileWriter;
        private long _lastWrite;
        private List<short[]> _processedAudio;
        private Dictionary<int, short[]> _sampleRemainders;

        public MixDownRecordingWriter(int sampleRate)
        {
            _sampleRate = sampleRate;
            _waveFormat = new WaveFormat(sampleRate, 1);
            _sampleRemainders = new Dictionary<int, short[]>();
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

        private short[] MixDownRadios(List<short[]> processedAudio)
        {
            short[] finalShortArray = new short[_sampleRate * 2];
            
            for(int i = 0; i < processedAudio.Count; i++)
            {
                finalShortArray = AudioManipulationHelper.MixSamples(finalShortArray, processedAudio[i], 0);
            }

            return finalShortArray;
        }

        public void ProcessAudio(ConcurrentQueue<ClientAudio>[] queues)
        {
            for (int i = 0; i < 11; i++)
            {
                short[] shortArray = new short[_sampleRate * 2];

                if(_sampleRemainders.ContainsKey(i))
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
                    if (indexPosition + firstInQueue.PcmAudioShort.Length < _sampleRate * 2)
                    {
                        queues[i].TryDequeue(out ClientAudio dequeued);
                        var mixedDown = AudioManipulationHelper.MixSamples(shortArray, dequeued.PcmAudioShort, indexPosition);
                        mixedDown.CopyTo(shortArray, indexPosition);
                    }

                    else if(indexPosition < _sampleRate * 2)
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
                _processedAudio.Add(shortArray);
            }
            short[] finalShortArray = MixDownRadios(_processedAudio);
            _lastWrite += TimeSpan.TicksPerSecond * 2;
            OutputToFile(ConversionHelpers.ShortArrayToByteArray(finalShortArray));
            _processedAudio.Clear();
        }

        public void Start()
        {
            if (!Directory.Exists("Recordings"))
            {
                Directory.CreateDirectory("Recordings");
            }

            _lastWrite = DateTime.Now.Ticks;

            string sanitisedDate = String.Join("-", DateTime.Now.ToShortDateString().Split(Path.GetInvalidFileNameChars()));
            string sanitisedTime = String.Join("-", DateTime.Now.ToLongTimeString().Split(Path.GetInvalidFileNameChars()));
            string _filePath = $"C:\\Recordings\\{sanitisedDate}-{sanitisedTime}.mp3";

            _mp3FileWriter = new LameMP3FileWriter(_filePath, _waveFormat,
                    (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue));
            _processedAudio = new List<short[]>();
        }

        public void Stop()
        {
            _mp3FileWriter.Dispose();
            _mp3FileWriter = null;          
        }
    }
}