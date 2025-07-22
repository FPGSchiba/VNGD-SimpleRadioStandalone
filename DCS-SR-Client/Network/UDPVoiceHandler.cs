using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using NLog;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Audio.Models;
using Vanguard.VCS.Client.Input;
using Vanguard.VCS.Client.Network.Models;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Client.Utils;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Helpers;
using Vanguard.VCS.Common.Network;
using Vanguard.VCS.Common.Setting;

namespace Vanguard.VCS.Client.Network
{
    internal class UdpVoiceHandler
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly IPAddress _address;
        private readonly AudioManager _audioManager;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;
        private readonly AudioInputSingleton _audioInputSingleton = AudioInputSingleton.Instance;

        private readonly BlockingCollection<VcsVoicePacket> _encodedAudio = new BlockingCollection<VcsVoicePacket>();
        private readonly Guid _guid;
        private readonly byte[] _guidAsciiBytes;
        private readonly InputDeviceManager _inputManager;
        private readonly CancellationTokenSource _pingStop = new CancellationTokenSource();
        private readonly int _port;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private readonly CancellationTokenSource _stopFlag = new CancellationTokenSource();

        private readonly int UDP_VOIP_TIMEOUT = 42; // seconds for timeout before redoing VoIP

        private ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        //    private readonly JitterBuffer _jitterBuffer = new JitterBuffer();
        private UdpClient _listener;

        private uint _packetNumber = 1;

        public volatile bool _ptt;
        private long _lastPTTPress; // to handle dodgy PTT - release time
        private long _firstPTTPress; // to delay start PTT time

        private long _lastVOXSend;

        private volatile bool _intercomPtt;

        private volatile bool _ready;

        private IPEndPoint _serverEndpoint;

        private volatile bool _stop;

      //  private Timer _timer;

        private long _udpLastReceived = 0;
        private DispatcherTimer _updateTimer;

        private RadioReceivingState[] _radioReceivingState;

        public UdpVoiceHandler(Guid guid, IPAddress address, int port, AudioManager audioManager,
            InputDeviceManager inputManager)
        {
            _radioReceivingState = _clientStateSingleton.RadioReceivingState;

            _audioManager = audioManager;

            _guid = guid;
            _address = address;
            _port = port;

            _serverEndpoint = new IPEndPoint(_address, _port);

            _inputManager = inputManager;

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _updateTimer.Tick += UpdateVOIPStatus;
            _updateTimer.Start();
        }

        private void UpdateVOIPStatus(object sender, EventArgs e)
        {
            TimeSpan diff = TimeSpan.FromTicks(DateTime.Now.Ticks - _udpLastReceived);

            //ping every 10 so after 40 seconds VoIP UDP issue
            _clientStateSingleton.IsVoipConnected = !(diff.TotalSeconds > UDP_VOIP_TIMEOUT);
        }

        public void Listen()
        {
            EstablishConnection();

            //start 2 audio processing threads
            var decoderThread = new Thread(UdpAudioDecode);
            decoderThread.Start();

            var settings = GlobalSettingsStore.Instance;
            _inputManager.StartDetectPtt(pressed =>
            {
                var radios = _clientStateSingleton.DcsPlayerRadioInfo;

                var radioSwitchPtt = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTT);
                var radioSwitchPttWhenValid = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTTOnlyWhenValid);

                //store the current PTT state and radios
                var currentRadioId = radios.selected;
                var currentPtt = _ptt;

                var ptt = false;
                var intercomPtt = false;
                foreach (var inputBindState in pressed.Where(inputBindState => inputBindState.IsActive))
                {
                    //radio switch?
                    if ((int)inputBindState.MainDevice.InputBind >= (int)InputBinding.Intercom &&
                        (int)inputBindState.MainDevice.InputBind <= (int)InputBinding.Switch10)
                    {
                        //gives you radio id if you minus 100
                        var radioId = (int)inputBindState.MainDevice.InputBind - 100;

                        if (radioId >= _clientStateSingleton.DcsPlayerRadioInfo.radios.Length) continue;
                        var clientRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[radioId];

                        if (!RadioHelper.SelectRadio(radioId)) continue;
                        //turn on PTT
                        if (!radioSwitchPttWhenValid && !radioSwitchPtt) continue;
                        _lastPTTPress = DateTime.Now.Ticks;
                        ptt = true;
                        //Store last release time
                    }
                    else switch (inputBindState.MainDevice.InputBind)
                    {
                        case InputBinding.Ptt:
                            _lastPTTPress = DateTime.Now.Ticks;
                            ptt = true;
                            break;
                        case InputBinding.IntercomPTT:
                            intercomPtt = true;
                            break;
                    }
                }

                /**
             * Handle DELAYING PTT START
             */

                if (!ptt)
                {
                    //reset
                    _firstPTTPress = -1;
                }

                if (_firstPTTPress == -1 && ptt)
                {
                    _firstPTTPress = DateTime.Now.Ticks;
                }

                if (ptt)
                {
                    //should inhibit for a bit
                    var startDiff = new TimeSpan(DateTime.Now.Ticks - _firstPTTPress);

                    var startInhibit = _globalSettings.ProfileSettingsStore
                        .GetClientSettingFloat(ProfileSettingsKeys.PTTStartDelay);

                    if (startDiff.TotalMilliseconds < startInhibit)
                    {
                        _ptt = false;
                        _lastPTTPress = -1;
                        return;
                    }
                }

                /**
                 * End Handle DELAYING PTT START
                 */


                /**
                 * Start Handle PTT HOLD after release
                 */

                //if length is zero - no keybinds or no PTT pressed set to false
                var diff = new TimeSpan(DateTime.Now.Ticks - _lastPTTPress);

                //Release the PTT ONLY if X ms have passed and we didnt switch radios to handle
                //shitty buttons
                var releaseTime = _globalSettings.ProfileSettingsStore
                    .GetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay);

                if (!ptt
                    && releaseTime > 0
                    && diff.TotalMilliseconds <= releaseTime
                    && currentRadioId == radios.selected)
                {
                    ptt = true;
                }

                /**
                 * End Handle PTT HOLD after release
                 */


                _intercomPtt = intercomPtt;
                _ptt = ptt;
            });

            StartPing();

            _packetNumber = 1; //reset packet number
            
            while (!_stop)
            {
                if (!_ready) continue;
                try
                {
                    var groupEp = new IPEndPoint(IPAddress.Any, _port);
                    
                    var bytes = _listener.Receive(ref groupEp);
                    
                    _udpLastReceived = DateTime.Now.Ticks;
                    if (bytes.Length < VcsVoicePacket.HeaderSize) continue;
                    var myClient = IsClientMetaDataValid(_guid);
                    if (myClient == null || !_clientStateSingleton.DcsPlayerRadioInfo.IsCurrent()) continue;
                    var udpVoicePacket = VcsVoicePacket.DecodePacket(bytes);
                    switch (udpVoicePacket.Type)
                    {
                        case VcsVoicePacketType.Keepalive:
                            Logger.Debug("Received Keepalive Packet from Server");
                            break;
                        case VcsVoicePacketType.Hello:
                        case VcsVoicePacketType.HelloAck:
                            // This should not happen here, but we can log it if needed
                            Logger.Warn("Received unexpected Hello or HelloAck packet from server.");
                            break;
                        case VcsVoicePacketType.Voice:
                            Logger.Debug("Received VoicePacket from Server");
                            _encodedAudio.Add(udpVoicePacket); // Push the packet to the audio decode queue if the packet is a voice packet
                            break;
                        case VcsVoicePacketType.Bye:
                            Logger.Debug("Received Bye Packet from Server");
                            RequestStop();
                            break;
                        default:
                            Logger.Error("Received unexpected Packet from Server");
                            break;
                    }
                }
                catch
                {
                    // IGNORE AS WE GET THIS WHEN THE UDP LISTENER IS TIMING OUT EVERY 3 SECONDS
                }
            }

            _ready = false;

            //stop UI Refreshing
            _updateTimer.Stop();

            _clientStateSingleton.IsVoipConnected = false;
        }
        public void RequestStop()
        {
            _stop = true;

            var byePacket = VcsVoicePacket.CreateByePacket(_guid, _packetNumber).EncodePacket();
            try
            {
                _listener?.Send(byePacket, byePacket.Length, _serverEndpoint);
            }
            catch (Exception e)
            {
                Logger.Error(e, "Exception Sending Bye Packet! " + e.Message);
            }
            
            try
            {
                _listener?.Close();
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Exception Closing UDP Listener");
            }

            _stopFlag.Cancel();
            _pingStop.Cancel();

            _inputManager.StopPtt();
        }

        private SRClient IsClientMetaDataValid(Guid clientGuid)
        {
            if (!_clients.ContainsKey(clientGuid)) return null;
            var client = _clients[_guid];
            return client ?? null;
        }

        private void UdpAudioDecode()
        {
            try
            {
                while (!_stop)
                {
                    try
                    {
                        _encodedAudio.TryTake(out var udpVoicePacket, 100000, _stopFlag.Token);

                        if (udpVoicePacket == null) continue;
                        
                        var globalFrequencies = _serverSettings.GlobalFrequencies;
                        var blockedRadios = CurrentlyBlockedRadios();

                        //Check if Global
                        var listeningFrequency = udpVoicePacket.FrequencyMHz * 1000000; // Convert to Hz
                        var globalFrequency = globalFrequencies.Contains(listeningFrequency);
                        
                        var radio = _clientStateSingleton.DcsPlayerRadioInfo.CanHearTransmission(listeningFrequency, RadioInformation.Modulation.AM, out var state);
                        RadioReceivingPriority radioReceivingPriority = null;
                        if (radio != null && state != null && (radio.modulation == RadioInformation.Modulation.INTERCOM || globalFrequency || !blockedRadios.Contains(state.ReceivedOn)))
                        {
                            radioReceivingPriority = new RadioReceivingPriority()
                            {
                                Frequency = listeningFrequency,
                                Modulation = udpVoicePacket.IsIntercom ? (short)RadioInformation.Modulation.INTERCOM : (short)radio.modulation,
                                ReceivingRadio = radio,
                                ReceivingState = state
                            };
                        }
                        
                        if (radioReceivingPriority == null) continue;

                        var audio = new ClientAudio
                        {
                            ClientGuid = udpVoicePacket.ClientId,
                            EncodedAudio = udpVoicePacket.Payload,
                            //Convert to Shorts!
                            ReceiveTime = DateTime.Now.Ticks,
                            Frequency = radioReceivingPriority.Frequency,
                            Modulation = radioReceivingPriority.Modulation,
                            Volume = radioReceivingPriority.ReceivingRadio.volume,
                            ReceivedRadio = radioReceivingPriority.ReceivingState.ReceivedOn,
                            // mark if we can decrypt it
                            RadioReceivingState = radioReceivingPriority.ReceivingState,
                            Sequence = udpVoicePacket.Sequence,
                            IsSecondary = radioReceivingPriority.ReceivingState.IsSecondary
                        };

                        var transmitterName = "";
                        if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME) && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.ShowTransmitterName) && _clients.TryGetValue(udpVoicePacket.ClientId, out var transmittingClient))
                        {
                            transmitterName = transmittingClient.Name;
                        }
                        
                        var newRadioReceivingState =  new RadioReceivingState
                        {
                            IsSecondary = radioReceivingPriority.ReceivingState.IsSecondary,
                            LastReceviedAt = DateTime.Now.Ticks,
                            ReceivedOn = radioReceivingPriority.ReceivingState.ReceivedOn,
                            SentBy = transmitterName
                        };
            
                        _radioReceivingState[audio.ReceivedRadio] = newRadioReceivingState;
                        
                        //we now WANT to duplicate through multiple pipelines ONLY if AM blocking is on
                        //this is a nice optimisation to save duplicated audio on servers without that setting 
                        if (!_serverSettings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE)) continue;
                        if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.RADIO_EFFECT_OVERRIDE))
                        {
                            audio.NoAudioEffects = _serverSettings.GlobalFrequencies.Contains(audio.Frequency);
                        }
                        
                        _audioManager.AddClientAudio(audio);
                    }
                    catch (Exception ex)
                    {
                        if (!_stop)
                        {
                            Logger.Warn(ex, "Failed to decode audio from Packet");
                        }
                    }
                }
            }
            catch (OperationCanceledException e)
            {
                Logger.Warn(e, "Stopped DeJitter Buffer");
            }
        }

        private List<int> CurrentlyBlockedRadios()
        {
            List<int> transmitting = new List<int>();
            if (!_serverSettings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_TX))
            {
                return transmitting;
            }

            if (!_ptt && !_clientStateSingleton.DcsPlayerRadioInfo.ptt)
            {
                return transmitting;
            }

            //Currently transmitting - PTT must be true - figure out if we can hear on those radios

            var currentRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[_clientStateSingleton.DcsPlayerRadioInfo.selected];

            if (currentRadio.modulation == RadioInformation.Modulation.FM 
                || currentRadio.modulation == RadioInformation.Modulation.AM 
                || currentRadio.modulation == RadioInformation.Modulation.MIDS 
                || currentRadio.modulation == RadioInformation.Modulation.HAVEQUICK)
            {
                //only AM and FM block - SATCOM etc dont

                transmitting.Add(_clientStateSingleton.DcsPlayerRadioInfo.selected);
            }
 

            if (_clientStateSingleton.DcsPlayerRadioInfo.simultaneousTransmission)
            {
                // Skip intercom
                for (int i = 1; i < 11; i++)
                {
                    var radio = _clientStateSingleton.DcsPlayerRadioInfo.radios[i];
                    if ( (radio.modulation == RadioInformation.Modulation.FM || radio.modulation == RadioInformation.Modulation.AM )&& radio.simul &&
                        i != _clientStateSingleton.DcsPlayerRadioInfo.selected)
                    {
                        transmitting.Add(i);
                    }
                }
            }

            return transmitting;
        }

        private int getCurrentSelected()
        {
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC)) //If both are active Intercom gets preferred, but UI does not allow this.
            {
                return 0;
            }
            else if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1))
            {
                return 1;
            }
            else { return -1; }
        }

        private List<RadioInformation> CheckVOXActivation(out int sendingOn, bool voice)
        {
            sendingOn = -1;
            if (_clientStateSingleton.InhibitTX.InhibitTX)
            {
                TimeSpan time = new TimeSpan(DateTime.Now.Ticks - _clientStateSingleton.InhibitTX.LastReceivedAt);

                //inhibit for up to 5 seconds since the last message from VAICOM
                if (time.TotalSeconds < 5)
                {
                    return new List<RadioInformation>();
                }
            }
            
            var radioInfo = _clientStateSingleton.DcsPlayerRadioInfo;
            //If its a hot intercom and thats not the currently selected radio
            //this is special logic currently for the gazelle as it has a hot mic, but no way of knowing if you're transmitting from the module itself
            //so we have to figure out what you're transmitting on in SRS
            if (!_ptt && !radioInfo.ptt && !_intercomPtt && (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1) || _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC)))
            {
                // Use this for VOX into the selected Channel: _clientStateSingleton.DcsPlayerRadioInfo.selected
                var currentSelected = getCurrentSelected(); // Change this -> If setting for specific Flick-Back Radio 
                if (currentSelected >= 0) // Could be used to enable vox not only for Intercom
                {
                    var selectedRadios = new List<RadioInformation>();
                    // TODO: Check if radio is disabled or anything else is amiss
                    var currentlySelectedRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[currentSelected];

                    if (currentlySelectedRadio != null && currentlySelectedRadio.modulation !=
                                                       RadioInformation.Modulation.DISABLED)
                    {
                        selectedRadios.Add(currentlySelectedRadio); // Return not only Intercom as transmitting radio
                        sendingOn = currentSelected;
                    }
                    
                    //check if hot mic ONLY activation
                    if (radioInfo.intercomHotMic && voice)
                    {
                        //only send on hotmic and voice 
                        //voice is always true is voice detection is disabled
                        //now check for lastHotmicVoice
                        _lastVOXSend = DateTime.Now.Ticks;
                        return selectedRadios;
                    }
                    if (radioInfo.intercomHotMic && !voice)
                    {
                        TimeSpan lastVOXSendDiff = new TimeSpan(DateTime.Now.Ticks - _lastVOXSend);
                        if (lastVOXSendDiff.TotalMilliseconds < _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMinimumTime))
                        {
                            return selectedRadios;
                        }

                        //VOX no longer detected
                        return new List<RadioInformation>();

                    }
                    
                    return selectedRadios;
                }
            }

            return new List<RadioInformation>();
        }

        private List<RadioInformation> CheckPTTActivation(out int sendingOn)
        {
            sendingOn = -1;
            
            var radioInfo = _clientStateSingleton.DcsPlayerRadioInfo;
            var transmittingRadios = new List<RadioInformation>();
            if (_ptt || _clientStateSingleton.DcsPlayerRadioInfo.ptt)
            {
                // Always add currently selected radio (if valid)
                var currentSelected = _clientStateSingleton.DcsPlayerRadioInfo.selected;
                RadioInformation currentlySelectedRadio = null;
                if (currentSelected >= 0
                    && currentSelected < _clientStateSingleton.DcsPlayerRadioInfo.radios.Length)
                {
                    currentlySelectedRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[currentSelected];

                    if (currentlySelectedRadio != null && currentlySelectedRadio.modulation !=
                                                       RadioInformation.Modulation.DISABLED
                                                       && (currentlySelectedRadio.freq > 100 ||
                                                           currentlySelectedRadio.modulation ==
                                                           RadioInformation.Modulation.INTERCOM))
                    {
                        sendingOn = currentSelected;
                        transmittingRadios.Add(currentlySelectedRadio);
                    }
                }

                // Add all radios toggled for simultaneous transmission if the global flag has been set
                if (_clientStateSingleton.DcsPlayerRadioInfo.simultaneousTransmission)
                {
                    //dont transmit on all if the INTERCOM is selected & AWACS
                    if (currentSelected == 0 && currentlySelectedRadio.modulation == RadioInformation.Modulation.INTERCOM)
                    {
                        //even if simul transmission is enabled - if we're an AWACS we probably dont want this
                        var intercom = new List<RadioInformation>();
                        intercom.Add(radioInfo.radios[0]);
                        sendingOn = 0;
                        return intercom;
                    }

                    var i = 0;
                    foreach (var radio in _clientStateSingleton.DcsPlayerRadioInfo.radios)
                    {
                        if (radio != null && radio.simul && radio.modulation != RadioInformation.Modulation.DISABLED
                            && (radio.freq > 100 || radio.modulation == RadioInformation.Modulation.INTERCOM)
                            && !transmittingRadios.Contains(radio)
                        ) // Make sure we don't add the selected radio twice
                        {
                            if (sendingOn == -1)
                            {
                                sendingOn = i;
                            }
                            transmittingRadios.Add(radio);
                        }

                        i++;
                    }
                }
            }

            return transmittingRadios;
        }
        
        

        private List<RadioInformation> PTTPressed(out int sendingOn, bool voice)
        {
            sendingOn = -1;
            List<RadioInformation> pttRadios = CheckPTTActivation(out sendingOn);
            if (pttRadios.Count > 0)
            {
                return pttRadios;
            }
            
            List<RadioInformation> voxRadios = CheckVOXActivation(out sendingOn, voice);
            
            return voxRadios;
        }

        public ClientAudio Send(byte[] bytes, int len, bool voice)
        {
            // List of radios the transmission is sent to (can me multiple if simultaneous transmission is enabled)
            List<RadioInformation> transmittingRadios;
            //if either PTT is true, a microphone is available && socket connected etc
            var sendingOn = -1;
            if (_ready
                && _listener != null
                && _clientStateSingleton.DcsPlayerRadioInfo.IsCurrent()
                && _audioInputSingleton.MicrophoneAvailable
                && (bytes != null)
                && (transmittingRadios = PTTPressed(out sendingOn, voice)).Count >0 )
                //can only send if DCS is connected
            {
                try
                {
                    if (transmittingRadios.Count > 0)
                    {
                        List<double> frequencies = [];
                        List<short> modulations = [];

                        foreach (var radio in transmittingRadios)
                        {
                            // Further deduplicate transmitted frequencies if they have the same freq./modulation/encryption (caused by differently named radios)
                            var radio1 = radio;
                            var alreadyIncluded = frequencies.Where((t, j) => t == radio1.freq && modulations[j] == (byte)radio1.modulation).Any();

                            if (alreadyIncluded)
                            {
                                continue;
                            }
                            
                            frequencies.Add(radio.freq);
                            modulations.Add((byte)radio.modulation);

                            //generate packet
                            var udpVoicePacket =
                                VcsVoicePacket.CreateVoicePacket(_guid, radio.freq, bytes, _packetNumber);
                            // Logger.Trace($"Sending voide packet on Frequency {udpVoicePacket.Frequency} with Sequence {_packetNumber} to {radio.name} with origintal frequency {radio.freq}");
                            udpVoicePacket.IsIntercom = radio.modulation == RadioInformation.Modulation.INTERCOM;
                            udpVoicePacket.IsPttActive = _ptt || _clientStateSingleton.DcsPlayerRadioInfo.ptt;

                            var encodedUdpVoicePacket = udpVoicePacket.EncodePacket();
                            // sending UDP Package here:
                            _listener.Send(encodedUdpVoicePacket, encodedUdpVoicePacket.Length, new IPEndPoint(_address, _port));
                            _packetNumber++;
                        }
                        
                        var currentlySelectedRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[sendingOn];

                        //not sending or really quickly switched sending
                        if (currentlySelectedRadio != null &&
                            (!_clientStateSingleton.RadioSendingState.IsSending || _clientStateSingleton.RadioSendingState.SendingOn != sendingOn))
                        {
                            // Transmission sound again here:
                            _audioManager.PlaySoundEffectStartTransmit(sendingOn,
                                currentlySelectedRadio.enc && (currentlySelectedRadio.encKey > 0),
                                currentlySelectedRadio.volume, currentlySelectedRadio.modulation);
                        }

                        //set radio overlay state
                        _clientStateSingleton.RadioSendingState = new RadioSendingState
                        {
                            IsSending = true,
                            LastSentAt = DateTime.Now.Ticks,
                            SendingOn = sendingOn
                        };

                        var send = new ClientAudio()
                        {
                            Frequency = frequencies[0],
                            Modulation = modulations[0],
                            EncodedAudio = bytes,
                            Volume = 1,
                            ReceivedRadio = sendingOn,
                            Sequence = _packetNumber,
                            ReceiveTime = DateTime.Now.Ticks,
                        };
                        _packetNumber++;

                        return send;
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Exception Sending Audio Message " + e.Message);
                }
            }
            else
            {
                if (_clientStateSingleton.RadioSendingState.IsSending)
                {
                    _clientStateSingleton.RadioSendingState.IsSending = false;

                    if (_clientStateSingleton.RadioSendingState.SendingOn >= 0)
                    {
                        var radio = _clientStateSingleton.DcsPlayerRadioInfo.radios[_clientStateSingleton.RadioSendingState.SendingOn];
                        // Transmitting sound is here:
                        _audioManager.PlaySoundEffectEndTransmit(_clientStateSingleton.RadioSendingState.SendingOn, radio.volume, radio.modulation);
                    }
                }
            }

            return null;
        }

        private void StartPing()
        {
            Logger.Info("Pinging Server - Starting");

            var message = VcsVoicePacket.CreateKeepalivePacket(_guid).EncodePacket();

            // Force immediate ping once to avoid race condition before starting to listen
            _listener.Send(message, message.Length, _serverEndpoint);

            var thread = new Thread(() =>
            {
                //wait for initial sync - then ping
                if (_pingStop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2)))
                {
                    return;
                }

                _ready = true;

                while (!_stop)
                {
                    try
                    {
                        _listener?.Send(message, message.Length,_serverEndpoint);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception Sending Audio Ping! " + e.Message);
                    }
                    
                    var cancelled = _pingStop.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(15));

                    if (cancelled)
                    {
                        return;
                    }

                    TimeSpan diff = TimeSpan.FromTicks(DateTime.Now.Ticks - _udpLastReceived);

                    //reconnect to UDP - port is no good!
                    if (diff.TotalSeconds > UDP_VOIP_TIMEOUT)
                    {
                        Logger.Error("VoIP Timeout - Recreating VoIP Connection");
                        _ready = false;
                        try
                        {
                            _listener?.Close();
                        }
                        catch (Exception e)
                        {
                            Logger.Warn(e, "Exception Closing UDP Listener");
                        }

                        _listener = null;

                        _udpLastReceived = 0;

                        _listener = new UdpClient();
                        try
                        {
                            _listener.AllowNatTraversal(true);
                        }
                        catch (Exception e)
                        {
                            Logger.Warn(e, "Exception Setting NAT Traversal on UDP Client");
                        }

                        try
                        {
                            // Force immediate ping once to avoid race condition before starting to listen
                            _listener.Send(message, message.Length, _serverEndpoint);
                            _ready = true;
                            Logger.Error("VoIP Timeout - Success Recreating VoIP Connection");
                        }
                        catch (Exception e) {
                            Logger.Error(e, "Exception Sending Audio Ping! " + e.Message);
                        }
                        
                    }
                   
                }
            });
            thread.Start();
        }

        private void EstablishConnection()
        {
            _udpLastReceived = 0;
            _ready = false;
            _listener = new UdpClient();
            try
            {
                _listener.AllowNatTraversal(true);
            }
            catch (Exception e)
            {
                Logger.Warn(e, "Failed to set NAT Traversal on UDP Client");
            }

            var helloMessage = VcsVoicePacket.CreateHelloPacket(_guid).EncodePacket();
            try
            {
                _listener.Send(helloMessage, helloMessage.Length, _serverEndpoint);
                Logger.Info("Sent Hello Packet to Server");
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to send Hello Packet to Server");
                throw;
            }
            
            // wait for helloAck Answer from Server and only proceed if we get it
            // Exponential backoff here with more hello messages
            var helloAckReceived = false;
            var attempts = 1;
            while (!helloAckReceived && attempts < 6)
            {
                try
                {
                    var groupEp = new IPEndPoint(IPAddress.Any, _port);
                    _listener.Client.ReceiveTimeout = 1500 * 2 * attempts; // 3 seconds timeout for all communication
                    var bytes = _listener.Receive(ref groupEp);

                    if (bytes.Length > 0)
                    {
                        var packet = VcsVoicePacket.DecodePacket(bytes);
                        if (packet != null && packet.Type == VcsVoicePacketType.HelloAck)
                        {
                            helloAckReceived = true;
                            Logger.Info("Received Hello Ack from Server");
                        }
                    }
                }
                catch (SocketException e)
                {
                    Logger.Warn(e, "SocketException while waiting for Hello Ack");
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Exception while waiting for Hello Ack");
                }

                attempts++;
            }
        }
    }
}