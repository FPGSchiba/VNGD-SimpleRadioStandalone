using NAudio.Wave;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio
{
    /// <summary>Buffered WaveProvider taking source data from WaveIn</summary>
    public class TestWaveInProvider : IWaveProvider
    {
        private readonly BufferedWaveProvider bufferedWaveProvider;
        private readonly IWaveIn waveIn;

        /// <summary>
        /// Creates a new WaveInProvider
        /// n.b. Should make sure the WaveFormat is set correctly on IWaveIn before calling
        /// </summary>
        /// <param name="waveIn">The source of wave data</param>
        public TestWaveInProvider(IWaveIn waveIn)
        {
            this.waveIn = waveIn;
            waveIn.DataAvailable += waveIn_DataAvailable;
            bufferedWaveProvider = new BufferedWaveProvider(WaveFormat);
        }

        /// <summary>The WaveFormat</summary>
        public WaveFormat WaveFormat
        {
            get { return waveIn.WaveFormat; }
        }

        /// <summary>Reads data from the WaveInProvider</summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            return bufferedWaveProvider.Read(buffer, 0, count);
        }

        private void waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            //Console.WriteLine("MIC: "+e.BytesRecorded);
            bufferedWaveProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);
        }
    }
}