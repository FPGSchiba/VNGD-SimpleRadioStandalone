using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Utils;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Utility;
using Vanguard.VCS.Common.DCSState;
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
        private static readonly TransmissionWavRecorder WavRecorder = TransmissionWavRecorder.Instance;

        // Track active recording for this transmission
        private string _activeRecordingKey = null;

        // Throttled logging counters to reduce spam
        private int _logWriteCounter;
        private int _logReadCounter;
        
        // Diagnostic wav recording
        private static readonly string DiagnosticsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VCS_AudioDiagnostics");
        private WaveFileWriter _jitterDiagnosticWriter;
        private object _jitterDiagnosticLock = new object();
        private DateTime _jitterTransmissionStartTime;
        private int _radioId = -1;

        // Simple in-process throttling map to reduce spam at high frequency.
        // Keyed by a short string identifying the log-site. Time is tracked in ms.
        private static readonly Dictionary<string, long> _throttleLastMs = new Dictionary<string, long>();
        private static readonly object _throttleLock = new object();

        // Helper: only emit the debug message if at least minMs milliseconds passed since last log for this key.
        private void DebugThrottled(string key, Func<string> messageFactory, int minMs = 250)
        {
            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            var should = false;
            lock (_throttleLock)
            {
                if (!_throttleLastMs.TryGetValue(key, out var last) || (now - last) >= minMs)
                {
                    _throttleLastMs[key] = now;
                    should = true;
                }
            }
            if (should && messageFactory != null && Logger.IsDebugEnabled)
            {
                Logger.Debug(messageFactory());
            }
        }

        private const int PrimingMs = 50; // 50ms priming time
        private bool _primed = false;
        private int _availableSamples = 0; // How many samples are currently available in the buffer

        private DeJitteredTransmission lastTransmission;

        private float[] returnBuffer;

        // Configurable priming frames (number of 20ms frames to buffer before priming)
        private readonly int _primingFrames;

        public JitterBufferProviderInterface(WaveFormat waveFormat, int primingFrames = 6)
        {
            WaveFormat = waveFormat;
            _circularBuffer = new CircularFloatBuffer(AudioManager.OUTPUT_SAMPLE_RATE * 3);
            Array.Clear(_silence, 0, _silence.Length);
    
            // Initialize lastTransmission with safe defaults
            lastTransmission = new DeJitteredTransmission()
            {
                Modulation = RadioInformation.Modulation.DISABLED,
                Frequency = 0,
                Volume = 1.0f,
                IsSecondary = false,
                ReceivedRadio = 0,
                NoAudioEffects = false,
                Guid = Guid.Empty
            };

            _primingFrames = primingFrames;
            DebugThrottled("JBP_Init", () => $"JitterBuffer initialized: silence buffer = {_silence.Length} samples ({_silence.Length * 1000.0 / AudioManager.OUTPUT_SAMPLE_RATE:0.0}ms), primingFrames={_primingFrames}", 5000);
        }

        public WaveFormat WaveFormat { get; }
        
        /// <summary>
        /// Gets the current number of queued packets in the jitter buffer (for diagnostics)
        /// </summary>
        public int GetQueueDepth()
        {
            lock (_lock) { return _bufferedAudio.Count; }
        }
        
        /// <summary>
        /// Returns true if the jitter buffer is primed and ready to output audio (for diagnostics)
        /// </summary>
        public bool IsPrimed()
        {
            lock (_lock) { return _primed; }
        }
        
        /// <summary>
        /// Gets the number of samples currently available in the circular buffer (for diagnostics)
        /// </summary>
        public int GetAvailableSamples()
        {
            lock (_lock) { return _availableSamples; }
        }
        
        /// <summary>
        /// Sets the radio ID for this jitter buffer (for diagnostic logging)
        /// </summary>
        public void SetRadioId(int radioId)
        {
            _radioId = radioId;
        }

        public DeJitteredTransmission Read(int count)
        {
            // coarse counter-based throttle for very frequent Read calls; increase mod to 200 and
            // increase minimum interval so we log less often during steady playback.
            if ((_logReadCounter++ % 200) == 0)
            {
                DebugThrottled("JBP_Read_Call", () => $"JBP[radio?] Read: count={count}, primed={_primed}, avail={_availableSamples}, queued={_bufferedAudio.Count}", 2000);
            }

            if (_primed)
            {
                DebugThrottled("JBP_Read_Primed", () => $"JitterBuffer Read called: count={count}, available={_availableSamples}, primed={_primed}", 3000);
                _logReadCounter = 0;
            }

            returnBuffer = BufferHelpers.Ensure(returnBuffer, count);

            var read = 0;
            lock (_lock)
            {
                
                do
                {
                    // 1) Try to read what's already buffered
                    var took = _circularBuffer.Read(returnBuffer, read, count - read);
                    if (took > 0)
                    {
                        read += took;
                        _availableSamples = Math.Max(0, _availableSamples - took);
                        
                        // Read-from-buffer events are common; only log occasionally.
                        DebugThrottled("JBP_Read_Took", () => $"JBP Read: read {took} samples from circular buffer, new avail={_availableSamples}", 5000);
                    }

                    // 2) If we still need more
                    if (read < count)
                    {
                        // If we are not primed yet, fill the remainder with silence and return.
                        if (!_primed)
                        {
                            if (_bufferedAudio.Count > 0)
                            {
                                TryPrimeIfNeeded();
                                // If priming progressed and we now have data available, loop again to consume it.
                                if (_primed || _availableSamples > 0)
                                {
                                    // continue the do/while loop to attempt reading newly-primed data
                                    continue;
                                }
                            }

                            DebugThrottled("JBP_NotPrimed", () => $"JBP Read: not primed, zero-filling {count - read} samples and returning", 5000);
                            Array.Clear(returnBuffer, read, count - read);
                            read = count;
                            break;
                        }

                        if (_bufferedAudio.Count == 0)
                        {
                            // Stop WAV recording if active (transmission ended)
                            if (!string.IsNullOrEmpty(_activeRecordingKey))
                            {
                                WavRecorder.StopRecording(_activeRecordingKey);
                                _activeRecordingKey = null;
                            }

                            // Nothing queued: zero-fill remainder to avoid stale repetition. This can
                            // happen frequently during silence - keep logs very sparse.
                            DebugThrottled("JBP_NoQueued", () => $"JBP Read: no queued packets, zero-filling {count - read} samples", 10000);
                            Array.Clear(returnBuffer, read, count - read);
                            read = count;
                            break;
                        }

                        // Pull next packet, write to circular buffer
                        var audio = _bufferedAudio.First.Value;
                        // Pulling queued packets happens at audio rate; keep the message coarse.
                        DebugThrottled("JBP_PullPacket", () => $"JBP Read: pulling queued packet pkt={audio.PacketNumber}, queuedBefore={_bufferedAudio.Count}", 5000);
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
                            ReceivedAtTicks = audio.ReceivedAtTicks
                        };

                        // Log detailed info about the packet we are about to write (throttled)
                        DebugThrottled("JBP_PacketAboutToWrite", () =>
                            $"JBP Read: preparing to write pkt={audio.PacketNumber}, radio={audio.ReceivedRadio}, len={(audio.Audio==null?0:audio.Audio.Length)}, lastRead={_lastRead}, queuedBefore={_bufferedAudio.Count}",
                            3000);

                        if (_lastRead == 0)
                            _lastRead = audio.PacketNumber;
                        else
                        {
                            if (_lastRead + 1 < audio.PacketNumber)
                            {
                                var missing = audio.PacketNumber - (_lastRead + 1);
                                int fill = (int)Math.Min(missing, 2);
                                if (_availableSamples >= 960 * 6) fill = 0;

                                DebugThrottled("JBP_MissingPackets", () => $"JBP Read: detected missing packets={missing}, will fill {fill} frames of silence", 5000);
                                for (var i = 0; i < fill; i++)
                                {
                                    DebugThrottled("JBP_WriteSilence", () => $"JBP Read: writing silence frame for missing packet pktGuess={audio.PacketNumber - (uint)(fill - i)}", 10000);
                                    int wroteSilence = _circularBuffer.Write(_silence, 0, _silence.Length);
                                    try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wroteSilence, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                                    if (wroteSilence < _silence.Length)
                                    {
                                        Logger.Warn($"JBP Read: circular buffer overflow while writing silence, wrote={wroteSilence}, expected={_silence.Length}");
                                    }
                                }
                            }
                            _lastRead = audio.PacketNumber;
                        }

                        if (audio.Audio != null && audio.Audio.Length > 0)
                        {
                            // Start WAV recording if enabled and not already recording
                            if (WavRecorder.IsEnabled && string.IsNullOrEmpty(_activeRecordingKey))
                            {
                                _activeRecordingKey = WavRecorder.StartRecording(
                                    audio.Guid, 
                                    _radioId, 
                                    audio.Frequency, 
                                    AudioManager.OUTPUT_SAMPLE_RATE, 
                                    1  // mono
                                );
                            }

                            // sanitize
                            for (int i = 0; i < audio.Audio.Length; i++)
                            {
                                float s = audio.Audio[i];
                                if (float.IsNaN(s) || float.IsInfinity(s)) audio.Audio[i] = 0f;
                                else if (s > 1f) audio.Audio[i] = 1f;
                                else if (s < -1f) audio.Audio[i] = -1f;
                            }

                            // Write to WAV recording if active
                            if (!string.IsNullOrEmpty(_activeRecordingKey))
                            {
                                WavRecorder.WriteSamples(_activeRecordingKey, audio.Audio, 0, audio.Audio.Length);
                            }

                            int wrote = _circularBuffer.Write(audio.Audio, 0, audio.Audio.Length);
                            
                            DebugThrottled("JBP_WriteResult", () => $"JBP Read: circular buffer write result pkt={audio.PacketNumber}, requested={audio.Audio.Length}, wrote={wrote}, availAfterWrite={_availableSamples}", 5000);
                            try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wrote, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                            if (wrote < audio.Audio.Length)
                            {
                                Logger.Warn($"JBP Read: circular buffer overflow while writing packet pkt={audio.PacketNumber}, wrote={wrote}, expected={audio.Audio.Length}, bufferMax={_circularBuffer.MaxLength}");
                            }

                            DebugThrottled("JBP_WrotePacket", () => $"JBP Read: wrote packet pkt={audio.PacketNumber} into circular buffer len={audio.Audio.Length}, avail={_availableSamples}", 5000);

                            _logWriteCounter++;
                            if (audio.Audio.Length >= 4 && (_logWriteCounter % 500) == 0)
                            {
                                DebugThrottled("JBP_WrotePacket_SampleDump", () => $"Wrote to circular buffer: {audio.Audio.Length} samples, first: {audio.Audio[0]:0.000}, {audio.Audio[1]:0.000}, {audio.Audio[2]:0.000}, {audio.Audio[3]:0.000}", 10000);
                            }
                        }

                        // Loop to try reading again (now that we wrote more)
                    }
                    else
                    {
                        // This branch shouldn't occur - protective debug log if it does
                        DebugThrottled("JBP_Read_UnexpectedBranch", () => $"JBP Read: unexpected branch encountered while attempting to fill read; read={read}, count={count}, avail={_availableSamples}", 10000);
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
                 Guid = lastTransmission.Guid,
                 ReceivedAtTicks = lastTransmission.ReceivedAtTicks
             };

             // Entry/exit debug for Read: report requested vs returned and queue state
             DebugThrottled("JBP_Read_Exit", () => $"JBP Read EXIT: requested={count}, returned={result.PCMAudioLength}, primed={_primed}, avail={_availableSamples}, queued={_bufferedAudio.Count}", 3000);
            
             result.PCMAudioLength = read;
             if (read > 0)
             {
                 var outCopy = new float[read];
                 Array.Copy(returnBuffer, 0, outCopy, 0, read);
                 result.PCMMonoAudio = outCopy;
                 
                 // Write samples read from jitter buffer to diagnostic file
                 WriteJitterDiagnosticSamples(outCopy, read);

                 // Capture jitter buffer output to WAV if diagnostics are enabled
                 double sumSq = 0;
                 float maxAbs = 0f;
                 float rms = (float)Math.Sqrt(sumSq / outCopy.Length);

                 // compute quick audio stats for debug (RMS, max)
                 foreach (var v in outCopy)
                 {
                     sumSq += v * v;
                     var a = Math.Abs(v);
                     if (a > maxAbs) maxAbs = a;
                 }
                 // Return-stream stats are useful but can be noisy; raise interval.
                 DebugThrottled("JBP_ReturningSamples", () =>
                 {
                     var ageMs = (DateTime.UtcNow.Ticks - result.ReceivedAtTicks) / TimeSpan.TicksPerMillisecond;
                     return $"JBP Read: returning {read} samples in DeJitteredTransmission (Guid={result.Guid}, radio={result.ReceivedRadio}) rms={rms:0.000}, max={maxAbs:0.000}, ageMs={ageMs}";
                 }, 5000);
             }
             else
             {
                 result.PCMMonoAudio = null;
                 DebugThrottled("JBP_ReturningZero", () => "JBP Read: returning 0 samples (silence)", 10000);
             }

             return result;
         }

         public void AddSamples(JitterBufferAudio jitterBufferAudio)
         {
             DebugThrottled("JBP_AddSamples_Entry", () => $"JBP AddSamples: pkt={jitterBufferAudio.PacketNumber}, radio={jitterBufferAudio.ReceivedRadio}, len={(jitterBufferAudio.Audio==null?0:jitterBufferAudio.Audio.Length)}, queuedBefore={_bufferedAudio.Count}, lastRead={_lastRead}, primed={_primed}, avail={_availableSamples}", 5000);
 
             // Log packet age (received at ticks is stamped by caller). If stamped, show age in ms.
             try
             {
                 if (jitterBufferAudio.ReceivedAtTicks > 0)
                 {
                     var ageMs = (DateTime.UtcNow.Ticks - jitterBufferAudio.ReceivedAtTicks) / TimeSpan.TicksPerMillisecond;
                     DebugThrottled("JBP_AddSamples_Age", () => $"JBP AddSamples: pkt={jitterBufferAudio.PacketNumber} receivedAgeMs={ageMs}", 5000);
                 }
             }
             catch { }

             // stamp arrival time for diagnostics
             try
             {
                 jitterBufferAudio.ReceivedAtTicks = DateTime.UtcNow.Ticks;
             }
             catch { }

             // additional audio statistics for debugging
             try
             {
                 if (jitterBufferAudio.Audio != null && jitterBufferAudio.Audio.Length > 0)
                 {
                     double sumSq = 0;
                     float maxAbs = 0f;
                     int len = Math.Min(jitterBufferAudio.Audio.Length, 480); // sample a window
                     for (int i = 0; i < len; i++)
                     {
                         var v = jitterBufferAudio.Audio[i];
                         sumSq += v * v;
                         var a = Math.Abs(v);
                         if (a > maxAbs) maxAbs = a;
                     }
                     var rms = Math.Sqrt(sumSq / len);
                     DebugThrottled("JBP_AddSamples_Stats", () => $"JBP AddSamples: audio stats pkt={jitterBufferAudio.PacketNumber} sampleCount={jitterBufferAudio.Audio.Length} sampleWindow={len} rms={rms:0.000}, max={maxAbs:0.000}", 10000);
                 }
                 else
                 {
                     DebugThrottled("JBP_AddSamples_Empty", () => $"JBP AddSamples: audio is null/empty for pkt={jitterBufferAudio.PacketNumber}", 10000);
                 }
             }
             catch (Exception ex)
             {
                 Logger.Warn(ex, "JBP AddSamples: failed to compute audio stats");
             }

             lock (_lock)
             {
                 if (_bufferedAudio.Count == 0)
                 {
                     // Start diagnostic recording on first packet of new transmission
                     StopJitterDiagnosticRecording(); // Stop any previous recording
                     StartJitterDiagnosticRecording(jitterBufferAudio.ReceivedRadio, jitterBufferAudio.Guid);
                     
                     _bufferedAudio.AddFirst(jitterBufferAudio);
                     DebugThrottled("JBP_AddSamples_First", () => $"JBP AddSamples: added as first packet pkt={jitterBufferAudio.PacketNumber}, queuedAfter={_bufferedAudio.Count}", 5000);
                     // check priming after adding the first packet
                     TryPrimeIfNeeded();
                     DebugThrottled("JBP_AddSamples_Exit", () => $"JBP AddSamples: exit queued={_bufferedAudio.Count}, primed={_primed}, avail={_availableSamples}", 5000);
                     return;
                 }
                 else if (jitterBufferAudio.PacketNumber > _lastRead)
                 {
                     var time = _bufferedAudio.Count * AudioManager.OUTPUT_AUDIO_LENGTH_MS;

                     if (time > MAXIMUM_BUFFER_SIZE_MS)
                     {
                         _bufferedAudio.Clear();
                         Logger.Warn($"Cleared Audio buffer - length was {time} ms");
                         DebugThrottled("JBP_AddSamples_Cleared", () => $"JBP AddSamples: cleared buffer due to size, inserting pkt={jitterBufferAudio.PacketNumber}", 10000);
                     }

                     for (var it = _bufferedAudio.First; it != null;)
                     {
                         var next = it.Next;

                         if (it.Value.PacketNumber == jitterBufferAudio.PacketNumber)
                         {
                             // Check priming before returning on duplicate
                             TryPrimeIfNeeded();
                             DebugThrottled("JBP_AddSamples_Duplicate", () => $"JBP AddSamples: duplicate packet pkt={jitterBufferAudio.PacketNumber} ignored", 10000);
                             return;
                         }

                         if (jitterBufferAudio.PacketNumber < it.Value.PacketNumber)
                         {
                             _bufferedAudio.AddBefore(it, jitterBufferAudio);
                             DebugThrottled("JBP_AddSamples_InsertedBefore", () => $"JBP AddSamples: inserted before pkt={it.Value.PacketNumber}, newQueued={_bufferedAudio.Count}", 5000);
                             TryPrimeIfNeeded();
                             return;
                         }

                         if ((jitterBufferAudio.PacketNumber > it.Value.PacketNumber) &&
                             ((next == null) || (jitterBufferAudio.PacketNumber < next.Value.PacketNumber)))
                         {
                             _bufferedAudio.AddAfter(it, jitterBufferAudio);
                             DebugThrottled("JBP_AddSamples_AddedAfter", () => $"JBP AddSamples: added after pkt={it.Value.PacketNumber}, newQueued={_bufferedAudio.Count}", 5000);
                             TryPrimeIfNeeded();
                             return;
                         }

                         it = next;
                     }
                 }
                 else
                 {
                     // Explicitly log and ignore packets that are older or equal to lastRead
                     DebugThrottled("JBP_AddSamples_Old", () => $"JBP AddSamples: ignoring old/duplicate packet pkt={jitterBufferAudio.PacketNumber} <= lastRead={_lastRead}", 10000);
                     TryPrimeIfNeeded();
                     DebugThrottled("JBP_AddSamples_Exit", () => $"JBP AddSamples: exit queued={_bufferedAudio.Count}, primed={_primed}, avail={_availableSamples}", 5000);
                     return;
                 }

                 // Check priming after any successful add (fallback)
                 TryPrimeIfNeeded();
                 DebugThrottled("JBP_AddSamples_Exit", () => $"JBP AddSamples: exit queued={_bufferedAudio.Count}, primed={_primed}, avail={_availableSamples}", 5000);
             }
         }

         private void TryPrimeIfNeeded()
         {
             if (_primed) return;

             // Target priming frames. Default is configurable (_primingFrames). Each frame is 20ms = 960 samples at 48kHz.
             int primingTargetSamples = 960 * _primingFrames; // primingFrames * 20ms

             int availBefore = _availableSamples;
             int packetsBefore = _bufferedAudio.Count;
             
             // Move packets from _bufferedAudio into _circularBuffer until we reach target
             while (_availableSamples < primingTargetSamples && _bufferedAudio.Count > 0)
             {
                 var audio = _bufferedAudio.First.Value;
                 DebugThrottled("JBP_TryPrime_WillPrime", () => $"JBP TryPrime: will prime packet pkt={audio.PacketNumber}, queuedBefore={_bufferedAudio.Count}, availBefore={_availableSamples}", 2000);
                 _bufferedAudio.RemoveFirst();

                 // Missing packet handling (cap to 2 frames)
                 if (_lastRead == 0)
                 {
                     _lastRead = audio.PacketNumber;
                 }
                 else
                 {
                     if (_lastRead + 1 < audio.PacketNumber)
                     {
                         var missing = audio.PacketNumber - (_lastRead + 1);
                         int fill = (int)Math.Min(missing, 2);
                         if (_availableSamples >= 960 * 6) fill = 0;

                         DebugThrottled("JBP_TryPrime_Missing", () => $"JBP TryPrime: detected missing={missing}, fillFrames={fill}", 5000);
                         for (int i = 0; i < fill; i++)
                         {
                             DebugThrottled("JBP_TryPrime_WriteSilence", () => "JBP TryPrime: writing silence frame during priming", 5000);
                             int wroteSilence = _circularBuffer.Write(_silence, 0, _silence.Length);
                             try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wroteSilence, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                             if (wroteSilence < _silence.Length)
                             {
                                 Logger.Warn($"JBP TryPrime: circular buffer overflow while writing silence, wrote={wroteSilence}, expected={_silence.Length}");
                             }
                         }
                     }
                     _lastRead = audio.PacketNumber;
                 }

                 if (audio.Audio != null && audio.Audio.Length > 0)
                 {
                     DebugThrottled("JBP_TryPrime_WritingPacket", () => $"JBP TryPrime: writing packet pkt={audio.PacketNumber} len={audio.Audio.Length} to circular buffer", 2000);
                     int wrote = _circularBuffer.Write(audio.Audio, 0, audio.Audio.Length);
                     try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wrote, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                     if (wrote < audio.Audio.Length)
                     {
                         Logger.Warn($"JBP TryPrime: circular buffer overflow while priming pkt={audio.PacketNumber}, wrote={wrote}, expected={audio.Audio.Length}, bufferMax={_circularBuffer.MaxLength}");
                     }

                     DebugThrottled("JBP_TryPrime_Progress", () => $"JBP TryPrime: wrote pkt={audio.PacketNumber}, availAfter={_availableSamples}, primingTarget={primingTargetSamples}, queuedRemaining={_bufferedAudio.Count}", 2000);

                     lastTransmission = new DeJitteredTransmission()
                     {
                         Modulation = audio.Modulation,
                         Frequency = audio.Frequency,
                         IsSecondary = audio.IsSecondary,
                         ReceivedRadio = audio.ReceivedRadio,
                         Volume = audio.Volume,
                         NoAudioEffects = audio.NoAudioEffects,
                         Guid = audio.Guid,
                        // preserve original receive timestamp so downstream can report packet age
                        ReceivedAtTicks = audio.ReceivedAtTicks
                     };
                 }
                 else
                 {
                     DebugThrottled("JBP_TryPrime_EmptyAudio", () => $"JBP TryPrime: encountered empty audio for pkt={audio.PacketNumber}, wrote silence instead", 5000);
                     int wrote = _circularBuffer.Write(_silence, 0, _silence.Length);
                     try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wrote, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                     if (wrote < _silence.Length)
                     {
                         Logger.Warn($"JBP TryPrime: circular buffer overflow while writing silence during priming, wrote={wrote}, expected={_silence.Length}");
                     }
                 }
             }

             if (_availableSamples >= primingTargetSamples)
             {
                 _primed = true;
                 int packetsUsed = packetsBefore - _bufferedAudio.Count;
                 Logger.Info($"Jitter buffer PRIMED: radio={_radioId}, avail={_availableSamples}, target={primingTargetSamples}, " +
                            $"packetsUsed={packetsUsed}, queueRemaining={_bufferedAudio.Count}, " +
                            $"primingFrames={_primingFrames}, availBefore={availBefore}");
                 DebugThrottled("JBP_PrimedState", () => $"JBP TryPrime: primed availBefore={availBefore}, availAfter={_availableSamples}, queueRemaining={_bufferedAudio.Count}", 5000);
             }
         }

         // Public debug accessor to report internal state (non-invasive)
         public string DebugState()
         {
             return $"queued={_bufferedAudio.Count}, primed={_primed}, avail={_availableSamples}, lastRead={_lastRead}";
         }

         // Force-inject decoded echo directly into the circular buffer for immediate playback.
         // This bypasses packet-order checks and is intended for test-frequency echo handling only.
         public void InjectEcho(JitterBufferAudio jitterBufferAudio)
         {
             if (jitterBufferAudio == null || jitterBufferAudio.Audio == null) return;

             lock (_lock)
             {
                 try
                 {
                     // Set lastRead to packet number to avoid old-packet logic discarding subsequent packets
                     _lastRead = jitterBufferAudio.PacketNumber;

                     // sanitize small audio arrays
                     for (int i = 0; i < jitterBufferAudio.Audio.Length; i++)
                     {
                         float s = jitterBufferAudio.Audio[i];
                         if (float.IsNaN(s) || float.IsInfinity(s)) jitterBufferAudio.Audio[i] = 0f;
                         else if (s > 1f) jitterBufferAudio.Audio[i] = 1f;
                         else if (s < -1f) jitterBufferAudio.Audio[i] = -1f;
                     }

                     // write into circular buffer and use the actual written count
                     int wrote = _circularBuffer.Write(jitterBufferAudio.Audio, 0, jitterBufferAudio.Audio.Length);

                     DebugThrottled("JBP_InjectEcho_Write", () => $"JBP InjectEcho: initial write attempt pkt={jitterBufferAudio.PacketNumber}, requested={jitterBufferAudio.Audio.Length}, wrote={wrote}", 2000);

                     // set available samples from the circular buffer's authoritative Count
                     try
                     {
                         _availableSamples = _circularBuffer.Count;
                     }
                     catch
                     {
                         // In case the CircularFloatBuffer doesn’t expose Count as public (fallback)
                         _availableSamples = Math.Min(_availableSamples + wrote, AudioManager.OUTPUT_SAMPLE_RATE * 3);
                     }

                     // If write failed due to full buffer, try to free a small amount of space and retry once
                     if (wrote == 0)
                     {
                         Logger.Warn($"JBP InjectEcho: circular buffer appears full, attempting to free space and retry");
                         try
                         {
                             int toDiscard = Math.Min(AudioManager.OUTPUT_SAMPLE_RATE / 50, _circularBuffer.Count); // discard 20ms worth
                             if (toDiscard > 0)
                             {
                                 var discardBuf = new float[toDiscard];
                                 var readDiscarded = _circularBuffer.Read(discardBuf, 0, toDiscard);
                                 DebugThrottled("JBP_InjectEcho_Discard", () => $"JBP InjectEcho: discarded {readDiscarded} old samples to make room", 2000);
                                 try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Max(0, _availableSamples - readDiscarded); }
                                 // try write again
                                 wrote = _circularBuffer.Write(jitterBufferAudio.Audio, 0, jitterBufferAudio.Audio.Length);
                                 DebugThrottled("JBP_InjectEcho_Retry", () => $"JBP InjectEcho: retry write pkt={jitterBufferAudio.PacketNumber}, wrote={wrote}", 2000);
                                 try { _availableSamples = _circularBuffer.Count; } catch { _availableSamples = Math.Min(_availableSamples + wrote, AudioManager.OUTPUT_SAMPLE_RATE * 3); }
                             }
                         }
                         catch (Exception ex)
                         {
                             Logger.Warn(ex, "JBP InjectEcho: failed while attempting to free buffer space");
                         }
                     }

                     if (wrote < jitterBufferAudio.Audio.Length)
                     {
                         Logger.Warn($"JBP InjectEcho: circular buffer overflow, wrote={wrote} expected={jitterBufferAudio.Audio.Length}, bufferMax={_circularBuffer.MaxLength}");
                     }

                     lastTransmission = new DeJitteredTransmission()
                     {
                         Modulation = jitterBufferAudio.Modulation,
                         Frequency = jitterBufferAudio.Frequency,
                         IsSecondary = jitterBufferAudio.IsSecondary,
                         ReceivedRadio = jitterBufferAudio.ReceivedRadio,
                         Volume = jitterBufferAudio.Volume,
                         NoAudioEffects = jitterBufferAudio.NoAudioEffects,
                         Guid = jitterBufferAudio.Guid,
                     };

                     DebugThrottled("JBP_InjectEcho_Result", () => $"JBP InjectEcho: injected pkt={jitterBufferAudio.PacketNumber} lenRequested={jitterBufferAudio.Audio.Length} wrote={wrote}, avail={_availableSamples}, primed={_primed}", 2000);

                     // Immediately prime so the next Read will return data
                     if (wrote > 0)
                     {
                         _primed = true;
                     }

                     DebugThrottled("JBP_InjectEcho_Exit", () => $"JBP InjectEcho: injected pkt={jitterBufferAudio.PacketNumber} lenRequested={jitterBufferAudio.Audio.Length} wrote={wrote}, avail={_availableSamples}, primed={_primed}", 2000);
                 }
                 catch (Exception ex)
                 {
                     Logger.Warn(ex, "JBP InjectEcho: failed to inject echo into circular buffer");
                 }
             }
         }

        private void StartJitterDiagnosticRecording(int radioId, Guid clientGuid)
        {
            try
            {
                lock (_jitterDiagnosticLock)
                {
                    if (!Directory.Exists(DiagnosticsFolder))
                    {
                        Directory.CreateDirectory(DiagnosticsFolder);
                    }

                    _radioId = radioId;
                    _jitterTransmissionStartTime = DateTime.Now;
                    var filename = $"JITTER_{_jitterTransmissionStartTime:yyyyMMdd_HHmmss_fff}_Client_{clientGuid.ToString().Substring(0, 8)}_Radio_{radioId}.wav";
                    var filepath = Path.Combine(DiagnosticsFolder, filename);

                    var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1);
                    _jitterDiagnosticWriter = new WaveFileWriter(filepath, waveFormat);

                    Logger.Info($"Started jitter diagnostic recording: {filepath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start jitter diagnostic recording");
            }
        }

        private void WriteJitterDiagnosticSamples(float[] samples, int sampleCount)
        {
            try
            {
                lock (_jitterDiagnosticLock)
                {
                    if (_jitterDiagnosticWriter != null && samples != null && sampleCount > 0)
                    {
                        _jitterDiagnosticWriter.WriteSamples(samples, 0, sampleCount);
                        _jitterDiagnosticWriter.Flush();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to write jitter diagnostic samples");
            }
        }

        private void StopJitterDiagnosticRecording()
        {
            try
            {
                lock (_jitterDiagnosticLock)
                {
                    if (_jitterDiagnosticWriter != null)
                    {
                        var duration = DateTime.Now - _jitterTransmissionStartTime;
                        Logger.Info($"Stopped jitter diagnostic recording. Duration: {duration.TotalSeconds:F2}s, Radio: {_radioId}");
                        _jitterDiagnosticWriter.Dispose();
                        _jitterDiagnosticWriter = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to stop jitter diagnostic recording");
            }
        }
     }
 }
