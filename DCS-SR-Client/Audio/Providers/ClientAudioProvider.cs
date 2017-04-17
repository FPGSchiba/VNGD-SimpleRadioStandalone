using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider : AudioProvider
    {
        public static readonly int SILENCE_PAD = 200;

        private readonly BiQuadFilter _highPassFilter;
        private readonly BiQuadFilter _lowPassFilter;

        private readonly Random _random = new Random();

        private Settings.SettingsStore _settings = Settings.SettingsStore.Instance;

        private int _lastReceivedOn = -1;

        public ClientAudioProvider()
        {
            _highPassFilter = BiQuadFilter.HighPassFilter(AudioManager.INPUT_SAMPLE_RATE, 520, 0.97f);
            _lowPassFilter = BiQuadFilter.LowPassFilter(AudioManager.INPUT_SAMPLE_RATE, 4130, 2.0f);

            JitterBufferProviderInterface = new JitterBufferProviderInterface(new WaveFormat(AudioManager.INPUT_SAMPLE_RATE, 2));

            SampleProvider = new Pcm16BitToSampleProvider(JitterBufferProviderInterface);
        }

        public JitterBufferProviderInterface JitterBufferProviderInterface { get; }
        public Pcm16BitToSampleProvider SampleProvider { get; }

        public long LastUpdate { get; private set; }

        public void AddClientAudioSamples(ClientAudio audio)
        {
            //sort out volume

            var decrytable = audio.Decryptable || (audio.Encryption == 0);

            if (decrytable)
            {
                //adjust for LOS + Distance + Volume
                AdjustVolume(audio);

                ////no radio effect for intercom
                if (audio.ReceivedRadio != 0)
                {
                    if (_settings.UserSettings[(int)SettingType.RadioEffects] == "ON")
                    {
                        AddRadioEffect(audio);
                    }
                }
            }
            else
            {
                AddEncryptionFailureEffect(audio);

                if (_settings.UserSettings[(int) SettingType.RadioEffects] == "ON")
                {
                    AddRadioEffect(audio);
                }
            }

            long now = Environment.TickCount;
            if ((now - LastUpdate) > 400) //400 ms since last update
            {
                //append 160ms of silence - this functions as our jitter buffer??
                var silencePad = (AudioManager.INPUT_SAMPLE_RATE/1000)*SILENCE_PAD;

                var newAudio = new short[audio.PcmAudioShort.Length + silencePad];

                Buffer.BlockCopy(audio.PcmAudioShort, 0, newAudio, silencePad, audio.PcmAudioShort.Length);

                audio.PcmAudioShort = newAudio;
            }

            _lastReceivedOn = audio.ReceivedRadio;
            LastUpdate = now;

            JitterBufferProviderInterface.AddSamples(new JitterBufferAudio
            {
                Audio = SeperateAudio(ConversionHelpers.ShortArrayToByteArray(audio.PcmAudioShort), audio.ReceivedRadio),
                PacketNumber = audio.PacketNumber
            });
        }

        private void AdjustVolume(ClientAudio clientAudio)
        {
            var audio = clientAudio.PcmAudioShort;
            for (var i = 0; i < audio.Length; i++)
            {
                var speaker1Short = (short) (audio[i]*clientAudio.Volume);
               
                //calculate % loss 
                var loss = 1 - clientAudio.RecevingPower;
                //add in radio loss
                //if less than loss reduce volume
                if (clientAudio.RecevingPower <= 0.1) // less than 10% or lower left
                {
                    //gives linear signal loss from 10% down to 0%
                    speaker1Short = (short) (speaker1Short* loss/0.1);
                }

                //0 is no loss so if more than 0 reduce volume
                if (clientAudio.LineOfSightLoss > 0)
                {
                    speaker1Short = (short) (speaker1Short*(1.0f - clientAudio.LineOfSightLoss));
                }

                audio[i] = speaker1Short;
            }
        }


        private void AddRadioEffect(ClientAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioShort;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                var audio = mixedAudio[i]/32768f;

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

                    mixedAudio[i] = (short) (audio*32767);
                }
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
            return (short) _random.Next(-32768/8, 32768/8);
        }
    }
}