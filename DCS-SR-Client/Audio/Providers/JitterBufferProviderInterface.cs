using System;
using System.Collections.Generic;
using NAudio.Utils;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Common.Helpers;

namespace Vanguard.VCS.Client.Audio.Providers
{
    public class JitterBufferProviderInterface
    {
        private readonly CircularFloatBuffer _circularBuffer;

        public static readonly int MAXIMUM_BUFFER_SIZE_MS = 2500;

        // FIXED: Use correct frame size for 20ms at 48kHz = 960 samples
        private readonly float[] _silence = new float[AudioManager.OUTPUT_SAMPLE_RATE * 20 / 1000]; // 20ms silence

        private readonly LinkedList<JitterBufferAudio> _bufferedAudio = new LinkedList<JitterBufferAudio>();

        private ulong _lastRead; // gives current index - unsigned as it'll loops eventually

        private readonly object _lock = new object();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        // Throttled logging counters to reduce spam
        private int _logWriteCounter;
        private int _logReadCounter;

        private const int PrimingMs = 50; // 50ms priming time
        private bool _primed = false;
        private int _availableSamples = 0; // How many samples are currently available in the buffer

        private DeJitteredTransmission lastTransmission;

        private float[] returnBuffer;

        public JitterBufferProviderInterface(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;

            _circularBuffer = new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * 3);//3 seconds worth of audio

            Array.Clear(_silence, 0, _silence.Length);

            // Debug log to verify silence buffer size
            Logger.Debug($"JitterBuffer initialized: silence buffer = {_silence.Length} samples ({_silence.Length * 1000.0 / AudioManager.OUTPUT_SAMPLE_RATE:0.0}ms)");
        }

        public WaveFormat WaveFormat { get; }

        public DeJitteredTransmission Read(int count)
        {
            _logReadCounter++;
            if (_primed)
            {
                Logger.Debug($"JitterBuffer Read called: count={count}, available={_availableSamples}, primed={_primed}");
                _logReadCounter = 0;
            }
            
            returnBuffer = BufferHelpers.Ensure(returnBuffer, count);
            
            var read = 0;
            lock (_lock)
            {
                do
                {
                    // 1) Try to read what’s already buffered
                    var took = _circularBuffer.Read(returnBuffer, read, count - read);
                    if (took > 0)
                    {
                        read += took;
                        _availableSamples = Math.Max(0, _availableSamples - took);
                    }

                    // 2) If we still need more
                    if (read < count)
                    {
                        // If we are not primed yet, fill the remainder with silence and return.
                        if (!_primed)
                        {
                            Array.Clear(returnBuffer, read, count - read);
                            read = count;
                            break;
                        }

                        if (_bufferedAudio.Count == 0)
                        {
                            // Nothing queued: zero-fill remainder to avoid stale repetition
                            Array.Clear(returnBuffer, read, count - read);
                            read = count;
                            break;
                        }

                        // Pull next packet, write to circular buffer
                        var audio = _bufferedAudio.First.Value;
                        _bufferedAudio.RemoveFirst();

                        lastTransmission = new DeJitteredTransmission()
                        {
                            Modulation = audio.Modulation,
                            Frequency = audio.Frequency,
                            IsSecondary = audio.IsSecondary,
                            ReceivedRadio = audio.ReceivedRadio,
                            Volume = audio.Volume,
                            NoAudioEffects = audio.NoAudioEffects,
                            Guid = audio.Guid,
                        };

                        if (_lastRead == 0)
                            _lastRead = audio.PacketNumber;
                        else
                        {
                            if (_lastRead + 1 < audio.PacketNumber)
                            {
                                var missing = audio.PacketNumber - (_lastRead + 1);

                                if (missing <= 4)
                                {
                                    var fill = Math.Min(missing, 4);
                                    for (var i = 0; i < (int)fill; i++)
                                    {
                                        _circularBuffer.Write(_silence, 0, _silence.Length);
                                        _availableSamples += _silence.Length;
                                    }
                                }
                            }
                            _lastRead = audio.PacketNumber;
                        }

                        if (audio.Audio != null && audio.Audio.Length > 0)
                        {
                            // sanitize
                            for (int i = 0; i < audio.Audio.Length; i++)
                            {
                                float s = audio.Audio[i];
                                if (float.IsNaN(s) || float.IsInfinity(s)) audio.Audio[i] = 0f;
                                else if (s > 1f) audio.Audio[i] = 1f;
                                else if (s < -1f) audio.Audio[i] = -1f;
                            }

                            _circularBuffer.Write(audio.Audio, 0, audio.Audio.Length);
                            _availableSamples += audio.Audio.Length;

                            _logWriteCounter++;
                            if (audio.Audio.Length >= 4 && (_logWriteCounter % 100) == 0)
                            {
                                Logger.Debug($"Wrote to circular buffer: {audio.Audio.Length} samples, first: {audio.Audio[0]:0.000}, {audio.Audio[1]:0.000}, {audio.Audio[2]:0.000}, {audio.Audio[3]:0.000}");
                            }
                        }

                        // Priming check: once we’ve buffered enough, mark primed
                        // Priming check: count both queued and circular buffer samples
                        if (_primed) continue;
                        var primingTarget = 960; // Just 1 packet = immediate priming
                        var totalAvailable = _availableSamples + (_bufferedAudio.Count * 960);
                        if (totalAvailable < primingTarget) continue;
                        _primed = true;
                        Logger.Debug($"Jitter buffer primed in AddSamples: total={totalAvailable}, target={primingTarget}, queue={_bufferedAudio.Count}");

                        // Loop to try reading again (now that we wrote more)
                    }
                } while (read < count);
            }

            var result = new DeJitteredTransmission
            {
                Modulation = lastTransmission.Modulation,
                Frequency = lastTransmission.Frequency,
                IsSecondary = lastTransmission.IsSecondary,
                ReceivedRadio = lastTransmission.ReceivedRadio,
                Volume = lastTransmission.Volume,
                NoAudioEffects = lastTransmission.NoAudioEffects,
                Guid = lastTransmission.Guid
            };

            result.PCMAudioLength = read;
            if (read > 0)
            {
                var outCopy = new float[read];
                Array.Copy(returnBuffer, 0, outCopy, 0, read);
                result.PCMMonoAudio = outCopy;
            }
            else
            {
                result.PCMMonoAudio = null;
            }

            return result;
        }

        public void AddSamples(JitterBufferAudio jitterBufferAudio)
        {
            lock (_lock)
            {
                if (_bufferedAudio.Count == 0)
                {
                    _bufferedAudio.AddFirst(jitterBufferAudio);
                }
                else if (jitterBufferAudio.PacketNumber > _lastRead)
                {
                    var time = _bufferedAudio.Count * AudioManager.OUTPUT_AUDIO_LENGTH_MS;

                    if (time > MAXIMUM_BUFFER_SIZE_MS)
                    {
                        _bufferedAudio.Clear();
                        Logger.Warn($"Cleared Audio buffer - length was {time} ms");
                    }

                    for (var it = _bufferedAudio.First; it != null;)
                    {
                        var next = it.Next;

                        if (it.Value.PacketNumber == jitterBufferAudio.PacketNumber) return;

                        if (jitterBufferAudio.PacketNumber < it.Value.PacketNumber)
                        {
                            _bufferedAudio.AddBefore(it, jitterBufferAudio);
                            return;
                        }

                        if ((jitterBufferAudio.PacketNumber > it.Value.PacketNumber) &&
                            ((next == null) || (jitterBufferAudio.PacketNumber < next.Value.PacketNumber)))
                        {
                            _bufferedAudio.AddAfter(it, jitterBufferAudio);
                            return;
                        }

                        it = next;
                    }
                }

                // Add priming check in AddSamples - this is where packets actually arrive
                if (!_primed)
                {
                    int primingTarget = 960; // Just 1 packet = immediate priming
                    int totalAvailable = _availableSamples + (_bufferedAudio.Count * 960);
                    if (totalAvailable >= primingTarget)
                    {
                        _primed = true;
                        Logger.Debug($"Jitter buffer primed in AddSamples: total={totalAvailable}, target={primingTarget}, queue={_bufferedAudio.Count}");
                    }
                }
            }
        }
    }
}