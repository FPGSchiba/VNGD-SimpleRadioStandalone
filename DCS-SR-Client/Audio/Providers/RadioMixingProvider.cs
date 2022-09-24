using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Dsp;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Providers
{
    public class RadioMixingProvider: ISampleProvider
    {
        private readonly int radioId;
        private readonly List<ClientAudioProvider> sources;
        private float[] sourceBuffer;
        private const int MaxInputs = 1024; // protect ourselves against doing something silly
        private readonly int radioCount;
        private CachedAudioEffect amEffect;
        private int amEffectPosition = 0;

        private readonly CachedAudioEffectProvider effectProvider = CachedAudioEffectProvider.Instance;

        private ClientEffectsPipeline pipeline = new ClientEffectsPipeline();

        protected readonly Settings.ProfileSettingsStore profileSettings = Settings.GlobalSettingsStore.Instance.ProfileSettingsStore;
     //   private readonly WaveFileWriter waveWriterMono;
        private readonly WaveFileWriter waveWriterStereo;
        private float[] mixBuffer;

        public RadioMixingProvider(WaveFormat waveFormat, int radioId)
        {
            if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                throw new ArgumentException("Mixer wave format must be IEEE float");
            }

            this.radioId = radioId;
            sources = new List<ClientAudioProvider>();
            WaveFormat = waveFormat;
            radioCount = ClientStateSingleton.Instance.DcsPlayerRadioInfo.radios.Length;

            //load AM interference effect
            amEffect = CachedAudioEffectProvider.Instance.AMCollision;

     //       waveWriterMono = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\outputmono{radioId}.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));
            //waveWriterStereo = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\outputstereo{radioId}.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 2));

        }

        /// <summary>
        /// Returns the mixer inputs (read-only - use AddMixerInput to add an input
        /// </summary>
        public IEnumerable<ClientAudioProvider> MixerInputs => sources;

        /// <summary>
        /// Adds a new mixer input
        /// </summary>
        /// <param name="mixerInput">Mixer input</param>
        public void AddMixerInput(ClientAudioProvider mixerInput)
        {
            // we'll just call the lock around add since we are protecting against an AddMixerInput at
            // the same time as a Read, rather than two AddMixerInput calls at the same time
            lock (sources)
            {
                sources.Add(mixerInput);
            }
        }

        public void RemoveMixerInput(ClientAudioProvider mixerInput)
        {
            lock (sources)
            {
                sources.Remove(mixerInput);
            }
        }

        /// <summary>
        /// Removes all mixer inputs
        /// </summary>
        public void RemoveAllMixerInputs()
        {
            lock (sources)
            {
                sources.Clear();
            }
        }

        /// <summary>
        /// The output WaveFormat of this sample provider
        /// </summary>
        public WaveFormat WaveFormat { get; private set; }

        /// <summary>
        /// Reads samples from this sample provider
        /// </summary>
        /// <param name="buffer">Sample buffer</param>
        /// <param name="offset">Offset into sample buffer</param>
        /// <param name="count">Number of samples required</param>
        /// <returns>Number of samples read</returns>
        public int Read(float[] buffer, int offset, int count)
        {
            int outputSamples = 0;
            bool multipleTranmissions = false;
            sourceBuffer = BufferHelpers.Ensure(sourceBuffer, count);
            mixBuffer = BufferHelpers.Ensure(mixBuffer, count);
            bool audio = false;
            RadioInformation.Modulation modulation = RadioInformation.Modulation.AM;
            float volume = 1.0f;
            double frequency = 1d;
            lock (sources)
            {
                int index = sources.Count - 1;
                while (index >= 0)
                {
                    var source = sources[index];

                    //ask for count/2 as the source is MONO but the request for this is STEREO
                    int samplesRead = source.JitterBufferProviderInterface[radioId].Read(sourceBuffer, 0, count /2 );
                    modulation = source.Modulation;
                    frequency = source.Frequency;

                    volume = source.Volume;
                    //TODO somehow check if its secondary (somehow)
                    //TODO this is a hack - as if its a simultaneous transmission its possible to avoid the AM effect but edge case?
                    if (outputSamples > 0 && samplesRead > 0 )
                    {
                        multipleTranmissions = true;
                    }

                    int outIndex = 0;
                    for (int n = 0; n < samplesRead; n++)
                    {
                        if (n >= outputSamples)
                        {
                            mixBuffer[outIndex++] = sourceBuffer[n];
                        }
                        else
                        {
                            mixBuffer[outIndex++] += sourceBuffer[n];
                        }
                    }
                    outputSamples = Math.Max(samplesRead, outputSamples);
                    index--;
                }
            }

            //if there is audio
            if (outputSamples > 0)
            {
                //TODO take info account server setting AND volume of this radio AND if its AM or FM
                //We loaded a MONO audio - but need to convert to stereo and also apply the correct balance (left/right) as per the radio config
                //Why is it double too? Can it be float?
                //Can this pipeline also contain the Start & End PTT tones and also the Encryption tones (which would simplify logic)
                //Can the NATO tone + Background tone be mixed in here rather than on the client?
                //Can we simply the client audio - and make it in effect just a jitter buffer
                //Audio seperation we can do in bulk for all the mixed down audio - rather than per user, along with the audio volume too
                if (radioId != 0 && multipleTranmissions && this.amEffect.Loaded)
                {
                    //replace the buffer with our own
                    int outIndex = 0;
                    while (outIndex < count)
                    {
                        var amByte = (float)this.amEffect.AudioEffectDouble[amEffectPosition++];

                        mixBuffer[outIndex++] = amByte;

                        if (amEffectPosition == amEffect.AudioEffectDouble.Length)
                        {
                            amEffectPosition = 0;
                        }
                    }
                }

                //run through pipeline
                //TODO figure out how to handle the secondary receiver - so AM doesnt block it too
                mixBuffer = pipeline.AddClientAudioSamples(mixBuffer, outputSamples, 0, modulation, volume, frequency);


                //   waveWriterMono.WriteSamples(buffer, 0, outputSamples);

                //convert to stereo
                buffer = SeperateAudio(mixBuffer, outputSamples, 0,buffer, offset,radioId);

                //waveWriterStereo.WriteSamples(buffer,offset,outputSamples*2);
                //its stereo now!
                outputSamples = outputSamples * 2;
            }

            // ensure we return a full buffer of STEREO
            if (outputSamples < count)
            {
                int outputIndex = offset + outputSamples;
                while (outputIndex < offset + count)
                {
                    buffer[outputIndex++] = 0;
                }
                outputSamples = count;
            }

            return outputSamples;
        }


        public float[] SeperateAudio(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat,  int dstOffset, int radioId)
        {
            var settingType = ProfileSettingsKeys.Radio1Channel;

            if (radioId == 0)
            {
                settingType = ProfileSettingsKeys.IntercomChannel;
            }
            else if (radioId == 1)
            {
                settingType = ProfileSettingsKeys.Radio1Channel;
            }
            else if (radioId == 2)
            {
                settingType = ProfileSettingsKeys.Radio2Channel;
            }
            else if (radioId == 3)
            {
                settingType = ProfileSettingsKeys.Radio3Channel;
            }
            else if (radioId == 4)
            {
                settingType = ProfileSettingsKeys.Radio4Channel;
            }
            else if (radioId == 5)
            {
                settingType = ProfileSettingsKeys.Radio5Channel;
            }
            else if (radioId == 6)
            {
                settingType = ProfileSettingsKeys.Radio6Channel;
            }
            else if (radioId == 7)
            {
                settingType = ProfileSettingsKeys.Radio7Channel;
            }
            else if (radioId == 8)
            {
                settingType = ProfileSettingsKeys.Radio8Channel;
            }
            else if (radioId == 9)
            {
                settingType = ProfileSettingsKeys.Radio9Channel;
            }
            else if (radioId == 10)
            {
                settingType = ProfileSettingsKeys.Radio10Channel;
            }
            else
            {
                return CreateBalancedMix(srcFloat,  srcCount,  srcOffset, dstFloat, dstOffset, 0);
            }

            float balance = 0;
            try
            {
                //TODO cache this
                balance = profileSettings.GetClientSettingFloat(settingType);
            }
            catch (Exception)
            {
                //ignore
            }

            return CreateBalancedMix(srcFloat, srcCount, srcOffset, dstFloat, dstOffset, balance);
        }

        public static float[] CreateBalancedMix(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat, int dstOffset, float balance)
        {
            float left = 1.0f;
            float right = 1.0f;

            //right
            if (balance > 0)
            {
                var leftBias = 1 - Math.Abs(balance);
                var rightBias = Math.Abs(balance);
                //right
                left = left * leftBias;
                right = right * rightBias;
            }
            else if (balance < 0)
            {
                var leftBias = Math.Abs(balance);
                var rightBias = 1 - Math.Abs(balance);
                //left
                left = left * leftBias;
                right = right * rightBias;
            }
            else
            {
                //equal balance
                left = 0.5f;
                right = 0.5f;
            }

            if (left > 1f)
            {
                left = 1f;
            }
            if (right > 1f)
            {
                right = 1f;
            }

         
            //temp set of mono floats
          

            int monoBufferPosition = 0;
            for (int i = 0; i < srcCount * 2; i += 2)
            {
                dstFloat[i + dstOffset] = srcFloat[monoBufferPosition+ srcOffset] * left;
                dstFloat[i + dstOffset + 1] = srcFloat[monoBufferPosition +srcOffset] * right;
                monoBufferPosition++;
            }

            return dstFloat;
        }
    }
}

