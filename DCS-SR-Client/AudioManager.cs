using Ciribob.DCS.SimpleRadio.Standalone.Common;
using FragLabs.Audio.Codecs;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class AudioManager
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        ConcurrentDictionary<String, SRClient> clientsList;
   
        ConcurrentDictionary<String, ClientAudioProvider> clientsBufferedAudio = new ConcurrentDictionary<string, ClientAudioProvider>();
    
        WaveIn _waveIn;
        WaveOut _waveOut;
        BufferedWaveProvider _playBuffer;
        OpusEncoder _encoder;
        OpusDecoder _decoder;

        int _segmentFrames;
        int _bytesPerSegment;
        volatile bool _stop = true;

        byte[] _notEncodedBuffer = new byte[0];
        UDPVoiceHandler udpVoiceHandler;
        MixingSampleProvider mixing;

        public AudioManager(ConcurrentDictionary<String, SRClient> clientsList)
        {
            this.clientsList = clientsList;
        }

        internal void addClientAudio(ClientAudio audio)
        {
            //16bit PCM Audio
            //Clean  - remove if we havent received audio in a while
            // If we have recieved audio, create a new buffered audio and read it
            ClientAudioProvider client = null;
           if (clientsBufferedAudio.ContainsKey(audio.ClientGUID))
           {
                client = clientsBufferedAudio[audio.ClientGUID];
                client.lastUpdate = GetTickCount64();
            }
            else
            {
                client = new ClientAudioProvider();
                client.lastUpdate = GetTickCount64(); 
                clientsBufferedAudio[audio.ClientGUID] = client;
               
                mixing.AddMixerInput(client.VolumeSampleProvider);
            }

            client.VolumeSampleProvider.Volume = audio.Volume;

            client.BufferedWaveProvider.AddSamples(audio.PCMAudio, 0, audio.PCMAudio.Length);


        }
  
        public void StartEncoding(int mic, int speakers, string guid, InputDeviceManager inputManager, IPAddress ipAddress)
        {
            
                _stop = false;

                _segmentFrames = 960; //960 frames is 20 ms of audio
                _encoder = OpusEncoder.Create(24000, 1, FragLabs.Audio.Codecs.Opus.Application.Restricted_LowLatency);
                //    _encoder.Bitrate = 8192;
                _decoder = OpusDecoder.Create(24000, 1);
                _bytesPerSegment = _encoder.FrameByteCount(_segmentFrames);

                _waveIn = new WaveIn(WaveCallbackInfo.FunctionCallback());
                _waveIn.BufferMilliseconds = 60;
                _waveIn.DeviceNumber = mic;
                _waveIn.DataAvailable += _waveIn_DataAvailable;
                _waveIn.WaveFormat = new NAudio.Wave.WaveFormat(48000, 16, 1); // should this be 44100??

            //       _playBuffer = new BufferedWaveProvider(new NAudio.Wave.WaveFormat(48000, 16, 1));

            mixing = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
            //add silence track?
            BufferedWaveProvider provider = new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
           
            mixing.AddMixerInput(provider);

                //TODO pass all this to the audio manager

                //Audio manager should start / stop and cleanup based on connection successfull and disconnect
                //Should use listeners to synchronise all the state

                _waveOut = new WaveOut();
                _waveOut.DesiredLatency = 100; //75ms latency in output buffer
                _waveOut.DeviceNumber = speakers;
                _waveOut.Init(mixing);
                _waveOut.Play();

                  udpVoiceHandler = new UDPVoiceHandler(clientsList, guid, ipAddress, _decoder, this, inputManager);
                  Thread voiceSenderThread = new Thread(udpVoiceHandler.Listen);

                 voiceSenderThread.Start();

                _waveIn.StartRecording();
        


        }



        

        void _waveIn_DataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] soundBuffer = new byte[e.BytesRecorded + _notEncodedBuffer.Length];

            for (int i = 0; i < _notEncodedBuffer.Length; i++)
                soundBuffer[i] = _notEncodedBuffer[i];

            for (int i = 0; i < e.BytesRecorded; i++)
                soundBuffer[i + _notEncodedBuffer.Length] = e.Buffer[i];

            int byteCap = _bytesPerSegment;
            //      Console.WriteLine("{0} ByteCao", byteCap);
            int segmentCount = (int)Math.Floor((decimal)soundBuffer.Length / byteCap);
            int segmentsEnd = segmentCount * byteCap;
            int notEncodedCount = soundBuffer.Length - segmentsEnd;
            _notEncodedBuffer = new byte[notEncodedCount];
            for (int i = 0; i < notEncodedCount; i++)
            {
                _notEncodedBuffer[i] = soundBuffer[segmentsEnd + i];
            }

            for (int i = 0; i < segmentCount; i++)
            {
                byte[] segment = new byte[byteCap];
                for (int j = 0; j < segment.Length; j++)
                    segment[j] = soundBuffer[(i * byteCap) + j];

                int len;
                byte[] buff = _encoder.Encode(segment, segment.Length, out len);

                if (udpVoiceHandler != null)
                    udpVoiceHandler.Send(buff, len);
            }
        }

        public void StopEncoding()
        {
            if(_waveIn!=null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }

            if(_waveOut!=null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if(_playBuffer!=null)
            {
                _playBuffer.ClearBuffer();
                _playBuffer = null; 
            }
        
            
            if(_encoder!=null)
            {
                _encoder.Dispose();
                _encoder = null;

            }
           
            if(_decoder!=null)
            {
                _decoder.Dispose();
                _decoder = null;

            }
            if (udpVoiceHandler != null)
            {
                udpVoiceHandler.RequestStop();
                udpVoiceHandler = null;

            }

            _stop = true;
        }
    }
}
