using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    class AudioRecordingManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static volatile AudioRecordingManager _instance = new AudioRecordingManager();
        private static object _lock = new Object();

        private readonly int _sampleRate;
        private readonly ConcurrentQueue<ClientAudio>[] _clientAudioQueues;

        private bool _stop;
        private AudioRecordingLameWriterBase _audioRecordingWriter;

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
                if(!GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
                {
                    _stop = true;
                }
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


            if (ConnectedClientsSingleton.Instance[audio.OriginalClientGuid].AllowRecord 
                || audio.OriginalClientGuid == ClientStateSingleton.Instance.ShortGUID) // Assume that client intends to record their outgoing transmissions
            {
                finalAudio = audio;
            }
            else if(GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.DisallowedAudioTone))
            {
                //TODO FIX
                // finalAudio = new ClientAudio
                // {
                //     PcmAudioShort = AudioManipulationHelper.SineWaveOut(audio.PcmAudioShort.Length, _sampleRate, 0.25),
                //     ReceivedRadio = audio.ReceivedRadio,
                //     ReceiveTime = audio.ReceiveTime,
                //     PacketNumber = audio.PacketNumber,
                //     OriginalClientGuid = audio.OriginalClientGuid,
                // };
            }
            else
            {
                return;
            }
            //TODO FIX
            //_clientAudioQueues[audio.ReceivedRadio].Enqueue(finalAudio);
        }

        public void Start()
        {
            _logger.Debug("Transmission recording started.");
            if(GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.SingleFileMixdown))
            {
                _audioRecordingWriter = new MixDownLameRecordingWriter(_sampleRate);
            }
            else
            {
                _audioRecordingWriter = new PerRadioLameRecordingWriter(_sampleRate);
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
