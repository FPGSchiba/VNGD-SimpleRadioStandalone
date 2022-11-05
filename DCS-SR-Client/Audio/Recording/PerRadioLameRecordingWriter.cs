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
        private readonly LameMP3FileWriter[] _mp3FileWriters;
        private readonly WaveFileWriter waveWriter;

        public PerRadioLameRecordingWriter(int sampleRate) : base(sampleRate)
        {
            _filePaths = new Dictionary<int, string>();
            _mp3FileWriters = new LameMP3FileWriter[11];

            waveWriter = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\output{Guid.NewGuid()}.wav", WaveFormat.CreateIeeeFloatWaveFormat(sampleRate,1));

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

                    if (radio == 1)
                    {
                        //figure out why the audio is terrible
                        //calculate the time between the last write - and fill in the rest of the audio with blank audio?
                        //The effects are now mixing in nicely - its just the actual audio thats wrecked for some reason
                        waveWriter.WriteSamples(floatArray,0,floatArray.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to write audio samples to output file: {ex.Message}");
            }
        }

        public override void ProcessAudio(List<CircularFloatBuffer> perRadioAudio)
        {
            
            for (int i = 0; i < perRadioAudio.Count; i++)
            {
                if (perRadioAudio[i].Count > 0)
                {
                    float[] floatArrray = new float[perRadioAudio[i].Count];

                    perRadioAudio[i].Read(floatArrray, 0, floatArrray.Length);
                    OutputToFile(i, floatArrray);
                }
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
            waveWriter.Close();
            waveWriter.Dispose();
        }
    }
}