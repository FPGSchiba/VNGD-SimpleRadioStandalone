using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
  public class RadioAudioProvider
  {
    private readonly Settings _settings;

    public VolumeSampleProvider VolumeSampleProvider { get; }
    public BufferedWaveProvider BufferedWaveProvider { get; }

    public RadioAudioProvider(bool radioFilter)
    {
          
      BufferedWaveProvider = new BufferedWaveProvider(new WaveFormat(AudioManager.SAMPLE_RATE, 16, 2));

      BufferedWaveProvider.DiscardOnBufferOverflow = true;
      BufferedWaveProvider.BufferDuration = new TimeSpan(0, 0, 2); //2 seconds buffer

      var pcm = new Pcm16BitToSampleProvider(BufferedWaveProvider);

      if (radioFilter)
      {
        var filter = new RadioFilter(pcm);
        VolumeSampleProvider = new VolumeSampleProvider(filter);
      }
      else
      {
        VolumeSampleProvider = new VolumeSampleProvider(pcm);
      }
      _settings = Settings.Instance;
    }

    public void AddAudioSamples(byte[] pcmAudio)
    {
      BufferedWaveProvider.AddSamples(pcmAudio, 0, pcmAudio.Length);
    }

  }

}
