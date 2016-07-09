using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using FragLabs.Audio.Codecs;
using NLog;
using static Ciribob.DCS.SimpleRadio.Standalone.Client.InputDevice;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    internal class UDPVoiceHandler
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private static readonly object _bufferLock = new object();
        private readonly OpusDecoder _decoder;
        private readonly IPAddress address;
        private readonly AudioManager audioManager;
        private readonly ConcurrentDictionary<string, SRClient> clientsList;

        private readonly BlockingCollection<byte[]> encodedAudio = new BlockingCollection<byte[]>();
        private readonly string guid;
        private readonly byte[] guidAsciiBytes;
        private readonly InputDeviceManager inputManager;
        private UdpClient listener;

        private volatile bool ptt;

        private volatile bool stop;

        private readonly CancellationTokenSource stopFlag = new CancellationTokenSource();


        public UDPVoiceHandler(ConcurrentDictionary<string, SRClient> clientsList, string guid, IPAddress address,
            OpusDecoder _decoder, AudioManager audioManager, InputDeviceManager inputManager)
        {
            this._decoder = _decoder;
            this.audioManager = audioManager;

            this.clientsList = clientsList;
            guidAsciiBytes = Encoding.ASCII.GetBytes(guid);

            this.guid = guid;
            this.address = address;

            this.inputManager = inputManager;
        }

        [DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        public void Listen()
        {
            listener = new UdpClient();
            listener.AllowNatTraversal(true);

            //start 2 audio processing threads
            var decoderThread = new Thread(UDPAudioDecode);
            decoderThread.Start();

            //open ports by sending
            //send to open ports
            try
            {
                var ip = new IPEndPoint(address, 5010);
                var bytes = new byte[5];
                listener.Send(bytes, 5, ip);
            }
            catch (Exception ex)
            {
            }


            inputManager.StartDetectPTT((pressed, deviceType) =>
            {
                var radios = RadioSyncServer.dcsPlayerRadioInfo;

                if (deviceType == InputBinding.PTT)
                {
                    ptt = pressed;
                }
                else if (pressed)
                {
                    if (radios.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                    {
                        switch (deviceType)
                        {
                            //TODO check here that we can switch radios
                            case InputBinding.SWITCH_1:

                                radios.selected = 0;
                                break;
                            case InputBinding.SWITCH_2:
                                radios.selected = 1;
                                break;
                            case InputBinding.SWITCH_3:
                                radios.selected = 2;
                                break;
                        }
                    }
                }
            });

            StartPing();

            while (!stop)
            {
                try
                {
                    var groupEP = new IPEndPoint(IPAddress.Any, 5010);
                    //   listener.Client.ReceiveTimeout = 3000;

                    var bytes = listener.Receive(ref groupEP);

                    if (bytes != null && bytes.Length > 36)
                    {
                        encodedAudio.Add(bytes);
                    }
                }
                catch (Exception e)
                {
                    //  logger.Error(e, "error listening for UDP Voip");
                }
            }

            try
            {
                listener.Close();
            }
            catch (Exception e)
            {
            }
        }

        public void RequestStop()
        {
            stop = true;
            try
            {
                listener.Close();
            }
            catch (Exception e)
            {
            }

            stopFlag.Cancel();

            inputManager.StopPTT();
        }

        private SRClient IsClientMetaDataValid(string clientGuid)
        {
            if (clientsList.ContainsKey(clientGuid))
            {
                var client = clientsList[guid];

                if (client != null && client.isCurrent())
                {
                    return client;
                }
            }
            return null;
        }

        private void UDPAudioDecode()
        {
            try
            {
                while (!stop)
                {
                    try
                    {
                        var encodedOpusAudio = new byte[0];
                        encodedAudio.TryTake(out encodedOpusAudio, 100000, stopFlag.Token);

                        var time = GetTickCount64(); //should add at the receive instead?

                        if (encodedOpusAudio != null && encodedOpusAudio.Length > 36)
                        {
                            //  process
                            // check if we should play audio

                            var myClient = IsClientMetaDataValid(guid);

                            if (myClient != null)
                            {
                                //last 36 bytes are guid!
                                var recievingGuid = Encoding.ASCII.GetString(
                                    encodedOpusAudio, encodedOpusAudio.Length - 36, 36);

                                var frequency = BitConverter.ToDouble(encodedOpusAudio,
                                    encodedOpusAudio.Length - 36 - 1 - 8);
                                //before guid and modulation so -36 and then -1
                                var modulation = (sbyte) encodedOpusAudio[encodedOpusAudio.Length - 36 - 1];
                                var unitId = -1; // TODO send unitID stuff

                                // check the radio
                                var radioId = -1;
                                var receivingRadio = CanHear(RadioSyncServer.dcsPlayerRadioInfo, frequency,
                                    modulation,
                                    unitId, out radioId);
                                if (receivingRadio != null)
                                {
                                    //now check that the radios match
                                    int len;
                                    //- 36 so we ignore the UUID
                                    var decoded = _decoder.Decode(encodedOpusAudio,
                                        encodedOpusAudio.Length - 36 - 1 - 8, out len);

                                    if (len > 0)
                                    {
                                        // for some reason if this is removed then it lags?!
                                        var tmp = new byte[len];
                                        Array.Copy(decoded, tmp, len);

                                        //ALL GOOD!
                                        //create marker for bytes
                                        var audio = new ClientAudio();
                                        audio.ClientGUID = recievingGuid;
                                        audio.PCMAudio = tmp;
                                        audio.ReceiveTime = GetTickCount64();
                                        audio.Frequency = frequency;
                                        audio.Modulation = modulation;
                                        audio.Volume = receivingRadio.volume;
                                        audio.ReceivedRadio = radioId;

                                        //TODO throw away audio for each client that is before the latest receive time!
                                        audioManager.addClientAudio(audio);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Info("Failed Decoding");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                logger.Info("Stopped DeJitter Buffer");
            }
        }

        private RadioInformation CanHear(DCSPlayerRadioInfo myClient, double frequency, sbyte modulation, int unitId,
            out int radioId)
        {
            for (var i = 0; i < 3; i++)
            {
                var receivingRadio = myClient.radios[i];

                if (receivingRadio != null)
                {
                    //handle INTERCOM Modulation is 2
                    if (receivingRadio.modulation == 2 && modulation == 2
                        && myClient.unitId > 0 && unitId > 0
                        && myClient.unitId == unitId)
                    {
                        SendUpdateToGUI(i, false);
                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.frequency == frequency
                        && receivingRadio.modulation == modulation
                        && receivingRadio.frequency > 1)
                    {
                        SendUpdateToGUI(i, false);
                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.secondaryFrequency == frequency
                        && receivingRadio.secondaryFrequency > 100)
                    {
                        SendUpdateToGUI(i, true);
                        radioId = i;
                        return receivingRadio;
                    }
                }
            }
            radioId = -1;
            return null;
        }


        public void Send(byte[] bytes, int len)
        {
            if (ptt)
            {
                try
                {
                    //Packet Format
                    //append OPUS bytes (unknown length?)
                    //append frequency - double (8bytes)
                    //append modulation - AM / FM (1 byte)
                    //append guid - String - (36 bytes)
                    var currentSelected = RadioSyncServer.dcsPlayerRadioInfo.selected;
                    //removes race condition by assigning here with the current selected changing
                    if (currentSelected >= 0 && currentSelected < 3)
                    {
                        var radio = RadioSyncServer.dcsPlayerRadioInfo.radios[currentSelected];

                        if (radio != null)
                        {
                            var combinedBytes = new byte[len + 8 + 1 + 36];
                            Buffer.BlockCopy(bytes, 0, combinedBytes, 0, len); // copy audio


                            var freq = BitConverter.GetBytes(radio.frequency); //8 bytes

                            combinedBytes[len] = freq[0];
                            combinedBytes[len + 1] = freq[1];
                            combinedBytes[len + 2] = freq[2];
                            combinedBytes[len + 3] = freq[3];
                            combinedBytes[len + 4] = freq[4];
                            combinedBytes[len + 5] = freq[5];
                            combinedBytes[len + 6] = freq[6];
                            combinedBytes[len + 7] = freq[7];

                            //modulation
                            combinedBytes[len + 8] = (byte) radio.modulation; //1 byte;

                            Buffer.BlockCopy(guidAsciiBytes, 0, combinedBytes, len + 9, 36); // copy guid

                            var ip = new IPEndPoint(address, 5010);

                            listener.Send(combinedBytes, combinedBytes.Length, ip);

                            SendUpdateToGUI(currentSelected, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception Handling Message " + e.Message);
                }
                //    }
            }
        }

        private void StartPing()
        {
            Task.Run(() =>
            {
                byte[] message = {1, 2, 3, 4, 5};
                while (!stop)
                {
                    logger.Info("Pinging Server");
                    try
                    {
                        Send(message, message.Length);
                    }
                    catch (Exception e)
                    {
                    }

                    Thread.Sleep(60*1000);
                }
            });
        }


        private void SendUpdateToGUI(int radio, bool secondary)
        {
            //  return; //TODO fix the string format?!
            var str = "{\"radio\": " + radio + " , \"secondary\": false }\r\n";
            var bytes = Encoding.ASCII.GetBytes(str);
            //multicast
            try
            {
                var client = new UdpClient();
                var ip = new IPEndPoint(IPAddress.Parse("239.255.50.10"), 35035);

                client.Send(bytes, bytes.Length, ip);
                client.Close();
            }
            catch (Exception e)
            {
            }
        }
    }
}