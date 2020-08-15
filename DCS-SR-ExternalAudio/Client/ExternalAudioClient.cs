using System.Net;
using System.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Models;
using Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Network;
using Easy.MessageHub;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.ExternalAudioClient.Client
{
    internal class ExternalAudioClient
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private string mp3Path;
        private double freq;
        private string modulation;
        private int coalition;
        private readonly int port;

        private readonly string Guid = ShortGuid.NewGuid();

        private bool _finished = false;
        private DCSPlayerRadioInfo gameState;
        private UdpVoiceHandler udpVoiceHandler;
        private string name;

        public ExternalAudioClient(string mp3Path, double freq, string modulation, int coalition, int port, string name)
        {
            this.mp3Path = mp3Path;
            this.freq = freq;
            this.modulation = modulation;
            this.coalition = coalition;
            this.port = port;
            this.name = name;
        }

        public void Start()
        {

            MessageHub.Instance.Subscribe<ReadyMessage>(ReadyToSend);
            MessageHub.Instance.Subscribe<DisconnectedMessage>(Disconnected);

            gameState = new DCSPlayerRadioInfo();
            gameState.radios[1].modulation = (RadioInformation.Modulation)(modulation == "AM" ? 0 : 1);
            gameState.radios[1].freq = this.freq * 1000000; // get into Hz
            gameState.radios[1].name = name;

            Logger.Info($"Starting with params:");
            Logger.Info($"Path: {mp3Path} ");
            Logger.Info($"Frequency: {gameState.radios[1].freq} Hz ");
            Logger.Info($"Modulation: {gameState.radios[1].modulation} ");
            Logger.Info($"Coalition: {coalition} ");
            Logger.Info($"IP: 127.0.0.1 ");
            Logger.Info($"Port: {port} ");
            Logger.Info($"Client Name: {name} ");

            var srsClientSyncHandler = new SRSClientSyncHandler(Guid, gameState,name, coalition);

            srsClientSyncHandler.TryConnect(new IPEndPoint(IPAddress.Loopback, port));

            while (!_finished)
            {
                Thread.Sleep(5000);
            }
            Logger.Info("Finished - Closing");

            udpVoiceHandler?.RequestStop();
            srsClientSyncHandler?.Disconnect();

            MessageHub.Instance.ClearSubscriptions();
        }

        private void ReadyToSend(ReadyMessage ready)
        {
            if (udpVoiceHandler == null)
            {
                Logger.Info($"Connecting UDP VoIP");
                udpVoiceHandler = new UdpVoiceHandler(Guid, IPAddress.Loopback, port, gameState);
                udpVoiceHandler.Start();
                new Thread(SendAudio).Start();
            }
        }

        private void Disconnected(DisconnectedMessage disconnected)
        {
            _finished = true;
        }

        private void SendAudio()
        {
            Logger.Info("Sending Audio... Please Wait");
            MP3OpusReader mp3 = new MP3OpusReader(mp3Path);
            var opusBytes = mp3.GetOpusBytes();
            int count = 0;
            foreach (var opusByte in opusBytes)
            {
                //can use timer to run through it
                Thread.Sleep(30);

                if (!_finished)
                {
                    udpVoiceHandler.Send(opusByte, opusByte.Length);
                    count++;

                    if (count % 50 ==0)
                    {
                        Logger.Info($"Playing audio - sent {count*40}ms - {((float)count / (float)opusBytes.Count ) * 100.0:F0}% ");
                    }
                }
                else
                {
                    Logger.Error("Client Disconnected");
                    return;
                }
            }

            //get all the audio as Opus frames of 40 ms
            //send on 40 ms timer 

            //when empty - disconnect

            Logger.Info("Finished Sending Audio");
            _finished = true;

        }
    }
}