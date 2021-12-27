using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers
{
    class AudioRecordingManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static volatile AudioRecordingManager _instance = new AudioRecordingManager();
        private static object _lock = new Object();

        private readonly int _sampleRate;
        private readonly ConcurrentQueue<ClientAudio>[] _clientAudioQueues;

        private bool _stop;
        private IAudioRecordingWriter _audioRecordingWriter;

        private AudioRecordingManager()
        {
            _sampleRate = 48000;
            _clientAudioQueues = new ConcurrentQueue<ClientAudio>[11];
            _stop = true;
        }

        public static AudioRecordingManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new AudioRecordingManager();
                    }
                }
                return _instance;
            }
        }

        private void ProcessQueues()
        {
            while (!_stop)
            {
                Thread.Sleep(2000);
                try
                {
                    _audioRecordingWriter.ProcessAudio(_clientAudioQueues);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Recording process failed: {ex}");
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
                    PcmAudioShort = AudioManipulationHelper.SineWaveOut(audio.PcmAudioShort.Length, _sampleRate, 0.25),
                    ReceivedRadio = audio.ReceivedRadio,
                    ReceiveTime = audio.ReceiveTime
                };
            }
            _clientAudioQueues[audio.ReceivedRadio].Enqueue(finalAudio);
        }

        public void Start()
        {
            _logger.Debug("Transmission recording started.");
            if(GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.SingleFileMixdown))
            {
                _audioRecordingWriter = new MixDownRecordingWriter(_sampleRate);
            }
            else
            {
                _audioRecordingWriter = new PerRadioRecordingWriter(_sampleRate);
            }
            _audioRecordingWriter.Start();
            _stop = false;

            for(int i  = 0; i < 11; i++)
            {
                _clientAudioQueues[i] = new ConcurrentQueue<ClientAudio>();
            }

            var processingThread = new Thread(ProcessQueues);
            processingThread.Start();
        }

        public void Stop()
        {
            if (_stop) { return; }
            _stop = true;
            _audioRecordingWriter.Stop();
            _logger.Debug("Transmission recording stopped.");
        }
    }
}
