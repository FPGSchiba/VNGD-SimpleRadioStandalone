using System;
using System.Collections.Concurrent;
using System.IO;
using NAudio.Wave;
using NLog;

namespace Vanguard.VCS.Client.Audio.Utility
{
    /// <summary>
    /// Records audio transmissions to WAV files for diagnostic purposes
    /// </summary>
    public class TransmissionWavRecorder : IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly object _lock = new object();
        private static TransmissionWavRecorder _instance;

        public static TransmissionWavRecorder Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new TransmissionWavRecorder();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly ConcurrentDictionary<string, WaveFileWriter> _activeRecordings = new ConcurrentDictionary<string, WaveFileWriter>();
        private readonly ConcurrentDictionary<string, TransmissionRecordingInfo> _recordingInfo = new ConcurrentDictionary<string, TransmissionRecordingInfo>();
        private string _outputDirectory;
        private bool _isEnabled;

        private TransmissionWavRecorder()
        {
            // Default to temp directory
            _outputDirectory = Path.Combine(Path.GetTempPath(), "VCS_AudioDiagnostics");
            _isEnabled = false;
        }

        public void SetOutputDirectory(string directory)
        {
            _outputDirectory = directory;
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }
            Logger.Info($"TransmissionWavRecorder: Output directory set to {_outputDirectory}");
        }

        public void Enable()
        {
            _isEnabled = true;
            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }
            Logger.Info($"TransmissionWavRecorder: Enabled. Recording to {_outputDirectory}");
        }

        public void Disable()
        {
            _isEnabled = false;
            StopAllRecordings();
            Logger.Info("TransmissionWavRecorder: Disabled");
        }

        public bool IsEnabled => _isEnabled;

        /// <summary>
        /// Starts recording a transmission
        /// </summary>
        /// <param name="clientGuid">Client GUID</param>
        /// <param name="radioId">Radio ID</param>
        /// <param name="frequency">Frequency in Hz</param>
        /// <param name="sampleRate">Sample rate</param>
        /// <param name="channels">Number of channels (1=mono, 2=stereo)</param>
        /// <returns>Recording key for this transmission</returns>
        public string StartRecording(Guid clientGuid, int radioId, double frequency, int sampleRate, int channels)
        {
            if (!_isEnabled) return null;

            try
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string key = $"{clientGuid}_{radioId}_{timestamp}";
                string filename = $"TX_{clientGuid.ToString().Substring(0, 8)}_Radio{radioId}_{frequency / 1000000.0:F3}MHz_{timestamp}.wav";
                string filepath = Path.Combine(_outputDirectory, filename);

                var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
                var writer = new WaveFileWriter(filepath, waveFormat);

                if (_activeRecordings.TryAdd(key, writer))
                {
                    var info = new TransmissionRecordingInfo
                    {
                        ClientGuid = clientGuid,
                        RadioId = radioId,
                        Frequency = frequency,
                        StartTime = DateTime.Now,
                        FilePath = filepath,
                        SampleRate = sampleRate,
                        Channels = channels,
                        SamplesWritten = 0
                    };
                    _recordingInfo.TryAdd(key, info);

                    Logger.Info($"Started WAV recording: {filename} (key={key})");
                    return key;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start WAV recording");
            }

            return null;
        }

        /// <summary>
        /// Writes audio samples to a recording
        /// </summary>
        /// <param name="key">Recording key</param>
        /// <param name="samples">Audio samples (float)</param>
        /// <param name="offset">Offset in samples array</param>
        /// <param name="count">Number of samples to write</param>
        public void WriteSamples(string key, float[] samples, int offset, int count)
        {
            if (!_isEnabled || string.IsNullOrEmpty(key)) return;

            if (_activeRecordings.TryGetValue(key, out var writer))
            {
                try
                {
                    writer.WriteSamples(samples, offset, count);
                    
                    if (_recordingInfo.TryGetValue(key, out var info))
                    {
                        info.SamplesWritten += count;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to write samples to recording {key}");
                }
            }
        }

        /// <summary>
        /// Stops recording a transmission
        /// </summary>
        /// <param name="key">Recording key</param>
        public void StopRecording(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            if (_activeRecordings.TryRemove(key, out var writer))
            {
                try
                {
                    writer.Flush();
                    writer.Dispose();

                    if (_recordingInfo.TryRemove(key, out var info))
                    {
                        var duration = DateTime.Now - info.StartTime;
                        Logger.Info($"Stopped WAV recording: {Path.GetFileName(info.FilePath)} " +
                                    $"(Duration={duration.TotalSeconds:F2}s, Samples={info.SamplesWritten}, " +
                                    $"Client={info.ClientGuid.ToString().Substring(0, 8)}, Radio={info.RadioId}, " +
                                    $"Freq={info.Frequency / 1000000.0:F3}MHz)");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to stop recording {key}");
                }
            }
        }

        /// <summary>
        /// Stops all active recordings
        /// </summary>
        public void StopAllRecordings()
        {
            Logger.Info("Stopping all WAV recordings");

            foreach (var key in _activeRecordings.Keys)
            {
                StopRecording(key);
            }
        }

        public void Dispose()
        {
            StopAllRecordings();
        }

        private class TransmissionRecordingInfo
        {
            public Guid ClientGuid { get; set; }
            public int RadioId { get; set; }
            public double Frequency { get; set; }
            public DateTime StartTime { get; set; }
            public string FilePath { get; set; }
            public int SampleRate { get; set; }
            public int Channels { get; set; }
            public long SamplesWritten { get; set; }
        }
    }
}

