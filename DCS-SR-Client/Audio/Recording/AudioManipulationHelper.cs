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

        public static float[] MixSamplesWithHeadroom(List<float[]> samplesToMixdown, int samplesLength)
        {
            float[] mixedDown = new float[samplesLength];

            foreach (float[] sample in samplesToMixdown)
            {
                for (int i = 0; i < samplesLength; i++)
                {
                    // Unlikely to have duplicate signals across n radios, can use sqrt to find a sensible headroom level
                    // FIXME: Users likely want a consistent mixdown regardless of radios in airframe, just hardcode a constant term?
                    mixedDown[i] += (float)(sample[i] / Math.Sqrt(samplesToMixdown.Count));
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

        public static float[] SineWaveOut(int sampleLength, int sampleRate, double volume)
        {
            float[] sineBuffer = new float[sampleLength];
            double amplitude = volume;

            for (int i = 0; i < sineBuffer.Length; i++)
            {
                sineBuffer[i] =(float) (amplitude * Math.Sin((2 * Math.PI * i * 175) / sampleRate));
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

        public static float[] MixArraysClipped(float[] array1, int array1Length, float[] array2, int array2Length, out int count)
        {
            if (array1Length > array2Length)
            {
                for (int i = 0; i < array2Length; i++)
                {
                    array1[i] += array2[i];

                    //clip
                    if (array1[i] > 1f)
                    {
                        array1[i] = 1.0f;
                    }
                    else if (array1[i] < -1f)
                    {
                        array1[i] = -1.0f;
                    }
                }

                count = array1Length;
                return array1;
            }
            else
            {
                for (int i = 0; i < array1Length; i++)
                {
                    array2[i] += array1[i];

                    //clip
                    if (array2[i] > 1f)
                    {
                        array2[i] = 1.0f;
                    }
                    else if(array2[i] < -1.0f)
                    {
                        array2[i] = -1.0f;
                    }
                }

                count = array2Length;
                return array2;
            }
        }

        public static float[] MixArraysNoClipping(float[] array1, int array1Length, float[] array2, int array2Length, out int count)
        {
            if (array1Length > array2Length)
            {
                for (int i = 0; i < array2Length; i++)
                {
                    array1[i] += array2[i];
                }

                count = array1Length;
                return array1;
            }
            else
            {
                for (int i = 0; i < array1Length; i++)
                {
                    array2[i] += array1[i];
                }

                count = array2Length;
                return array2;
            }
        }

        public static float[] ClipArray(float[] array, int arrayLength)
        {
            for (int i = 0; i < arrayLength; i++)
            {
                if (array[i] > 1f)
                {
                    array[i] = 1.0f;
                }
                else if (array[i] < -1.0f)
                {
                    array[i] = -1.0f;
                }
            }
            return array;
        }
    }
}
