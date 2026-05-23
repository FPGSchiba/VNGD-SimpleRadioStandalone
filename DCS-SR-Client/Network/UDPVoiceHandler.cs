using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
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

        // Simple throttling for hot UDP debug logs
        private static readonly Dictionary<string, long> _udpThrottleLastMs = new Dictionary<string, long>();
        private static readonly object _udpThrottleLock = new object();
        private void DebugThrottledUdp(string key, string message, int minMs = 500)
        {
            var now = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            var should = false;
            lock (_udpThrottleLock)
            {
                if (!_udpThrottleLastMs.TryGetValue(key, out var last) || (now - last) >= minMs)
                {
                    _udpThrottleLastMs[key] = now;
                    should = true;
                }
            }
            if (should) Logger.Debug(message);
        }

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
        private readonly RadioStateManager _radioStateManager;

        //    private readonly JitterBuffer _jitterBuffer = new JitterBuffer();
        private volatile UdpClient _listener;

        private uint _packetNumber = 1;

        public volatile bool _ptt;
        private long _lastPTTPress; // to handle dodgy PTT - release time
        private long _firstPTTPress; // to delay start PTT time

        private long _lastVOXSend;
        private RadioInformation.Modulation _lastTransmitModulation = RadioInformation.Modulation.AM;

        private volatile bool _intercomPtt;

        private volatile bool _ready;

        private IPEndPoint _serverEndpoint;

        private volatile bool _stop;

      //  private Timer _timer;

        private long _udpLastReceived = 0; // accessed via Interlocked
        private DispatcherTimer _updateTimer;

        private RadioReceivingState[] _radioReceivingState;

        public UdpVoiceHandler(Guid guid, IPAddress address, int port, AudioManager audioManager,
            InputDeviceManager inputManager, RadioStateManager radioStateManager = null)
        {
            _radioReceivingState = _clientStateSingleton.RadioReceivingState;

            _audioManager = audioManager;

            _guid = guid;
            _address = address;
            _port = port;

            _serverEndpoint = new IPEndPoint(_address, _port);

            _inputManager = inputManager;
            _radioStateManager = radioStateManager;

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _updateTimer.Tick += UpdateVOIPStatus;
            _updateTimer.Start();
        }

        private void UpdateVOIPStatus(object sender, EventArgs e)
        {
            TimeSpan diff = TimeSpan.FromTicks(DateTime.Now.Ticks - Interlocked.Read(ref _udpLastReceived));

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
                var radioSwitchPtt = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTT);
                var radioSwitchPttWhenValid = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTTOnlyWhenValid);

                var currentPtt = _ptt;

                var ptt = false;
                var intercomPtt = false;
                foreach (var inputBindState in pressed.Where(inputBindState => inputBindState.IsActive))
                {
                    //radio switch?
                    if ((int)inputBindState.MainDevice.InputBind >= (int)InputBinding.Intercom &&
                        (int)inputBindState.MainDevice.InputBind <= (int)InputBinding.Switch10)
                    {
                        if (!RadioHelper.SelectRadio((int)inputBindState.MainDevice.InputBind - 100)) continue;
                        if (!radioSwitchPttWhenValid && !radioSwitchPtt) continue;
                        _lastPTTPress = DateTime.Now.Ticks;
                        ptt = true;
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

                //Release the PTT ONLY if X ms have passed
                var releaseTime = _globalSettings.ProfileSettingsStore
                    .GetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay);

                if (!ptt
                    && releaseTime > 0
                    && diff.TotalMilliseconds <= releaseTime)
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

                    var sock = _listener;
                    if (sock == null) break;
                    var bytes = sock.Receive(ref groupEp);

                    Interlocked.Exchange(ref _udpLastReceived, DateTime.Now.Ticks);
                    if (bytes.Length < VcsVoicePacket.HeaderSize) continue;
                    var myClient = IsClientMetaDataValid(_guid);
                    if (myClient == null) continue;
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
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut
                                                  || ex.SocketErrorCode == SocketError.Interrupted)
                {
                    // expected: receive timeout
                }
                catch (ObjectDisposedException)
                {
                    break; // socket was intentionally closed
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Unexpected error in UDP receive loop");
                }
            }

            _ready = false;

            //stop UI Refreshing
            Application.Current?.Dispatcher.Invoke(() => _updateTimer?.Stop());

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

                        // Allow test frequencies from server to bypass blocking only if the sender is ourselves (echo test)
                        var testFrequencies = _serverSettings.TestFrequencies ?? new List<double>();
                        var isTestFrequency = testFrequencies.Contains(listeningFrequency);
                        var isSenderUs = udpVoicePacket.ClientId == _guid;
                        if (isTestFrequency && isSenderUs)
                        {
                            // Treat as global for this receive so that it will bypass CurrentlyBlockedRadios
                            globalFrequency = true;
                            DebugThrottledUdp("UDP_TestFreqLocal", $"UdpAudioDecode: Test frequency match and sender is local. Freq={listeningFrequency/1e6:F6} MHz, Client={udpVoicePacket.ClientId}", 2000);
                        }

                        // Check if frequency matches any of the client's enabled radios
                        const int defaultReceiveSlot = 0; // 0-based: INTERCOM channel as fallback for unmatched global packets
                        var radioMatchSlot = defaultReceiveSlot;
                        var radioFrequency = false;
                        if (_radioStateManager != null)
                        {
                            var radios = _radioStateManager.CurrentState.Radios;
                            for (int i = 0; i < radios.Count; i++)
                            {
                                if (radios[i].Enabled && Math.Abs(radios[i].FrequencyHz - listeningFrequency) < 1.0)
                                {
                                    radioFrequency = true;
                                    // 0-based: matches JitterBufferProviderInterface[i], RadioMixingProvider[i], and UI _actualRadioIndex
                                    if (i < _radioReceivingState.Length)
                                        radioMatchSlot = i;
                                    break;
                                }
                            }
                        }

                        // Debug log the important packet metadata and blocking state for troubleshooting
                        DebugThrottledUdp("UDP_PacketMeta", $"UdpAudioDecode: Packet from {udpVoicePacket.ClientId} freq={listeningFrequency/1e6:F6} MHz, IsIntercom={udpVoicePacket.IsIntercom}, IsPTTActive={udpVoicePacket.IsPttActive}, globalFrequency={globalFrequency}, radioFrequency={radioFrequency}, radioMatchSlot={radioMatchSlot}, isTestFrequency={isTestFrequency}, isSenderUs={isSenderUs}, blockedRadios=[{string.Join(',', blockedRadios)}]", 2000);

                        // Accept packets on global frequencies, client-configured radio frequencies, or self-sent test frequencies
                        if (!globalFrequency && !radioFrequency && !(isTestFrequency && isSenderUs))
                        {
                            DebugThrottledUdp("UDP_Drop_NoRadio", $"UdpAudioDecode: Dropping packet - not a global/radio/test frequency. Freq={listeningFrequency/1e6:F6} MHz", 2000);
                            continue;
                        }

                        // Use the matched radio slot for accurate receive-state tracking; fall back to slot 1 for global-only matches
                        var receiveSlot = radioFrequency ? radioMatchSlot : defaultReceiveSlot;
                        var receiveState = new RadioReceivingState
                        {
                            IsSecondary = false,
                            LastReceviedAt = DateTime.Now.Ticks,
                            ReceivedOn = receiveSlot,
                            SentBy = ""
                        };

                        var audio = new ClientAudio
                        {
                            ClientGuid = udpVoicePacket.ClientId,
                            EncodedAudio = udpVoicePacket.Payload,
                            ReceiveTime = DateTime.Now.Ticks,
                            Frequency = listeningFrequency,
                            Modulation = udpVoicePacket.IsIntercom ? (short)RadioInformation.Modulation.INTERCOM : (short)RadioInformation.Modulation.AM,
                            Volume = 1.0f,
                            ReceivedRadio = receiveSlot,
                            RadioReceivingState = receiveState,
                            Sequence = udpVoicePacket.Sequence,
                            IsSecondary = false
                        };

                        var showName = _serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME)
                                       || _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AlwaysShowTransmitterName);
                        if (showName && _clients.TryGetValue(udpVoicePacket.ClientId, out var transmittingClient))
                        {
                            receiveState.SentBy = transmittingClient.Name; // empty string if name not yet known
                        }

                        _radioReceivingState[audio.ReceivedRadio] = receiveState;
                        
                        //we now WANT to duplicate through multiple pipelines ONLY if AM blocking is on
                        //this is a nice optimisation to save duplicated audio on servers without that setting 
                        // if (!_serverSettings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE)) continue;
                        if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.RADIO_EFFECT_OVERRIDE))
                        {
                            audio.NoAudioEffects = _serverSettings.GlobalFrequencies.Contains(audio.Frequency);
                            DebugThrottledUdp("UDP_SetNoAudioEffects", $"UdpAudioDecode: Setting NoAudioEffects={audio.NoAudioEffects} for freq={audio.Frequency/1e6:F6} MHz", 2000);
                        }
                        
                        _audioManager.AddClientAudio(audio);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Failed to decode audio from Packet - this is expected if the packet is malformed or not a voice packet");
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
            // Without DCS radio info we cannot determine which radios are blocked during TX
            return new List<int>();
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
            if (!voice) return new List<RadioInformation>();
            if (_radioStateManager == null) return new List<RadioInformation>();
            var voxIndex = getCurrentSelected();
            if (voxIndex < 0) return new List<RadioInformation>();

            var ri = _radioStateManager.GetRadio(voxIndex + 1);
            if (ri == null || ri.modulation == RadioInformation.Modulation.DISABLED)
                return new List<RadioInformation>();

            sendingOn = voxIndex + 1;
            return new List<RadioInformation> { ri };
        }

        private List<RadioInformation> CheckPTTActivation(out int sendingOn)
        {
            sendingOn = -1;
            if (_radioStateManager == null) return new List<RadioInformation>();

            if (_intercomPtt)
            {
                var state = _radioStateManager.CurrentState;
                for (int i = 0; i < state.Radios.Count; i++)
                {
                    if (state.Radios[i].Enabled && state.Radios[i].IsIntercom)
                    {
                        var ri = _radioStateManager.GetRadio(i + 1);
                        if (ri == null) continue;
                        sendingOn = i + 1;
                        return new List<RadioInformation> { ri };
                    }
                }
                return new List<RadioInformation>();
            }

            if (!_ptt) return new List<RadioInformation>();

            var radioIndex = _radioStateManager.SelectedRadioIndex;
            if (radioIndex < 0) return new List<RadioInformation>();

            var selected = _radioStateManager.GetRadio(radioIndex + 1);
            if (selected == null || selected.modulation == RadioInformation.Modulation.DISABLED)
                return new List<RadioInformation>();

            sendingOn = radioIndex + 1;
            return new List<RadioInformation> { selected };
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
            var wasSending = _clientStateSingleton.RadioSendingState.IsSending;
            var prevSendingOn = _clientStateSingleton.RadioSendingState.SendingOn;

            var radios = PTTPressed(out int sendingOn, voice);

            if (radios.Count == 0)
            {
                if (wasSending)
                {
                    _clientStateSingleton.RadioSendingState.IsSending = false;
                    _audioManager?.PlaySoundEffectEndTransmit(prevSendingOn - 1, 1.0f, _lastTransmitModulation);
                }
                return null;
            }

            foreach (var radio in radios)
            {
                try
                {
                    var audioBytes = new byte[len];
                    Buffer.BlockCopy(bytes, 0, audioBytes, 0, len);

                    var packet = VcsVoicePacket.CreateVoicePacket(
                        _guid,
                        radio.freq,
                        audioBytes,
                        _packetNumber++);

                    packet.IsIntercom = radio.modulation == RadioInformation.Modulation.INTERCOM;

                    var encoded = packet.EncodePacket();
                    _listener?.Send(encoded, encoded.Length, _serverEndpoint);

                    Logger.Debug($"Sent voice on {radio.name} ({radio.freq / 1_000_000.0:F3} MHz) seq={packet.Sequence}");
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Exception sending voice packet");
                }
            }

            _clientStateSingleton.RadioSendingState.IsSending = true;
            _clientStateSingleton.RadioSendingState.SendingOn = sendingOn;
            _clientStateSingleton.RadioSendingState.LastSentAt = DateTime.Now.Ticks;

            if (!wasSending)
            {
                var mod = radios[0].modulation;
                _lastTransmitModulation = mod;
                // sendingOn is 1-based for UI; mixer array is 0-based, so subtract 1
                _audioManager?.PlaySoundEffectStartTransmit(sendingOn - 1, radios[0].enc, 1.0f, mod);
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

                    TimeSpan diff = TimeSpan.FromTicks(DateTime.Now.Ticks - Interlocked.Read(ref _udpLastReceived));

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

                        Interlocked.Exchange(ref _udpLastReceived, 0);

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
            Interlocked.Exchange(ref _udpLastReceived, 0);
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
            var helloAckReceived = false;

            for (int attempt = 1; attempt <= 5 && !helloAckReceived; attempt++)
            {
                // (Re)send Hello on each attempt so the server can reply even after packet loss
                try
                {
                    _listener.Send(helloMessage, helloMessage.Length, _serverEndpoint);
                    Logger.Info($"Sent Hello Packet to Server (attempt {attempt}/5)");
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Failed to send Hello Packet (attempt {attempt}/5)");
                    continue;
                }

                // Wait for HelloAck with a per-attempt timeout (1.5 s, 3 s, 4.5 s, 6 s, 7.5 s)
                try
                {
                    var groupEp = new IPEndPoint(IPAddress.Any, _port);
                    _listener.Client.ReceiveTimeout = 1500 * attempt;
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
                catch (SocketException e) when (e.SocketErrorCode == SocketError.TimedOut)
                {
                    Logger.Warn($"Timeout waiting for Hello Ack (attempt {attempt}/5)");
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Exception waiting for Hello Ack (attempt {attempt}/5)");
                }
            }

            if (!helloAckReceived)
            {
                Logger.Error("Failed to receive Hello Ack after 5 attempts — UDP handshake incomplete, proceeding with keepalive path");
            }
        }
    }
}

