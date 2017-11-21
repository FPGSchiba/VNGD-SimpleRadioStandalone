using System;
using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using NAudio.Utils;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    public class JitterBufferProviderInterface : IWaveProvider
    {
        private readonly CircularBuffer _circularBuffer;

        private readonly byte[] _silence = new byte[AudioManager.SEGMENT_FRAMES*2]; //*2 for stereo

        private uint _lastRead; // gives current index

        private readonly object _lock = new object();
        private uint _missing; // counts missing packets

      //  private const int INITIAL_DELAY_MS = 200;
     //   private long _delayedUntil = -1; //holds audio for a period of time

        public JitterBufferProviderInterface(WaveFormat waveFormat)
        {
            WaveFormat = waveFormat;

            _circularBuffer = new CircularBuffer(WaveFormat.AverageBytesPerSecond*6);

            Array.Clear(_silence, 0, _silence.Length);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            return _circularBuffer.Read(buffer, offset, count);
        }

        public void AddSamples(JitterBufferAudio jitterBufferAudio)
        {
            _circularBuffer.Write(jitterBufferAudio.Audio, 0, jitterBufferAudio.Audio.Length);
        }

       
    }
}