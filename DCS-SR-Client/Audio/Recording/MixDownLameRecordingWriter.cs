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
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    internal class MixDownLameRecordingWriter : AudioRecordingLameWriterBase
    {
        private LameMP3FileWriter _mp3FileWriter;
        public MixDownLameRecordingWriter(int sampleRate) : base(sampleRate)
        {
        }

        private void OutputToFile(float[] floatArray, int count)
        {
            if (_mp3FileWriter == null)
            {
                Start();
            }
            try
            {
                if (_mp3FileWriter != null)
                {
                    // create a byte array and copy the floats into it...
                    var byteArray = new byte[count * 4];
                    Buffer.BlockCopy(floatArray, 0, byteArray, 0, byteArray.Length);

                    _mp3FileWriter.Write(byteArray, 0, byteArray.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to write audio samples to output file: {ex.Message}");
            }
        }

        public override void ProcessAudio(List<CircularFloatBuffer> perRadioClientAudio)
        {
            //2 seconds at worse
            float[] mixdown = new float[AudioManager.OUTPUT_SAMPLE_RATE * 2];
            int count = 0;
            for (int i = 0; i < perRadioClientAudio.Count; i++)
            {
                CircularFloatBuffer buf = perRadioClientAudio[i];
                int length = buf.Count;

                if (length > 0)
                {
                    float[] client = new float[length];
                    int read = buf.Read(client, 0, length);
                    //mix without clipping
                    //may not clip later on
                    AudioManipulationHelper.MixArraysNoClipping(mixdown, mixdown.Length, client, read, out int mixLength);
                    count = Math.Max(count, length);
                }
            }

            if (count > 0)
            {
                mixdown = AudioManipulationHelper.ClipArray(mixdown, count);
                OutputToFile(mixdown, count);
            }
        }

        protected override void Start()
        {
            string partialFilePath = base.CreateFilePath();

            string _filePath = $"{partialFilePath}.mp3";

            _mp3FileWriter = new LameMP3FileWriter(_filePath, _waveFormat,
                    (LAMEPreset)Enum.Parse(typeof(LAMEPreset),
                    GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.RecordingQuality).RawValue));
        }

        public override void Stop()
        {
            _mp3FileWriter?.Dispose();
            _mp3FileWriter = null;          
        }
    }
}