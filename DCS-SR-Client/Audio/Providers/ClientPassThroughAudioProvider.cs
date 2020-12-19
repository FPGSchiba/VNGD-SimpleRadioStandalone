using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using FragLabs.Audio.Codecs;
using MathNet.Filtering;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Providers
{
    public class ClientPassThroughAudioProvider
    {
        private readonly Random _random = new Random();

        private OnlineFilter[] _filters;

        private readonly BiQuadFilter _highPassFilter;
        private readonly BiQuadFilter _lowPassFilter;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        //used for comparison
        public static readonly short FM = Convert.ToInt16((int) RadioInformation.Modulation.FM);
        public static readonly short HQ = Convert.ToInt16((int) RadioInformation.Modulation.HAVEQUICK);
        public static readonly short AM = Convert.ToInt16((int) RadioInformation.Modulation.AM);

        private static readonly double HQ_RESET_CHANCE = 0.8;

        private int hqTonePosition = 0;
        private int natoPosition = 0;
        private int fmNoisePosition = 0;
        private int vhfNoisePosition = 0;
        private int uhfNoisePosition = 0;
        private int hfNoisePosition = 0;

        private readonly CachedAudioEffectProvider effectProvider = CachedAudioEffectProvider.Instance;
        private readonly ProfileSettingsStore profileSettings;

        public ClientPassThroughAudioProvider()
        {
            profileSettings = Settings.GlobalSettingsStore.Instance.ProfileSettingsStore;
            _filters = new OnlineFilter[2];
            _filters[0] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 560, 3900);
            _filters[1] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 100, 4500);

            _highPassFilter = BiQuadFilter.HighPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 520, 0.97f);
            _lowPassFilter = BiQuadFilter.LowPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 4130, 2.0f);

        }

        public byte[] AddClientAudioSamples(TransmittedAudio audio, short[] pcmShort)
        {
            audio.PcmAudioShort = pcmShort;

            if (audio.Modulation == (short) RadioInformation.Modulation.INTERCOM ||
                audio.Modulation == (short) RadioInformation.Modulation.MIDS)
            {
                if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects))
                {
                    AddRadioEffectIntercom(audio);
                }
            }
            else
            {
                AddRadioEffect(audio);
            }

            return ConversionHelpers.ShortArrayToByteArray(audio.PcmAudioShort);

        }

        private void AddRadioEffectIntercom(TransmittedAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioShort;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                var audio = mixedAudio[i] / 32768f;

                audio = _highPassFilter.Transform(audio);

                if (float.IsNaN(audio))
                    audio = _lowPassFilter.Transform(mixedAudio[i]);
                else
                    audio = _lowPassFilter.Transform(audio);

                if (!float.IsNaN(audio))
                {
                    // clip
                    if (audio > 1.0f)
                        audio = 1.0f;
                    if (audio < -1.0f)
                        audio = -1.0f;

                    mixedAudio[i] = (short) (audio * 32767);
                }
            }

        }


        private void AddRadioEffect(TransmittedAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioShort;
            var natoToneEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.NATOTone);
            var hqToneEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone);
            var radioEffectsEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects);
            var clippingEnabled = profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping);
            var hqToneVolume = profileSettings.GetClientSetting(ProfileSettingsKeys.HQToneVolume)
                .DoubleValue;
            var natoToneVolume = profileSettings.GetClientSetting(ProfileSettingsKeys.NATOToneVolume)
                .DoubleValue;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                var audio = (double)mixedAudio[i] / 32768f;

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
                            audio = mixedAudio[j];
                        }

                        audio *= RadioFilter.BOOST;
                    }
                }

                if (clientAudio.Modulation == FM
                    && effectProvider.NATOTone.Loaded
                    && natoToneEnabled)
                {
                    var natoTone = effectProvider.NATOTone.AudioEffectDouble;
                    audio += ((double)(natoTone[natoPosition]) * natoToneVolume);
                    natoPosition++;

                    if (natoPosition == natoTone.Length)
                    {
                        natoPosition = 0;
                    }
                }

                if (clientAudio.Modulation == HQ
                     && effectProvider.HAVEQUICKTone.Loaded
                     && hqToneEnabled)
                {
                    var hqTone = effectProvider.HAVEQUICKTone.AudioEffectDouble;

                    audio += ((double)(hqTone[hqTonePosition]) * hqToneVolume);
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

                audio = AddRadioBackgroundNoiseEffect(audio, clientAudio);

                // clip
                if (audio > 1.0f)
                    audio = 1.0f;
                if (audio < -1.0f)
                    audio = -1.0f;

                mixedAudio[i] = (short)(audio * 32768f);
            }
        }

        private double AddRadioBackgroundNoiseEffect(double audio, TransmittedAudio clientAudio)
        {
            var fmVol = profileSettings.GetClientSetting(ProfileSettingsKeys.FMNoiseVolume)
                .DoubleValue; 

            var hfVol = profileSettings.GetClientSetting(ProfileSettingsKeys.HFNoiseVolume)
                .DoubleValue;
            var uhfVol = profileSettings.GetClientSetting(ProfileSettingsKeys.UHFNoiseVolume)
                .DoubleValue;
            var vhfVol = profileSettings.GetClientSetting(ProfileSettingsKeys.VHFNoiseVolume)
                .DoubleValue;

            if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioBackgroundNoiseEffect))
            {
                if (clientAudio.Modulation == HQ || clientAudio.Modulation == AM)
                {
                    //mix in based on frequency
                    if (clientAudio.Frequency >= 200d * 1000000)
                    {
                        if (effectProvider.UHFNoise.Loaded)
                        {
                            var noise = effectProvider.UHFNoise.AudioEffectDouble;
                            //UHF Band?
                            audio += ((noise[uhfNoisePosition]) * uhfVol);
                            uhfNoisePosition++;

                            if (uhfNoisePosition == noise.Length)
                            {
                                uhfNoisePosition = 0;
                            }
                        }
                    }
                    else if (clientAudio.Frequency > 80d * 1000000)
                    {
                        if (effectProvider.VHFNoise.Loaded)
                        {
                            //VHF Band? - Very rough
                            var noise = effectProvider.VHFNoise.AudioEffectDouble;
                            audio += ((double)(noise[vhfNoisePosition]) * vhfVol);
                            vhfNoisePosition++;

                            if (vhfNoisePosition == noise.Length)
                            {
                                vhfNoisePosition = 0;
                            }
                        }
                    }
                    else
                    {
                        if (effectProvider.HFNoise.Loaded)
                        {
                            //HF!
                            var noise = effectProvider.HFNoise.AudioEffectDouble;
                            audio += ((double)(noise[hfNoisePosition]) * hfVol);
                            hfNoisePosition++;

                            if (hfNoisePosition == noise.Length)
                            {
                                hfNoisePosition = 0;
                            }
                        }
                    }
                }
                else if (clientAudio.Modulation == FM)
                {
                    if (effectProvider.FMNoise.Loaded)
                    {

                        //FM picks up most of the 20-60 ish range + has a different effect
                        //HF!
                        var noise = effectProvider.FMNoise.AudioEffectDouble;
                        //UHF Band?
                        audio += ((double)(noise[fmNoisePosition]) * fmVol);
                        fmNoisePosition++;

                        if (fmNoisePosition == noise.Length)
                        {
                            fmNoisePosition = 0;
                        }
                    }
                }
            }

            return audio;
        }
    }
}
