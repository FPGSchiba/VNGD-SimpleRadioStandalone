using System;
using System.Diagnostics;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using MathNet.Filtering;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using FragLabs.Audio.Codecs;
using NLog;
using static Ciribob.DCS.SimpleRadio.Standalone.Common.RadioInformation;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider : AudioProvider
    {
        public static readonly int SILENCE_PAD = 200;

        private readonly Random _random = new Random();

        private int _lastReceivedOn = -1;
        private OnlineFilter[] _filters;

        private readonly BiQuadFilter _highPassFilter;
        private readonly BiQuadFilter _lowPassFilter;

        private OpusDecoder _decoder;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();


        //used for comparison
        public static readonly short FM = Convert.ToInt16((int)RadioInformation.Modulation.FM);
        public static readonly short HQ = Convert.ToInt16((int)RadioInformation.Modulation.HAVEQUICK);
        public static readonly short AM = Convert.ToInt16((int)RadioInformation.Modulation.AM);
        private int hqTonePosition = 0;
        private int natoPosition = 0;
        private int fmNoisePosition = 0;
        private int amNoisePosition = 0;
        private int vhfNoisePosition = 0;
        private int uhfNoisePosition = 0;
        private int hfNoisePosition = 0;
        private readonly CachedAudioEffectProvider effectProvider = CachedAudioEffectProvider.Instance;

        public ClientAudioProvider()
        {
            _filters = new OnlineFilter[2];
            _filters[0] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 560, 3900);
            _filters[1] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.OUTPUT_SAMPLE_RATE, 100, 4500);

            JitterBufferProviderInterface =
                new JitterBufferProviderInterface(new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 2));

            SampleProvider = new Pcm16BitToSampleProvider(JitterBufferProviderInterface);

            _decoder = OpusDecoder.Create(AudioManager.OUTPUT_SAMPLE_RATE, 1);
            _decoder.ForwardErrorCorrection = false;
            _decoder.MaxDataBytes = AudioManager.OUTPUT_SAMPLE_RATE * 4;

            _highPassFilter = BiQuadFilter.HighPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 520, 0.97f);
            _lowPassFilter = BiQuadFilter.LowPassFilter(AudioManager.OUTPUT_SAMPLE_RATE, 4130, 2.0f);

        }

        public JitterBufferProviderInterface JitterBufferProviderInterface { get; }
        public Pcm16BitToSampleProvider SampleProvider { get; }

        public long LastUpdate { get; private set; }

        //is it a new transmission?
        public bool LikelyNewTransmission()
        {
            //400 ms since last update
            long now = DateTime.Now.Ticks;
            if ((now - LastUpdate) > 4000000) //400 ms since last update
            {
                return true;
            }

            return false;
        }

        public void AddClientAudioSamples(ClientAudio audio)
        {
            //sort out volume
//            var timer = new Stopwatch();
//            timer.Start();

            bool newTransmission = LikelyNewTransmission();

            int decodedLength = 0;

            var decoded = _decoder.Decode(audio.EncodedAudio,
                audio.EncodedAudio.Length, out decodedLength, newTransmission);

            if (decodedLength <= 0)
            {
                Logger.Info("Failed to decode audio from Packet for client");
                return;
            }

            // for some reason if this is removed then it lags?!
            //guess it makes a giant buffer and only uses a little?
            //Answer: makes a buffer of 4000 bytes - so throw away most of it
            var tmp = new byte[decodedLength];
            Buffer.BlockCopy(decoded, 0, tmp, 0, decodedLength);

            audio.PcmAudioShort = ConversionHelpers.ByteArrayToShortArray(tmp);

            var decrytable = audio.Decryptable || (audio.Encryption == 0);

            if (decrytable)
            {
                //adjust for LOS + Distance + Volume
                AdjustVolumeForLoss(audio);

                if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects))
                {
                    if (audio.ReceivedRadio == 0 
                        || audio.Modulation == (short)RadioInformation.Modulation.MIDS)
                    {
                        AddRadioEffectIntercom(audio);
                    }
                    else
                    {
                        AddRadioEffect(audio);
                    }
                }

                //final adjust
                AdjustVolume(audio);

            }
            else
            {
                AddEncryptionFailureEffect(audio);

                if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffects))
                {
                    AddRadioEffect(audio);
                }

                //final adjust
                AdjustVolume(audio);

            }

            if (newTransmission)
            {
                // System.Diagnostics.Debug.WriteLine(audio.ClientGuid+"ADDED");
                //append ms of silence - this functions as our jitter buffer??
                var silencePad = (AudioManager.OUTPUT_SAMPLE_RATE / 1000) * SILENCE_PAD;

                var newAudio = new short[audio.PcmAudioShort.Length + silencePad];

                Buffer.BlockCopy(audio.PcmAudioShort, 0, newAudio, silencePad, audio.PcmAudioShort.Length);

                audio.PcmAudioShort = newAudio;
            }

            _lastReceivedOn = audio.ReceivedRadio;
            LastUpdate = DateTime.Now.Ticks;

            JitterBufferProviderInterface.AddSamples(new JitterBufferAudio
            {
                Audio =
                    SeperateAudio(ConversionHelpers.ShortArrayToByteArray(audio.PcmAudioShort),
                        audio.ReceivedRadio),
                PacketNumber = audio.PacketNumber
            });

            //timer.Stop();
        }

        private void AdjustVolume(ClientAudio clientAudio)
        {

            var audio = clientAudio.PcmAudioShort;
            for (var i = 0; i < audio.Length; i++)
            {
                var speaker1Short = (short) (audio[i] * clientAudio.Volume);

                audio[i] = speaker1Short;
            }
        }

        private void AddRadioEffectIntercom(ClientAudio clientAudio)
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

                    mixedAudio[i] = (short)(audio * 32767);
                }
            }

        }

        private void AdjustVolumeForLoss(ClientAudio clientAudio)
        {
            if (clientAudio.Modulation == (short)Modulation.MIDS || clientAudio.Modulation == (short)Modulation.SATCOM)
            {
                return;
            }

            var audio = clientAudio.PcmAudioShort;
            for (var i = 0; i < audio.Length; i++)
            {
                var speaker1Short = audio[i];

                //add in radio loss
                //if less than loss reduce volume
                if (clientAudio.RecevingPower > 0.85) // less than 20% or lower left
                {
                    //gives linear signal loss from 15% down to 0%
                    speaker1Short = (short)(speaker1Short * (1.0f - clientAudio.RecevingPower));
                }

                //0 is no loss so if more than 0 reduce volume
                if (clientAudio.LineOfSightLoss > 0)
                {
                    speaker1Short = (short)(speaker1Short * (1.0f - clientAudio.LineOfSightLoss));
                }

                audio[i] = speaker1Short;
            }
        }


        private void AddRadioEffect(ClientAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioShort;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                var audio = (double) mixedAudio[i] / 32768f;

                if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping))
                {
                    audio = audio * RadioFilter.BOOST;

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
                        audio = (double) mixedAudio[j] / 32768f;
                    }
                }

                if (clientAudio.Modulation == FM
                    && effectProvider.NATOTone.Loaded 
                    && profileSettings.GetClientSettingBool(ProfileSettingsKeys.NATOTone))
                {
                    var natoTone = effectProvider.NATOTone.AudioEffectShort;
                    audio +=  (short)(natoTone[natoPosition] / 32768f);
                    natoPosition++;

                    if (natoPosition == natoTone.Length)
                    {
                        natoPosition = 0;
                    }
                }

                if (clientAudio.Modulation == HQ
                     && effectProvider.HAVEQUICKTone.Loaded 
                     && profileSettings.GetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone))
                {
                    var hqTone = effectProvider.HAVEQUICKTone.AudioEffectShort;

                    audio += (short) (hqTone[hqTonePosition] / 32768f);
                    hqTonePosition++;

                    if (hqTonePosition == hqTone.Length)
                    {
                        hqTonePosition = 0;
                    }
                }

                audio = AddRadioBackgroundNoiseEffect(audio, clientAudio);


                // clip
                if (audio > 1.0f)
                    audio = 1.0f;
                if (audio < -1.0f)
                    audio = -1.0f;

                mixedAudio[i] = (short) (audio * 32768f);
            }
        }

        private double AddRadioBackgroundNoiseEffect(double audio,ClientAudio clientAudio)
        {
            if (profileSettings.GetClientSettingBool(ProfileSettingsKeys.RadioBackgroundNoiseEffect))
            {
                if (clientAudio.Modulation == HQ || clientAudio.Modulation == AM)
                {
                    //mix in based on frequency
                    if (clientAudio.Frequency >= 200d * 1000000)
                    {
                        if (effectProvider.UHFNoise.Loaded)
                        {
                            var noise = effectProvider.UHFNoise.AudioEffectShort;
                            //UHF Band?
                            audio += (double)(noise[uhfNoisePosition] / 32768f);
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
                            var noise = effectProvider.VHFNoise.AudioEffectShort;
                            audio += (double)(noise[vhfNoisePosition] / 32768f);
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
                            var noise = effectProvider.HFNoise.AudioEffectShort;
                            audio += (double)(noise[hfNoisePosition] / 32768f);
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
                        var noise = effectProvider.FMNoise.AudioEffectShort;
                        //UHF Band?
                        audio += (double)(noise[fmNoisePosition] / 32768f);
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


        private void AddEncryptionFailureEffect(ClientAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioShort;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                mixedAudio[i] = RandomShort();
            }
        }

        private short RandomShort()
        {
            //random short at max volume at eights
            return (short) _random.Next(-32768 / 8, 32768 / 8);
        }

        //destructor to clear up opus
        ~ClientAudioProvider()
        {
            _decoder.Dispose();
            _decoder = null;
        }

    }
}