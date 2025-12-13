using System;
using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using MathNet.Filtering;
using NAudio.Dsp;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Audio.Utility;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Setting;
using NLog;

namespace Vanguard.VCS.Client.Audio.Providers
{
    public class ClientEffectsPipeline
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Random _random = new Random();

        private OnlineFilter[] _filters;

        private readonly BiQuadFilter _highPassFilter;
        private readonly BiQuadFilter _lowPassFilter;

        private static readonly double HQ_RESET_CHANCE = 0.8;

        private int hqTonePosition = 0;
        private int natoPosition = 0;
        private int fmNoisePosition = 0;
        private int vhfNoisePosition = 0;
        private int uhfNoisePosition = 0;
        private int hfNoisePosition = 0;

        private readonly CachedAudioEffectProvider effectProvider = CachedAudioEffectProvider.Instance;

        private bool natoToneEnabled;
        private bool hqToneEnabled;
        private bool radioEffectsEnabled;
        private bool clippingEnabled;
        private float hqToneVolume;
        private float natoToneVolume;

        private float fmVol;
        private float hfVol;
        private float uhfVol;
        private float vhfVol;

        private long lastRefresh = 0; //last refresh of settings

        private readonly ProfileSettingsStore profileSettings;

        private bool radioEffects;
        private bool radioBackgroundNoiseEffect;

        private CachedAudioEffect amCollisionEffect;
        private int amEffectPosition = 0;
        private float amCollisionVol = 1.0f;

        private bool irlRadioRXInterference = false;

        private readonly SyncedServerSettings serverSettings;


        public ClientEffectsPipeline()
        {
            profileSettings = GlobalSettingsStore.Instance.ProfileSettingsStore;
            serverSettings =  SyncedServerSettings.Instance;

            _filters = new OnlineFilter[2];
            _filters[0] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 560, 3900);
            _filters[1] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 100, 4500);

            _highPassFilter = BiQuadFilter.HighPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 520, 0.97f);
            _lowPassFilter = BiQuadFilter.LowPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 4130, 2.0f);
            RefreshSettings();

            amCollisionEffect = CachedAudioEffectProvider.Instance.AMCollision;
           
        }
        
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

        private void RefreshSettings()
        {
            //only get settings every 3 seconds - and cache them - issues with performance
            long now = DateTime.Now.Ticks;

            if (TimeSpan.FromTicks(now - lastRefresh).TotalSeconds > 3) //3 seconds since last refresh
            {
                lastRefresh = now;

                natoToneEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.NATOTone);
                hqToneEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone);
                radioEffectsEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects);
                clippingEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping);
                hqToneVolume = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.HQToneVolume);
                natoToneVolume = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume);
                amCollisionVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.AMCollisionVolume);

                fmVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume);
                hfVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume);
                uhfVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume);
                vhfVol = profileSettings.GetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume);

                radioEffects = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects);

                radioBackgroundNoiseEffect = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioBackgroundNoiseEffect) ;

                irlRadioRXInterference = serverSettings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE);
            }
        }

        public float[] ProcessClientTransmissions(float[] tempBuffer, List<DeJitteredTransmission> transmissions, out int clientTransmissionLength)
        {
            RefreshSettings();

            // Defensive guard: transmissions can be null or empty (observed in logs leading to OOR when accessing [0])
            if (Logger.IsTraceEnabled)
            {
                Logger.Trace("ProcessClientTransmissions START: tempBufferLen={0}, transmissionsCount={1}", tempBuffer == null ? 0 : tempBuffer.Length, transmissions == null ? 0 : transmissions.Count);
            }

            if (transmissions == null || transmissions.Count == 0)
            {
                clientTransmissionLength = 0;
                if (tempBuffer != null && tempBuffer.Length > 0)
                    Array.Clear(tempBuffer, 0, tempBuffer.Length);
                DebugThrottledRm("Transmission-Cleared", () => "ProcessClientTransmissions: no transmissions - returning cleared buffer");
                return tempBuffer;
            }

            // New debug: log per-transmission lengths to detect doubled frame sizes
            // keep a coarse count log, but avoid spamming per-packet detail every frame by
            // only emitting the first transmission's details (if present) and relying on
            // the aggregated message for the rest.
            DebugThrottledRm("Transmission",  () => $"ProcessClientTransmissions: transmissions.Count={transmissions.Count}");
            if (transmissions.Count > 0)
            {
                var tr0 = transmissions[0];
                DebugThrottledRm("Transmission-First", () => $"  tx[0] Guid={tr0.Guid}, PCMAudioLength={tr0.PCMAudioLength}, PCMMonoLen={(tr0.PCMMonoAudio==null?0:tr0.PCMMonoAudio.Length)}, Volume={tr0.Volume}");
            }

            DeJitteredTransmission lastTransmission = transmissions[0];
            if (tempBuffer != null && tempBuffer.Length > 0)
                Array.Clear(tempBuffer, 0, tempBuffer.Length);
            // Prevent accumulation from previous calls; stale content can create a steady whine

            clientTransmissionLength = 0;
            int skippedCount = 0;
            foreach (var transmission in transmissions)
            {
                // DeJitteredTransmission is a struct; it cannot be null. Validate its PCMMonoAudio array instead.
                var mixCount = Math.Min(transmission.PCMAudioLength, (tempBuffer==null?0:tempBuffer.Length));

                if (transmission.PCMMonoAudio == null)
                {
                    Logger.Warn($"ProcessClientTransmissions: SKIPPED - PCMMonoAudio is NULL for Guid={transmission.Guid}, PCMAudioLength={transmission.PCMAudioLength}");
                    skippedCount++;
                    continue;
                }

                // Clamp mixCount to the actual array length to avoid OOR
                mixCount = Math.Min(mixCount, transmission.PCMMonoAudio.Length);
                if (mixCount <= 0)
                {
                    Logger.Warn($"ProcessClientTransmissions: SKIPPED - mixCount={mixCount} for Guid={transmission.Guid}, PCMAudioLength={transmission.PCMAudioLength}, PCMMonoLen={transmission.PCMMonoAudio.Length}, tempBufferLen={tempBuffer?.Length ?? 0}");
                    skippedCount++;
                    continue;
                }

                for (var i = 0; i < mixCount; i++)
                {
                    tempBuffer[i] += transmission.PCMMonoAudio[i];
                }

                clientTransmissionLength = Math.Max(clientTransmissionLength, mixCount);
            }
            
            var transmissionCount = clientTransmissionLength;

            if (skippedCount > 0 || clientTransmissionLength == 0)
            {
                Logger.Warn($"ProcessClientTransmissions: transmissions={transmissions.Count}, skipped={skippedCount}, finalLength={clientTransmissionLength}");
            }

            DebugThrottledRm("Aggregated-Transmission", () => $"ProcessClientTransmissions: aggregated clientTransmissionLength={transmissionCount}");

            bool process = true;

            // take info account server setting AND volume of this radio AND if its AM or FM
            // FOR HAVEQUICK - only if its MORE THAN TWO
            if (lastTransmission.ReceivedRadio != 0
                && !lastTransmission.NoAudioEffects
                && (lastTransmission.Modulation == RadioInformation.Modulation.AM
                    || lastTransmission.Modulation == RadioInformation.Modulation.FM
                    || lastTransmission.Modulation == RadioInformation.Modulation.HAVEQUICK)
                && irlRadioRXInterference)
            {
                if (transmissions.Count > 1)
                {
                    //All AM is wrecked if more than one transmission
                    //For HQ - only if more than TWO transmissions
                    if (lastTransmission.Modulation == RadioInformation.Modulation.AM && amCollisionEffect.Loaded
                    || lastTransmission.Modulation == RadioInformation.Modulation.HAVEQUICK && transmissions.Count > 2)
                    {
                        //replace the buffer with our own
                        int outIndex = 0;

                        var af = amCollisionEffect?.AudioEffectFloat;
                        if (af == null || af.Length == 0)
                        {
                            DebugThrottledRm("AF-Null", () => "ProcessClientTransmissions: AM collision effect buffer missing or empty - skipping AM collision replacement");
                        }
                        else
                        {
                            while (outIndex < clientTransmissionLength)
                            {
                                // ensure position always valid
                                if (amEffectPosition >= af.Length) amEffectPosition = 0;

                                var amByte = af[amEffectPosition++];

                                tempBuffer[outIndex++] = (amByte * amCollisionVol) * lastTransmission.Volume;

                                if (amEffectPosition == af.Length)
                                {
                                    amEffectPosition = 0;
                                }
                            }

                            process = false;
                        }
                    }
                    else if (lastTransmission.Modulation == RadioInformation.Modulation.FM)
                    {
                        //FM picketing / picket fencing - pick one transmission at random
                        //TODO improve this to pick the stronger frequency?

                        int index = _random.Next(transmissions.Count);
                        var transmission = transmissions[index];

                        if (transmission.PCMMonoAudio == null)
                        {
                            DebugThrottledRm("PCMMono-Null", () => $"ProcessClientTransmissions: FM pick selected transmission index={index} but PCMMonoAudio is null, skipping picketing copy");
                        }
                        else
                        {
                            int copyLen = Math.Min(transmission.PCMAudioLength, transmission.PCMMonoAudio.Length);
                            copyLen = Math.Min(copyLen, (tempBuffer==null?0:tempBuffer.Length));

                            for (int i = 0; i < copyLen; i++)
                            {
                                tempBuffer[i] = transmission.PCMMonoAudio[i];
                            }

                            clientTransmissionLength = copyLen;
                        }
                    }

                }
            }

            //only process if AM effect doesnt apply
            if (process)
                tempBuffer = ProcessClientAudioSamples(tempBuffer, clientTransmissionLength, 0, lastTransmission);

            // Capture audio after effects processing
            try
            {
                if (AudioDiagnosticLogger.Instance.IsRunning && tempBuffer != null && clientTransmissionLength > 0)
                {
                    AudioDiagnosticLogger.Instance.CaptureEffectsOutput(
                        lastTransmission.ReceivedRadio, 
                        tempBuffer, 
                        clientTransmissionLength
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error capturing after-effects audio");
            }

            // Only log the first samples when we actually have a buffer and enough samples
            if (tempBuffer != null && clientTransmissionLength >= 4)
            {
                if (Logger.IsTraceEnabled)
                {
                    Logger.Trace("Effects out first samples: {0:0.000}, {1:0.000}, {2:0.000}, {3:0.000}", tempBuffer[0], tempBuffer[1], tempBuffer[2], tempBuffer[3]);
                }
            }

            return tempBuffer;
        }

        public float[] ProcessClientAudioSamples(float[] buffer, int count, int offset, DeJitteredTransmission transmission)
        {
            if (buffer == null || count <= 0) return buffer;

            // Ensure we don’t read/write past the buffer
            if (offset < 0) offset = 0;
            if (offset > buffer.Length) return buffer;
            if (offset + count > buffer.Length) count = buffer.Length - offset;

            // QUICK BYPASS to isolate the effects layer during debugging
            // Toggle this to true to skip effects and just apply volume
            #if DEBUG
            var bypassEffectsForDebug = false;
            #else
            var bypassEffectsForDebug = true;
            #endif
            
            if (!transmission.NoAudioEffects && !bypassEffectsForDebug)
            {
                if (transmission.Modulation == RadioInformation.Modulation.MIDS
                    || transmission.Modulation == RadioInformation.Modulation.SATCOM
                    || transmission.Modulation == RadioInformation.Modulation.INTERCOM)
                {
                    if (radioEffects)
                    {
                        AddRadioEffectIntercom(buffer, count, offset, transmission.Modulation);
                    }
                }
                else
                {
                    AddRadioEffect(buffer, count, offset, transmission.Modulation, transmission.Frequency);
                }
            }

            //final adjust
            AdjustVolume(buffer, count, offset, transmission.Volume);
            var end = offset + count;
            for (var i = offset; i < end; i++)
            {
                var s = buffer[i];
                if (float.IsNaN(s) || float.IsInfinity(s)) { buffer[i] = 0f; continue; }
                if (s > 1f) buffer[i] = 1f;
                else if (s < -1f) buffer[i] = -1f;
            }

            return buffer;
        }

        private void AdjustVolume(float[] buffer, int count, int offset, float volume)
        {
            int outputIndex = offset;
            while (outputIndex < offset + count)
            {
                buffer[outputIndex] *= volume;

                outputIndex++;
            }
        }

        private void AddRadioEffectIntercom(float[] buffer, int count, int offset,RadioInformation.Modulation modulation)
        {
            int outputIndex = offset;
            while (outputIndex < offset + count)
            {
                var audio = _highPassFilter.Transform(buffer[outputIndex]);

                audio = _highPassFilter.Transform(audio);

                if (float.IsNaN(audio))
                    audio = _lowPassFilter.Transform(buffer[outputIndex]);
                else
                    audio = _lowPassFilter.Transform(audio);

                if (!float.IsNaN(audio))
                {
                    // clip
                    if (audio > 1.0f)
                        audio = 1.0f;
                    if (audio < -1.0f)
                        audio = -1.0f;

                    buffer[outputIndex] = audio;
                }

                outputIndex++;
            }
        }


        private void AddRadioEffect(float[] buffer, int count, int offset, RadioInformation.Modulation modulation, double freq)
        {
            int outputIndex = offset;
             
            while (outputIndex < offset + count)
            {
                var audio = (double) buffer[outputIndex];

                if (radioEffectsEnabled)
                {
                    if (clippingEnabled)
                    {
                        if (audio > RadioFilter.CLIPPING_MAX)
                        {
                            audio = RadioFilter.CLIPPING_MAX;
                        }
                        else if (audio < RadioFilter.CLIPPING_MIN)
                        {
                            audio = RadioFilter.CLIPPING_MIN;
                        }
                    }

                    //high and low pass filter
                    for (int j = 0; j < _filters.Length; j++)
                    {
                        var filter = _filters[j];
                        audio = filter.ProcessSample(audio);
                        if (double.IsNaN(audio))
                        {
                            audio = (double)buffer[outputIndex];
                        }

                        audio *= RadioFilter.BOOST;
                    }
                }

                if (modulation == RadioInformation.Modulation.FM
                    && effectProvider.NATOTone.Loaded
                    && natoToneEnabled)
                {
                    var natoTone = effectProvider.NATOTone.AudioEffectFloat;
                    if (natoTone != null && natoTone.Length > 0)
                    {
                        if (natoPosition >= natoTone.Length) natoPosition = 0;
                        audio += ((natoTone[natoPosition]) * natoToneVolume);
                        natoPosition++;
                    }
                    else
                    {
                        DebugThrottledRm("Nato-Tune", () => "AddRadioEffect: NATOTone buffer missing or empty");
                    }
                }

                if (modulation == RadioInformation.Modulation.HAVEQUICK
                     && effectProvider.HAVEQUICKTone.Loaded
                     && hqToneEnabled)
                {
                    var hqTone = effectProvider.HAVEQUICKTone.AudioEffectFloat;
                    if (hqTone != null && hqTone.Length > 0)
                    {
                        if (hqTonePosition >= hqTone.Length) hqTonePosition = 0;

                        audio += ((hqTone[hqTonePosition]) * hqToneVolume);
                        hqTonePosition++;

                        if (hqTonePosition == hqTone.Length)
                        {
                            var reset = _random.NextDouble();

                            if (reset > HQ_RESET_CHANCE)
                            {
                                hqTonePosition = 0;
                            }
                            else
                            {
                                //one back to try again
                                hqTonePosition += -1;
                            }
                        }
                    }
                    else
                    {
                        DebugThrottledRm("HQ-Tune", () => "AddRadioEffect: HAVEQUICKTone buffer missing or empty");
                    }
                }

                audio = AddRadioBackgroundNoiseEffect(audio, modulation,freq);

                // clip
                if (audio > 1.0f)
                    audio = 1.0f;
                if (audio < -1.0f)
                    audio = -1.0f;

                buffer[outputIndex] = (float) audio;

                outputIndex++;
            }
        }

        private double AddRadioBackgroundNoiseEffect(double audio, RadioInformation.Modulation modulation, double freq)
        {
            if (radioBackgroundNoiseEffect)
            {
                if (modulation == RadioInformation.Modulation.HAVEQUICK || modulation == RadioInformation.Modulation.AM)
                {
                    //mix in based on frequency
                    if (freq >= 200d * 1000000)
                    {
                        if (effectProvider.UHFNoise.Loaded)
                        {
                            var noise = effectProvider.UHFNoise.AudioEffectFloat;
                            if (noise != null && noise.Length > 0)
                            {
                                if (uhfNoisePosition >= noise.Length) uhfNoisePosition = 0;
                                audio += ((noise[uhfNoisePosition]) * uhfVol);
                                uhfNoisePosition++;

                                if (uhfNoisePosition == noise.Length)
                                {
                                    uhfNoisePosition = 0;
                                }
                            }
                            else
                            {
                                DebugThrottledRm("UHF-Noise", () => "AddRadioBackgroundNoiseEffect: UHFNoise buffer missing or empty");
                            }
                        }
                    }
                    else if (freq > 80d * 1000000)
                    {
                        if (effectProvider.VHFNoise.Loaded)
                        {
                            //VHF Band? - Very rough
                            var noise = effectProvider.VHFNoise.AudioEffectFloat;
                            if (noise != null && noise.Length > 0)
                            {
                                if (vhfNoisePosition >= noise.Length) vhfNoisePosition = 0;
                                audio += ((noise[vhfNoisePosition]) * vhfVol);
                                vhfNoisePosition++;

                                if (vhfNoisePosition == noise.Length)
                                {
                                    vhfNoisePosition = 0;
                                }
                            }
                            else
                            {
                                DebugThrottledRm("Noise",  () => "AddRadioBackgroundNoiseEffect: VHFNoise buffer missing or empty");
                            }
                        }
                    }
                    else
                    {
                        if (effectProvider.HFNoise.Loaded)
                        {
                            //HF!
                            var noise = effectProvider.HFNoise.AudioEffectFloat;
                            if (noise != null && noise.Length > 0)
                            {
                                if (hfNoisePosition >= noise.Length) hfNoisePosition = 0;
                                audio += ((noise[hfNoisePosition]) * hfVol);
                                hfNoisePosition++;

                                if (hfNoisePosition == noise.Length)
                                {
                                    hfNoisePosition = 0;
                                }
                            }
                            else
                            {
                                DebugThrottledRm("HF-Noise", () => "AddRadioBackgroundNoiseEffect: HFNoise buffer missing or empty");
                            }
                        }
                    }
                }
                else if (modulation == RadioInformation.Modulation.FM)
                {
                    if (effectProvider.FMNoise.Loaded)
                    {

                        //FM picks up most of the 20-60 ish range + has a different effect
                        //HF!
                        var noise = effectProvider.FMNoise.AudioEffectFloat;
                        if (noise != null && noise.Length > 0)
                        {
                            if (fmNoisePosition >= noise.Length) fmNoisePosition = 0;
                            audio += ((noise[fmNoisePosition]) * fmVol);
                            fmNoisePosition++;

                            if (fmNoisePosition == noise.Length)
                            {
                                fmNoisePosition = 0;
                            }
                        }
                        else
                        {
                            DebugThrottledRm("FM-Noise", () => "AddRadioBackgroundNoiseEffect: FMNoise buffer missing or empty");
                        }
                    }
                }
            }

            return audio;
        }
    }
}
