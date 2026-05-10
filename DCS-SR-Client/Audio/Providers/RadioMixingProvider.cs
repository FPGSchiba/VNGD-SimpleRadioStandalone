using System;
using System.Collections.Generic;
using System.Linq;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using NAudio.Utils;
using NAudio.Wave;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Recording;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
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

        // In-process throttle for debug logs to prevent log spam from tight audio loops
        private static readonly Dictionary<string, long> _rmThrottleLastMs = new Dictionary<string, long>();
        private static readonly object _rmThrottleLock = new object();
        private void DebugThrottledRm(string key, Func<string> messageFactory, int minMs = 200)
        {
            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            var should = false;
            lock (_rmThrottleLock)
            {
                if (!_rmThrottleLastMs.TryGetValue(key, out var last) || (now - last) >= minMs)
                {
                    _rmThrottleLastMs[key] = now;
                    should = true;
                }
            }
            if (should && messageFactory != null && Logger.IsDebugEnabled)
            {
                Logger.Debug(messageFactory());
            }
        }

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
                DebugThrottledRm($"RM_AddMixerInput_{radioId}", () => $"RadioMixingProvider[{radioId}]: AddMixerInput called. new sources count={sources.Count}", 2000);
            }
        }

        public void RemoveMixerInput(ClientAudioProvider mixerInput)
        {
            lock (sources)
            {
                sources.Remove(mixerInput);
                DebugThrottledRm($"RM_RemoveMixerInput_{radioId}", () => $"RadioMixingProvider[{radioId}]: RemoveMixerInput called. new sources count={sources.Count}", 2000);
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
            // Throttle the very hot "Read called" message
            DebugThrottledRm("RM_Read_Called", () => "RadioMixingProvider Read called", 1000);
            int monoNeeded = count / 2;

            // Ensure mixBuffer large enough for monoNeeded
            mixBuffer = BufferHelpers.Ensure(mixBuffer, monoNeeded);
            Array.Clear(mixBuffer, 0, monoNeeded);

            DebugThrottledRm("RM_Read_Start", () => $"Read START: monoNeeded={monoNeeded}, sources.Count={sources.Count}, _outResidueCount={_outResidueCount}, _outResidueLen={(_outResidue==null?0:_outResidue.Length)}", 500);

            // 1) Serve from output residue first
            int monoWrittenOut = 0;
            if (_outResidueCount > 0)
            {
                int take = Math.Min(_outResidueCount, monoNeeded);
                // Defensive clamp against mismatched array sizes
                int safeOutLen = _outResidue == null ? 0 : _outResidue.Length;
                int safeMixLen = mixBuffer == null ? 0 : mixBuffer.Length;
                int safeTake = Math.Min(take, Math.Min(safeOutLen, safeMixLen));
                if (safeTake != take)
                {
                    Logger.Warn($"Adjusted residue take from {take} to {safeTake} due to buffer sizes (outResidueLen={safeOutLen}, mixBufferLen={safeMixLen})");
                    take = safeTake;
                }

                if (take > 0)
                {
                    Array.Copy(_outResidue, 0, mixBuffer, 0, take);
                    monoWrittenOut += take;

                    // shift residue
                    int remain = _outResidueCount - take;
                    if (remain > 0)
                    {
                        // clamp remain to array lengths
                        int safeRemain = Math.Min(remain, safeOutLen - take);
                        if (safeRemain > 0)
                            Array.Copy(_outResidue, take, _outResidue, 0, safeRemain);

                        _outResidueCount = safeRemain;
                    }
                    else
                    {
                        _outResidueCount = 0;
                    }
                    DebugThrottledRm("RM_ServedResidue", () => $"Served {take} from residue, monoWrittenOut={monoWrittenOut}, residue remaining={_outResidueCount}", 1000);
                }
                else
                {
                    // nothing safe to copy
                    _outResidueCount = 0;
                }
            }

            // 2) Produce new 960-sample blocks as needed to fill monoNeeded, but produce in whole 960 blocks
            const int block = 960;
            // Figure how many additional mono samples we must produce (in whole 960-blocks).
            // Use the remaining need (monoNeeded minus what we've already written out) so we don't produce extra
            // when residue already satisfied the request.
            int produceSamples = 0;
            int remainingNeed = monoNeeded - monoWrittenOut; // how many mono samples we still need in mixBuffer
            int neededAgainstResidue = remainingNeed - _outResidueCount; // how many samples we must produce beyond current residue
            if (neededAgainstResidue > 0)
            {
                produceSamples = ((neededAgainstResidue + block - 1) / block) * block; // round up to whole 960 blocks
            }

            int blocksToProduce = (produceSamples / block);
            DebugThrottledRm("RM_ProducePlan", () => $"Produce plan: monoNeeded={monoNeeded}, monoWrittenOut={monoWrittenOut}, _outResidueCount={_outResidueCount}, remainingNeed={remainingNeed}, produceSamples={produceSamples}, blocksToProduce={blocksToProduce}", 500);

            // Produce the required number of whole blocks (or until we detect under-run)
            for (int b = 0; b < blocksToProduce; b++)
            {
                DebugThrottledRm("RM_CallProduce", () => $"Calling ProduceOneMonoBlock, blockIndex={b+1}/{blocksToProduce}", 500);
                float[] produced = ProduceOneMonoBlock(block, out int producedCount, out bool anyTx, out bool ky58ToneLocal, out int outSamplesAfterEffects);
                DebugThrottledRm("RM_ProducedResult", () => $"ProduceOneMonoBlock returned: producedCount={producedCount}, anyTx={anyTx}", 500);

                if (producedCount > 0)
                {
                    int neededCapacity = _outResidueCount + producedCount;
                    _outResidue = BufferHelpers.Ensure(_outResidue, neededCapacity);
                    int safeProducedLen = produced == null ? 0 : produced.Length;
                    int safeCopy = Math.Min(producedCount, safeProducedLen);
                    if (safeCopy != producedCount)
                    {
                        Logger.Warn($"Adjusted producedCount from {producedCount} to {safeCopy} due to produced array length");
                    }
                    Array.Copy(produced, 0, _outResidue, _outResidueCount, safeCopy);
                    _outResidueCount += safeCopy;
                    DebugThrottledRm("RM_AppendedResidue", () => $"Appended {safeCopy} to residue, new residue count={_outResidueCount}", 1000);
                }

                // If we produced nothing and residue is empty, stop producing and fallthrough to padding
                if (producedCount == 0 && _outResidueCount == 0)
                {
                    DebugThrottledRm("RM_Underrun", () => $"Under-run detected during production: producedCount=0, residue=0, will pad later", 2000);
                    break;
                }
            }

            // 3) Consume from residue into mixBuffer to reach monoNeeded
            int need = monoNeeded - monoWrittenOut;
            int take2 = Math.Min(_outResidueCount, need);
            if (take2 > 0)
            {
                int safeOutLen2 = _outResidue == null ? 0 : _outResidue.Length;
                int safeMixLen2 = mixBuffer == null ? 0 : mixBuffer.Length;
                int safeTake2 = Math.Min(take2, Math.Min(safeOutLen2, safeMixLen2 - monoWrittenOut));
                if (safeTake2 != take2)
                {
                    Logger.Warn($"Adjusted residue->mix take from {take2} to {safeTake2} due to buffer sizes (outResidueLen={safeOutLen2}, mixBufferLen={safeMixLen2}, monoWrittenOut={monoWrittenOut})");
                    take2 = safeTake2;
                }

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
                    DebugThrottledRm("RM_ConsumedResidue", () => $"Consumed {take2} from residue into mixBuffer, monoWrittenOut={monoWrittenOut}, residue remaining={_outResidueCount}", 1000);
                }
            }

            // If after production and consumption we still don't have enough, pad remaining with zeros
            if (monoWrittenOut < monoNeeded)
            {
                int rest = monoNeeded - monoWrittenOut;
                DebugThrottledRm("RM_Padding", () => $"Padding {rest} zeros into mixBuffer (monoWrittenOut={monoWrittenOut}, monoNeeded={monoNeeded})", 1000);
                Array.Clear(mixBuffer, monoWrittenOut, rest);
                monoWrittenOut = monoNeeded;
            }

            DebugThrottledRm("RM_Read_End", () => $"Read END: monoNeeded={monoNeeded}, monoWrittenOut={monoWrittenOut}, residue={_outResidueCount}", 500);

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
                        int safeResBufLen = residue.Buffer == null ? 0 : residue.Buffer.Length;
                        int safeTake = Math.Min(take, safeResBufLen);
                        if (safeTake != take)
                        {
                            Logger.Warn($"Adjusted residue take for source from {take} to {safeTake} due to residue buffer length");
                            take = safeTake;
                        }

                        if (take > 0)
                        {
                            Array.Copy(residue.Buffer, 0, _tempMono, 0, take);
                            written += take;

                            int remain = residue.Count - take;
                            if (remain > 0)
                                Array.Copy(residue.Buffer, take, residue.Buffer, 0, remain);
                            residue.Count = remain;

                            DebugThrottledRm("RM_UsedResidue", () => $"RadioMixingProvider: used {take} samples from residue for source (written={written}, residueRemaining={residue.Count})", 2000);
                        }
                    }

                    // 2) pull full 960s (here block is 960, so while rarely runs; kept for clarity)
                    while (written + 960 <= block)
                    {
                        var t = source.JitterBufferProviderInterface[radioId].Read(960);
                        // compute packet age for diagnostics (may be -1 if not stamped)
                        long ageMsRead = -1;
                        try { ageMsRead = (t.ReceivedAtTicks > 0) ? (DateTime.UtcNow.Ticks - t.ReceivedAtTicks) / TimeSpan.TicksPerMillisecond : -1; } catch { ageMsRead = -1; }
                        DebugThrottledRm("RM_ReadFromJitter", () => $"RadioMixingProvider: Read from jitterBuffer[{radioId}] for source, got PCMAudioLength={t.PCMAudioLength}, PCMMonoLen={(t.PCMMonoAudio==null?0:t.PCMMonoAudio.Length)}, IsSecondary={t.IsSecondary}, Guid={t.Guid}, ageMs={ageMsRead}, lastReadState={source?.LastUpdate}", 500);
                        // Additional, sparser log when we get a short frame / underflow to highlight issues
                        if ((t.PCMAudioLength <= 0) || (t.PCMMonoAudio == null) || (t.PCMAudioLength < 960))
                        {
                            DebugThrottledRm("RM_ReadFromJitter_Short", () => $"RadioMixingProvider: jitter Read returned short/empty for radio[{radioId}] guid={t.Guid} pcmlen={t.PCMAudioLength} expected=960 ageMs={ageMsRead}", 2000);
                        }
                        if (t.PCMAudioLength > 0 && t.PCMMonoAudio != null)
                        {
                            // compute quick stats for this transmission
                            double sumSqT = 0; float maxAbsT = 0f;
                            int actualLen = Math.Min(t.PCMAudioLength, t.PCMMonoAudio.Length);
                            for (int s = 0; s < actualLen; s++)
                            {
                                var v = t.PCMMonoAudio[s];
                                sumSqT += v * v;
                                var a = Math.Abs(v);
                                if (a > maxAbsT) maxAbsT = a;
                            }
                            var rmsT = Math.Sqrt(actualLen>0?sumSqT/actualLen:0);
                            DebugThrottledRm("RM_TransmissionStats", () => $"RadioMixingProvider: Transmission stats guid={t.Guid}, pktLen={t.PCMAudioLength}, actualLen={actualLen}, rms={rmsT:0.000}, max={maxAbsT:0.000}, ageMs={ageMsRead}", 1000);

                            // Clamp copy length to avoid OOR
                            int copyLen = Math.Min(actualLen, block - written);
                            if (copyLen < actualLen)
                            {
                                DebugThrottledRm("RM_CopyClamp", () => $"RadioMixingProvider: clamped copyLen from {actualLen} to {copyLen} due to block/written constraints", 2000);
                            }
                            if (copyLen > 0)
                            {
                                Array.Copy(t.PCMMonoAudio, 0, _tempMono, written, copyLen);
                                written += copyLen;
                            }

                            if (t.IsSecondary) _secondaryAudio.Add(t);
                            else _mainAudio.Add(t);

                            DebugThrottledRm("RM_AddedJitterEntry", () => $"RadioMixingProvider: Added jitter entry to {(t.IsSecondary ? "secondary" : "main")} audio lists. Count main={_mainAudio.Count}, secondary={_secondaryAudio.Count}, guid={t.Guid}, pkt={t.PCMAudioLength}", 1000);

                            // Log if this is local client's own echo
                            try
                            {
                                var localGuid = ClientStateSingleton.Instance.ClientId;
                                if (t.Guid == localGuid)
                                {
                                    DebugThrottledRm("RM_LocalGuidDetected", () => $"RadioMixingProvider: Detected local-guid transmission in {(t.IsSecondary?"secondary":"main")} audio. guid={t.Guid}", 5000);
                                }
                            }
                            catch (Exception) { }
                            DebugThrottledRm("RM_LastModulation", () => $"RadioMixingProvider: lastModulation set to {t.Modulation}, lastVolume={t.Volume}", 1000);
                        }
                        else
                        {
                            // jitter buffer returned no data -> zero-fill this frame. Log sparsely with age if available.
                            DebugThrottledRm("RM_ZeroFill", () =>
                            {
                                var ageMsg = ageMsRead >= 0 ? $"ageMs={ageMsRead}" : "ageMs=unknown";
                                return $"RadioMixingProvider: jitter read returned empty for radio[{radioId}] - zero-filling 960 samples ({ageMsg})";
                            }, 2000);
                            Array.Clear(_tempMono, written, 960);
                            written += 960;
                        }
                    }

                    // 3) tail (should be zero because block==960, but keep robust)
                    int tail = block - written;
                    if (tail > 0)
                    {
                        var t = source.JitterBufferProviderInterface[radioId].Read(960);
                        int got = (t.PCMAudioLength > 0 && t.PCMMonoAudio != null) ? Math.Min(t.PCMAudioLength, t.PCMMonoAudio.Length) : 0;

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

                                // compute age for tail-entry logging
                                long ageMsTail = -1;
                                try { ageMsTail = (t.ReceivedAtTicks > 0) ? (DateTime.UtcNow.Ticks - t.ReceivedAtTicks) / TimeSpan.TicksPerMillisecond : -1; } catch { ageMsTail = -1; }
                                DebugThrottledRm("RM_AddedTailEntry", () => $"RadioMixingProvider: Added jitter tail entry to {(t.IsSecondary?"secondary":"main")} lists. guid={t.Guid}, got={got}, ageMs={ageMsTail}", 1000);
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

                    DebugThrottledRm("RM_MixedSource", () => $"RadioMixingProvider: mixed source idx={idx} into accumulator (block={block}, written={written})", 1000);

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

             // Detect if clipping actually modified values
             bool clipped = false;
             for (int i = 0; i < block; i++)
             {
                 var v = _blockMono[i];
                 if (v >= 1f || v <= -1f)
                 {
                     clipped = true;
                     break;
                 }
             }
             
             // Process primary and secondary
             var procMain = pipeline.ProcessClientTransmissions(_blockMono, _mainAudio, out var primarySamples);
             DebugThrottledRm("RM_ProcessPrimary", () => $"RadioMixingProvider: pipeline.ProcessClientTransmissions returned primarySamples={primarySamples}", 500);

            Array.Clear(_blockSecondary, 0, block);
             var procSec = pipeline.ProcessClientTransmissions(_blockSecondary, _secondaryAudio, out var secondarySamples);
             DebugThrottledRm("RM_ProcessSecondary", () => $"RadioMixingProvider: pipeline.ProcessClientTransmissions (secondary) returned secondarySamples={secondarySamples}", 500);

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

            try
             {
                 var localGuid = ClientStateSingleton.Instance.ClientId;
                 var containsLocal = _mainAudio.Any(t => t.Guid == localGuid) || _secondaryAudio.Any(t => t.Guid == localGuid);
                 if (producedCount > 0)
                 {
                     var firstSample = (mixed != null && mixed.Length > 0) ? mixed[0].ToString("0.000") : "0.000";
                     // copy values to locals to avoid capturing out/ref parameters inside lambda
                     int producedCountLocal = producedCount;
                     int mainCountLocal = _mainAudio.Count;
                     int secondaryCountLocal = _secondaryAudio.Count;
                     bool containsLocalLocal = containsLocal;
                     string firstSampleLocal = firstSample;
                     DebugThrottledRm("RM_ProduceSummary", () => $"ProduceOneMonoBlock: producedCount={producedCountLocal}, mainCount={mainCountLocal}, secondaryCount={secondaryCountLocal}, containsLocal={containsLocalLocal}, firstSample={firstSampleLocal}, clipped={clipped}", 500);

                     // compute RMS of mixed block for debug
                     double sumSqM = 0;
                     float maxAbsM = 0f;
                     int sampleLen = Math.Min(mixed.Length, producedCount);
                     for (int i = 0; i < sampleLen; i++)
                     {
                         var v = mixed[i];
                         sumSqM += v * v;
                         var a = Math.Abs(v);
                         if (a > maxAbsM) maxAbsM = a;
                     }
                     var rmsM = Math.Sqrt(sampleLen > 0 ? sumSqM / sampleLen : 0);
                     DebugThrottledRm("RM_ProduceStats", () => $"ProduceOneMonoBlock: mixed stats rms={rmsM:0.000}, max={maxAbsM:0.000}", 1000);
                 }
                 else
                 {
                    DebugThrottledRm("RM_ProduceZero", () => $"ProduceOneMonoBlock: producedCount=0, mainCount={_mainAudio.Count}, secondaryCount={_secondaryAudio.Count}, containsLocal={containsLocal}", 1000);
                 }
             }
             catch (Exception ex)
             {
                 Logger.Warn(ex, "ProduceOneMonoBlock: error logging local presence");
             }
             return mixed;
          }
      }
  }

