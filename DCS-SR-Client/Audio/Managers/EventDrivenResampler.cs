using System;
using System.Diagnostics;
using NAudio.Dmo;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers
{

    public class EventDrivenResampler
    {
        private DmoResampler dmoResampler;
        private MediaBuffer inputMediaBuffer;
        private DmoOutputDataBuffer outputBuffer;

        public EventDrivenResampler(bool windowsN, WaveFormat input,WaveFormat output)
        {
            dmoResampler = new DmoResampler();
            if (!dmoResampler.MediaObject.SupportsInputWaveFormat(0, input))
            {
                throw new ArgumentException("Unsupported Input Stream format", nameof(input));
            }

            dmoResampler.MediaObject.SetInputWaveFormat(0, input);
            if (!dmoResampler.MediaObject.SupportsOutputWaveFormat(0, output))
            {
                throw new ArgumentException("Unsupported Output Stream format", nameof(output));
            }

            dmoResampler.MediaObject.SetOutputWaveFormat(0, output);

            inputMediaBuffer = new MediaBuffer(input.AverageBytesPerSecond);
            outputBuffer = new DmoOutputDataBuffer(output.AverageBytesPerSecond);

        }

        public byte[] Resample(byte[] inputByteArray, int length)
        {
            // 1. Read from the input stream 

            // 2. copy into our DMO's input buffer
            inputMediaBuffer.LoadData(inputByteArray, inputByteArray.Length);

            // 3. Give the input buffer to the DMO to process
            dmoResampler.MediaObject.ProcessInput(0, inputMediaBuffer, DmoInputDataBufferFlags.None, 0, 0);

            outputBuffer.MediaBuffer.SetLength(0);
            outputBuffer.StatusFlags = DmoOutputDataBufferFlags.None;

            // 4. Now ask the DMO for some output data
            dmoResampler.MediaObject.ProcessOutput(DmoProcessOutputFlags.None, 1, new[] { outputBuffer });

            if (outputBuffer.Length == 0)
            {
                Debug.WriteLine("ResamplerDmoStream.Read: No output data available");
                return new byte[0];
            }

            byte[] result = new byte[outputBuffer.Length];
            // 5. Now get the data out of the output buffer
            outputBuffer.RetrieveData(result, 0);
            return result;
        }

        /// <summary>
        /// Dispose
        /// </summary>
        /// <param name="disposing">True if disposing (not from finalizer)</param>
        public void Dispose(bool disposing)
        {
            if (inputMediaBuffer != null)
            {
                inputMediaBuffer.Dispose();
                inputMediaBuffer = null;
            }
            outputBuffer.Dispose();
            if (dmoResampler != null)
            {
                dmoResampler = null;
            }
    
        }
        ~EventDrivenResampler(){
            Dispose(false);
        }

    }
}