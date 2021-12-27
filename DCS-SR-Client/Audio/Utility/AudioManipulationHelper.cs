using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers
{
    public static class AudioManipulationHelper
    {
        public static short[] MixSamples(short[] existingAudio, short[] newAudio, int offset)
        {
            short[] mixedDown;
            mixedDown = new short[newAudio.Length];

            for (int i = 0; i < mixedDown.Length; i++)
            {
                mixedDown[i] = MixDown(existingAudio[i + offset], newAudio[i]);
            }

            return mixedDown;
        }

        public static short MixDown(short pcmAudioOne, short pcmAudioTwo)
        {
            // Based on http://www.vttoth.com/CMS/index.php/technical-notes/68, it appears to be the best light weight solution to
            // mixing down audio to avoid clipping and volume issues

            short shortMixedDown;
            ushort mixedDownUnsigned;

            ushort audioOneUnsigned = (ushort)(pcmAudioOne + 32768);
            ushort audioTwoUnsigned = (ushort)(pcmAudioTwo + 32768);

            if ((audioOneUnsigned < 32768) || (audioTwoUnsigned < 32768))
            {
                mixedDownUnsigned = (ushort)(audioOneUnsigned * audioTwoUnsigned / 32768);
            }
            else
            {
                mixedDownUnsigned = (ushort)(2 * (audioOneUnsigned + audioTwoUnsigned) - (audioOneUnsigned * audioTwoUnsigned) / 32768 - 65536);
            }

            shortMixedDown = (short)(mixedDownUnsigned - 32768);

            return shortMixedDown;
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
        }
    }
}
