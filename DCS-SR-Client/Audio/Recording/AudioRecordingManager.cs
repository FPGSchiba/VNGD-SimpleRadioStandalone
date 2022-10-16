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
        private readonly ConcurrentQueue<AudioRecordingSample>[] _playerAudioSampleQueue;

        //create 2 sets of queues with 10 each

        private bool _stop;
        private AudioRecordingLameWriterBase _audioRecordingWriter;

        private ConnectedClientsSingleton _connectedClientsSingleton = ConnectedClientsSingleton.Instance;
        //private WaveFileWriter waveWriter;

        private AudioRecordingManager()
        {
            _sampleRate = 48000;
            //TODO change that hardcoded 11 to run off number of radios
            //TODO get proper format for MP3
         //   _clientSampleQueue = new ConcurrentQueue<AudioRecordingSample>[11];

    
            _stop = true;

            _clientMixDownQueue = new List<CircularFloatBuffer>();
            for (int i = 0; i < 11; i++)
            {
                //TODO check size
                //5 seconds of audio
                _clientMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE*5));
            //    _clientSampleQueue[i] = new ConcurrentQueue<AudioRecordingSample>();
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
                //todo leave the thread running but paused if you dont opt in to recording
                if (!GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
                {
                    _stop = true;
                }

                Thread.Sleep(2000);
                try
                {
                    //we now have mixdown audio per queue
                    //im worried it'll always be off by 2 seconds though
                    _audioRecordingWriter.ProcessAudio(_clientMixDownQueue);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Recording process failed: {ex}");
                }
            }
        }

        private float[] SingleRadioMixDown(AudioRecordingSample sample, out int count)
        {

            //should be no more than 80 ms of audio
            //should really be 40 but just in case
            //TODO reuse this but return a new array of the right length
            float[] mixBuffer = new float[AudioManager.OUTPUT_SEGMENT_FRAMES * 10];
            float[] secondaryMixBuffer = new float[0];

            int primarySamples = 0;
            int secondarySamples = 0;

            //run this sample through - mix down all the audio for now PER radio 
            //we can then decide what to do with it later
            //same pipeline (ish) as RadioMixingProvider

            if (sample.MainRadioClientTransmissions?.Count > 0)
            {
                mixBuffer = pipeline.ProcessClientTransmissions(mixBuffer, sample.MainRadioClientTransmissions,
                    out primarySamples);
            }

            //handle guard
            if (sample.SecondaryRadioClientTransmissions?.Count > 0)
            {
                secondaryMixBuffer =  new float[AudioManager.OUTPUT_SEGMENT_FRAMES * 10];
                secondaryMixBuffer = pipeline.ProcessClientTransmissions(secondaryMixBuffer, sample.SecondaryRadioClientTransmissions, out  secondarySamples);
            }

            mixBuffer = AudioManipulationHelper.MixArraysClipped(mixBuffer, primarySamples, secondaryMixBuffer, secondarySamples, out int outputSamples);

            count = outputSamples;

            return mixBuffer;
        }

      
        public void AppendClientAudio(DeJitteredTransmission audio)
        {
            if (_stop)
            {
                Start();
            }
            
            DeJitteredTransmission finalAudio;
            
            //TODO fix with the allowrecord enable
            //TODO audio is now dejittered - so we dont need another degitter - just straight to a recording - or SINE wave if recording isnt allowed
            // if(true)
            
            
            
            if(!_connectedClientsSingleton.TryGetValue(audio.OriginalClientGuid, out SRClient client))
                return;
            
            //todo undo
            if (true || client.AllowRecord 
                || audio.OriginalClientGuid == ClientStateSingleton.Instance.ShortGUID) // Assume that client intends to record their outgoing transmissions
            {
                finalAudio = audio;
            }
            else if(GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.DisallowedAudioTone))
            {
                //TODO TEST
                //replace their audio with 
                finalAudio = new DeJitteredTransmission
                {
                    PCMMonoAudio = AudioManipulationHelper.SineWaveOut(audio.PCMMonoAudio.Length, _sampleRate, 0.25),
                    ReceivedRadio = audio.ReceivedRadio,
                };
            }
            else
            {
                return;
            }
            //TODO FIX
           // _clientAudioQueues[audio.ReceivedRadio].Enqueue(finalAudio);
        }

        public void Start()
        {
            _logger.Debug("Transmission recording started.");
            // if(GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.SingleFileMixdown))
            // {
            //     _audioRecordingWriter = new MixDownLameRecordingWriter(_sampleRate);
            // }
            // else
            {
                _audioRecordingWriter = new PerRadioLameRecordingWriter(_sampleRate);
            }
            _audioRecordingWriter.Start();
            _stop = false;

            _clientMixDownQueue.Clear();
            for (int i = 0; i < 11; i++)
            {
                //TODO check size
                //5 seconds of audio
                _clientMixDownQueue.Add(new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * 5));
                //    _clientSampleQueue[i] = new ConcurrentQueue<AudioRecordingSample>();
            }

            //waveWriter = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\output{Guid.NewGuid()}.wav", WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1));


            var processingThread = new Thread(ProcessQueues);
            processingThread.Start();
        }

        public void Stop()
        {
            if (_stop) { return; }
            _stop = true;
            _audioRecordingWriter.Stop();
            _logger.Debug("Transmission recording stopped.");

            //waveWriter.Flush();
            //waveWriter.Dispose();

        }

        internal void AppendClientAudio(List<DeJitteredTransmission> mainAudio, List<DeJitteredTransmission> secondaryAudio, int radioId)
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

                float[] buf = SingleRadioMixDown(new AudioRecordingSample()
                {
                    MainRadioClientTransmissions = mainAudio,
                    SecondaryRadioClientTransmissions = secondaryAudio,
                    RadioId = radioId
                }, out int count);
                if (count > 0)
                {
                    _clientMixDownQueue[radioId].Write(buf, 0, count);
                }
            }
        }
    }
}
