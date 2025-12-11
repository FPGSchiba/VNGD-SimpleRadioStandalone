using System;
using FragLabs.Audio.Codecs;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
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

        private readonly bool passThrough;

        public ClientAudioProvider(bool passThrough = false)
        {
            this.passThrough = passThrough;

            if (!passThrough)
            {
                var radios = ClientStateSingleton.Instance.DcsPlayerRadioInfo.radios.Length;
                JitterBufferProviderInterface = new JitterBufferProviderInterface[radios];

                for (int i = 0; i < radios; i++)
                {
                    // Ensure float @ 48k mono throughout the mixing/jitter pipeline
                    JitterBufferProviderInterface[i] =
                        new JitterBufferProviderInterface(WaveFormat.CreateIeeeFloatWaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));
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
                Logger.Warn($"Dropping too-small opus packet: len={audio.EncodedAudio?.Length ?? 0}");
                return null;
            }
            
            bool newTransmission = LikelyNewTransmission();

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
                JitterBufferProviderInterface[audio.ReceivedRadio].AddSamples(new JitterBufferAudio
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

        ~ClientAudioProvider()
        {
            _decoder?.Dispose();
            _decoder = null;
        }
    }
}