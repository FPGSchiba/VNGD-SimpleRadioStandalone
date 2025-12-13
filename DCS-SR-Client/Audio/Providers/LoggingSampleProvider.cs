using System;
using NAudio.Wave;
using NLog;

namespace Vanguard.VCS.Client.Audio.Providers
{
    public class LoggingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _inner;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private int _counter = 0;

        public LoggingSampleProvider(ISampleProvider inner)
        {
            _inner = inner;
        }

        public WaveFormat WaveFormat => _inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _inner.Read(buffer, offset, count);
            _counter++;
            // throttle logging to every 10 reads to increase visibility during debugging
            if ((_counter % 10) == 0)
            {
                float max = 0f; double sumSq = 0;
                int len = Math.Min(read, 480);
                for (int i = 0; i < len; i++)
                {
                    var v = buffer[offset + i];
                    if (Math.Abs(v) > max) max = Math.Abs(v);
                    sumSq += v * v;
                }
                double rms = len > 0 ? Math.Sqrt(sumSq / len) : 0.0;
                Logger.Debug($"LoggingSampleProvider.Read: requested={count}, returned={read}, sampleWindow={len}, rms={rms:0.000}, max={max:0.000}");
                if (read > 0 && len >= 4)
                {
                    Logger.Debug($"LoggingSampleProvider.Read first samples: {buffer[offset+0]:0.000}, {buffer[offset+1]:0.000}, {buffer[offset+2]:0.000}, {buffer[offset+3]:0.000}");
                }
            }
            return read;
        }
    }
}
