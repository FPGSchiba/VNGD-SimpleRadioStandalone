using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Lame;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers
{
    class RecordingManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static volatile RecordingManager _instance = new RecordingManager();
        private static object _lock = new Object();

        private readonly Dictionary<int, string> _filePaths;
        private readonly WaveFormat _waveFormat;
        private readonly DCSPlayerRadioInfo _playerInfo;
        private readonly int _sampleRate;
        private readonly long[] _lastWrite;
        private readonly LameMP3FileWriter[] _mp3FileWriters;

        private BlockingCollection<ClientAudio> _audioQueue;
        private bool _stop;

        private RecordingManager()
        {
            _sampleRate = 48000;
            _waveFormat = new WaveFormat(_sampleRate, 1);
            _playerInfo = ClientStateSingleton.Instance.DcsPlayerRadioInfo;
            _lastWrite = new long[11];
            _mp3FileWriters = new LameMP3FileWriter[11];
            _audioQueue = new BlockingCollection<ClientAudio>();
            _filePaths = new Dictionary<int, string>();
            _stop = true;
        }

        public static RecordingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new RecordingManager();
                    }
                }

                return _instance;
            }
        }

        private void RefreshRecordingStatus()
        {
            for (int i = 0; i < 11; i++)
            {
                // instantiate mp3 writer stream if radio becomes available mid session
                if (_playerInfo.radios[i].modulation != RadioInformation.Modulation.DISABLED && _mp3FileWriters[i] == null)
                {
                    _mp3FileWriters[i] = new LameMP3FileWriter(_filePaths[i], _waveFormat, _sampleRate);
                    _logger.Info($"Radio {i} enabled, creating MP3 writer stream");
                }
            }
        }

        private int CalculateSamples(long start, long end)
        {
            double elapsedSinceLastWrite = ((double)end - start) / 10000000;
            int necessarySamples = Convert.ToInt32(elapsedSinceLastWrite * _sampleRate);
            // prevent any potential issues due to a negative time being returned
            return necessarySamples >= 0 ? necessarySamples : 0;
        }

        private void WriteToStream(int radio, byte[] byteArray)
        {
            try
            {
                if (_mp3FileWriters[radio] != null)
                {
                    _mp3FileWriters[radio].Write(byteArray, 0, byteArray.Length);
                }
                else
                {
                    // In case audio samples are still in the queue after a radio has been disabled
                    using (var mp3writer = new LameMP3FileWriter(_filePaths[radio], _waveFormat, _sampleRate))
                    {
                        mp3writer.Write(byteArray, 0, byteArray.Length);
                    }
                }

            }
            catch (Exception ex)
            {
                _logger.Error($"Unable to write audio samples to output file: {ex.Message}");
            }
        }

        private void ProcessAudio()
        {
            while (!_stop)
            {
                RefreshRecordingStatus();
                try
                {
                    _audioQueue.TryTake(out ClientAudio audio, 100000);
                    if (audio != null || _playerInfo.radios[audio.ReceivedRadio].modulation != RadioInformation.Modulation.DISABLED)
                    {
                        int requiredEmptySamples = CalculateSamples(_lastWrite[audio.ReceivedRadio], audio.ReceiveTime);

                        // Get the ticks at which the PCM short data ends
                        _lastWrite[audio.ReceivedRadio] = audio.ReceiveTime + audio.PcmAudioShort.Length * (long)208.33;

                        int fullSize = requiredEmptySamples + audio.PcmAudioShort.Length;

                        /* Corner case of expansion radios being enabled mid mission or a radio receiving a transmission after more than 5.7 hours of no transmission
                         * results in number of samples exceeding the 2GB array limit. A better solution should be implemented as currently this will potentially dump
                         * several GB worth of audio into the write strem.
                        */
                        if (fullSize > 2000000000)
                        {

                            int requiredSplits = (fullSize / 2000000000) + 1;

                            for (int i = 0; i < requiredSplits - 1; i++)
                            {
                                short[] silence = new short[2000000000];
                                byte[] silenceByteArray = ConversionHelpers.ShortArrayToByteArray(silence);
                                WriteToStream(audio.ReceivedRadio, silenceByteArray);
                                requiredEmptySamples -= 2000000000;
                            }
                        }

                        short[] fullShortArray = new short[requiredEmptySamples + audio.PcmAudioShort.Length];
                        audio.PcmAudioShort.CopyTo(fullShortArray, requiredEmptySamples);
                        byte[] byteArray = ConversionHelpers.ShortArrayToByteArray(fullShortArray);
                        WriteToStream(audio.ReceivedRadio, byteArray);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error recording recevied audio to file: {ex.Message}");
                }
            }
        }

        public void AppendClientAudio(ClientAudio audio)
        {
            if (_stop)
            {
                Start();
            }

            ClientAudio finalAudio;

            if (ConnectedClientsSingleton.Instance[audio.OriginalClientGuid].AllowRecord)
            {
                finalAudio = audio;
            }
            else
            {
                finalAudio = new ClientAudio
                {
                    PcmAudioShort = SineWaveOut(audio.PcmAudioShort.Length),
                    ReceivedRadio = audio.ReceivedRadio,
                    ReceiveTime = audio.ReceiveTime
                };
            }

            _audioQueue.Add(finalAudio);
        }

        private short[] SineWaveOut(int sampleLength)
        {
            short[] sineBuffer = new short[sampleLength];
            double amplitude = 0.25 * short.MaxValue;

            for (int i = 0; i < sineBuffer.Length; i++)
            {
                sineBuffer[i] = (short)(amplitude * Math.Sin((2 * Math.PI * i * 175) / _sampleRate));
            }
            return sineBuffer;
        }

        public void Start()
        {
            _logger.Debug("Transmission recording started.");
            _stop = false;

            long sessionStart = DateTime.Now.Ticks;
            if (!Directory.Exists("Recordings"))
            {
                Directory.CreateDirectory("Recordings");
            }

            for (int i = 0; i < _playerInfo.radios.Length; i++)
            {
                _lastWrite[i] = sessionStart;
                string sanitisedDate = String.Join("-", DateTime.Now.ToShortDateString().Split(Path.GetInvalidFileNameChars()));
                string sanitisedTime = String.Join("-", DateTime.Now.ToLongTimeString().Split(Path.GetInvalidFileNameChars()));
                _filePaths.Add(i, $"Recordings\\{sanitisedDate}-{sanitisedTime}-Radio{i}.mp3");
            }

            var processingThread = new Thread(ProcessAudio);
            processingThread.Start();
        }

        public void Stop()
        {
            _stop = true;
            _filePaths.Clear();
            _audioQueue = new BlockingCollection<ClientAudio>();
            _logger.Debug("Transmission recording stopped.");

            for (int i = 0; i < _playerInfo.radios.Length; i++)
            {
                if (_mp3FileWriters[i] != null)
                {
                    _mp3FileWriters[i].Close();
                    _mp3FileWriters[i] = null;
                }
            }
        }
    }
}
