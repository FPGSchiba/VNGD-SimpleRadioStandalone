using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using NAudio.Dsp;
using NAudio.Utils;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Providers
{
    public class RadioMixingProvider : ISampleProvider
    {
        private readonly int radioId;
        private readonly List<ClientAudioProvider> sources;
        private CachedAudioEffect amCollisionEffect;
        private int amEffectPosition = 0;
        private float amCollisionVol = 1.0f;

        private ClientEffectsPipeline pipeline = new ClientEffectsPipeline();

        private readonly Settings.ProfileSettingsStore profileSettings = Settings.GlobalSettingsStore.Instance.ProfileSettingsStore;
        private float[] mixBuffer;
        private float[] secondaryMixBuffer;
        private readonly SyncedServerSettings serverSettings = SyncedServerSettings.Instance;
        private List<DeJitteredTransmission> _mainAudio = new List<DeJitteredTransmission>();
        private List<DeJitteredTransmission> _secondaryAudio = new List<DeJitteredTransmission>();

        private Random random = new Random();
      //  private readonly WaveFileWriter waveWriter;

        public RadioMixingProvider(WaveFormat waveFormat, int radioId)
        {
            if (waveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                throw new ArgumentException("Mixer wave format must be IEEE float");
            }

            this.radioId = radioId;
            sources = new List<ClientAudioProvider>();
            WaveFormat = waveFormat;

            //load AM interference effect
            amCollisionEffect = CachedAudioEffectProvider.Instance.AMCollision;
            amCollisionVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.AMCollisionVolume);

         //   waveWriter = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\output{Guid.NewGuid()}.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 2));
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

        public float[] ClearArray(float[] buffer)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = 0;
            }

            return buffer;
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
            _mainAudio.Clear();
            _secondaryAudio.Clear();
            int primarySamples = 0;
            int secondarySamples = 0;

            mixBuffer = BufferHelpers.Ensure(mixBuffer, count);
            secondaryMixBuffer = BufferHelpers.Ensure(secondaryMixBuffer, count);

            ClearArray(mixBuffer);
            ClearArray(secondaryMixBuffer);
  
            lock (sources)
            {
                int index = sources.Count - 1;
                while (index >= 0)
                {
                    var source = sources[index];

                    //ask for count/2 as the source is MONO but the request for this is STEREO
                    var transmission = source.JitterBufferProviderInterface[radioId].Read(count / 2);

                    if (transmission.count > 0)
                    {
                        if (transmission.IsSecondary)
                        {
                            _secondaryAudio.Add(transmission);
                        }
                        else
                        {
                            _mainAudio.Add(transmission);
                        }
                    }

                    index--;
                }
            }

            //TODO
            //copy to the recording service - as we have everything we need to know about the audio
            //at this point

            if (_mainAudio.Count > 0)
            {
                mixBuffer = ProcessClientTransmissions(mixBuffer, _mainAudio, out primarySamples);
            }
                

            //handle guard
            if (_secondaryAudio.Count > 0)
            {
                secondaryMixBuffer = ProcessClientTransmissions(secondaryMixBuffer, _secondaryAudio, out secondarySamples);
            }

            //reuse mix buffer
            mixBuffer = MixArrays(mixBuffer,primarySamples, secondaryMixBuffer, secondarySamples, out int outputSamples);

            buffer = SeparateAudio(mixBuffer, outputSamples, 0, buffer, offset, radioId);

            //we're now stereo - double the samples
            outputSamples = outputSamples * 2;

        //    waveWriter.WriteSamples(buffer,offset, outputSamples);

            //clear
            _mainAudio.Clear();
            _secondaryAudio.Clear();
            return EnsureFullBuffer(buffer, outputSamples, offset, count);
        }

        private float[] MixArrays(float[] array1, int array1Length, float[] array2, int array2Length, out int count)
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

        private float[] ProcessClientTransmissions(float[] tempBuffer, List<DeJitteredTransmission> transmissions, out int clientTransmissionLength)
        {
            DeJitteredTransmission lastTransmission = transmissions[0];

            clientTransmissionLength = 0;
            foreach (var transmission in transmissions)
            {
                for (int i = 0; i < transmission.pcmAudio.Length; i++)
                {
                    tempBuffer[i] += transmission.pcmAudio[i];
                }

                clientTransmissionLength = Math.Max(clientTransmissionLength, transmission.pcmAudio.Length);
            }

            bool process = true;

            //TODO take info account server setting AND volume of this radio AND if its AM or FM
            if (radioId != 0 
                && !lastTransmission.NoAudioEffects
                && (lastTransmission.Modulation == RadioInformation.Modulation.AM 
                    || lastTransmission.Modulation == RadioInformation.Modulation.FM)
                             && serverSettings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE))
            {
                if (transmissions.Count > 1)
                {
                    if (lastTransmission.Modulation == RadioInformation.Modulation.AM && amCollisionEffect.Loaded)
                    {
                        //replace the buffer with our own
                        int outIndex = 0;
                        while (outIndex < clientTransmissionLength)
                        {
                            var amByte = this.amCollisionEffect.AudioEffectFloat[amEffectPosition++];

                            tempBuffer[outIndex++] = amByte * lastTransmission.Volume;

                            if (amEffectPosition == amCollisionEffect.AudioEffectFloat.Length)
                            {
                                amEffectPosition = 0;
                            }
                        }

                        process = false;
                    }
                    else
                    {
                        //FM picketing / picket fencing - pick one transmission at random
                        //TODO improve this to pick the stronger frequency?

                        int index = random.Next(transmissions.Count);
                        var transmission = transmissions[index];
    
                        for (int i = 0; i < transmission.pcmAudio.Length; i++)
                        {
                            tempBuffer[i] = transmission.pcmAudio[i];
                        }

                        clientTransmissionLength = transmission.pcmAudio.Length;
                    }
                }
            }

            //TODO only apply pipeline if AM or FM affect doesnt apply?
            if (process)
                tempBuffer = pipeline.ProcessClientAudioSamples(tempBuffer, clientTransmissionLength, 0, lastTransmission);


            return tempBuffer;
        }

        private float[] SeparateAudio(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat, int dstOffset, int radioId)
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
                return CreateBalancedMix(srcFloat, srcCount, srcOffset, dstFloat, dstOffset, 0);
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
            float left = (1.0f - balance) / 2.0f;
            float right = 1.0f - left;

            //temp set of mono floats
            int monoBufferPosition = 0;
            for (int i = 0; i < srcCount * 2; i += 2)
            {
                dstFloat[i + dstOffset] = srcFloat[monoBufferPosition + srcOffset] * left;
                dstFloat[i + dstOffset + 1] = srcFloat[monoBufferPosition + srcOffset] * right;
                monoBufferPosition++;
            }

            return dstFloat;
        }

        private int EnsureFullBuffer(float[] buffer, int samplesCount,  int offset, int count)
        {
            // ensure we return a full buffer of STEREO
            if (samplesCount < count)
            {
                int outputIndex = offset + samplesCount;
                while (outputIndex < offset + count)
                {
                    buffer[outputIndex++] = 0;
                }
                samplesCount = count;
            }

            return samplesCount;
        }
    }
}