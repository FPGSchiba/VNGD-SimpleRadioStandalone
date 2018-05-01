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

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider : AudioProvider
    {
        public static readonly int SILENCE_PAD = 300;

        private readonly Random _random = new Random();

        private Settings.SettingsStore _settings = Settings.SettingsStore.Instance;

        private int _lastReceivedOn = -1;
        private OnlineFilter[] _filters;


        public ClientAudioProvider()
        {
            _filters = new OnlineFilter[2];
            _filters[0] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.INPUT_SAMPLE_RATE, 560, 3900);
            _filters[1] =
                OnlineFilter.CreateBandpass(ImpulseResponse.Finite, AudioManager.INPUT_SAMPLE_RATE, 100, 4500);

            JitterBufferProviderInterface =
                new JitterBufferProviderInterface(new WaveFormat(AudioManager.INPUT_SAMPLE_RATE, 2));

            SampleProvider = new Pcm16BitToSampleProvider(JitterBufferProviderInterface);
        }

        public JitterBufferProviderInterface JitterBufferProviderInterface { get; }
        public Pcm16BitToSampleProvider SampleProvider { get; }

        public long LastUpdate { get; private set; }

        public void AddClientAudioSamples(ClientAudio audio)
        {
            //sort out volume
//            var timer = new Stopwatch();
//            timer.Start();

            var decrytable = audio.Decryptable || (audio.Encryption == 0);

            if (decrytable)
            {
                //adjust for LOS + Distance + Volume
                AdjustVolume(audio);

                ////no radio effect for intercom
                if (audio.ReceivedRadio != 0)
                {
                    if (_settings.GetClientSetting(SettingsKeys.RadioEffects).BoolValue)
                    {
                        AddRadioEffect(audio);
                    }
                }
            }
            else
            {
                AddEncryptionFailureEffect(audio);

                if (_settings.GetClientSetting(SettingsKeys.RadioEffects).BoolValue)
                {
                    AddRadioEffect(audio);
                }
            }

            long now = DateTime.Now.Ticks;
            if ((now - LastUpdate) > 4000000) //400 ms since last update
            {
                // System.Diagnostics.Debug.WriteLine(audio.ClientGuid+"ADDED");
                //append ms of silence - this functions as our jitter buffer??
                var silencePad = (AudioManager.INPUT_SAMPLE_RATE / 1000) * SILENCE_PAD;

                var newAudio = new short[audio.PcmAudioShort.Length + silencePad];

                Buffer.BlockCopy(audio.PcmAudioShort, 0, newAudio, silencePad, audio.PcmAudioShort.Length);

                audio.PcmAudioShort = newAudio;
            }

            _lastReceivedOn = audio.ReceivedRadio;
            LastUpdate = now;

            JitterBufferProviderInterface.AddSamples(new JitterBufferAudio
            {
                Audio =
                    SeperateAudio(ConversionHelpers.ShortArrayToByteArray(audio.PcmAudioShort), audio.ReceivedRadio),
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
                    speaker1Short = (short) (speaker1Short * (1.0f - clientAudio.LineOfSightLoss));
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

                if (_settings.GetClientSetting(SettingsKeys.RadioEffectsClipping).BoolValue)
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
                        audio = (double) mixedAudio[j] / 32768f;
                    else
                    {
                        // clip
                        if (audio > 1.0f)
                            audio = 1.0f;
                        if (audio < -1.0f)
                            audio = -1.0f;
                    }
                }

                mixedAudio[i] = (short) (audio * 32767);
            }
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
    }
}