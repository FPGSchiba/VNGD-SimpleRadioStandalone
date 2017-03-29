using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using FragLabs.Audio.Codecs;
using NLog;
using Timer = Cabhishek.Timers.Timer;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    internal class UdpVoiceHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static volatile RadioSendingState RadioSendingState = new RadioSendingState();
        public static volatile RadioReceivingState[] RadioReceivingState = new RadioReceivingState[11];

        private readonly IPAddress _address;
        private readonly AudioManager _audioManager;
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private readonly OpusDecoder _decoder;

        private readonly BlockingCollection<byte[]> _encodedAudio = new BlockingCollection<byte[]>();
        private readonly string _guid;
        private readonly byte[] _guidAsciiBytes;
        private readonly InputDeviceManager _inputManager;
        private readonly int _port;

        private readonly CancellationTokenSource _stopFlag = new CancellationTokenSource();

        private readonly int JITTER_BUFFER = 50; //in milliseconds

        //    private readonly JitterBuffer _jitterBuffer = new JitterBuffer();
        private UdpClient _listener;

        private uint _packetNumber = 1;

        private volatile bool _ptt;

        private volatile bool _stop;

        private Timer _timer;
        private bool hasSentVoicePacket; //used to force sending of first voice packet to establish comms

        public UdpVoiceHandler(ConcurrentDictionary<string, SRClient> clientsList, string guid, IPAddress address,
            int port, OpusDecoder decoder, AudioManager audioManager, InputDeviceManager inputManager)
        {
            _decoder = decoder;
            _audioManager = audioManager;

            _clientsList = clientsList;
            _guidAsciiBytes = Encoding.ASCII.GetBytes(guid);

            _guid = guid;
            _address = address;
            _port = port;

            _inputManager = inputManager;
        }

        [DllImport("kernel32.dll")]
        private static extern long GetTickCount64();

        private void AudioEffectCheckTick()
        {
            for (var i = 0; i < RadioReceivingState.Length; i++)
            {
                //Nothing on this radio!
                //play out if nothing after 200ms
                //and Audio hasn't been played already
                var radioState = RadioReceivingState[i];
                if ((radioState != null) && !radioState.PlayedEndOfTransmission && !radioState.IsReceiving())
                {
                    radioState.PlayedEndOfTransmission = true;
                    _audioManager.PlaySoundEffectEndReceive(i, RadioDCSSyncServer.DcsPlayerRadioInfo.radios[i].volume);
                }
            }
        }

        public void Listen()
        {
            _listener = new UdpClient();
            _listener.AllowNatTraversal(true);

            //start 2 audio processing threads
            var decoderThread = new Thread(UdpAudioDecode);
            decoderThread.Start();

            var settings = Settings.Instance;
            _inputManager.StartDetectPtt(pressed =>
            {
                var radios = RadioDCSSyncServer.DcsPlayerRadioInfo;

                var radioSwitchPtt = settings.UserSettings[(int)SettingType.RadioSwitchIsPTT] == "ON";

                var ptt = false;
                foreach (var inputBindState in pressed)
                {
                    if (inputBindState.IsActive)
                    {
                        //radio switch?
                        if ((int) inputBindState.MainDevice.InputBind >= (int) InputBinding.Intercom &&
                            (int) inputBindState.MainDevice.InputBind <= (int) InputBinding.Switch10)
                        {
                            //gives you radio id if you minus 100
                            var radioId = (int) inputBindState.MainDevice.InputBind - 100;

                            if (radioId < RadioDCSSyncServer.DcsPlayerRadioInfo.radios.Length)
                            {
                                var clientRadio = RadioDCSSyncServer.DcsPlayerRadioInfo.radios[radioId];

                                if (clientRadio.modulation != RadioInformation.Modulation.DISABLED && radios.control == DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
                                {
                                    radios.selected = (short)radioId;
                                }

                                //turn on PTT
                                if (radioSwitchPtt)
                                {
                                    ptt = true;
                                }
                            }
                        }
                        else if (inputBindState.MainDevice.InputBind == InputBinding.Ptt)
                        {
                            ptt = true;
                        }
                    }
                }

                //if length is zero - no keybinds or no PTT pressed set to false
                _ptt = ptt;
            });

            StartPing();

            StartTimer();

            //set to false so we sent one packet to open up the radio
            //automatically rather than the user having to press Send
            hasSentVoicePacket = false;

            _packetNumber = 1; //reset packet number

            while (!_stop)
            {
                try
                {
                    var groupEp = new IPEndPoint(IPAddress.Any, _port);
                    //   listener.Client.ReceiveTimeout = 3000;

                    var bytes = _listener.Receive(ref groupEp);

                    if ((bytes != null) && (bytes.Length > 36))
                    {
                        _encodedAudio.Add(bytes);
                    }
                    else if ((bytes != null) && (bytes.Length == 15) && (bytes[0] == 1) && (bytes[14] == 15))
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

        public void StartTimer()
        {
            StopTimer();

            // _jitterBuffer.Clear();
            _timer = new Timer(AudioEffectCheckTick, TimeSpan.FromMilliseconds(JITTER_BUFFER));
            _timer.Start();
        }

        public void StopTimer()
        {
            if (_timer != null)
            {
                //    _jitterBuffer.Clear();
                _timer.Stop();
                _timer = null;
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

            StopTimer();
        }

        private SRClient IsClientMetaDataValid(string clientGuid)
        {
            if (_clientsList.ContainsKey(clientGuid))
            {
                var client = _clientsList[_guid];

                if ((client != null) && client.isCurrent())
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

                        if ((encodedOpusAudio != null) && (encodedOpusAudio.Length > 36))
                        {
                            //  process
                            // check if we should play audio

                            var myClient = IsClientMetaDataValid(_guid);

                            if ((myClient != null) && RadioDCSSyncServer.DcsPlayerRadioInfo.IsCurrent())
                            {
                                //Decode bytes
                                var udpVoicePacket = UDPVoicePacket.DecodeVoicePacket(encodedOpusAudio);

                                // check the radio
                                RadioReceivingState receivingState = null;
                                var receivingRadio =
                                    RadioDCSSyncServer.DcsPlayerRadioInfo.CanHearTransmission(udpVoicePacket.Frequency,
                                        (RadioInformation.Modulation) udpVoicePacket.Modulation,
                                        udpVoicePacket.UnitId, out receivingState);

                                //Check that we're not transmitting on this radio

                                double receivingPowerLossPercent = 0;
                                float lineOfSightLoss = 0;

                                if ((receivingRadio != null) && (receivingState != null)
                                    &&
                                    ((receivingRadio.modulation == RadioInformation.Modulation.INTERCOM)
                                     // INTERCOM Modulation is 2 so if its two dont bother checking LOS and Range
                                     ||
                                     (
                                         HasLineOfSight(udpVoicePacket, out lineOfSightLoss)
                                         &&
                                         InRange(udpVoicePacket, out receivingPowerLossPercent)
                                         &&
                                         !ShouldBlockRxAsTransmitting(receivingState.ReceivedOn)
                                     )
                                    )
                                )
                                {
                                    //  RadioReceivingState[receivingState.ReceivedOn] = receivingState;

                                    //DECODE audio
                                    int len1;
                                    var decoded = _decoder.Decode(udpVoicePacket.AudioPart1Bytes,
                                        udpVoicePacket.AudioPart1Bytes.Length, out len1);


                                    if (len1 > 0)
                                    {
                                        // for some reason if this is removed then it lags?!
                                        //guess it makes a giant buffer and only uses a little?
                                        var tmp = new byte[len1];
                                        Buffer.BlockCopy(decoded, 0, tmp, 0, len1);

                                        //ALL GOOD!
                                        //create marker for bytes
                                        var audio = new ClientAudio
                                        {
                                            ClientGuid = udpVoicePacket.Guid,
                                            PcmAudioShort = ConversionHelpers.ByteArrayToShortArray(tmp),
                                            //Convert to Shorts!
                                            ReceiveTime = GetTickCount64(),
                                            Frequency = udpVoicePacket.Frequency,
                                            Modulation = udpVoicePacket.Modulation,
                                            Volume = receivingRadio.volume,
                                            ReceivedRadio = receivingState.ReceivedOn,
                                            UnitId = udpVoicePacket.UnitId,
                                            Encryption = udpVoicePacket.Encryption,
                                            Decryptable =
                                                (udpVoicePacket.Encryption == receivingRadio.encKey) &&
                                                receivingRadio.enc,
                                            // mark if we can decrypt it
                                            RadioReceivingState = receivingState,
                                            RecevingPower = receivingPowerLossPercent, //loss of 1.0 or greater is total loss
                                            LineOfSightLoss = lineOfSightLoss, // Loss of 1.0 or greater is total loss
                                            PacketNumber = udpVoicePacket.PacketNumber
                                        };


                                        //handle effects
                                        var radioState = RadioReceivingState[audio.ReceivedRadio];

                                        if ((radioState == null) || radioState.PlayedEndOfTransmission ||
                                            !radioState.IsReceiving())
                                        {
                                            var decrytable = audio.Decryptable || (audio.Encryption == 0);

                                            //mark that we have decrpyted encrypted audio for sound effects
                                            if (decrytable && (audio.Encryption > 0))
                                            {
                                                _audioManager.PlaySoundEffectStartReceive(audio.ReceivedRadio,
                                                    true,
                                                    audio.Volume);
                                            }
                                            else
                                            {
                                                _audioManager.PlaySoundEffectStartReceive(audio.ReceivedRadio,
                                                    false,
                                                    audio.Volume);
                                            }
                                        }

                                        RadioReceivingState[audio.ReceivedRadio] = new RadioReceivingState
                                        {
                                            IsSecondary = receivingState.IsSecondary,
                                            LastReceviedAt = Environment.TickCount,
                                            PlayedEndOfTransmission = false,
                                            ReceivedOn = receivingState.ReceivedOn
                                        };

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


        private bool ShouldBlockRxAsTransmitting(int radioId)
        {
            //Return based on server settings as well
            if (!ClientSync.ServerSettings[(int) ServerSettingType.IRL_RADIO_TX])
            {
                return false;
            }

            if (radioId == 0)
            {
                //intercoms dont block
                return false;
            }

            return (_ptt || RadioDCSSyncServer.DcsPlayerRadioInfo.ptt)
                   && (RadioDCSSyncServer.DcsPlayerRadioInfo.selected == radioId);
        }

        private bool HasLineOfSight(UDPVoicePacket udpVoicePacket, out float losLoss)
        {
            losLoss = 0; //0 is NO LOSS
            if (!ClientSync.ServerSettings[(int) ServerSettingType.LOS_ENABLED])
            {
                return true;
            }

            SRClient transmittingClient;
            if (_clientsList.TryGetValue(udpVoicePacket.Guid, out transmittingClient))
            {

                var myPosition = RadioDCSSyncServer.DcsPlayerRadioInfo.pos;

                var clientPos = transmittingClient.Position;

                if (((myPosition.x == 0) && (myPosition.z == 0)) || ((clientPos.x == 0) && (clientPos.z == 0)))
                {
                    //no real position therefore no line of Sight!
                    return true;
                }

                losLoss = transmittingClient.LineOfSightLoss;
                return transmittingClient.LineOfSightLoss < 1.0f; // 1.0 or greater  is TOTAL loss
            }

            losLoss = 0;
            return false;
        }

        private bool InRange(UDPVoicePacket udpVoicePacket, out double signalStrength)
        {
            signalStrength = 0;
            if (!ClientSync.ServerSettings[(int) ServerSettingType.DISTANCE_ENABLED])
            {
                return true;
            }

            SRClient transmittingClient;
            if (_clientsList.TryGetValue(udpVoicePacket.Guid, out transmittingClient))
            {
                var myPosition = RadioDCSSyncServer.DcsPlayerRadioInfo.pos;

                var clientPos = transmittingClient.Position;

                if (((myPosition.x == 0) && (myPosition.z == 0)) || ((clientPos.x == 0) && (clientPos.z == 0)))
                {
                    //no real position
                    return true;
                }

                var dist = RadioCalculator.CalculateDistance(myPosition, clientPos);

                var max = RadioCalculator.FriisMaximumTransmissionRange(udpVoicePacket.Frequency);

                signalStrength = (dist/max);

                return max > dist;
            }
            return false;
        }

        public void Send(byte[] bytes, int len)
        {
            //if either PTT is true
            if ((_ptt || RadioDCSSyncServer.DcsPlayerRadioInfo.ptt)
                && RadioDCSSyncServer.DcsPlayerRadioInfo.IsCurrent() && (bytes != null))
                //can only send if DCS is connected
            {
                try
                {
                    var currentSelected = RadioDCSSyncServer.DcsPlayerRadioInfo.selected;
                    //removes race condition by assigning here with the current selected changing
                    if ((currentSelected >= 0)
                        && (currentSelected < RadioDCSSyncServer.DcsPlayerRadioInfo.radios.Length))
                    {
                        var radio = RadioDCSSyncServer.DcsPlayerRadioInfo.radios[currentSelected];

                        if (((radio != null) && (radio.freq > 100) &&
                             (radio.modulation != RadioInformation.Modulation.DISABLED))
                            || (radio.modulation == RadioInformation.Modulation.INTERCOM))
                        {
                            //generate packet
                            var udpVoicePacket = new UDPVoicePacket
                            {
                                GuidBytes = _guidAsciiBytes,
                                AudioPart1Bytes = bytes,
                                AudioPart1Length = (ushort) bytes.Length,
                                Frequency = radio.freq,
                                UnitId = RadioDCSSyncServer.DcsPlayerRadioInfo.unitId,
                                Encryption = radio.enc ? radio.encKey : (byte) 0,
                                Modulation = (byte) radio.modulation,
                                PacketNumber = _packetNumber++
                            }.EncodePacket();


                            //no need to auto send packet anymore
                            hasSentVoicePacket = true;

                            var ip = new IPEndPoint(_address, _port);
                            _listener.Send(udpVoicePacket, udpVoicePacket.Length, ip);

                            //not sending or really quickly switched sending
                            if (!RadioSendingState.IsSending || (RadioSendingState.SendingOn != currentSelected))
                            {
                                _audioManager.PlaySoundEffectStartTransmit(currentSelected,
                                    radio.enc && (radio.encKey > 0), radio.volume);
                            }

                            //set radio overlay state
                            RadioSendingState = new RadioSendingState
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
                    Logger.Error(e, "Exception Sending Audio Message " + e.Message);
                }
            }
            else
            {
                if (RadioSendingState.IsSending)
                {
                    RadioSendingState.IsSending = false;

                    if (RadioSendingState.SendingOn >= 0)
                    {
                        var radio = RadioDCSSyncServer.DcsPlayerRadioInfo.radios[RadioSendingState.SendingOn];

                        _audioManager.PlaySoundEffectEndTransmit(RadioSendingState.SendingOn, radio.volume);
                    }
                }

                if (!hasSentVoicePacket)
                {
                    try
                    {
                        var udpVoicePacket = new UDPVoicePacket
                        {
                            GuidBytes = _guidAsciiBytes,
                            AudioPart1Bytes = bytes,
                            AudioPart1Length = (ushort) bytes.Length,
                            Frequency = 100,
                            UnitId = 1,
                            Encryption = 0,
                            Modulation = 4
                        }.EncodePacket();

                        hasSentVoicePacket = true;

                        var ip = new IPEndPoint(_address, _port);
                        _listener.Send(udpVoicePacket, udpVoicePacket.Length, ip);

                        Logger.Info("Sent First Voice Packet");
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception Sending First Audio Message " + e.Message);
                    }
                }
            }
        }

        private void StartPing()
        {
            var thread = new Thread(() =>
            {
                byte[] message = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15};

                while (!_stop)
                {
                    //Logger.Info("Pinging Server");
                    try
                    {
                        var ip = new IPEndPoint(_address, _port);
                        _listener.Send(message, message.Length, ip);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception Sending Audio Ping! " + e.Message);
                    }

                    Thread.Sleep(25*1000);
                }
            });
            thread.Start();
        }
    }
}