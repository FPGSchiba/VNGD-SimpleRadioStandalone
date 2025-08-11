using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Windows;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Providers;
using Vanguard.VCS.Client.Audio.Utility;
using Vanguard.VCS.Client.Input;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Audio.Recording;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Common.Helpers;
using Vanguard.VCS.Common.Network;
using Easy.MessageHub;
using FragLabs.Audio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;
using Vanguard.VCS.Common.DCSState;
using WebRtcVadSharp;
using Application = FragLabs.Audio.Codecs.Opus.Application;

namespace Vanguard.VCS.Client.Audio.Managers
{
    public class AudioManager
    {
        public static readonly int MIC_SAMPLE_RATE = 48000;
        public static readonly int MIC_INPUT_AUDIO_LENGTH_MS = 20;
        public static readonly int MIC_SEGMENT_FRAMES = (MIC_SAMPLE_RATE / 1000) * MIC_INPUT_AUDIO_LENGTH_MS;
        public static readonly int OUTPUT_SAMPLE_RATE = 48000;
        public static readonly int OUTPUT_AUDIO_LENGTH_MS = 20;
        public static readonly int OUTPUT_SEGMENT_FRAMES = (OUTPUT_SAMPLE_RATE / 1000) * OUTPUT_AUDIO_LENGTH_MS;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly CachedAudioEffectProvider _cachedAudioEffectsProvider;
        private readonly ConcurrentDictionary<Guid, ClientAudioProvider> _clientsBufferedAudio = new();

        //TEMP
        private List<RadioMixingProvider> _radioMixingProvider;
        private MixingSampleProvider _finalMixdown;
        private OpusEncoder _encoder;
        private readonly Queue<short> _micInputQueue = new Queue<short>(MIC_SEGMENT_FRAMES * 3);

        //buffers intialised once for use repeatedly
        short[] _pcmShort = new short[AudioManager.MIC_SEGMENT_FRAMES];
        byte[] _pcmBytes = new byte[AudioManager.MIC_SEGMENT_FRAMES * 2];

        private byte[] _tempMicOutputBuffer = null;

        private float _speakerBoost = 1.0f;
        private UdpVoiceHandler _udpVoiceHandler;
        private VolumeSampleProviderWithPeak _volumeSampleProvider;

        private WasapiCapture _wasapiCapture;
        private WasapiOut _waveOut;
        private EventDrivenResampler _resampler;

        public float MicMax { get; set; } = -100;
        public float SpeakerMax { get; set; } = -100;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly AudioInputSingleton _audioInputSingleton = AudioInputSingleton.Instance;
        private readonly AudioOutputSingleton _audioOutputSingleton = AudioOutputSingleton.Instance;
        private readonly AudioRecordingManager _audioRecordingManager = AudioRecordingManager.Instance;

        private WebRtcVad _voxDectection;

        private WasapiOut _micWaveOut;
        private BufferedWaveProvider _micWaveOutBuffer;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        private Preprocessor _speex;
        private readonly bool windowsN;

        private ClientAudioProvider _passThroughAudioProvider;
        private ClientEffectsPipeline _clientEffectsPipeline;
        private IMessageHub  _hub;
        private Guid _guid;

        public AudioManager(bool windowsN, IMessageHub hub)
        {
            this.windowsN = windowsN;
            _hub = hub;

            _cachedAudioEffectsProvider = CachedAudioEffectProvider.Instance;
            _clientEffectsPipeline = new ClientEffectsPipeline();

            //_beforeWaveFile = new WaveFileWriter(@"C:\Temp\Test-Preview-Before.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 32, 1));
        }

        public float SpeakerBoost
        {
            get { return _speakerBoost; }
            set
            {
                _speakerBoost = value;
                if (_volumeSampleProvider != null)
                {
                    _volumeSampleProvider.Volume = value;
                }
            }
        }

        public void StartEncoding(InputDeviceManager inputManager, IPAddress ipAddress, int port)
        {
            var guid = ClientStateSingleton.Instance.ClientId;

            MMDevice speakers = null;
            if (_audioOutputSingleton.SelectedAudioOutput.Value == null)
            {
                speakers = WasapiOut.GetDefaultAudioEndpoint();
            }
            else
            {
                speakers = (MMDevice)_audioOutputSingleton.SelectedAudioOutput.Value;
            }

            MMDevice micOutput = null;
            if (_audioOutputSingleton.SelectedMicAudioOutput.Value != null)
            {
                micOutput = (MMDevice)_audioOutputSingleton.SelectedMicAudioOutput.Value;
            }

            try
            {
                _micInputQueue.Clear();

                InitMixers();

                InitVox();

                AudioRecordingManager.Instance.Start();

                //Audio manager should start / stop and cleanup based on connection successfull and disconnect
                //Should use listeners to synchronise all the state

                _waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, true, 40,windowsN);

                //add final volume boost to all mixed audio
                _volumeSampleProvider = new VolumeSampleProviderWithPeak(_finalMixdown,
                    (peak => SpeakerMax = peak));
                _volumeSampleProvider.Volume = SpeakerBoost;

                if (speakers.AudioClient.MixFormat.Channels == 1)
                {
                    if (_volumeSampleProvider.WaveFormat.Channels == 2)
                    {
                        _waveOut.Init(_volumeSampleProvider.ToMono());
                    }
                    else
                    {
                        //already mono
                        _waveOut.Init(_volumeSampleProvider);
                    }
                }
                else
                {
                    if (_volumeSampleProvider.WaveFormat.Channels == 1)
                    {
                        _waveOut.Init(_volumeSampleProvider.ToStereo());
                    }
                    else
                    {
                        //already stereo
                        _waveOut.Init(_volumeSampleProvider);
                    }
                }
                _waveOut.Play();

                //opus
                _encoder = OpusEncoder.Create(48000, 1, Application.Audio);
                _encoder.ForwardErrorCorrection = false;
                _encoder.Bitrate = 48000;

                //speex
                _speex = new Preprocessor(AudioManager.MIC_SEGMENT_FRAMES, AudioManager.MIC_SAMPLE_RATE);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Output - Quitting! " + ex.Message);


                ShowOutputError("Problem Initialising Audio Output!");


                Environment.Exit(1);
            }

            _passThroughAudioProvider = new ClientAudioProvider(true);

            if (micOutput != null) // && micOutput !=speakers
            {
                try
                {
                    _micWaveOut = new WasapiOut(micOutput, AudioClientShareMode.Shared, true, 40,windowsN);

                    _micWaveOutBuffer = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(OUTPUT_SAMPLE_RATE, 1));
                    _micWaveOutBuffer.ReadFully = true;
                    _micWaveOutBuffer.DiscardOnBufferOverflow = true;

                    var sampleProvider = _micWaveOutBuffer.ToSampleProvider();

                    if (micOutput.AudioClient.MixFormat.Channels == 1)
                    {
                        if (sampleProvider.WaveFormat.Channels == 2)
                        {
                            _micWaveOut.Init(sampleProvider.ToMono());
                        }
                        else
                        {
                            //already mono
                            _micWaveOut.Init(sampleProvider);
                        }
                    }
                    else
                    {
                        if (sampleProvider.WaveFormat.Channels == 1)
                        {
                            _micWaveOut.Init(sampleProvider.ToStereo());
                        }
                        else
                        {
                            //already stereo
                            _micWaveOut.Init(sampleProvider);
                        }
                    }

                    _micWaveOut.Play();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error starting mic audio Output - Quitting! " + ex.Message);

                    ShowOutputError("Problem Initialising Mic Audio Output!");


                    Environment.Exit(1);
                }
            }

            if (_audioInputSingleton.MicrophoneAvailable)
            {
                try
                {
                    var device = (MMDevice) _audioInputSingleton.SelectedAudioInput.Value;

                    if (device == null)
                    {
                        device = WasapiCapture.GetDefaultCaptureDevice();
                    }

                    device.AudioEndpointVolume.Mute = false;

                    _wasapiCapture = new WasapiCapture(device, true);
                    _wasapiCapture.ShareMode = AudioClientShareMode.Shared;
                    _wasapiCapture.DataAvailable += WasapiCaptureOnDataAvailable;
                    _wasapiCapture.RecordingStopped += WasapiCaptureOnRecordingStopped;

                    _udpVoiceHandler = new UdpVoiceHandler(guid, ipAddress, port, this, inputManager);
                    var voiceSenderThread = new Thread(_udpVoiceHandler.Listen);

                    voiceSenderThread.Start();

                    _wasapiCapture.StartRecording();

                    _hub.Subscribe<SRClient>(RemoveClientBuffer);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error starting audio Input - Quitting! " + ex.Message);

                    ShowInputError("Problem initialising Audio Input!");

                    Environment.Exit(1);
                }
            }
            else
            {
                //no mic....
                _udpVoiceHandler =
                    new UdpVoiceHandler(guid, ipAddress, port, this, inputManager);
                _hub.Subscribe<SRClient>(RemoveClientBuffer);
                var voiceSenderThread = new Thread(_udpVoiceHandler.Listen);
                voiceSenderThread.Start();
            }
        }


        private void WasapiCaptureOnRecordingStopped(object sender, StoppedEventArgs e)
        {
            Logger.Error(e.Exception, "Recording Stopped");
        }

        Stopwatch _stopwatch = new Stopwatch();
        // private WaveFileWriter _beforeWaveFile;
        // private WaveFileWriter _afterFileWriter;


        private void WasapiCaptureOnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_resampler == null)
            {
                //create and use in the same thread or COM issues
                _resampler = new EventDrivenResampler(windowsN, _wasapiCapture.WaveFormat, new WaveFormat(AudioManager.MIC_SAMPLE_RATE, 16, 1));

                // _afterFileWriter = new WaveFileWriter(@"C:\Temp\Test-Preview-after.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 16, 1));
            }

            if (e.BytesRecorded > 0)
            {
                //Logger.Info($"Time: {_stopwatch.ElapsedMilliseconds} - Bytes: {e.BytesRecorded}");
                short[] resampledPCM16Bit = _resampler.Resample(e.Buffer, e.BytesRecorded);

                // Logger.Info($"Time: {_stopwatch.ElapsedMilliseconds} - Bytes: {resampledPCM16Bit.Length}");
                //fill sound buffer
                for (var i = 0; i < resampledPCM16Bit.Length; i++)
                {
                    _micInputQueue.Enqueue(resampledPCM16Bit[i]);
                }

                //read out the queue
                while (_micInputQueue.Count >= AudioManager.MIC_SEGMENT_FRAMES)
                {

                    for (var i = 0; i < AudioManager.MIC_SEGMENT_FRAMES; i++)
                    {
                        _pcmShort[i] = _micInputQueue.Dequeue();
                    }

                    try
                    {
                        //ready for the buffer shortly
                        //check for voice before any pre-processing
                        bool voice = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1) || _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC);

                        if (voice && !_udpVoiceHandler._ptt) // VOX Setting here
                        {
                            Buffer.BlockCopy(_pcmShort, 0, _pcmBytes, 0, _pcmBytes.Length);
                            voice = DoesFrameContainSpeech(_pcmBytes, _pcmShort);
                        }

                        //process with Speex
                        _speex.Process(new ArraySegment<short>(_pcmShort));

                        float max = 0;
                        for (var i = 0; i < _pcmShort.Length; i++)
                        {
                            //determine peak
                            if (_pcmShort[i] > max)
                            {
                                max = _pcmShort[i];
                            }
                        }

                        //convert to dB
                        MicMax = (float)VolumeConversionHelper.CalculateRMS(_pcmShort);

                        //copy and overwrite with new PCM data post processing
                        Buffer.BlockCopy(_pcmShort, 0, _pcmBytes, 0, _pcmBytes.Length);

                        //encode as opus bytes
                        int len;
                        var buff = _encoder.Encode(_pcmBytes, _pcmBytes.Length, out len);
                        
                        byte toc = buff[0];
                        int config = toc >> 3; // top 5 bits
                        bool celtOnly = config >= 16;
                        Logger.Debug($"Opus TOC config={config} (CELT-only={celtOnly}) len={len}");

                        if ((_udpVoiceHandler != null) && (buff != null) && (len > 0))
                        {
                            //create copy with small buffer
                            var encoded = new byte[len];

                            Buffer.BlockCopy(buff, 0, encoded, 0, len);

                            // Console.WriteLine("Sending: " + e.BytesRecorded);
                            var clientAudio = _udpVoiceHandler.Send(encoded, len, voice);

                            // _beforeWaveFile.Write(pcmBytes, 0, pcmBytes.Length);

                            if (clientAudio != null && (_micWaveOutBuffer != null
                                                        || GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio)))
                            {

                                //todo see if we can fix the resample / opus decode
                                //send audio so play over local too
                                var jitterBufferAudio = _passThroughAudioProvider?.AddClientAudioSamples(clientAudio);

                                // //process bytes and add effects
                                if (jitterBufferAudio!=null)
                                {
                                    DeJitteredTransmission deJittered =  new DeJitteredTransmission()
                                    {
                                        PCMAudioLength = jitterBufferAudio.Audio.Length,
                                        Modulation = jitterBufferAudio.Modulation,
                                        Volume = jitterBufferAudio.Volume,
                                        Decryptable = true,
                                        Frequency = jitterBufferAudio.Frequency,
                                        IsSecondary = jitterBufferAudio.IsSecondary,
                                        NoAudioEffects = jitterBufferAudio.NoAudioEffects,
                                        ReceivedRadio = jitterBufferAudio.ReceivedRadio,
                                        PCMMonoAudio = jitterBufferAudio.Audio,
                                        Guid = _guid,
                                        OriginalClientGuid = _guid
                                    };

                                    //process audio
                                    float[] tempFloat = jitterBufferAudio.Audio;

                                    // Optional: very low volume while debugging to save ears
                                    const float debugGain = 0.1f; // try 0.05 if it’s still loud

                                    // Sanitize + apply simple gain + clamp to [-1, 1]
                                    for (int i = 0; i < tempFloat.Length; i++)
                                    {
                                        float s = tempFloat[i];
                                        if (float.IsNaN(s) || float.IsInfinity(s)) s = 0f;
                                        s *= debugGain;
                                        if (s > 1f) s = 1f;
                                        else if (s < -1f) s = -1f;
                                        tempFloat[i] = s;
                                    }

                                    // Debug a few sample values
                                    if (tempFloat.Length >= 4)
                                    {
                                        Logger.Debug($"Post-bypass first samples: {tempFloat[0]:0.000}, {tempFloat[1]:0.000}, {tempFloat[2]:0.000}, {tempFloat[3]:0.000}");
                                    }

                                    if (_micWaveOut != null && ShouldMonitorSidetone())
                                    {
                                        // Very low sidetone to avoid feedback (-26 dB)
                                        const float sidetoneGain = 0.05f;

                                        for (int i = 0; i < tempFloat.Length; i++)
                                        {
                                            float s = tempFloat[i];
                                            if (float.IsNaN(s) || float.IsInfinity(s)) s = 0f;
                                            s *= sidetoneGain;
                                            if (s > 1f) s = 1f; else if (s < -1f) s = -1f;
                                            tempFloat[i] = s;
                                        }

                                        _tempMicOutputBuffer = BufferHelpers.Ensure(_tempMicOutputBuffer, tempFloat.Length * 4);
                                        Buffer.BlockCopy(tempFloat, 0, _tempMicOutputBuffer, 0, tempFloat.Length * 4);
                                        _micWaveOutBuffer.AddSamples(_tempMicOutputBuffer, 0, tempFloat.Length * 4);
                                    }

                                    if (GlobalSettingsStore.Instance.GetClientSettingBool(
                                        GlobalSettingsKeys.RecordAudio))
                                    {
                                        ///TODO cache this to avoid the contant lookup
                                        _audioRecordingManager.AppendPlayerAudio(tempFloat, jitterBufferAudio.ReceivedRadio);
                                    }

                                }
                            }

                        }
                        else
                        {
                            Logger.Error($"Invalid Bytes for Encoding - {_pcmShort.Length} should be {MIC_SEGMENT_FRAMES} ");
                        }

                        _errorCount = 0;
                    }
                    catch (Exception ex)
                    {
                        // Can safely ignore this error as it's just a single frame
                        _errorCount++;
                        if (_errorCount < 10)
                        {
                            Logger.Warn(ex, "Error encoding Opus! " + ex.Message);
                        }
                        else if (_errorCount == 10)
                        {
                            Logger.Warn(ex, "Final Log of Error encoding Opus! " + ex.Message);
                        }
                    }
                }
            }
        }
        private void ShowInputError(string message)
        {
            if (Environment.OSVersion.Version.Major == 10)
            {
                var messageBoxResult = MessageBox.Show(
                    $"{message}\n\n" +
                    "If you are using Windows 10, this could be caused by your privacy settings (make sure to allow apps to access your microphone)." +
                    "\nAlternatively, try a different Input device and please post your client log to the support Discord server.",
                    "Audio Input Error",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Error);

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    Process.Start("ms-settings:privacy-microphone");
                }
                else if (messageBoxResult == MessageBoxResult.No)
                {
                    Process.Start("https://discord.gg/baw7g3t");
                }
            }
            else
            {
                var messageBoxResult = MessageBox.Show(
                    $"{message}\n\n" +
                    "Try a different Input device and please post your client log to the support Discord server.",
                    "Audio Input Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (messageBoxResult == MessageBoxResult.Yes)
                {
                    Process.Start("https://discord.gg/baw7g3t");
                }
            }
        }

        private void ShowOutputError(string message)
        {
            var messageBoxResult = MessageBox.Show(
                $"{message}\n\n" +
                "Try a different output device and please post your client log to the support Discord server.",
                "Audio Output Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (messageBoxResult == MessageBoxResult.Yes)
            {
                Process.Start("https://discord.gg/baw7g3t");
            }
        }

        private void InitMixers()
        {
            _finalMixdown = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(OUTPUT_SAMPLE_RATE, 2));
            _finalMixdown.ReadFully = true;

            _radioMixingProvider = new List<RadioMixingProvider>();
            for (int i = 0; i < _clientStateSingleton.DcsPlayerRadioInfo.radios.Length; i++)
            {
                var mix = new RadioMixingProvider(WaveFormat.CreateIeeeFloatWaveFormat(OUTPUT_SAMPLE_RATE, 2), i);
                _radioMixingProvider.Add(mix);
                _finalMixdown.AddMixerInput(mix);
            }
        }

        private void InitVox()
        {
            if (_voxDectection != null)
            {
                _voxDectection.Dispose();
                _voxDectection = null;
            }

            _voxDectection = new WebRtcVad
            {
                SampleRate = SampleRate.Is16kHz,
                FrameLength = FrameLength.Is20ms,
                OperatingMode = (OperatingMode)_globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMode)
            };
        }

        private int _errorCount = 0;
        //Stopwatch _stopwatch = new Stopwatch();

        object lockObj = new object();
        public void StopEncoding()
        {
            lock(lockObj)
            {
                _wasapiCapture?.StopRecording();
                try
                {
                    _wasapiCapture?.Dispose();
                }
                catch (PlatformNotSupportedException ex)
                {
                    Logger.Warn(ex, "WasapiCapture.Dispose() failed due to unsupported Thread.Abort.");
                }
                _wasapiCapture = null;

                _voxDectection?.Dispose();
                _voxDectection = null;

                _resampler?.Dispose(true);
                _resampler = null;

                _udpVoiceHandler?.RequestStop();
                _udpVoiceHandler = null;

                _waveOut?.Stop();
                _waveOut?.Dispose();
                _waveOut = null;

                _micWaveOut?.Stop();
                _micWaveOut?.Dispose();
                _micWaveOut = null;

                _volumeSampleProvider = null;

                if(_radioMixingProvider!=null)
                    foreach (var mixer in _radioMixingProvider)
                    {
                        mixer.RemoveAllMixerInputs();
                    }

                _radioMixingProvider = new List<RadioMixingProvider>();

                _finalMixdown?.RemoveAllMixerInputs();
                _finalMixdown = null;

                _clientsBufferedAudio.Clear();

                _encoder?.Dispose();
                _encoder = null;

                if (_udpVoiceHandler != null)
                {
                    _udpVoiceHandler.RequestStop();
                    _udpVoiceHandler = null;
                }

                _speex?.Dispose();
                _speex = null;

                SpeakerMax = -100;
                MicMax = -100;

                AudioRecordingManager.Instance.Stop();

                _hub.ClearSubscriptions();
            }
        }

        public void AddClientAudio(ClientAudio audio)
        {
            // Reuse a per-client audio provider instead of creating one per packet
            if (audio == null)
            {
                return;
            }

            var key = audio.ClientGuid;
            if (key == Guid.Empty)
            {
                // Fall back to original behavior if no guid is present
                var fallback = new ClientAudioProvider();
                foreach (var mix in _radioMixingProvider)
                {
                    mix.AddMixerInput(fallback);
                }
                fallback.AddClientAudioSamples(audio);
                return;
            }

            // Get or create the ClientAudioProvider for this client
            if (!_clientsBufferedAudio.TryGetValue(key, out var provider))
            {
                provider = new ClientAudioProvider();

                // Attach to all radio mixers once
                foreach (var mix in _radioMixingProvider)
                {
                    mix.AddMixerInput(provider);
                }

                _clientsBufferedAudio[key] = provider;
            }

            // Feed decoded samples into the client’s jitter buffer / stream
            provider.AddClientAudioSamples(audio);
        }

        private void RemoveClientBuffer(SRClient srClient)
        {
            ClientAudioProvider clientAudio = null;
            _clientsBufferedAudio.TryRemove(srClient.ClientGuid, out clientAudio);

            if (clientAudio == null)
            {
                return;
            }

            try
            {
                foreach (var mixer in _radioMixingProvider)
                {
                    mixer.RemoveMixerInput(clientAudio);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error removing client input");
            }
        }

        //MIC SEGMENT FRAMES IS SHORTS not bytes - which is two bytes
        //however we only want half of a frame IN BYTES not short - so its MIC_SEGMENT_FRAMES *2 (for bytes) then / 2 for bytes again
        //declare here to save on garbage collection
        byte[] tempBufffer20ms = new byte[MIC_SEGMENT_FRAMES];
        bool DoesFrameContainSpeech(byte[] audioFrame, short[] pcmShort)
        {
            // Single 20 ms frame at 16 kHz mono is 320 samples = 640 bytes
            Buffer.BlockCopy(audioFrame, 0, tempBufffer20ms, 0, MIC_SEGMENT_FRAMES);

            OperatingMode mode = (OperatingMode)_globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMode);

            if (_voxDectection.OperatingMode != mode)
            {
                InitVox();
            }

            // Run VAD on this single 20 ms buffer
            bool voice = _voxDectection.HasSpeech(tempBufffer20ms);

            if (voice)
            {
                // Gate with RMS threshold if configured
                double rms = VolumeConversionHelper.CalculateRMS(pcmShort);
                double min = _globalSettings.GetClientSettingDouble(GlobalSettingsKeys.VOXMinimumDB);
                return rms > min;
            }
            return false;
        }
        
        private bool ShouldMonitorSidetone()
        {
            // Must have PTT active to monitor sidetone (prevents open-loop howl)
            if (_udpVoiceHandler == null || !_udpVoiceHandler._ptt) return false;

            // Only allow sidetone if mic monitor device is not the same as main speakers
            var speakers = _audioOutputSingleton.SelectedAudioOutput.Value as MMDevice ?? WasapiOut.GetDefaultAudioEndpoint();
            var micOut = _audioOutputSingleton.SelectedMicAudioOutput.Value as MMDevice;
            if (micOut == null) return false;

            // If the same endpoint, don’t monitor to avoid feedback
            bool sameEndpoint = (speakers != null) && (speakers.ID == micOut.ID);
            return !sameEndpoint;
        }

        public void PlaySoundEffectStartTransmit(int sendingOn, bool enc, float volume, RadioInformation.Modulation modulation)
        {
            _radioMixingProvider[sendingOn]?.PlaySoundEffectStartTransmit(enc,volume,modulation);
        }

        public void PlaySoundEffectEndTransmit(int sendingOn, float radioVolume, RadioInformation.Modulation radioModulation)
        {
            _radioMixingProvider[sendingOn]?.PlaySoundEffectEndTransmit(radioVolume,radioModulation);
        }
    }
}