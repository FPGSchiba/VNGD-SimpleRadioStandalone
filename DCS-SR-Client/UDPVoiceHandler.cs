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
using static Ciribob.DCS.SimpleRadio.Standalone.Client.UI.InputDevice;
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
        public static volatile RadioSendingState RadioSendingState = new RadioSendingState();
        public static volatile RadioReceivingState RadioReceivingState = new RadioReceivingState();

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

            var settings = Settings.Instance;
            _inputManager.StartDetectPtt((pressed) =>
            {
                var radios = RadioSyncServer.DcsPlayerRadioInfo;

                //can be overriden by PTT Settings
                var ptt = pressed[(int)InputBinding.ModifierPtt] && pressed[(int) InputBinding.Ptt];

                //check we're allowed to switch radios
                if (radios.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    if (pressed[(int) InputBinding.ModifierSwitch1] && pressed[(int) InputBinding.Switch1])
                    {
                        radios.selected = 0;
                    }
                    else if (pressed[(int) InputBinding.ModifierSwitch2] && pressed[(int) InputBinding.Switch2])
                    {
                        radios.selected = 1;
                    }
                    else if (pressed[(int) InputBinding.ModifierSwitch3] && pressed[(int) InputBinding.Switch3])
                    {
                        radios.selected = 2;
                    }
                }

                var radioSwitchPtt = settings.UserSettings[(int) SettingType.RadioSwitchIsPTT] == "ON";

                if (radioSwitchPtt && ((pressed[(int)InputBinding.ModifierSwitch1] && pressed[(int)InputBinding.Switch1])
                                       || (pressed[(int)InputBinding.ModifierSwitch2] && pressed[(int)InputBinding.Switch2])
                                       || (pressed[(int)InputBinding.ModifierSwitch3] && pressed[(int)InputBinding.Switch3])))
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
                    else if (bytes != null && bytes.Length == 15 && bytes[0] == 1 && bytes[14] == 15)
                    {
                        Logger.Info("Received Ping Back from Server");
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

                            if (myClient != null && RadioSyncServer.DcsPlayerRadioInfo.IsCurrent())
                            {
                                //last 22 bytes are guid!
                                var recievingGuid = Encoding.ASCII.GetString(
                                    encodedOpusAudio, encodedOpusAudio.Length - 22, 22);

                                var frequency = BitConverter.ToDouble(encodedOpusAudio,
                                    encodedOpusAudio.Length - 22 - 4 - 1 - 1 - 8);

                                //before guid and modulation so - 22 - 4 - 1 - 1  
                                var modulation = (sbyte) encodedOpusAudio[encodedOpusAudio.Length - 22 - 4 - 1 - 1 ];

                                var encryption = (sbyte)encodedOpusAudio[encodedOpusAudio.Length - 22 - 4 - 1 ];

                                var unitId = BitConverter.ToUInt32(encodedOpusAudio, encodedOpusAudio.Length - 22 - 4);

                                // check the radio
                                var radioId = -1;
                                var receivingRadio = CanHear(RadioSyncServer.DcsPlayerRadioInfo, frequency,
                                    modulation,
                                    unitId, out radioId);
                                if (receivingRadio != null)
                                {
                                    var ecnAudio1 = BitConverter.ToUInt16(encodedOpusAudio, 0);
                                    var ecnAudio2 = BitConverter.ToUInt16(encodedOpusAudio, 2);

                                    var part1 = new byte[ecnAudio1];
                                    Buffer.BlockCopy(encodedOpusAudio, 4, part1, 0, ecnAudio1);

                                    var part2 = new byte[ecnAudio2];
                                    Buffer.BlockCopy(encodedOpusAudio, 4 + ecnAudio1, part2, 0, ecnAudio2);

                                    //now check that the radios match
                                    int len1;
                                
                                    //- 22 so we ignore the UUID
                                    var decoded = _decoder.Decode(part1,
                                        part1.Length, out len1);

                                    int len2;
                                    var decoded2 = _decoder.Decode(part2,
                                        part2.Length, out len2);

                                    if (len1 > 0 && len2 > 0)
                                    {
                                        // for some reason if this is removed then it lags?!
                                        var tmp = new byte[len1 +len2];
                                        Buffer.BlockCopy(decoded, 0, tmp, 0, len1);
                                        Buffer.BlockCopy(decoded2, 0, tmp, len1, len2);
                                    //    Array.Copy(decoded2, tmp, len1);

                                        //ALL GOOD!
                                        //create marker for bytes
                                        var audio = new ClientAudio
                                        {
                                            ClientGuid = recievingGuid,
                                            PcmAudio = tmp,
                                            ReceiveTime = GetTickCount64(),
                                            Frequency = frequency,
                                            Modulation = modulation,
                                            Volume = receivingRadio.volume,
                                            ReceivedRadio = radioId,
                                            UnitId = unitId,
                                            Encryption = encryption,
                                            Decryptable = encryption == receivingRadio.enc // mark if we can decrypt it
                                        };

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
            if (!myClient.IsCurrent())
            {
                radioId = -1;
                return null;
            }
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
                        RadioReceivingState = new RadioReceivingState()
                        {
                            IsSecondary = false,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };

                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.frequency == frequency
                        && receivingRadio.modulation == modulation
                        && receivingRadio.frequency > 1)
                    {
                        RadioReceivingState = new RadioReceivingState()
                        {
                            IsSecondary = false,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };
                        radioId = i;
                        return receivingRadio;
                    }
                    if (receivingRadio.secondaryFrequency == frequency
                        && receivingRadio.secondaryFrequency > 100)
                    {
                        RadioReceivingState = new RadioReceivingState()
                        {
                            IsSecondary = true,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };
                        radioId = i;
                        return receivingRadio;
                    }
                }
            }
            radioId = -1;
            return null;
        }

        private byte[] part1;
        private byte[] part2;

        public void Send(byte[] bytes, int len)
        {
            if (part1 == null)
            {
                part1 = new byte[len];
                Buffer.BlockCopy(bytes, 0, part1, 0, len);
            }
            else if (part2 == null)
            {
                part2 = new byte[len];
                Buffer.BlockCopy(bytes, 0, part2, 0, len);
            }
            else
            {
                part2 = part1;

                part1 = new byte[len];
                Buffer.BlockCopy(bytes, 0, part1, 0, len);
            }
            
            //if either PTT is true
            if ((_ptt || RadioSyncServer.DcsPlayerRadioInfo.ptt)
                && RadioSyncServer.DcsPlayerRadioInfo.IsCurrent() && part1 != null && part2 != null)
                //can only send if DCS is connected
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

                        if (radio != null && (radio.frequency > 100 && radio.modulation != 3)
                            || radio.modulation == 2)
                        {
                            var combinedLength = part1.Length + part2.Length + 4;
                                //2 * int16 at the start giving the two segments

                            var combinedBytes = new byte[combinedLength + 8 + 1 + 1 + 4 + 22];


                            byte[] part1Size = BitConverter.GetBytes(Convert.ToUInt16(part1.Length));
                            combinedBytes[0] = part1Size[0];
                            combinedBytes[1] = part1Size[1];

                            byte[] part2Size = BitConverter.GetBytes(Convert.ToUInt16(part2.Length));
                            combinedBytes[2] = part2Size[0];
                            combinedBytes[3] = part2Size[1];

                            //copy audio segments after we've added the two length heads
                            Buffer.BlockCopy(part1, 0, combinedBytes, 4, part1.Length); // copy audio
                            Buffer.BlockCopy(part2, 0, combinedBytes, part1.Length + 4, part2.Length); // copy audio

                            part1 = null;
                            part2 = null;

                            var freq = BitConverter.GetBytes(radio.frequency); //8 bytes

                            combinedBytes[combinedLength] = freq[0];
                            combinedBytes[combinedLength + 1] = freq[1];
                            combinedBytes[combinedLength + 2] = freq[2];
                            combinedBytes[combinedLength + 3] = freq[3];
                            combinedBytes[combinedLength + 4] = freq[4];
                            combinedBytes[combinedLength + 5] = freq[5];
                            combinedBytes[combinedLength + 6] = freq[6];
                            combinedBytes[combinedLength + 7] = freq[7];

                            //modulation
                            combinedBytes[combinedLength + 8] = (byte) radio.modulation; //1 byte;

                            combinedBytes[combinedLength + 9] = (byte) radio.enc; //1 byte;

                            //unit Id
                            var unitId = BitConverter.GetBytes(RadioSyncServer.DcsPlayerRadioInfo.unitId); //4 bytes
                            combinedBytes[combinedLength + 10] = unitId[0];
                            combinedBytes[combinedLength + 11] = unitId[1];
                            combinedBytes[combinedLength + 12] = unitId[2];
                            combinedBytes[combinedLength + 13] = unitId[3];

                            Buffer.BlockCopy(_guidAsciiBytes, 0, combinedBytes, combinedLength + 8 + 1 + +1 + 4, 22);
                                // copy short guid

                            var ip = new IPEndPoint(_address, 5010);
                            _listener.Send(combinedBytes, combinedBytes.Length, ip);

                            RadioSendingState = new RadioSendingState()
                            {
                                IsSending = true,
                                LastSentAt = Environment.TickCount,
                                SendingOn = currentSelected
                            };
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e,"Exception Sending Audio Message " + e.Message);
                }
            }
            else if (part1 != null && part2 != null)
            {
              

                RadioSendingState.IsSending = false;
            }
           
        }

        private void StartPing()
        {
            Task.Run(() =>
            {
                byte[] message = { 1, 2, 3, 4, 5,6,7,8,9,10,11,12,13,14,15 };

                while (!_stop)
                {
                    Logger.Info("Pinging Server");
                    try
                    {
                        var ip = new IPEndPoint(_address, 5010);
                        _listener.Send(message, message.Length, ip);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception Sending Audio Ping! " + e.Message);

                    }

                    Thread.Sleep(25 * 1000);

                }
            });
        }

        
    }
}