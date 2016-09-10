using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{

    /// <summary>Buffered WaveProvider taking source data from WaveIn</summary>
    public class TestWaveInProvider : IWaveProvider
    {
        private IWaveIn waveIn;
        private BufferedWaveProvider bufferedWaveProvider;

        /// <summary>The WaveFormat</summary>
        public WaveFormat WaveFormat
        {
            get
            {
                return this.waveIn.WaveFormat;
            }
        }

        /// <summary>
        /// Creates a new WaveInProvider
        /// n.b. Should make sure the WaveFormat is set correctly on IWaveIn before calling
        /// </summary>
        /// <param name="waveIn">The source of wave data</param>
        public TestWaveInProvider(IWaveIn waveIn)
        {
            this.waveIn = waveIn;
            waveIn.DataAvailable += new EventHandler<WaveInEventArgs>(this.waveIn_DataAvailable);
            this.bufferedWaveProvider = new BufferedWaveProvider(this.WaveFormat);
        }

        private void waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            Console.WriteLine("MIC: "+e.BytesRecorded);
            this.bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }

        /// <summary>Reads data from the WaveInProvider</summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            return this.bufferedWaveProvider.Read(buffer, 0, count);
        }
    }
}
