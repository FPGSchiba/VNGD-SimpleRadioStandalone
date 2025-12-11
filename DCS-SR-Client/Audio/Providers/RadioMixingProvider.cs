using System;
using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using NAudio.Utils;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Recording;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Helpers;

namespace Vanguard.VCS.Client.Audio.Providers
{
    internal class SourceResidueBuffer
    {
        public float[] Buffer;
        public int Count;
    }
    
    public class RadioMixingProvider : ISampleProvider
    {
        private readonly int radioId;
        private readonly List<ClientAudioProvider> sources;
        
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private ClientEffectsPipeline pipeline = new ClientEffectsPipeline();

        private readonly ProfileSettingsStore profileSettings =
            GlobalSettingsStore.Instance.ProfileSettingsStore;

        private float[] mixBuffer;
        private float[] secondaryMixBuffer;
        private List<DeJitteredTransmission> _mainAudio = new List<DeJitteredTransmission>();
        private List<DeJitteredTransmission> _secondaryAudio = new List<DeJitteredTransmission>();

        private AudioRecordingManager _audioRecordingManager = AudioRecordingManager.Instance;

        private CircularFloatBuffer effectsBuffer;

        private readonly CachedAudioEffectProvider _cachedAudioEffectsProvider;

        RadioInformation.Modulation lastModulation = RadioInformation.Modulation.DISABLED;

        // put these in a struct? 
        private bool hasPlayedTransmissionEnd = true;
        private long lastReceivedAt = 0;
        private bool hasPlayedTransmissionStart = false;
        private float lastVolume = 1;
        
        // Mixer-level output residue (mono)
        private float[] _outResidue;
        private int _outResidueCount = 0;
        
        private Dictionary<ClientAudioProvider, SourceResidueBuffer> _sourceResidues = new Dictionary<ClientAudioProvider, SourceResidueBuffer>();
        private float[] _monoAccum;
        private float[] _tempMono;

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

            //5 seconds worth of buffer
            effectsBuffer = new CircularFloatBuffer(WaveFormat.SampleRate * 5);
            _cachedAudioEffectsProvider = CachedAudioEffectProvider.Instance;
            ;

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
            Logger.Debug("RadioMixingProvider Read called");
            int monoNeeded = count / 2;

            // Ensure mixBuffer large enough for monoNeeded
            mixBuffer = BufferHelpers.Ensure(mixBuffer, monoNeeded);
            Array.Clear(mixBuffer, 0, monoNeeded);

            Logger.Debug($"Read START: monoNeeded={monoNeeded}, sources.Count={sources.Count}, _outResidueCount={_outResidueCount}");

            // 1) Serve from output residue first
            int monoWrittenOut = 0;
            if (_outResidueCount > 0)
            {
                int take = Math.Min(_outResidueCount, monoNeeded);
                Array.Copy(_outResidue, 0, mixBuffer, 0, take);
                monoWrittenOut += take;

                // shift residue
                int remain = _outResidueCount - take;
                if (remain > 0)
                {
                    Array.Copy(_outResidue, take, _outResidue, 0, remain);
                }
                _outResidueCount = remain;
                Logger.Debug($"Served {take} from residue, monoWrittenOut={monoWrittenOut}, residue remaining={_outResidueCount}");
            }

            // 2) Produce new 960-sample blocks as needed to fill monoNeeded
            int loopCount = 0;
            while (monoWrittenOut < monoNeeded)
            {
                loopCount++;
                if (loopCount > 10)
                {
                    Logger.Error($"Infinite loop detected in Read(), breaking. monoWrittenOut={monoWrittenOut}, monoNeeded={monoNeeded}");
                    break;
                }

                // Produce exactly 960 mono samples (20 ms) into a fresh block, then append to output residue.
                const int block = 960;

                Logger.Debug($"Calling ProduceOneMonoBlock, loop={loopCount}");
                // Build a 960-sample mixed mono block
                float[] produced = ProduceOneMonoBlock(block, out int producedCount, out bool anyTx, out bool ky58ToneLocal, out int outSamplesAfterEffects);
                Logger.Debug($"ProduceOneMonoBlock returned: producedCount={producedCount}, anyTx={anyTx}");

                // Append producedCount to residue FIFO
                if (producedCount > 0)
                {
                    int neededCapacity = _outResidueCount + producedCount;
                    _outResidue = BufferHelpers.Ensure(_outResidue, neededCapacity);
                    Array.Copy(produced, 0, _outResidue, _outResidueCount, producedCount);
                    _outResidueCount += producedCount;
                    Logger.Debug($"Appended {producedCount} to residue, new residue count={_outResidueCount}");
                }

                // 3) Consume from residue into mixBuffer to reach monoNeeded
                int need = monoNeeded - monoWrittenOut;
                int take2 = Math.Min(_outResidueCount, need);
                if (take2 > 0)
                {
                    Array.Copy(_outResidue, 0, mixBuffer, monoWrittenOut, take2);
                    monoWrittenOut += take2;

                    // shift residue
                    int remain2 = _outResidueCount - take2;
                    if (remain2 > 0)
                    {
                        Array.Copy(_outResidue, take2, _outResidue, 0, remain2);
                    }
                    _outResidueCount = remain2;
                    Logger.Debug($"Consumed {take2} from residue into mixBuffer, monoWrittenOut={monoWrittenOut}, residue remaining={_outResidueCount}");
                }

                // If we produced nothing (under-run) and residue is empty, break to avoid infinite loop
                if (producedCount == 0 && _outResidueCount == 0)
                {
                    Logger.Debug($"Under-run detected: producedCount=0, residue=0, padding {monoNeeded - monoWrittenOut} zeros");
                    // pad remaining with zeros
                    int rest = monoNeeded - monoWrittenOut;
                    if (rest > 0)
                    {
                        Array.Clear(mixBuffer, monoWrittenOut, rest);
                        monoWrittenOut = monoNeeded;
                    }
                    break;
                }
            }

            Logger.Debug($"Read END: monoNeeded={monoNeeded}, monoWrittenOut={monoWrittenOut}, residue={_outResidueCount}");

            // Convert to stereo using monoWrittenOut (should equal monoNeeded)
            buffer = SeparateAudio(mixBuffer, monoWrittenOut, 0, buffer, offset, radioId);
            return EnsureFullBuffer(buffer, monoWrittenOut * 2, offset, count);
        }

        private float[] HandleStartEndTones(float[] mixBuffer, int count, bool transmisson,
          RadioInformation.Modulation modulation, bool encryption, out int outputSamples)
        {
            //enqueue
            if (transmisson && !hasPlayedTransmissionStart)
            {
                hasPlayedTransmissionStart = true;
                hasPlayedTransmissionEnd = false;

                PlaySoundEffectStartReceive(encryption, modulation);
            }
            else if (!transmisson && !hasPlayedTransmissionEnd && IsEndOfTransmission)
            {
                hasPlayedTransmissionStart = false;
                hasPlayedTransmissionEnd = true;

                //TODO not sure about simultaneous
                //We used to have this logic https://github.com/ciribob/DCS-SimpleRadioStandalone/blob/cd8fcbf7e2b2fafcf30875fc958276e3083e0ebb/DCS-SR-Client/Network/UDPVoiceHandler.cs#L135
                //if (!radioReceivingState.IsSimultaneous)
                PlaySoundEffectEndReceive(modulation);
            }

            //read
            if (effectsBuffer.Count > 0)
            {
                float[] tempBuffer = new float[count];

                effectsBuffer.Read(tempBuffer, 0, count);

                for (int i = 0; i < count; i++)
                {
                    mixBuffer[i] += (tempBuffer[i] * lastVolume);
                    /// should we clip here?
                }

                outputSamples = count;
            }
            else
            {
                outputSamples = 0;
            }

            return mixBuffer;
        }

        private void PlaySoundEffectEndReceive(RadioInformation.Modulation modulation)
        {
            if (!profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End))
            {
                return;
            }

            bool midsTone = profileSettings.GetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect);

            if (radioId == 0)
            {
                var effect = _cachedAudioEffectsProvider.SelectedIntercomTransmissionEndEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else if (modulation == RadioInformation.Modulation.MIDS && midsTone)
            {
                //end receive tone for MIDS
                var effect = _cachedAudioEffectsProvider.MIDSEndTone;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else
            {
                var effect = _cachedAudioEffectsProvider.SelectedRadioTransmissionEndEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
        }

        public void PlaySoundEffectStartReceive(bool encrypted, RadioInformation.Modulation modulation)
        {
            if (!profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start))
            {
                return;
            }

            bool midsTone = profileSettings.GetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect);

            if (modulation == RadioInformation.Modulation.MIDS && midsTone)
            {
                //no tone for MIDS
                return;
            }


            if (radioId == 0)
            {
                var effect = _cachedAudioEffectsProvider.SelectedIntercomTransmissionStartEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else if (encrypted &&
                     profileSettings.GetClientSettingBool(ProfileSettingsKeys
                         .RadioEncryptionEffects))
            {
                var effect = _cachedAudioEffectsProvider.KY58EncryptionEndTone;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else
            {
                var effect = _cachedAudioEffectsProvider.SelectedRadioTransmissionStartEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
        }

        public void PlaySoundEffectStartTransmit(bool encrypted, float volume, RadioInformation.Modulation modulation)
        {
            lastModulation = modulation;
            lastVolume = volume;

            if (!profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start))
            {
                return;
            }

            bool midsTone = profileSettings.GetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect);

            if (radioId == 0)
            {
                var effect = _cachedAudioEffectsProvider.SelectedIntercomTransmissionStartEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else if (encrypted && (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEncryptionEffects)))
            {
                var effect = _cachedAudioEffectsProvider.KY58EncryptionTransmitTone;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else if (modulation == RadioInformation.Modulation.MIDS && midsTone)
            {
                var effect = _cachedAudioEffectsProvider.MIDSTransmitTone;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else
            {
                var effect = _cachedAudioEffectsProvider.SelectedRadioTransmissionStartEffect;

                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
        }

        public void PlaySoundEffectEndTransmit(float volume, RadioInformation.Modulation modulation)
        {
            lastModulation = modulation;
            lastVolume = volume;

            if (!profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End))
            {
                return;
            }

            bool midsTone = profileSettings.GetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect);

            if (radioId == 0)
            {
                var effect = _cachedAudioEffectsProvider.SelectedIntercomTransmissionEndEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else if (modulation == RadioInformation.Modulation.MIDS && midsTone)
            {
                var effect = _cachedAudioEffectsProvider.MIDSEndTone;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
            else
            {
                var effect = _cachedAudioEffectsProvider.SelectedRadioTransmissionEndEffect;
                if (effect.Loaded)
                {
                    effectsBuffer.Write(effect.AudioEffectFloat, 0, effect.AudioEffectFloat.Length);
                }
            }
        }



        public float[] SeparateAudio(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat, int dstOffset,
            int radioId)
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
                balance = profileSettings.GetClientSettingFloat(settingType);
            }
            catch (Exception)
            {
                //ignore
            }

            return CreateBalancedMix(srcFloat, srcCount, srcOffset, dstFloat, dstOffset, balance);
        }

        public static float[] CreateBalancedMix(float[] srcFloat, int srcCount, int srcOffset, float[] dstFloat,
            int dstOffset, float balance)
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

        private int EnsureFullBuffer(float[] buffer, int samplesCount, int offset, int count)
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

            //Should be impossible - ensures audio doesnt crash if its not
            if (samplesCount > count)
            {
                samplesCount = count;
            }

            return samplesCount;
        }

        public bool IsEndOfTransmission
        {
            get { return (DateTime.Now.Ticks - lastReceivedAt) < 3500000; }
        }
        
        private float[] _blockMono;
        private float[] _blockSecondary;

        private float[] ProduceOneMonoBlock(int block, out int producedCount, out bool anyTransmission, out bool ky58Tone, out int outSamplesAfterEffects)
        {
            anyTransmission = false;
            ky58Tone = false;
            producedCount = 0;
            outSamplesAfterEffects = 0;

            _mainAudio.Clear();
            _secondaryAudio.Clear();

            _blockMono = BufferHelpers.Ensure(_blockMono, block);
            Array.Clear(_blockMono, 0, block);

            _blockSecondary = BufferHelpers.Ensure(_blockSecondary, block);
            Array.Clear(_blockSecondary, 0, block);

            // Mix sources into _blockMono exactly 'block' samples
            lock (sources)
            {
                // Accumulator for all sources (mono, block-sized)
                float[] accum = _blockMono;

                int idx = sources.Count - 1;
                while (idx >= 0)
                {
                    var source = sources[idx];

                    // Per-source residue
                    if (!_sourceResidues.ContainsKey(source))
                        _sourceResidues[source] = new SourceResidueBuffer();
                    var residue = _sourceResidues[source];

                    // Temp mono for this source
                    _tempMono = BufferHelpers.Ensure(_tempMono, block);
                    Array.Clear(_tempMono, 0, block);

                    int written = 0;

                    // 1) residue first
                    if (residue.Count > 0)
                    {
                        int take = Math.Min(residue.Count, block);
                        Array.Copy(residue.Buffer, 0, _tempMono, 0, take);
                        written += take;

                        int remain = residue.Count - take;
                        if (remain > 0)
                            Array.Copy(residue.Buffer, take, residue.Buffer, 0, remain);
                        residue.Count = remain;
                    }

                    // 2) pull full 960s (here block is 960, so while rarely runs; kept for clarity)
                    while (written + 960 <= block)
                    {
                        var t = source.JitterBufferProviderInterface[radioId].Read(960);
                        Logger.Debug($"RadioMixingProvider: Read from jitterBuffer[{radioId}] for source, got PCMAudioLength={t.PCMAudioLength}, IsSecondary={t.IsSecondary}");
                        if (t.PCMAudioLength > 0 && t.PCMMonoAudio != null)
                        {
                            Array.Copy(t.PCMMonoAudio, 0, _tempMono, written, t.PCMAudioLength);
                            written += t.PCMAudioLength;

                            if (t.IsSecondary) _secondaryAudio.Add(t);
                            else _mainAudio.Add(t);

                            Logger.Debug($"RadioMixingProvider: Added jitter entry to {(t.IsSecondary ? "secondary" : "main")} audio lists. Count main={_mainAudio.Count}, secondary={_secondaryAudio.Count}");

                            lastModulation = t.Modulation;
                            lastVolume = t.Volume;
                        }
                        else
                        {
                            Array.Clear(_tempMono, written, 960);
                            written += 960;
                        }
                    }

                    // 3) tail (should be zero because block==960, but keep robust)
                    int tail = block - written;
                    if (tail > 0)
                    {
                        var t = source.JitterBufferProviderInterface[radioId].Read(960);
                        int got = (t.PCMAudioLength > 0 && t.PCMMonoAudio != null) ? t.PCMAudioLength : 0;

                        if (got >= tail)
                        {
                            Array.Copy(t.PCMMonoAudio, 0, _tempMono, written, tail);
                            written += tail;

                            int leftover = got - tail;
                            if (leftover > 0)
                            {
                                if (leftover > 960) leftover = 960;
                                residue.Buffer = BufferHelpers.Ensure(residue.Buffer, leftover);
                                Array.Copy(t.PCMMonoAudio, tail, residue.Buffer, 0, leftover);
                                residue.Count = leftover;
                            }

                            if (got > 0)
                            {
                                if (t.IsSecondary) _secondaryAudio.Add(t);
                                else _mainAudio.Add(t);

                                lastModulation = t.Modulation;
                                lastVolume = t.Volume;
                            }
                        }
                        else
                        {
                            Array.Clear(_tempMono, written, tail);
                            written += tail;
                            residue.Count = 0;
                        }
                    }

                    // Mix this source into accumulator
                    for (int i = 0; i < block; i++)
                        accum[i] += _tempMono[i];

                    idx--;
                }
            }

            if (_mainAudio.Count > 0 || _secondaryAudio.Count > 0)
            {
                lastReceivedAt = DateTime.Now.Ticks;
                hasPlayedTransmissionEnd = false;
                _audioRecordingManager.AppendClientAudio(_mainAudio, _secondaryAudio, radioId);
                anyTransmission = true;
            }

            // Soft clip before pipeline
            for (int i = 0; i < block; i++)
            {
                float x = _blockMono[i];
                if (x > 1f) x = 1f;
                else if (x < -1f) x = -1f;
                _blockMono[i] = x;
            }

            // Process primary and secondary
            int primarySamples = 0;
            int secondarySamples = 0;

            var procMain = pipeline.ProcessClientTransmissions(_blockMono, _mainAudio, out primarySamples);

            Array.Clear(_blockSecondary, 0, block);
            var procSec = pipeline.ProcessClientTransmissions(_blockSecondary, _secondaryAudio, out secondarySamples);

            // Mix main + secondary
            var mixed = AudioManipulationHelper.MixArraysNoClipping(procMain, primarySamples, procSec, secondarySamples, out int outputSamples);

            // Start/end tones
            bool transmitting = (_mainAudio.Count > 0 || _secondaryAudio.Count > 0);
            mixed = HandleStartEndTones(mixed, outputSamples, transmitting, lastModulation, ky58Tone, out int effectOut);

            producedCount = Math.Max(outputSamples, effectOut);
            if (producedCount < block)
            {
                // pad to full block so downstream logic can assume 960 available
                mixed = AudioManipulationHelper.ClipArray(mixed, block); // ensures capacity
                Array.Clear(mixed, producedCount, block - producedCount);
                producedCount = block;
            }

            return mixed;
        }
    }
}

