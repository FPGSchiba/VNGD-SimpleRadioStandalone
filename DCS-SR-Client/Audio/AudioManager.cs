using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Windows;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Easy.MessageHub;
using FragLabs.Audio.Codecs;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLog;
using Application = FragLabs.Audio.Codecs.Opus.Application;
using BufferedWaveProvider = Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.BufferedWaveProvider;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class AudioManager
    {
        public static readonly int INPUT_SAMPLE_RATE = 16000;
       // public static readonly int OUTPUT_SAMPLE_RATE = 44100;
        public static readonly int SEGMENT_FRAMES = 640; //640 is 40ms as INPUT_SAMPLE_RATE / 1000 *40 = 640
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly CachedAudioEffect[] _cachedAudioEffects;

        private readonly ConcurrentDictionary<string, ClientAudioProvider> _clientsBufferedAudio =
            new ConcurrentDictionary<string, ClientAudioProvider>();

        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private MixingSampleProvider _clientAudioMixer;

        private OpusDecoder _decoder;

        //buffer for effects
        //plays in parallel with radio output buffer
        private RadioAudioProvider[] _effectsOutputBuffer;
        private OpusEncoder _encoder;

        private readonly Queue<byte> _micInputQueue = new Queue<byte>(SEGMENT_FRAMES*3);

        private BufferedWaveProvider _playBuffer;

        private float _speakerBoost = 1.0f;
        private volatile bool _stop = true;
        private UdpVoiceHandler _udpVoiceHandler;
        private VolumeSampleProviderWithPeak _volumeSampleProvider;

        private WaveIn _waveIn;
        private WasapiOut _waveOut;

        public short MicMax { get; set; }
        public float SpeakerMax { get; set; }

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

        public void StartEncoding(int mic, MMDevice speakers, string guid, InputDeviceManager inputManager,
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

                _waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, true, 120);

                //add final volume boost to all mixed audio
                _volumeSampleProvider = new VolumeSampleProviderWithPeak(_clientAudioMixer,(peak => SpeakerMax = peak));
                _volumeSampleProvider.Volume = SpeakerBoost;

                _waveOut.Init(_volumeSampleProvider);

                _waveOut.Play();

                //opus
                _encoder = OpusEncoder.Create(INPUT_SAMPLE_RATE, 1, Application.Voip);
                _encoder.ForwardErrorCorrection = false;
                _decoder = OpusDecoder.Create(INPUT_SAMPLE_RATE, 1);
                _decoder.ForwardErrorCorrection = false;

            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Output - Quitting! " + ex.Message);

                MessageBox.Show($"Problem Initialising Audio Output! Try a different Output device and please post your client log on the forums", "Audio Output Error", MessageBoxButton.OK,
                            MessageBoxImage.Error);

                Environment.Exit(1);
            }

            try
            {
                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback())
                {
                    BufferMilliseconds = 20,
                    DeviceNumber = mic
                };

                _waveIn.NumberOfBuffers = 1;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new WaveFormat(INPUT_SAMPLE_RATE, 16, 1);

                _udpVoiceHandler = new UdpVoiceHandler(_clientsList, guid, ipAddress, port, _decoder, this, inputManager);
                var voiceSenderThread = new Thread(_udpVoiceHandler.Listen);

                voiceSenderThread.Start();

                _waveIn.StartRecording();


                MessageHub.Instance.Subscribe<SRClient>(RemoveClientBuffer);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Input - Quitting! " + ex.Message);

                MessageBox.Show($"Problem Initialising Audio Input! Try a different Input device and please post your client log on the forums", "Audio Input Error", MessageBoxButton.OK,
                            MessageBoxImage.Error);

                Environment.Exit(1);
            }
        }

        private void InitMixers()
        {
            _clientAudioMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(INPUT_SAMPLE_RATE, 2));
            _clientAudioMixer.ReadFully = true;
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

        // Stopwatch _stopwatch = new Stopwatch();
        private void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
//            if(_stopwatch.ElapsedMilliseconds > 22)
//            Console.WriteLine($"Time: {_stopwatch.ElapsedMilliseconds} - Bytes: {e.BytesRecorded}");
//            _stopwatch.Restart();

            //fill sound buffer

            byte[] soundBuffer = null;
            if ((e.BytesRecorded == SEGMENT_FRAMES) && (_micInputQueue.Count == 0))
            {
                //perfect!
                soundBuffer = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, soundBuffer, 0, e.BytesRecorded);
            }
            else
            {
                for (var i = 0; i < e.BytesRecorded; i++)
                {
                    _micInputQueue.Enqueue(e.Buffer[i]);
                }
            }

            //read out the queue
            while ((soundBuffer != null) || (_micInputQueue.Count >= SEGMENT_FRAMES))
            {
                //null sound buffer so read from the queue
                if (soundBuffer == null)
                {
                    soundBuffer = new byte[SEGMENT_FRAMES];

                    for (var i = 0; i < SEGMENT_FRAMES; i++)
                    {
                        soundBuffer[i] = _micInputQueue.Dequeue();
                    }
                }

                short max = 0;
                for (var n = 0; n < soundBuffer.Length; n += 2)
                {
                    var sample = (short) ((soundBuffer[n + 1] << 8) | soundBuffer[n + 0]);

                    // n.b. no clipping test going on here // FROM NAUDIO SOURCE !
                    sample = (short) (sample*MicBoost);

                    //determine peak
                    if (sample > max)
                        max = sample;

                    //convert back
                    soundBuffer[n] = (byte) (sample & 0xFF);
                    soundBuffer[n + 1] = (byte) (sample >> 8);   
                }

                MicMax = max;

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

            SpeakerMax = 0;
            MicMax = 0;

            MessageHub.Instance.ClearSubscriptions();
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

                _clientAudioMixer.AddMixerInput(client.SampleProvider);
            }

            client.AddClientAudioSamples(audio);
        }

        private void RemoveClientBuffer(SRClient srClient)
        {
            ClientAudioProvider clientAudio = null;
            _clientsBufferedAudio.TryRemove(srClient.ClientGuid, out clientAudio);

            if (clientAudio != null)
            {
                try
                {
                    _clientAudioMixer.RemoveMixerInput(clientAudio.SampleProvider);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error removing client input");
                }
            }
        }
    }
}