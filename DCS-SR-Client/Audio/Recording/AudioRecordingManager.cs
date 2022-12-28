using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;
using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Providers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Recording;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    class AudioRecordingManager
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private static volatile AudioRecordingManager _instance = new AudioRecordingManager();
        private static object _lock = new Object();

        private ClientEffectsPipeline pipeline = new ClientEffectsPipeline();

        private readonly int _sampleRate;
        private readonly List<CircularFloatBuffer> _clientMixDownQueue;
        private readonly List<CircularFloatBuffer> _playerMixDownQueue;
        private readonly List<CircularFloatBuffer> _finalMixDownQueue;

        private static readonly int MAX_BUFFER_SECONDS = 3;

        private bool _stop;
        private AudioRecordingLameWriterBase _audioRecordingWriter;

        private ConnectedClientsSingleton _connectedClientsSingleton = ConnectedClientsSingleton.Instance;
        //private WaveFileWriter waveWriter;

        private AudioRecordingManager()
        {
            _sampleRate = 48000;
        
            _stop = true;

            _clientMixDownQueue = new List<CircularFloatBuffer>();
            _playerMixDownQueue = new List<CircularFloatBuffer>();
            _finalMixDownQueue = new List<CircularFloatBuffer>();
            //TODO change that hardcoded 11 to run off number of radios
            for (int i = 0; i < 11; i++)
            {
                // seconds of audio
                _clientMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE* MAX_BUFFER_SECONDS));
                _playerMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * MAX_BUFFER_SECONDS));
                _finalMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * MAX_BUFFER_SECONDS));
            }
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
                Thread.Sleep(500);

                //leave the thread running but paused if you dont opt in to recording
                if (!GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
                {
                    _logger.Info("Recording disabled");
                    _stop = true;
                    break;
                }
                
                try
                {
                    //mix two queues

                    //we now have mixdown audio per queue
                    //it'll always be off by 500  milliseconds though - as this is a lazy way of mixing

                    //two seconds of buffer - but runs every 500 milliseconds
                    float[] clientBuffer = new float[AudioManager.OUTPUT_SAMPLE_RATE * 2];
                    float[] playerBuffer = new float[AudioManager.OUTPUT_SAMPLE_RATE * 2];
                    for (var i = 0; i < _finalMixDownQueue.Count; i++)
                    {
                        //find longest queue
                        int playerAudioLength = _playerMixDownQueue[i].Count;
                        int clientAudioLength = _clientMixDownQueue[i].Count;


                        _playerMixDownQueue[i].Read(playerBuffer, 0, playerAudioLength);
                        _clientMixDownQueue[i].Read(clientBuffer, 0, clientAudioLength);

                        float[] mixDown = AudioManipulationHelper.MixArraysClipped(playerBuffer, playerAudioLength, clientBuffer,
                            clientAudioLength, out int count);

                        _finalMixDownQueue[i].Write(mixDown, 0, count);
                    }

                    _audioRecordingWriter.ProcessAudio(_finalMixDownQueue);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Recording process failed: {ex}");
                }
            }

            _logger.Info("Stop recording thread");
        }

        private float[] SingleRadioMixDown(List<DeJitteredTransmission> mainAudio, List<DeJitteredTransmission> secondaryAudio, int radio, out int count)
        {

            //should be no more than 80 ms of audio
            //should really be 40 but just in case
            //TODO reuse this but return a new array of the right length
            float[] mixBuffer = new float[AudioManager.OUTPUT_SEGMENT_FRAMES * 2];
            float[] secondaryMixBuffer = new float[0];

            int primarySamples = 0;
            int secondarySamples = 0;
            int outputSamples = 0;

            //run this sample through - mix down all the audio for now PER radio 
            //we can then decide what to do with it later
            //same pipeline (ish) as RadioMixingProvider

            if (mainAudio?.Count > 0)
            {
                mixBuffer = pipeline.ProcessClientTransmissions(mixBuffer, mainAudio,
                    out primarySamples);
            }

            //handle guard
            if (secondaryAudio?.Count > 0)
            {
                secondaryMixBuffer =  new float[AudioManager.OUTPUT_SEGMENT_FRAMES * 2];
                secondaryMixBuffer = pipeline.ProcessClientTransmissions(secondaryMixBuffer, secondaryAudio, out  secondarySamples);
            }

            if(primarySamples>0 || secondarySamples>0)
                mixBuffer = AudioManipulationHelper.MixArraysClipped(mixBuffer, primarySamples, secondaryMixBuffer, secondarySamples, out outputSamples);

            count = outputSamples;

            return mixBuffer;
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
         
            _stop = false;

            _clientMixDownQueue.Clear();
            _playerMixDownQueue.Clear();
            _finalMixDownQueue.Clear();

            for (int i = 0; i < 11; i++)
            {
                //TODO check size
                //5 seconds of audio
                _clientMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * MAX_BUFFER_SECONDS));
                _playerMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * MAX_BUFFER_SECONDS));
                _finalMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * MAX_BUFFER_SECONDS));
            }

            _audioRecordingWriter.Start();

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

        public void AppendPlayerAudio(float[] transmission, int radioId)
        {
            if (_stop)
            {
                Start();
            }

            //only record if we need too
            if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
            {
                
                _playerMixDownQueue[radioId]?.Write(transmission, 0, transmission.Length);
            }
        }

        public void AppendClientAudio(List<DeJitteredTransmission> mainAudio, List<DeJitteredTransmission> secondaryAudio, int radioId)
        {
            if (_stop)
            {
                Start();
            }

            //only record if we need too
            if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
            {
                //TODO
                //represents a moment in time
                //should I include time so we can line up correctly?

                mainAudio = FilterTransmisions(mainAudio);
                secondaryAudio = FilterTransmisions(secondaryAudio);

                float[] buf = SingleRadioMixDown( mainAudio,secondaryAudio,radioId, out int count);
                if (count > 0)
                {
                    _clientMixDownQueue[radioId].Write(buf, 0, count);
                }
            }
        }

        private List<DeJitteredTransmission> FilterTransmisions(List<DeJitteredTransmission> originalTransmissions)
        {
            if (originalTransmissions == null || originalTransmissions.Count == 0)
            {
                return new List<DeJitteredTransmission>();
            }

            List<DeJitteredTransmission> filteredTransmisions = new List<DeJitteredTransmission>();

            foreach (var transmission in originalTransmissions)
            {
                if (_connectedClientsSingleton.TryGetValue(transmission.OriginalClientGuid, out SRClient client))
                {
                    if (client.AllowRecord
                        || transmission.OriginalClientGuid == ClientStateSingleton.Instance.ShortGUID) // Assume that client intends to record their outgoing transmissions
                    {
                        filteredTransmisions.Add(transmission);
                    }
                    else if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.DisallowedAudioTone))
                    {
                        DeJitteredTransmission toneTransmission = new DeJitteredTransmission
                        {
                            PCMMonoAudio = AudioManipulationHelper.SineWaveOut(transmission.PCMAudioLength, _sampleRate, 0.25),
                            ReceivedRadio = transmission.ReceivedRadio,
                            PCMAudioLength = transmission.PCMAudioLength,
                            Decryptable = transmission.Decryptable,
                            Frequency = transmission.Frequency,
                            Guid = transmission.Guid,
                            IsSecondary = transmission.IsSecondary,
                            Modulation = transmission.Modulation,
                            NoAudioEffects = transmission.NoAudioEffects,
                            OriginalClientGuid = transmission.OriginalClientGuid,
                            Volume = transmission.Volume
                        };
                        filteredTransmisions.Add(toneTransmission);
                    }
                }
            }

            return filteredTransmisions;
        }
    }
}
