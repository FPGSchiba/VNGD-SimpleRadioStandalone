using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider: RadioAudioProvider
    {
        public long LastUpdate { get; private set; }

        public static readonly int SILENCE_PAD = 100;


        public ClientAudioProvider():base(AudioManager.INPUT_SAMPLE_RATE)
        {

        }

        public void AddClientAudioSamples(ClientAudio audio)
        {
            long now = Environment.TickCount;

            if (now - LastUpdate > 120)
            {
                //append 80ms of silence - this functions as our jitter buffer??
                int silencePad = (AudioManager.INPUT_SAMPLE_RATE/1000)*SILENCE_PAD;

                var newAudio = new short[audio.PcmAudioShort.Length + silencePad];

                Buffer.BlockCopy(audio.PcmAudioShort, 0, newAudio, silencePad, audio.PcmAudioShort.Length);

                audio.PcmAudioShort = newAudio;
            }

            LastUpdate = Environment.TickCount;

            VolumeSampleProvider.Volume = audio.Volume;


            AddAudioSamples(ConversionHelpers.ShortArrayToByteArray(audio.PcmAudioShort),audio.ReceivedRadio,false);
        }
    }
}

