using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CircularBuffer;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Dsp;
using NAudio.Utils;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    class BufferedSampleProvider : ISampleProvider
    {
        private  CircularBuffer<float> _circularBuffer;
        private float[] sourceBuffer;

        public WaveFormat WaveFormat { get; private set; }

        public BufferedSampleProvider(WaveFormat waveFormat)
        {
            if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                throw new ArgumentException("Mixer wave format must be IEEE float");
            }
          
            this.WaveFormat = waveFormat;

            _circularBuffer = new CircularBuffer<float>(AudioManager.SAMPLE_RATE * 5); // 5 seconds worth
        }

   

    
        public void AddSample(float[] samples)
        {
            foreach (var sample in samples)
            { 
                _circularBuffer.PushBack(sample);
            }
          
        }

        /// <summary>
        /// Reads samples from this sample provider
        /// </summary>
        /// <param name="buffer">Sample buffer</param>
        /// <param name="offset">Offset into sample buffer</param>
        /// <param name="count">Number of samples required</param>
        /// <returns>Number of samples read</returns>
        public int Read(float[] buffer, int offset, int count)
        {


//            this.sourceBuffer = BufferHelpers.Ensure(this.sourceBuffer, count);
//
//            int outputSamples = Math.Min(this._circularBuffer.Size, count);
//            for (int i = 0; i < outputSamples; i++)
//            {
//                this.
//            }
//         
//            // optionally ensure we return a full buffer
//            if (ReadFully && outputSamples < count)
//            {
//                int outputIndex = offset + outputSamples;
//                while (outputIndex < offset + count)
//                {
//                    buffer[outputIndex++] = 0;
//                }
//                outputSamples = count;
//            }
//            return outputSamples;
            return 0;
        }

        public void Clear()
        {
            _circularBuffer = new CircularBuffer<float>(AudioManager.SAMPLE_RATE * 5); // 5 seconds worth
        }

    }
}
