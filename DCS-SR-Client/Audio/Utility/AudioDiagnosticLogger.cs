using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;

namespace Vanguard.VCS.Client.Audio.Utility
{
    /// <summary>
    /// Manages diagnostic logging for audio flow analysis with WAV file capture
    /// </summary>
    public class AudioDiagnosticLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static AudioDiagnosticLogger _instance;
        private static readonly object _lock = new object();

        private readonly string _diagnosticsFolder;
        private readonly string _wavFilesFolder;
        private StreamWriter _csvWriter;
        private readonly BlockingCollection<AudioDiagnosticEntry> _queue;
        private Thread _writerThread;
        private bool _isRunning;
        private DateTime _sessionStartTime;
        
        // WAV capture
        private bool _captureWavFiles = true; // Auto-enabled for diagnostics
        private readonly WaveFormat _wavFormat = new WaveFormat(48000, 16, 1); // Mono 48kHz 16-bit
        private readonly ConcurrentDictionary<string, WaveFileWriter> _activeWaveWriters = 
            new ConcurrentDictionary<string, WaveFileWriter>();
        private readonly ConcurrentDictionary<string, TransmissionInfo> _activeTransmissions = 
            new ConcurrentDictionary<string, TransmissionInfo>();
        private Timer _transmissionTimeoutTimer;
        private const int TRANSMISSION_TIMEOUT_MS = 500; // Close WAV if no samples for 500ms
        
        // Per-client sequence and timing tracking
        private readonly ConcurrentDictionary<string, ClientPacketTracker> _packetTrackers = 
            new ConcurrentDictionary<string, ClientPacketTracker>();
        
        private class ClientPacketTracker
        {
            public ulong LastSequence { get; set; }
            public DateTime LastPacketTime { get; set; }
            public int DuplicateCount { get; set; }
            public int GapCount { get; set; }
            public int TotalPackets { get; set; }
        }
        
        private class TransmissionInfo
        {
            public string ClientGuid { get; set; }
            public int RadioId { get; set; }
            public DateTime StartTime { get; set; }
            public DateTime LastPacketTime { get; set; }
            public int PacketCount { get; set; }
            public List<int> Sequences { get; set; } = new List<int>();
            public string WavFilePath { get; set; }
            public int TotalSamplesWritten { get; set; }
        }

        private AudioDiagnosticLogger()
        {
            _diagnosticsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "VCS_AudioDiagnostics"
            );
            _wavFilesFolder = Path.Combine(_diagnosticsFolder, "WAV_Captures");
            _queue = new BlockingCollection<AudioDiagnosticEntry>(10000);
        }

        public static AudioDiagnosticLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new AudioDiagnosticLogger();
                        }
                    }
                }
                return _instance;
            }
        }
        
        public bool IsRunning => _isRunning;
        public bool IsCapturingWav => _captureWavFiles;

        public void StartSession(bool captureWavFiles = true)
        {
            try
            {
                if (_isRunning)
                {
                    Logger.Warn("Diagnostic logging session already running");
                    return;
                }

                _captureWavFiles = captureWavFiles;

                if (!Directory.Exists(_diagnosticsFolder))
                {
                    Directory.CreateDirectory(_diagnosticsFolder);
                }
                
                if (_captureWavFiles && !Directory.Exists(_wavFilesFolder))
                {
                    Directory.CreateDirectory(_wavFilesFolder);
                }

                _sessionStartTime = DateTime.Now;
                var filename = $"AudioDiagnostics_{_sessionStartTime:yyyyMMdd_HHmmss}.csv";
                var filepath = Path.Combine(_diagnosticsFolder, filename);

                _csvWriter = new StreamWriter(filepath, false, Encoding.UTF8);
                _csvWriter.WriteLine(AudioDiagnosticEntry.CsvHeader());
                _csvWriter.Flush();

                _isRunning = true;
                _writerThread = new Thread(WriterThreadProc)
                {
                    Name = "AudioDiagnosticWriter",
                    IsBackground = true
                };
                _writerThread.Start();
                
                // Start transmission timeout monitoring
                if (_captureWavFiles)
                {
                    _transmissionTimeoutTimer = new Timer(CheckTransmissionTimeouts, null, 
                        TRANSMISSION_TIMEOUT_MS, TRANSMISSION_TIMEOUT_MS);
                }

                Logger.Info($"Started diagnostic logging session: {filepath}, WAV capture: {_captureWavFiles}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start diagnostic logging session");
            }
        }
        
        private void CheckTransmissionTimeouts(object state)
        {
            try
            {
                var now = DateTime.Now;
                var keysToRemove = new List<string>();
                
                foreach (var kvp in _activeTransmissions)
                {
                    var info = kvp.Value;
                    var timeSinceLastPacket = (now - info.LastPacketTime).TotalMilliseconds;
                    
                    if (timeSinceLastPacket > TRANSMISSION_TIMEOUT_MS)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var key in keysToRemove)
                {
                    if (_activeTransmissions.TryGetValue(key, out var info))
                    {
                        CloseTransmissionCapture(info.ClientGuid, info.RadioId);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking transmission timeouts");
            }
        }

        public void StopSession()
        {
            try
            {
                if (!_isRunning)
                {
                    return;
                }

                Logger.Info("Stopping diagnostic logging session...");

                _isRunning = false;
                _queue.CompleteAdding();
                
                // Stop timeout timer
                _transmissionTimeoutTimer?.Dispose();
                _transmissionTimeoutTimer = null;
                
                // Close all active WAV writers before stopping the thread
                CloseAllWavCaptures();

                if (_writerThread != null && _writerThread.IsAlive)
                {
                    _writerThread.Join(TimeSpan.FromSeconds(5));
                }
                
                // Close all active WAV writers
                foreach (var kvp in _activeWaveWriters)
                {
                    try
                    {
                        kvp.Value?.Dispose();
                        Logger.Debug($"Closed WAV file: {kvp.Key}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error closing WAV writer: {kvp.Key}");
                    }
                }
                _activeWaveWriters.Clear();
                _activeTransmissions.Clear();

                if (_csvWriter != null)
                {
                    _csvWriter.Flush();
                    _csvWriter.Close();
                    _csvWriter.Dispose();
                    _csvWriter = null;
                }

                var duration = DateTime.Now - _sessionStartTime;
                Logger.Info($"Stopped diagnostic logging session. Duration: {duration.TotalSeconds:F2}s, WAV files: {_activeWaveWriters.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to stop diagnostic logging session");
            }
        }

        public void LogEntry(AudioDiagnosticEntry entry)
        {
            if (!_isRunning || entry == null)
            {
                return;
            }

            try
            {
                if (!_queue.TryAdd(entry, TimeSpan.FromMilliseconds(100)))
                {
                    Logger.Warn("Diagnostic log queue full, dropping entry");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to queue diagnostic entry");
            }
        }

        private void WriterThreadProc()
        {
            try
            {
                while (_isRunning || _queue.Count > 0)
                {
                    if (_queue.TryTake(out var entry, TimeSpan.FromMilliseconds(100)))
                    {
                        try
                        {
                            _csvWriter?.WriteLine(entry.ToCsv());
                            
                            // Flush periodically
                            if (_queue.Count == 0)
                            {
                                _csvWriter?.Flush();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to write diagnostic entry");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Diagnostic writer thread failed");
            }
        }

        public void LogPacketReceived(Guid clientGuid, ulong sequence, int radioId, int encodedLength)
        {
            var now = DateTime.Now;
            var key = $"{clientGuid}_R{radioId}";
            
            // Get or create tracker for this client/radio
            var tracker = _packetTrackers.GetOrAdd(key, _ => new ClientPacketTracker 
            { 
                LastPacketTime = DateTime.MinValue,
                LastSequence = 0 
            });
            
            // Calculate inter-packet timing
            double interPacketMs = 0;
            if (tracker.LastPacketTime != DateTime.MinValue)
            {
                interPacketMs = (now - tracker.LastPacketTime).TotalMilliseconds;
            }
            
            // Check for sequence gaps or duplicates
            long sequenceGap = 0;
            string notes = $"EncodedBytes={encodedLength}";
            
            if (tracker.TotalPackets > 0)
            {
                if (sequence == tracker.LastSequence)
                {
                    // Duplicate packet
                    tracker.DuplicateCount++;
                    notes += $", DUPLICATE(#{tracker.DuplicateCount})";
                    Logger.Warn($"Duplicate packet: Client={clientGuid}, Radio={radioId}, Seq={sequence}");
                }
                else if (sequence < tracker.LastSequence)
                {
                    // Out of order packet
                    notes += $", OUT_OF_ORDER(expected>{tracker.LastSequence})";
                    Logger.Warn($"Out-of-order packet: Client={clientGuid}, Radio={radioId}, Seq={sequence}, Last={tracker.LastSequence}");
                }
                else
                {
                    // Calculate gap
                    var expectedSeq = tracker.LastSequence + 1;
                    sequenceGap = (long)(sequence - expectedSeq);
                    if (sequenceGap > 0)
                    {
                        tracker.GapCount++;
                        notes += $", GAP({sequenceGap} packets missing)";
                        Logger.Warn($"Sequence gap: Client={clientGuid}, Radio={radioId}, Expected={expectedSeq}, Got={sequence}, Gap={sequenceGap}");
                    }
                }
            }
            
            // Update tracker
            tracker.LastSequence = sequence;
            tracker.LastPacketTime = now;
            tracker.TotalPackets++;
            
            LogEntry(new AudioDiagnosticEntry
            {
                Timestamp = now,
                TicksUtc = DateTime.UtcNow.Ticks,
                Stage = "Received",
                ClientGuid = clientGuid,
                Sequence = sequence,
                RadioId = radioId,
                SampleCount = encodedLength,
                InterPacketMs = interPacketMs,
                SequenceGap = sequenceGap,
                Notes = notes
            });
        }

        public void LogPacketDecoded(Guid clientGuid, ulong sequence, int radioId, float[] samples, int sampleCount)
        {
            var entry = new AudioDiagnosticEntry
            {
                Timestamp = DateTime.Now,
                TicksUtc = DateTime.UtcNow.Ticks,
                Stage = "Decoded",
                ClientGuid = clientGuid,
                Sequence = sequence,
                RadioId = radioId,
                SampleCount = sampleCount
            };

            if (samples != null && sampleCount > 0)
            {
                entry.FirstSample = samples[0];
                float rms, maxAbs;
                ComputeAudioStats(samples, sampleCount, out rms, out maxAbs);
                entry.Rms = rms;
                entry.MaxAbs = maxAbs;
            }

            LogEntry(entry);
        }

        public void LogAddedToJitter(Guid clientGuid, ulong sequence, int radioId, float[] samples, int sampleCount, 
            int queueDepth, bool primed, int availSamples)
        {
            var entry = new AudioDiagnosticEntry
            {
                Timestamp = DateTime.Now,
                TicksUtc = DateTime.UtcNow.Ticks,
                Stage = "AddedToJitter",
                ClientGuid = clientGuid,
                Sequence = sequence,
                RadioId = radioId,
                SampleCount = sampleCount,
                JitterQueueDepth = queueDepth,
                JitterPrimed = primed,
                JitterAvailableSamples = availSamples
            };

            if (samples != null && sampleCount > 0)
            {
                entry.FirstSample = samples[0];
                float rms, maxAbs;
                ComputeAudioStats(samples, sampleCount, out rms, out maxAbs);
                entry.Rms = rms;
                entry.MaxAbs = maxAbs;
                
                // Capture audio to WAV file
                if (_captureWavFiles)
                {
                    CaptureTransmissionAudio(clientGuid.ToString(), radioId, (int)sequence, samples, sampleCount);
                }
            }

            LogEntry(entry);
        }
        
        /// <summary>
        /// Captures audio samples to a WAV file for transmission analysis
        /// </summary>
        public void CaptureTransmissionAudio(string clientId, int radioId, int sequence, float[] samples, int sampleCount)
        {
            try
            {
                string key = $"{clientId}_R{radioId}";
                
                // Get or create WAV writer
                if (!_activeWaveWriters.TryGetValue(key, out var writer))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var filename = $"TX_{clientId.Substring(0, 8)}_Radio{radioId}_{timestamp}.wav";
                    var filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    // Create mono 48kHz WAV file (standard VCS audio format)
                    var waveFormat = new WaveFormat(48000, 16, 1); // 48kHz, 16-bit, mono
                    writer = new WaveFileWriter(filepath, waveFormat);
                    
                    if (_activeWaveWriters.TryAdd(key, writer))
                    {
                        var info = new TransmissionInfo
                        {
                            ClientGuid = clientId,
                            RadioId = radioId,
                            StartTime = DateTime.Now,
                            LastPacketTime = DateTime.Now,
                            WavFilePath = filepath
                        };
                        _activeTransmissions.TryAdd(key, info);
                        
                        Logger.Info($"Started WAV capture: {filename}");
                    }
                    else
                    {
                        writer?.Dispose();
                        _activeWaveWriters.TryGetValue(key, out writer);
                    }
                }
                
                // Write samples (convert from float to 16-bit PCM)
                if (writer != null && samples != null && sampleCount > 0)
                {
                    int safeCount = Math.Min(sampleCount, samples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, samples[i])); // Clamp
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    writer.WriteSamples(pcmSamples, 0, safeCount);
                    
                    // Update transmission info
                    if (_activeTransmissions.TryGetValue(key, out var info))
                    {
                        info.PacketCount++;
                        info.TotalSamplesWritten += safeCount;
                        info.LastPacketTime = DateTime.Now; // Update activity timestamp
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing transmission audio for {clientId} radio {radioId}");
            }
        }
        
        /// <summary>
        /// Closes the WAV file for a specific transmission (call when transmission ends)
        /// </summary>
        public void CloseTransmissionCapture(string clientId, int radioId)
        {
            if (!_captureWavFiles) return;
            
            try
            {
                string key = $"{clientId}_R{radioId}";
                
                if (_activeWaveWriters.TryRemove(key, out var writer))
                {
                    writer?.Dispose();
                    
                    if (_activeTransmissions.TryRemove(key, out var info))
                    {
                        Logger.Info($"Closed WAV capture: {Path.GetFileName(info.WavFilePath)}, " +
                                  $"Packets: {info.PacketCount}, Samples: {info.TotalSamplesWritten}, " +
                                  $"Duration: {(DateTime.Now - info.StartTime).TotalSeconds:F2}s");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error closing transmission capture for {clientId} radio {radioId}");
            }
        }

        public void LogReadFromJitter(int radioId, float[] samples, int sampleCount, 
            int queueDepth, bool primed, int availSamples, string notes = null)
        {
            var entry = new AudioDiagnosticEntry
            {
                Timestamp = DateTime.Now,
                TicksUtc = DateTime.UtcNow.Ticks,
                Stage = "ReadFromJitter",
                ClientGuid = Guid.Empty,
                Sequence = 0,
                RadioId = radioId,
                SampleCount = sampleCount,
                JitterQueueDepth = queueDepth,
                JitterPrimed = primed,
                JitterAvailableSamples = availSamples,
                Notes = notes
            };

            if (samples != null && sampleCount > 0)
            {
                entry.FirstSample = samples[0];
                float rms, maxAbs;
                ComputeAudioStats(samples, sampleCount, out rms, out maxAbs);
                entry.Rms = rms;
                entry.MaxAbs = maxAbs;
            }

            LogEntry(entry);
        }

        private void ComputeAudioStats(float[] samples, int count, out float rms, out float maxAbs)
        {
            double sumSq = 0;
            float max = 0f;
            int safeCount = Math.Min(count, samples?.Length ?? 0);

            for (int i = 0; i < safeCount; i++)
            {
                var v = samples[i];
                sumSq += v * v;
                var a = Math.Abs(v);
                if (a > max) max = a;
            }

            rms = safeCount > 0 ? (float)Math.Sqrt(sumSq / safeCount) : 0f;
            maxAbs = max;
        }

        /// <summary>
        /// Capture audio at the point it comes OUT of the jitter buffer (before effects)
        /// </summary>
        public void CaptureJitterBufferOutput(string clientId, int radioId, int sequence, float[] monoSamples, int sampleCount, 
            int queuedPackets, int availableSamples)
        {
            if (!_captureWavFiles || monoSamples == null || sampleCount <= 0) return;

            try
            {
                string key = $"JitterOut_{clientId}_R{radioId}";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var shortClientId = clientId.Length >= 8 ? clientId.Substring(0, 8) : clientId;
                    string filename = $"JITTER_{timestamp}_Client_{shortClientId}_Radio_{radioId}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    var writer = new WaveFileWriter(filepath, _wavFormat);
                    _activeWaveWriters[key] = writer;
                    
                    var info = new TransmissionInfo
                    {
                        ClientGuid = clientId,
                        RadioId = radioId,
                        WavFilePath = filepath,
                        StartTime = DateTime.Now,
                        LastPacketTime = DateTime.Now
                    };
                    _activeTransmissions[key] = info;
                    
                    Logger.Info($"Started JitterOut WAV capture: {filename}");
                }
                
                var writer2 = _activeWaveWriters[key];
                if (writer2 != null && sampleCount > 0)
                {
                    int safeCount = Math.Min(sampleCount, monoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, monoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    writer2.WriteSamples(pcmSamples, 0, safeCount);
                    
                    if (_activeTransmissions.TryGetValue(key, out var info))
                    {
                        info.PacketCount++;
                        info.TotalSamplesWritten += safeCount;
                        info.LastPacketTime = DateTime.Now;
                    }
                    
                    // Log this capture to CSV
                    LogEntry(new AudioDiagnosticEntry
                    {
                        Timestamp = DateTime.Now,
                        TicksUtc = DateTime.UtcNow.Ticks,
                        Stage = "JitterOutput",
                        ClientGuid = Guid.TryParse(clientId, out var guid) ? guid : Guid.Empty,
                        Sequence = (ulong)sequence,
                        RadioId = radioId,
                        SampleCount = sampleCount,
                        JitterQueueDepth = queuedPackets,
                        JitterAvailableSamples = availableSamples,
                        Notes = "Captured from jitter buffer to WAV"
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing jitter buffer output for {clientId} radio {radioId}");
            }
        }

        /// <summary>
        /// Capture audio AFTER effects pipeline processing
        /// </summary>
        public void CaptureAfterEffects(int radioId, float[] monoSamples, int sampleCount)
        {
            if (!_captureWavFiles || monoSamples == null || sampleCount <= 0) return;

            try
            {
                string key = $"Radio{radioId}_AfterEffects";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    string filename = $"Radio{radioId}_AfterEffects_{DateTime.Now:HHmmss_fff}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    var writer = new WaveFileWriter(filepath, _wavFormat);
                    _activeWaveWriters[key] = writer;
                    
                    Logger.Debug($"Started AfterEffects WAV capture: {filename}");
                }
                
                var writer2 = _activeWaveWriters[key];
                if (writer2 != null)
                {
                    int safeCount = Math.Min(sampleCount, monoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, monoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    writer2.WriteSamples(pcmSamples, 0, safeCount);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing after-effects audio for radio {radioId}");
            }
        }

        /// <summary>
        /// Capture final mixed stereo output
        /// </summary>
        public void CaptureFinalMix(int radioId, float[] stereoSamples, int sampleCount)
        {
            if (!_captureWavFiles || stereoSamples == null || sampleCount <= 0) return;

            try
            {
                string key = $"FinalMix_R{radioId}";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var filename = $"FINALMIX_{timestamp}_Radio_{radioId}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    // Use stereo format for final mix
                    var stereoFormat = new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 16, 2);
                    var writer = new WaveFileWriter(filepath, stereoFormat);
                    _activeWaveWriters[key] = writer;
                    
                    Logger.Info($"Started FinalMix WAV capture: {filename}");
                }
                
                var writer2 = _activeWaveWriters[key];
                if (writer2 != null)
                {
                    int safeCount = Math.Min(sampleCount, stereoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, stereoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    writer2.WriteSamples(pcmSamples, 0, safeCount);
                    
                    // Log activity
                    LogEntry(new AudioDiagnosticEntry
                    {
                        Timestamp = DateTime.Now,
                        TicksUtc = DateTime.UtcNow.Ticks,
                        Stage = "FinalMixOutput",
                        ClientGuid = Guid.Empty,
                        Sequence = 0,
                        RadioId = radioId,
                        SampleCount = sampleCount / 2, // Convert stereo to mono count for logging
                        Notes = $"Stereo samples={sampleCount}"
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing final mix for radio {radioId}");
            }
        }
        
        /// <summary>
        /// Captures audio output from jitter buffer (before mixing)
        /// </summary>
        public void CaptureJitterBufferOutput(Guid clientGuid, int radioId, float[] monoSamples, int sampleCount, 
            ulong sequence, string notes = null)
        {
            if (!_captureWavFiles || !_isRunning) return;
            
            try
            {
                string key = $"JitterOut_{clientGuid}_{radioId}";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var filename = $"JitterOut_{clientGuid.ToString().Substring(0, 8)}_R{radioId}_{timestamp}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    var monoFormat = new WaveFormat(48000, 16, 1);
                    var writer = new WaveFileWriter(filepath, monoFormat);
                    _activeWaveWriters[key] = writer;
                    
                    Logger.Info($"Started JitterOut WAV capture: {filename}");
                }
                
                var wavWriter = _activeWaveWriters[key];
                if (wavWriter != null && monoSamples != null && sampleCount > 0)
                {
                    int safeCount = Math.Min(sampleCount, monoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, monoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    wavWriter.WriteSamples(pcmSamples, 0, safeCount);
                    
                    // Log this capture event
                    LogEntry(new AudioDiagnosticEntry
                    {
                        Timestamp = DateTime.Now,
                        TicksUtc = DateTime.UtcNow.Ticks,
                        Stage = "JitterBufferOutput",
                        ClientGuid = clientGuid,
                        Sequence = sequence,
                        RadioId = radioId,
                        SampleCount = sampleCount,
                        Notes = notes
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing jitter buffer output for {clientGuid} radio {radioId}");
            }
        }
        
        /// <summary>
        /// Captures audio after mixing but before resampling (RadioMixingProvider output)
        /// </summary>
        public void CaptureMixedOutput(int radioId, float[] monoSamples, int sampleCount, int sourceCount, 
            bool containsLocalAudio, string notes = null)
        {
            if (!_captureWavFiles || !_isRunning) return;
            
            try
            {
                string key = $"MixedOutput_R{radioId}";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var filename = $"MixedOutput_R{radioId}_{timestamp}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    var monoFormat = new WaveFormat(48000, 16, 1);
                    var writer = new WaveFileWriter(filepath, monoFormat);
                    _activeWaveWriters[key] = writer;
                    
                    Logger.Info($"Started MixedOutput WAV capture: {filename}");
                }
                
                var wavWriter = _activeWaveWriters[key];
                if (wavWriter != null && monoSamples != null && sampleCount > 0)
                {
                    int safeCount = Math.Min(sampleCount, monoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, monoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    wavWriter.WriteSamples(pcmSamples, 0, safeCount);
                    
                    // Log this capture event
                    LogEntry(new AudioDiagnosticEntry
                    {
                        Timestamp = DateTime.Now,
                        TicksUtc = DateTime.UtcNow.Ticks,
                        Stage = "MixedOutput",
                        ClientGuid = Guid.Empty,
                        Sequence = 0,
                        RadioId = radioId,
                        SampleCount = sampleCount,
                        Notes = $"Sources={sourceCount}, Local={containsLocalAudio}{(string.IsNullOrEmpty(notes) ? "" : ", " + notes)}"
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing mixed output for radio {radioId}");
            }
        }
        
        /// <summary>
        /// Captures audio after effects pipeline processing
        /// </summary>
        public void CaptureEffectsOutput(int radioId, float[] monoSamples, int sampleCount, string effectsApplied = null)
        {
            if (!_captureWavFiles || !_isRunning) return;
            
            try
            {
                string key = $"EffectsOut_R{radioId}";
                
                if (!_activeWaveWriters.ContainsKey(key))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    var filename = $"EFFECTS_{timestamp}_Radio_{radioId}.wav";
                    string filepath = Path.Combine(_wavFilesFolder, filename);
                    
                    var monoFormat = new WaveFormat(48000, 16, 1);
                    var writer = new WaveFileWriter(filepath, monoFormat);
                    _activeWaveWriters[key] = writer;
                    
                    Logger.Info($"Started EffectsOut WAV capture: {filename}");
                }
                
                var wavWriter = _activeWaveWriters[key];
                if (wavWriter != null && monoSamples != null && sampleCount > 0)
                {
                    int safeCount = Math.Min(sampleCount, monoSamples.Length);
                    short[] pcmSamples = new short[safeCount];
                    for (int i = 0; i < safeCount; i++)
                    {
                        float sample = Math.Max(-1f, Math.Min(1f, monoSamples[i]));
                        pcmSamples[i] = (short)(sample * short.MaxValue);
                    }
                    
                    wavWriter.WriteSamples(pcmSamples, 0, safeCount);
                    
                    // Log activity to verify capture is happening
                    LogEntry(new AudioDiagnosticEntry
                    {
                        Timestamp = DateTime.Now,
                        TicksUtc = DateTime.UtcNow.Ticks,
                        Stage = "EffectsOutput",
                        ClientGuid = Guid.Empty,
                        Sequence = 0,
                        RadioId = radioId,
                        SampleCount = sampleCount,
                        Notes = $"Captured to WAV: {effectsApplied}"
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error capturing effects output for radio {radioId}");
            }
        }
        
        /// <summary>
        /// Closes WAV capture for a specific key pattern (e.g., close all files for a radio)
        /// </summary>
        public void CloseWavCaptureByPattern(string keyPattern)
        {
            if (!_captureWavFiles) return;
            
            try
            {
                var keysToClose = _activeWaveWriters.Keys.Where(k => k.Contains(keyPattern)).ToList();
                
                foreach (var key in keysToClose)
                {
                    if (_activeWaveWriters.TryRemove(key, out var writer))
                    {
                        writer?.Dispose();
                        Logger.Info($"Closed WAV capture: {key}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error closing WAV captures for pattern: {keyPattern}");
            }
        }
        
        /// <summary>
        /// Closes all active WAV captures
        /// </summary>
        private void CloseAllWavCaptures()
        {
            try
            {
                Logger.Info($"Closing {_activeWaveWriters.Count} active WAV captures...");
                
                foreach (var kvp in _activeWaveWriters.ToList())
                {
                    try
                    {
                        kvp.Value?.Flush();
                        kvp.Value?.Dispose();
                        
                        if (_activeTransmissions.TryGetValue(kvp.Key, out var info))
                        {
                            Logger.Info($"Closed WAV: {Path.GetFileName(info.WavFilePath)}, " +
                                      $"Packets: {info.PacketCount}, Samples: {info.TotalSamplesWritten}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error disposing WAV writer: {kvp.Key}");
                    }
                }
                
                _activeWaveWriters.Clear();
                _activeTransmissions.Clear();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error closing all WAV captures");
            }
        }
    }
}

