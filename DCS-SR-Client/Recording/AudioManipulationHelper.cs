using System;
using System.Collections.Generic;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Recording
{
    public static class AudioManipulationHelper
    {
        public static short[] MixSamplesClipped(short[] pcmAudioOne, short[] pcmAudioTwo, int samplesLength)
        {
            short[] mixedDown = new short[samplesLength];

            for(int i = 0; i < samplesLength; i++)
            {
                int result = (pcmAudioOne[i] + pcmAudioTwo[i]);
                if(result > short.MaxValue)
                {
                    result = short.MaxValue;
                }
                else if (result < short.MinValue)
                {
                    result = short.MinValue;
                }
                mixedDown[i] = (short)result;
            }

            return mixedDown;
        }

        public static short[] MixSamplesWithHeadroom(List<short[]> samplesToMixdown, int samplesLength)
        {
            short[] mixedDown = new short[samplesLength];

            foreach(short[] sample in samplesToMixdown)
            {           
                for(int i = 0; i < samplesLength; i++)
                {
                    mixedDown[i] += (short)(sample[i] / samplesToMixdown.Count);
                }
            }

            return mixedDown;
        }


        public static (short[], short[]) SplitSampleByTime(long samplesRemaining, short[] samples)
        {
            short[] toWrite = new short[samplesRemaining];
            short[] remainder = new short[samples.Length - samplesRemaining];

            Array.Copy(samples, 0, toWrite, 0, samplesRemaining);
            Array.Copy(samples, samplesRemaining, remainder, 0, remainder.Length);

            return (toWrite, remainder);
        }

        public static short[] SineWaveOut(int sampleLength, int sampleRate, double volume)
        {
            short[] sineBuffer = new short[sampleLength];
            double amplitude = volume * short.MaxValue;

            for (int i = 0; i < sineBuffer.Length; i++)
            {
                sineBuffer[i] = (short)(amplitude * Math.Sin((2 * Math.PI * i * 175) / sampleRate));
            }
            return sineBuffer;
        }

        public static int CalculateSamplesStart(long start, long end, int sampleRate)
        {
            double elapsedSinceLastWrite = ((double)end - start) / 10000000;
            int necessarySamples = Convert.ToInt32(elapsedSinceLastWrite * sampleRate);
            // prevent any potential issues due to a negative time being returned
            return necessarySamples >= 0 ? necessarySamples : 0;
            //return necessarySamples
        }
    }
}
