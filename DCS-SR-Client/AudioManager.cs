using System;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
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
        public static readonly int SAMPLE_RATE = 16000;
        public static readonly int SEGMENT_FRAMES = 960; //480 for 8000 960 for 24000
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private int _bytesPerSegment;

        private readonly ConcurrentDictionary<string, ClientAudioProvider> _clientsBufferedAudio =
            new ConcurrentDictionary<string, ClientAudioProvider>();

        private OpusDecoder _decoder;
        private OpusEncoder _encoder;
        private MixingSampleProvider _mixing;

        private byte[] _notEncodedBuffer = new byte[0];
        private BufferedWaveProvider _playBuffer;

        private int _segmentFrames;
        private volatile bool _stop = true;

        private WaveIn _waveIn;
        private WaveOut _waveOut;

        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private UdpVoiceHandler _udpVoiceHandler;

        public AudioManager(ConcurrentDictionary<string, SRClient> clientsList)
        {
            this._clientsList = clientsList;
        }

        public float MicBoost { get; set; } = 1.0f;
        public float SpeakerBoost { get; set; } = 1.0f;

        [DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        internal void AddClientAudio(ClientAudio audio)
        {
            //16bit PCM Audio
            //TODO: Clean  - remove if we havent received audio in a while
            // If we have recieved audio, create a new buffered audio and read it
            ClientAudioProvider client = null;
            if (_clientsBufferedAudio.ContainsKey(audio.ClientGuid))
            {
                client = _clientsBufferedAudio[audio.ClientGuid];
                client.LastUpdate = GetTickCount64();
            }
            else
            {
                client = new ClientAudioProvider {LastUpdate = GetTickCount64()};
                _clientsBufferedAudio[audio.ClientGuid] = client;

                _mixing.AddMixerInput(client.VolumeSampleProvider);
            }

            client.VolumeSampleProvider.Volume = audio.Volume *SpeakerBoost;

            client.AddSamples(audio);
        }

        public void StartEncoding(int mic, int speakers, string guid, InputDeviceManager inputManager,
            IPAddress ipAddress)
        {
            _stop = false;

            try
            {
                //       _playBuffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(48000, 16, 1));

                _mixing = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, 2));

                //add silence track?
                var provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SAMPLE_RATE, 2));
                //  provider.BufferDuration = TimeSpan.FromMilliseconds(100);

                _mixing.AddMixerInput(provider);

                //TODO pass all this to the audio manager

                //Audio manager should start / stop and cleanup based on connection successfull and disconnect
                //Should use listeners to synchronise all the state

                _waveOut = new WaveOut();
                _waveOut.DesiredLatency = 100; //75ms latency in output buffer
                _waveOut.DeviceNumber = speakers;
             
                _waveOut.Init(_mixing);
                _waveOut.Play();

                _segmentFrames = SEGMENT_FRAMES; //960 frames is 20 ms of audio
                _encoder = OpusEncoder.Create(SAMPLE_RATE, 1, Application.Restricted_LowLatency);
                //    _encoder.Bitrate = 8192;
                _decoder = OpusDecoder.Create(SAMPLE_RATE, 1);
                _bytesPerSegment = _encoder.FrameByteCount(_segmentFrames);

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback());
                _waveIn.BufferMilliseconds = 100;
                _waveIn.DeviceNumber = mic;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new WaveFormat(SAMPLE_RATE, 16, 1); // should this be 44100??

                _udpVoiceHandler = new UdpVoiceHandler(_clientsList, guid, ipAddress, _decoder, this, inputManager);
                var voiceSenderThread = new Thread(_udpVoiceHandler.Listen);

                voiceSenderThread.Start();

                _waveIn.StartRecording();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting audio Quitting!");


                Environment.Exit(1);
            }
        }


        private void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            var soundBuffer = new byte[e.BytesRecorded + _notEncodedBuffer.Length];

            for (var i = 0; i < _notEncodedBuffer.Length; i++)
                soundBuffer[i] = _notEncodedBuffer[i];

            for (var i = 0; i < e.BytesRecorded; i++)
                soundBuffer[i + _notEncodedBuffer.Length] = e.Buffer[i];

            var byteCap = _bytesPerSegment;
            //      Console.WriteLine("{0} ByteCao", byteCap);
            var segmentCount = (int) Math.Floor((decimal) soundBuffer.Length/byteCap);
            var segmentsEnd = segmentCount*byteCap;
            var notEncodedCount = soundBuffer.Length - segmentsEnd;
            _notEncodedBuffer = new byte[notEncodedCount];
            for (var i = 0; i < notEncodedCount; i++)
            {
                _notEncodedBuffer[i] = soundBuffer[segmentsEnd + i];
            }

            for (var i = 0; i < segmentCount; i++)
            {
                //create segment of audio
                var segment = new byte[byteCap];
                for (var j = 0; j < segment.Length; j++)
                {
                    segment[j] = soundBuffer[i*byteCap + j];
                }

                //boost microphone volume if needed
                if (MicBoost != 1.0f)
                {
                    for (var n = 0; n < segment.Length; n += 2)
                    {
                        var sample = (short) ((segment[n + 1] << 8) | segment[n + 0]);
                        // n.b. no clipping test going on here // FROM NAUDIO SOURCE !
                        sample = (short) (sample*MicBoost);
                        segment[n] = (byte) (sample & 0xFF);
                        segment[n + 1] = (byte) (sample >> 8);
                    }
                }

                //encode as opus bytes
                int len;
                var buff = _encoder.Encode(segment, segment.Length, out len);

                if (_udpVoiceHandler != null && buff!= null && len > 0)
                    _udpVoiceHandler.Send(buff, len);
            }
        }

        public void StopEncoding()
        {
            if (_mixing != null)
            {
                _mixing.RemoveAllMixerInputs();
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
    }
}