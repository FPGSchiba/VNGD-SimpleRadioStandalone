using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using FragLabs.Audio.Codecs;
using FragLabs.Audio.Codecs.Opus;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class AudioManager
    {
        public static readonly int INPUT_SAMPLE_RATE = 16000;
        public static readonly int OUTPUT_SAMPLE_RATE = 44100;
        public static readonly int SEGMENT_FRAMES = 640; //640 is 40ms as INPUT_SAMPLE_RATE / 1000 *40 = 640
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly CachedAudioEffect[] _cachedAudioEffects;

        private readonly ConcurrentDictionary<string, ClientAudioProvider> _clientsBufferedAudio =
            new ConcurrentDictionary<string, ClientAudioProvider>();

        private Queue<byte> _micInputQueue = new Queue<byte>(SEGMENT_FRAMES*3);

        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private MixingSampleProvider _clientAudioMixer;

        private OpusDecoder _decoder;

        //buffer for effects
        //plays in parallel with radio output buffer
        private RadioAudioProvider[] _effectsOutputBuffer;
        private OpusEncoder _encoder;

        private BufferedWaveProvider _playBuffer;

        private float _speakerBoost = 1.0f;
        private volatile bool _stop = true;
        private UdpVoiceHandler _udpVoiceHandler;
        private VolumeSampleProvider _volumeSampleProvider;

        private WaveIn _waveIn;
        private WaveOut _waveOut;

        public AudioManager(ConcurrentDictionary<string, SRClient> clientsList)
        {
            _clientsList = clientsList;

            _cachedAudioEffects =
                new CachedAudioEffect[Enum.GetNames(typeof(CachedAudioEffect.AudioEffectTypes)).Length];
            for (var i = 0; i < _cachedAudioEffects.Length; i++)
            {
                _cachedAudioEffects[i] = new CachedAudioEffect((CachedAudioEffect.AudioEffectTypes) i);
            }
        }

        public float MicBoost { get; set; } = 1.0f;

        public float SpeakerBoost
        {
            get { return _speakerBoost; }
            set
            {
                _speakerBoost = value;
                if (_volumeSampleProvider != null)
                {
                    _volumeSampleProvider.Volume = value;
                }
            }
        }

        public bool Resample { get; set; }


        public void StartEncoding(int mic, int speakers, string guid, InputDeviceManager inputManager,
            IPAddress ipAddress, int port)
        {
            _stop = false;

            try
            {
                _micInputQueue.Clear();

                InitMixers();

                InitAudioBuffers();

                //Audio manager should start / stop and cleanup based on connection successfull and disconnect
                //Should use listeners to synchronise all the state

                _waveOut = new WaveOut
                {
                    DesiredLatency = 80, // half to get tick rate - so 40ms
                    DeviceNumber = speakers
                };

                //add final volume boost to all mixed audio
                _volumeSampleProvider = new VolumeSampleProvider(_clientAudioMixer);
                _volumeSampleProvider.Volume = SpeakerBoost;

                //resample client audio to 44100
                var resampler = new WdlResamplingSampleProvider(_volumeSampleProvider, OUTPUT_SAMPLE_RATE);
                    //resample and output at 44100

                _waveOut.Init(resampler);

                _waveOut.Play();

                //opus
                _encoder = OpusEncoder.Create(INPUT_SAMPLE_RATE, 1, Application.Restricted_LowLatency);
                _decoder = OpusDecoder.Create(INPUT_SAMPLE_RATE, 1);

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback())
                {
                    BufferMilliseconds = 20,
                    DeviceNumber = mic
                };

                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new WaveFormat(INPUT_SAMPLE_RATE, 16, 1);

                _udpVoiceHandler = new UdpVoiceHandler(_clientsList, guid, ipAddress, port, _decoder, this, inputManager);
                var voiceSenderThread = new Thread(_udpVoiceHandler.Listen);

                voiceSenderThread.Start();

                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Quitting! Error:" + ex.Message);

                Environment.Exit(1);
            }
        }

        private void InitMixers()
        {
            _clientAudioMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(INPUT_SAMPLE_RATE, 2));
            _clientAudioMixer.ReadFully = true;

//            _effectsMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(OUTPUT_SAMPLE_RATE, 2));
//            _effectsMixer.ReadFully = true;
        }

        private void InitAudioBuffers()
        {
            _effectsOutputBuffer = new RadioAudioProvider[RadioDCSSyncServer.DcsPlayerRadioInfo.radios.Length];

            for (var i = 0; i < RadioDCSSyncServer.DcsPlayerRadioInfo.radios.Length; i++)
            {
                _effectsOutputBuffer[i] = new RadioAudioProvider(INPUT_SAMPLE_RATE);
                _clientAudioMixer.AddMixerInput(_effectsOutputBuffer[i].VolumeSampleProvider);
            }
        }


        public void PlaySoundEffectStartReceive(int transmitOnRadio, bool encrypted, float volume)
        {
            var radioEffects = Settings.Instance.UserSettings[(int) SettingType.RadioClickEffects];
            if (radioEffects == "ON")
            {
                var _effectsBuffer = _effectsOutputBuffer[transmitOnRadio];

                var encyptionEffects = Settings.Instance.UserSettings[(int) SettingType.RadioEncryptionEffects];
                if (encrypted && (encyptionEffects == "ON"))
                {
                    _effectsBuffer.VolumeSampleProvider.Volume = volume;
                    _effectsBuffer.AddAudioSamples(
                        _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.KY_58_RX].AudioEffectBytes,
                        transmitOnRadio);
                }
                else
                {
                    _effectsBuffer.VolumeSampleProvider.Volume = volume;
                    _effectsBuffer.AddAudioSamples(
                        _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.RADIO_TX].AudioEffectBytes,
                        transmitOnRadio);
                }
            }
        }

        public void PlaySoundEffectStartTransmit(int transmitOnRadio, bool encrypted, float volume)
        {
            var radioEffects = Settings.Instance.UserSettings[(int) SettingType.RadioClickEffectsTx];
            if (radioEffects == "ON")
            {
                var _effectBuffer = _effectsOutputBuffer[transmitOnRadio];

                var encyptionEffects = Settings.Instance.UserSettings[(int) SettingType.RadioEncryptionEffects];

                if (encrypted && (encyptionEffects == "ON"))
                {
                    _effectBuffer.VolumeSampleProvider.Volume = volume;
                    _effectBuffer.AddAudioSamples(
                        _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.KY_58_TX].AudioEffectBytes,
                        transmitOnRadio);
                }
                else
                {
                    _effectBuffer.VolumeSampleProvider.Volume = volume;
                    _effectBuffer.AddAudioSamples(
                        _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.RADIO_TX].AudioEffectBytes,
                        transmitOnRadio);
                }
            }
        }


        public void PlaySoundEffectEndReceive(int transmitOnRadio, float volume)
        {
            var radioEffects = Settings.Instance.UserSettings[(int) SettingType.RadioClickEffects];
            if (radioEffects == "ON")
            {
                var _effectsBuffer = _effectsOutputBuffer[transmitOnRadio];

                _effectsBuffer.VolumeSampleProvider.Volume = volume;
                _effectsBuffer.AddAudioSamples(
                    _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.RADIO_RX].AudioEffectBytes,
                    transmitOnRadio);
            }
        }

        public void PlaySoundEffectEndTransmit(int transmitOnRadio, float volume)
        {
            var radioEffects = Settings.Instance.UserSettings[(int) SettingType.RadioClickEffectsTx];
            if (radioEffects == "ON")
            {
                var _effectBuffer = _effectsOutputBuffer[transmitOnRadio];

                _effectBuffer.VolumeSampleProvider.Volume = volume;
                _effectBuffer.AddAudioSamples(
                    _cachedAudioEffects[(int) CachedAudioEffect.AudioEffectTypes.RADIO_RX].AudioEffectBytes,
                    transmitOnRadio);
            }
        }


        private void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            //fill sound buffer

            byte[] soundBuffer = null;
            if (e.BytesRecorded == SEGMENT_FRAMES && _micInputQueue.Count == 0)
            {
                //perfect!
                soundBuffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, soundBuffer, 0, e.BytesRecorded);
            }
            else
            {
                for (int i = 0; i < e.BytesRecorded; i++)
                {
                    _micInputQueue.Enqueue(e.Buffer[i]);
                }
            }
       
            //read out the queue
            while(soundBuffer != null ||_micInputQueue.Count >= SEGMENT_FRAMES)
            {
                //null sound buffer so read from the queue
                if (soundBuffer == null)
                {
                    soundBuffer = new byte[SEGMENT_FRAMES];

                    for (int i = 0; i < SEGMENT_FRAMES; i++)
                    {
                        soundBuffer[i] = _micInputQueue.Dequeue();
                    }
                }

                //boost microphone volume if needed
                if (MicBoost != 1.0f)
                {
                    for (var n = 0; n < soundBuffer.Length; n += 2)
                    {
                        var sample = (short) ((soundBuffer[n + 1] << 8) | soundBuffer[n + 0]);
                        // n.b. no clipping test going on here // FROM NAUDIO SOURCE !
                        sample = (short) (sample*MicBoost);
                        soundBuffer[n] = (byte) (sample & 0xFF);
                        soundBuffer[n + 1] = (byte) (sample >> 8);
                    }
                }

                try
                {
                    //encode as opus bytes
                    int len;
                    var buff = _encoder.Encode(soundBuffer, soundBuffer.Length, out len);

                    if ((_udpVoiceHandler != null) && (buff != null) && (len > 0))
                    {
                        //create copy with small buffer
                        var encoded = new byte[len];

                        Buffer.BlockCopy(buff, 0, encoded, 0, len);

                        // Console.WriteLine("Sending: " + e.BytesRecorded);
                        _udpVoiceHandler.Send(encoded, len);
                    }
                    else
                    {
                        Logger.Error($"Invalid Bytes for Encoding - {e.BytesRecorded} should be {SEGMENT_FRAMES} ");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error encoding Opus! " + ex.Message);
                }

                soundBuffer = null;
            }

        }

        public void StopEncoding()
        {
            if (_clientAudioMixer != null)
            {
                _effectsOutputBuffer = null;

                _volumeSampleProvider = null;
                _clientAudioMixer.RemoveAllMixerInputs();
                _clientAudioMixer = null;

            }

            _clientsBufferedAudio.Clear();

            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_playBuffer != null)
            {
                _playBuffer.ClearBuffer();
                _playBuffer = null;
            }


            if (_encoder != null)
            {
                _encoder.Dispose();
                _encoder = null;
            }

            if (_decoder != null)
            {
                _decoder.Dispose();
                _decoder = null;
            }
            if (_udpVoiceHandler != null)
            {
                _udpVoiceHandler.RequestStop();
                _udpVoiceHandler = null;
            }

            _stop = true;
        }

        public void AddClientAudio(ClientAudio audio)
        {
            //sort out effects!

            //16bit PCM Audio
            //TODO: Clean  - remove if we havent received audio in a while?
            // If we have recieved audio, create a new buffered audio and read it
            ClientAudioProvider client = null;
            if (_clientsBufferedAudio.ContainsKey(audio.ClientGuid))
            {
                client = _clientsBufferedAudio[audio.ClientGuid];
            }
            else
            {
                client = new ClientAudioProvider();
                _clientsBufferedAudio[audio.ClientGuid] = client;

                _clientAudioMixer.AddMixerInput(client.VolumeSampleProvider);
            }

            client.AddClientAudioSamples(audio);
        }
    }
}