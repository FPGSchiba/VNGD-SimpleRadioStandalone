using System;
using System.IO;
using FragLabs.Audio.Codecs;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Utility;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Audio.Providers
{
    public class ClientAudioProvider : AudioProvider
    {
        private readonly Random _random = new Random();

        // Silence pad on new transmission adds latency and perceived slowdown; keep 0
        public static readonly int SILENCE_PAD = 0;

        private OpusDecoder _decoder;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        
        // Diagnostic wav recording
        private static readonly string DiagnosticsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VCS_AudioDiagnostics");
        private WaveFileWriter _diagnosticWriter;
        private object _diagnosticLock = new object();
        private DateTime _transmissionStartTime;

        private readonly bool passThrough;

        public ClientAudioProvider(bool passThrough = false)
        {
            this.passThrough = passThrough;

            Logger.Debug($"ClientAudioProvider ctor: hash={this.GetHashCode()}, passThrough={passThrough}");

            if (!passThrough)
            {
                var radios = ClientStateSingleton.Instance.DcsPlayerRadioInfo.radios.Length;
                JitterBufferProviderInterface = new JitterBufferProviderInterface[radios];

                for (int i = 0; i < radios; i++)
                {
                    // Ensure float @ 48k mono throughout the mixing/jitter pipeline
                    // Use a slightly smaller priming target (3 frames = 60ms) to reduce startup latency. This is configurable in the JBP ctor.
                    JitterBufferProviderInterface[i] =
                        new JitterBufferProviderInterface(WaveFormat.CreateIeeeFloatWaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1), 3);
                    JitterBufferProviderInterface[i].SetRadioId(i); // Set radio ID for diagnostics
                    Logger.Debug($"ClientAudioProvider ctor: hash={this.GetHashCode()} created JBP for radio {i} -> jbpHash={JitterBufferProviderInterface[i].GetHashCode()}");
                }
            }

            _decoder = OpusDecoder.Create(AudioManager.OUTPUT_SAMPLE_RATE, 1);
            _decoder.ForwardErrorCorrection = false; // keep off unless using FEC properly
            // Increase decoded output buffer to safely hold up to 120ms of float audio
            _decoder.MaxDataBytes = (int)(AudioManager.OUTPUT_SAMPLE_RATE * 0.12 * sizeof(float) * _decoder.OutputChannels);
        }

        public JitterBufferProviderInterface[] JitterBufferProviderInterface { get; }

        public long LastUpdate { get; private set; }

        //is it a new transmission?
        public bool LikelyNewTransmission()
        {
            if (passThrough)
            {
                return false;
            }

            long now = DateTime.Now.Ticks;
            // 400 ms since last update
            return (now - LastUpdate) > 4000000;
        }

        public JitterBufferAudio AddClientAudioSamples(ClientAudio audio)
        {
            Logger.Debug($"ClientAudioProvider.AddClientAudioSamples: client={audio.ClientGuid}, seq={audio.Sequence}, passThrough={passThrough}, receivedRadio={audio.ReceivedRadio}, encodedLen={audio.EncodedAudio?.Length ?? 0}");
            if (audio.EncodedAudio == null || audio.EncodedAudio.Length < 5)
            {
                return null;
            }
            
            bool newTransmission = LikelyNewTransmission();
            
            // Start diagnostic recording on new transmission
            if (newTransmission)
            {
                StopDiagnosticRecording(); // Stop any previous recording
                StartDiagnosticRecording(audio);
            }

            // Proper handling of DecodeFloat: returns byte[] containing floats; decodedLength is in bytes
            var decodedBytes = _decoder.DecodeFloat(
                audio.EncodedAudio,
                audio.EncodedAudio.Length,
                out var decodedLength,
                false
            );

            if (decodedBytes == null || decodedLength <= 0 || (decodedLength % sizeof(float)) != 0)
            {
                Logger.Warn($"Failed to decode or misaligned length: decodedLength={decodedLength}");
                return null;
            }

            int expected = AudioManager.OUTPUT_SEGMENT_FRAMES; // 960 for 20ms@48k
            int sampleCount = decodedLength / sizeof(float);
            if (sampleCount <= 0)
            {
                Logger.Warn("Decoded zero samples; dropping packet");
                return null;
            }

            var tmp = new float[sampleCount];
            Buffer.BlockCopy(decodedBytes, 0, tmp, 0, decodedLength);

            // sanitize
            for (var i = 0; i < tmp.Length; i++)
            {
                var s = tmp[i];
                if (float.IsNaN(s) || float.IsInfinity(s)) tmp[i] = 0f;
                else if (s > 1f) tmp[i] = 1f;
                else if (s < -1f) tmp[i] = -1f;
            }

            Logger.Debug($"ClientAudioProvider: decoded sampleCount={sampleCount}, expected={expected}, sequence={audio.Sequence}, client={audio.ClientGuid}");

            // Normalize exactly to 960 samples
            var normalized = new float[expected];
            if (sampleCount >= expected)
            {
                Array.Copy(tmp, 0, normalized, 0, expected);
            }
            else
            {
                Array.Copy(tmp, 0, normalized, 0, sampleCount);
                Array.Clear(normalized, sampleCount, expected - sampleCount);
            }

            audio.PcmAudioFloat = normalized;

            AdjustVolumeForLoss(audio);

            Logger.Debug($"ClientAudioProvider: normalized to {audio.PcmAudioFloat.Length} samples, firstSamples={audio.PcmAudioFloat[0]:0.000},{audio.PcmAudioFloat[1]:0.000}");
            
            // Write decoded and normalized samples to diagnostic wav file
            WriteDiagnosticSamples(audio.PcmAudioFloat, audio.PcmAudioFloat.Length);

            if (newTransmission && SILENCE_PAD > 0)
            {
                int silencePadSamples = (AudioManager.OUTPUT_SAMPLE_RATE / 1000) * SILENCE_PAD;
                var newAudio = new float[audio.PcmAudioFloat.Length + silencePadSamples];
                Array.Copy(audio.PcmAudioFloat, 0, newAudio, silencePadSamples, audio.PcmAudioFloat.Length);
                audio.PcmAudioFloat = newAudio;
            }

            LastUpdate = DateTime.Now.Ticks;

            if (audio.ClientGuid == ClientStateSingleton.Instance.ClientId)
            {
                Logger.Debug($"ClientAudioProvider: packet is from local client {audio.ClientGuid}. passThrough={passThrough}");
                if (passThrough)
                {
                    Logger.Debug($"ClientAudioProvider: returning JitterBufferAudio for pass-through local packet seq={audio.Sequence}");
                    return new JitterBufferAudio
                    {
                        Audio = audio.PcmAudioFloat,
                        PacketNumber = audio.Sequence,
                        Modulation = (RadioInformation.Modulation)audio.Modulation,
                        ReceivedRadio = audio.ReceivedRadio,
                        Volume = audio.Volume,
                        IsSecondary = audio.IsSecondary,
                        Frequency = audio.Frequency,
                        NoAudioEffects = audio.NoAudioEffects,
                        Guid = audio.ClientGuid,
                    };
                }

                Logger.Debug($"ClientAudioProvider: dropping local packet because provider is not passThrough seq={audio.Sequence}");
                return null;
            }

            if (!passThrough)
            {
                var jbp = JitterBufferProviderInterface[audio.ReceivedRadio];
                
                jbp.AddSamples(new JitterBufferAudio
                {
                    Audio = audio.PcmAudioFloat,
                    PacketNumber = audio.Sequence,
                    Modulation = (RadioInformation.Modulation)audio.Modulation,
                    ReceivedRadio = audio.ReceivedRadio,
                    Volume = audio.Volume,
                    IsSecondary = audio.IsSecondary,
                    Frequency = audio.Frequency,
                    NoAudioEffects = audio.NoAudioEffects,
                    Guid = audio.ClientGuid,
                });

                Logger.Debug($"Added to jitter buffer[{audio.ReceivedRadio}]: {audio.PcmAudioFloat.Length} samples, first: {audio.PcmAudioFloat[0]:0.000}");

                return null;
            }

            Logger.Debug($"ClientAudioProvider: passThrough provider returning audio for client {audio.ClientGuid} seq={audio.Sequence}");
            return new JitterBufferAudio
            {
                Audio = audio.PcmAudioFloat,
                PacketNumber = audio.Sequence,
                Modulation = (RadioInformation.Modulation)audio.Modulation,
                ReceivedRadio = audio.ReceivedRadio,
                Volume = audio.Volume,
                IsSecondary = audio.IsSecondary,
                Frequency = audio.Frequency,
                NoAudioEffects = audio.NoAudioEffects,
                Guid = audio.ClientGuid
            };
        }

        private void AdjustVolumeForLoss(ClientAudio clientAudio)
        {
            return;
        }

        private float RandomFloat()
        {
            float f = ((float)_random.Next(-32768 / 8, 32768 / 8)) / 32768f;
            if (f > 1) f = 1;
            if (f < -1) f = -1;
            return f;
        }

        private void StartDiagnosticRecording(ClientAudio audio)
        {
            try
            {
                lock (_diagnosticLock)
                {
                    // Create diagnostics folder if it doesn't exist
                    if (!Directory.Exists(DiagnosticsFolder))
                    {
                        Directory.CreateDirectory(DiagnosticsFolder);
                    }

                    _transmissionStartTime = DateTime.Now;
                    var filename = $"TX_{_transmissionStartTime:yyyyMMdd_HHmmss_fff}_Client_{audio.ClientGuid.ToString().Substring(0, 8)}_Radio_{audio.ReceivedRadio}_Seq_{audio.Sequence}.wav";
                    var filepath = Path.Combine(DiagnosticsFolder, filename);

                    // Create wave file writer: mono float @ 48kHz
                    var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1);
                    _diagnosticWriter = new WaveFileWriter(filepath, waveFormat);

                    Logger.Info($"Started diagnostic recording: {filepath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start diagnostic recording");
            }
        }

        private void WriteDiagnosticSamples(float[] samples, int sampleCount)
        {
            try
            {
                lock (_diagnosticLock)
                {
                    if (_diagnosticWriter != null && samples != null && sampleCount > 0)
                    {
                        _diagnosticWriter.WriteSamples(samples, 0, sampleCount);
                        _diagnosticWriter.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write diagnostic samples");
            }
        }

        private void StopDiagnosticRecording()
        {
            try
            {
                lock (_diagnosticLock)
                {
                    if (_diagnosticWriter != null)
                    {
                        var duration = DateTime.Now - _transmissionStartTime;
                        Logger.Info($"Stopped diagnostic recording. Duration: {duration.TotalSeconds:F2}s");
                        _diagnosticWriter.Dispose();
                        _diagnosticWriter = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to stop diagnostic recording");
            }
        }

        ~ClientAudioProvider()
        {
            StopDiagnosticRecording();
            _decoder?.Dispose();
            _decoder = null;
        }
    }
}