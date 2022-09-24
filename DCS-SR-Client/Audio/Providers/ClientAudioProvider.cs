using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using MathNet.Filtering;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Ciribob.DCS.SimpleRadio.Standalone.Client.DSP;
using FragLabs.Audio.Codecs;
using NLog;
using static Ciribob.DCS.SimpleRadio.Standalone.Common.RadioInformation;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Recording;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class ClientAudioProvider : AudioProvider
    {
        private readonly Random _random = new Random();

        public static readonly int SILENCE_PAD = 200;

        private OpusDecoder _decoder;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private bool passThrough;
        //private readonly WaveFileWriter waveWriter;

        public bool IsSecondary { get; set; }

        public ClientAudioProvider(bool passThrough = false)
        {
            this.passThrough = passThrough;

            if (!passThrough)
            {
                var radios = ClientStateSingleton.Instance.DcsPlayerRadioInfo.radios.Length;
                JitterBufferProviderInterface =
                    new JitterBufferProviderInterface[radios];

                for (int i = 0;  i < radios; i++)
                {
                    JitterBufferProviderInterface[i] =
                        new JitterBufferProviderInterface(new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));

                }
                
            }
           // waveWriter = new NAudio.Wave.WaveFileWriter($@"C:\\temp\\output{RandomFloat()}.wav", new WaveFormat(AudioManager.OUTPUT_SAMPLE_RATE, 1));
            _decoder = OpusDecoder.Create(AudioManager.OUTPUT_SAMPLE_RATE, 1);
            _decoder.ForwardErrorCorrection = false;
            _decoder.MaxDataBytes = AudioManager.OUTPUT_SAMPLE_RATE * 4;
        }

        public JitterBufferProviderInterface[] JitterBufferProviderInterface { get; }

        public long LastUpdate { get; private set; }

        //is it a new transmission?
        public bool LikelyNewTransmission()
        {
            if (passThrough)
            {
                return false;
            }

            //400 ms since last update
            long now = DateTime.Now.Ticks;
            if ((now - LastUpdate) > 4000000) //400 ms since last update
            {
                return true;
            }

            return false;
        }

        public float[] AddClientAudioSamples(ClientAudio audio, bool skipEffects = false)
        {
            //TODO this is a hack
            IsSecondary = audio.IsSecondary;
            Volume = audio.Volume;
            Modulation = (Modulation)audio.Modulation;
            Frequency = audio.Frequency;

            //sort out volume
            //            var timer = new Stopwatch();
            //            timer.Start();

            bool newTransmission = LikelyNewTransmission();

            //TODO reduce the size of this buffer
            var decoded = _decoder.DecodeFloat(audio.EncodedAudio,
                audio.EncodedAudio.Length, out var decodedLength, newTransmission);

            if (decodedLength <= 0)
            {
                Logger.Info("Failed to decode audio from Packet for client");
                return null;
            }

            // for some reason if this is removed then it lags?!
            //guess it makes a giant buffer and only uses a little?
            //Answer: makes a buffer of 4000 bytes - so throw away most of it
            var tmp = new float[decodedLength/4];
            Buffer.BlockCopy(decoded, 0, tmp, 0, decodedLength);

            //convert the byte buffer to a wave buffer
         //   var waveBuffer = new WaveBuffer(tmp);

            var floatBuffer = tmp;

         //   waveWriter.WriteSamples(floatBuffer,0,floatBuffer.Length);
            
            audio.PcmAudioFloat = floatBuffer;

            var decrytable = audio.Decryptable /* || (audio.Encryption == 0) <--- this test has already been performed by all callers and would require another call to check for STRICT_AUDIO_ENCRYPTION */;

            if (decrytable)
            {
                //adjust for LOS + Distance + Volume
                AdjustVolumeForLoss(audio);
            }
            else
            {
                AddEncryptionFailureEffect(audio);
            }

            if (newTransmission)
            {
                // System.Diagnostics.Debug.WriteLine(audio.ClientGuid+"ADDED");
                //append ms of silence - this functions as our jitter buffer??
                var silencePad = (AudioManager.OUTPUT_SAMPLE_RATE / 1000) * SILENCE_PAD;
                var newAudio = new float[audio.PcmAudioFloat.Length + silencePad];
                Buffer.BlockCopy(audio.PcmAudioFloat, 0, newAudio, silencePad, audio.PcmAudioFloat.Length);
                audio.PcmAudioFloat = newAudio;
            }

            LastUpdate = DateTime.Now.Ticks;

            if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RecordAudio))
            {
                AudioRecordingManager.Instance.AppendClientAudio(audio);
            }

            if(audio.OriginalClientGuid == ClientStateSingleton.Instance.ShortGUID)
            {
                // catch own transmissions and prevent them from being added to JitterBuffer unless its passthrough
                if (passThrough)
                {
                    //return MONO PCM 16 as bytes
                    return audio.PcmAudioFloat;
                }
                else
                {
                    return null;
                }

            }
            else if (!passThrough)
            {
                JitterBufferProviderInterface[audio.ReceivedRadio].AddSamples(new JitterBufferAudio
                {
                    //TODO dont seperate audio here - do it at the mixdown radio mixing provider
                    Audio = audio.PcmAudioFloat,
                    PacketNumber = audio.PacketNumber,
                    Decryptable = decrytable,
                    Modulation = audio.Modulation,
                    ReceivedRadio = audio.ReceivedRadio,
                    Volume = audio.Volume,
                    IsSecondary = audio.IsSecondary
                });

                return null;
            }
            else
            {
                //return MONO PCM 32 as bytes
                return audio.PcmAudioFloat;
            }

            //timer.Stop();
        }

        public float Volume { get; set; }
        public Modulation Modulation { get; private set; }
        public double Frequency { get; set; }

        private void AdjustVolumeForLoss(ClientAudio clientAudio)
        {
            if (clientAudio.Modulation == (short)Modulation.MIDS || clientAudio.Modulation == (short)Modulation.SATCOM)
            {
                return;
            }

            var audio = clientAudio.PcmAudioFloat;
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
        private void AddEncryptionFailureEffect(ClientAudio clientAudio)
        {
            var mixedAudio = clientAudio.PcmAudioFloat;

            for (var i = 0; i < mixedAudio.Length; i++)
            {
                mixedAudio[i] = RandomFloat();
            }
        }


        private float RandomFloat()
        {
            //random float at max volume at eights
            float f = ((float)_random.Next(-32768 / 8, 32768 / 8)) / (float)32768;
            if (f > 1) f = 1;
            if (f < -1) f = -1;
         
            return f;
        }


        //destructor to clear up opus
        ~ClientAudioProvider()
        {
            // waveWriter.Flush();
            // waveWriter.Dispose();
            _decoder.Dispose();
            _decoder = null;
        }

    }
}