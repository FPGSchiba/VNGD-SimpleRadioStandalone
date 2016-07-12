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
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    internal class UdpVoiceHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static readonly object BufferLock = new object();
        private readonly OpusDecoder _decoder;
        private readonly IPAddress _address;
        private readonly AudioManager _audioManager;
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;

        private readonly BlockingCollection<byte[]> _encodedAudio = new BlockingCollection<byte[]>();
        private readonly string _guid;
        private readonly byte[] _guidAsciiBytes;
        private readonly InputDeviceManager _inputManager;
        private UdpClient _listener;

        private volatile bool _ptt;

        private volatile bool _stop;

        private readonly CancellationTokenSource _stopFlag = new CancellationTokenSource();


        public UdpVoiceHandler(ConcurrentDictionary<string, SRClient> clientsList, string guid, IPAddress address,
            OpusDecoder decoder, AudioManager audioManager, InputDeviceManager inputManager)
        {
            this._decoder = decoder;
            this._audioManager = audioManager;

            this._clientsList = clientsList;
            _guidAsciiBytes = Encoding.ASCII.GetBytes(guid);

            this._guid = guid;
            this._address = address;

            this._inputManager = inputManager;
        }

        [DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        public void Listen()
        {
            _listener = new UdpClient();
            _listener.AllowNatTraversal(true);

            //start 2 audio processing threads
            var decoderThread = new Thread(UdpAudioDecode);
            decoderThread.Start();

            //open ports by sending
            //send to open ports
            try
            {
                var ip = new IPEndPoint(_address, 5010);
                var bytes = new byte[5];
                _listener.Send(bytes, 5, ip);
            }
            catch (Exception ex)
            {
            }

            var settings = Settings.Instance;
            _inputManager.StartDetectPtt((pressed) =>
            {
                var radios = RadioSyncServer.DcsPlayerRadioInfo;

                //can be overriden by PTT Settings
                var ptt = pressed[(int)InputBinding.Ptt];
                  
                //check we're allowed to switch radios
                if (radios.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    if (pressed[(int)InputBinding.Switch1])
                    { 
                        
                        radios.selected = 0;
                    }
                    else if (pressed[(int)InputBinding.Switch2])
                    {
                      
                        radios.selected = 1;
                    }
                    else if (pressed[(int)InputBinding.Switch3])
                    {
                        
                        radios.selected = 2;
                    }
                }

            var radioSwitchPtt = settings.UserSettings[(int)SettingType.RadioSwitchIsPTT] == "ON";

            if (radioSwitchPtt && (pressed[(int)InputBinding.Switch1] 
                || pressed[(int)InputBinding.Switch2] 
                || pressed[(int)InputBinding.Switch3]))
            {
                    ptt = true;
            }

                //set final PTT AFTER mods
                this._ptt = ptt;
                
            });

            StartPing();

            while (!_stop)
            {
                try
                {
                    var groupEp = new IPEndPoint(IPAddress.Any, 5010);
                    //   listener.Client.ReceiveTimeout = 3000;

                    var bytes = _listener.Receive(ref groupEp);

                    if (bytes != null && bytes.Length > 36)
                    {
                        _encodedAudio.Add(bytes);
                    }
                }
                catch (Exception e)
                {
                    //  logger.Error(e, "error listening for UDP Voip");
                }
            }

            try
            {
                _listener.Close();
            }
            catch (Exception e)
            {
            }
        }

        public void RequestStop()
        {
            _stop = true;
            try
            {
                _listener.Close();
            }
            catch (Exception e)
            {
            }

            _stopFlag.Cancel();

            _inputManager.StopPtt();
        }

        private SRClient IsClientMetaDataValid(string clientGuid)
        {
            if (_clientsList.ContainsKey(clientGuid))
            {
                var client = _clientsList[_guid];

                if (client != null && client.isCurrent())
                {
                    return client;
                }
            }
            return null;
        }

        private void UdpAudioDecode()
        {
            try
            {
                while (!_stop)
                {
                    try
                    {
                        var encodedOpusAudio = new byte[0];
                        _encodedAudio.TryTake(out encodedOpusAudio, 100000, _stopFlag.Token);

                        var time = GetTickCount64(); //should add at the receive instead?

                        if (encodedOpusAudio != null && encodedOpusAudio.Length > 36)
                        {
                            //  process
                            // check if we should play audio

                            var myClient = IsClientMetaDataValid(_guid);

                            if (myClient != null)
                            {
                                //last 22 bytes are guid!
                                var recievingGuid = Encoding.ASCII.GetString(
                                    encodedOpusAudio, encodedOpusAudio.Length - 22, 22);

                                var frequency = BitConverter.ToDouble(encodedOpusAudio,
                                    encodedOpusAudio.Length - 22 - 4 - 1 - 8);

                                //before guid and modulation so -36 and then -1
                                var modulation = (sbyte)encodedOpusAudio[encodedOpusAudio.Length - 22 - 4 - 1];

                                var unitId = BitConverter.ToUInt32(encodedOpusAudio,encodedOpusAudio.Length - 22 - 4); 

                                // check the radio
                                var radioId = -1;
                                var receivingRadio = CanHear(RadioSyncServer.DcsPlayerRadioInfo, frequency,
                                    modulation,
                                    unitId, out radioId);
                                if (receivingRadio != null)
                                {
                                    //now check that the radios match
                                    int len;
                                    //- 22 so we ignore the UUID
                                    var decoded = _decoder.Decode(encodedOpusAudio,
                                        encodedOpusAudio.Length - 22 - 4 - 1 - 8, out len);

                                    if (len > 0)
                                    {
                                        // for some reason if this is removed then it lags?!
                                        var tmp = new byte[len];
                                        Array.Copy(decoded, tmp, len);

                                        //ALL GOOD!
                                        //create marker for bytes
                                        var audio = new ClientAudio();
                                        audio.ClientGuid = recievingGuid;
                                        audio.PcmAudio = tmp;
                                        audio.ReceiveTime = GetTickCount64();
                                        audio.Frequency = frequency;
                                        audio.Modulation = modulation;
                                        audio.Volume = receivingRadio.volume;
                                        audio.ReceivedRadio = radioId;
                                        audio.UnitId = unitId;

                                        //TODO throw away audio for each client that is before the latest receive time!
                                        _audioManager.AddClientAudio(audio);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Info("Failed Decoding");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Logger.Info("Stopped DeJitter Buffer");
            }
        }

        private RadioInformation CanHear(DCSPlayerRadioInfo myClient, double frequency, sbyte modulation, UInt32 unitId,
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
                        SendUpdateToGui(i, false);
                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.frequency == frequency
                        && receivingRadio.modulation == modulation
                        && receivingRadio.frequency > 1)
                    {
                        SendUpdateToGui(i, false);
                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.secondaryFrequency == frequency
                        && receivingRadio.secondaryFrequency > 100)
                    {
                        SendUpdateToGui(i, true);
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
            //if either PTT is true
            if (_ptt || RadioSyncServer.DcsPlayerRadioInfo.ptt)
            {
                try
                {
                    //Packet Format
                    //append OPUS bytes (unknown length?)
                    //append frequency - double (8bytes)
                    //append modulation - AM / FM (1 byte)
                    //append guid - String - (36 bytes)
                    var currentSelected = RadioSyncServer.DcsPlayerRadioInfo.selected;
                    //removes race condition by assigning here with the current selected changing
                    if (currentSelected >= 0 && currentSelected < 3)
                    {
                        var radio = RadioSyncServer.DcsPlayerRadioInfo.radios[currentSelected];

                        if (radio != null)
                        {
                            var combinedBytes = new byte[len + 8 + 1 + 4 + 22];
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

                            //unit Id
                            var unitId = BitConverter.GetBytes(RadioSyncServer.DcsPlayerRadioInfo.unitId); //4 bytes
                            combinedBytes[len + 9] = unitId[0];
                            combinedBytes[len + 10] = unitId[1];
                            combinedBytes[len + 11] = unitId[2];
                            combinedBytes[len + 12] = unitId[3];

                            Buffer.BlockCopy(_guidAsciiBytes, 0, combinedBytes, len + 8 + 1 + 4, 22); // copy short guid

                            var ip = new IPEndPoint(_address, 5010);

                            _listener.Send(combinedBytes, combinedBytes.Length, ip);

                            SendUpdateToGui(currentSelected, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Exception Handling Audio Message " + e.Message);
                }
                //    }
            }
        }

        private void StartPing()
        {
            Task.Run(() =>
            {
                byte[] message = {1, 2, 3, 4, 5};
                while (!_stop)
                {
                    Logger.Info("Pinging Server");
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


        private void SendUpdateToGui(int radio, bool secondary)
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