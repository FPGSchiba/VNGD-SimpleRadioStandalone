using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class DCSRadioSyncHandler
    {
        private readonly DCSRadioSyncManager.SendRadioUpdate _radioUpdate;
        private readonly ConcurrentDictionary<string, SRClient> _clients;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        private UdpClient _dcsUdpListener;
        private UdpClient _dcsRadioUpdateSender;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private volatile bool _stop;

        public delegate void NewAircraft(string name);

        private readonly NewAircraft _newAircraftCallback;

        public DCSRadioSyncHandler(DCSRadioSyncManager.SendRadioUpdate radioUpdate,
            ConcurrentDictionary<string, SRClient> clients, NewAircraft _newAircraft)
        {
            _radioUpdate = radioUpdate;
            _clients = clients;
            _newAircraftCallback = _newAircraft;
        }

        public void Start()
        {
            _dcsUdpListener = new UdpClient();
            _dcsUdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _dcsUdpListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            //   var multicastaddress = IPAddress.Parse("239.255.50.10");
            //      _dcsUdpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, _globalSettings.GetNetworkSetting(GlobalSettingsKeys.DCSIncomingUDP));
            _dcsUdpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;

            //reset last sent
            _clientStateSingleton.LastSent = 0;

            Task.Factory.StartNew(() =>
            {
                using (_dcsUdpListener)
                {
                    while (!_stop)
                    {
                        try
                        {
                            var groupEp = new IPEndPoint(IPAddress.Any,
                            _globalSettings.GetNetworkSetting(GlobalSettingsKeys.DCSIncomingUDP));
                            var bytes = _dcsUdpListener.Receive(ref groupEp);

                            var str = Encoding.UTF8.GetString(
                                bytes, 0, bytes.Length).Trim();

                            var message =
                                JsonConvert.DeserializeObject<DCSPlayerRadioInfo>(str);

                            Logger.Debug($"Recevied Message from DCS {str}");

                            if (!string.IsNullOrWhiteSpace(message.name) && message.name != "Unknown" && message.name != _clientStateSingleton.LastSeenName)
                            {
                                _clientStateSingleton.LastSeenName = message.name;
                            }

                            _clientStateSingleton.DcsExportLastReceived = DateTime.Now.Ticks;

                            //sync with others
                            //Radio info is marked as Stale for FC3 aircraft after every frequency change

                            ProcessRadioInfo(message);
                        }
                        catch (SocketException e)
                        {
                            // SocketException is raised when closing app/disconnecting, ignore so we don't log "irrelevant" exceptions
                            if (!_stop)
                            {
                                Logger.Error(e, "SocketException Handling DCS Message");
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS Message");
                        }
                    }

                    try
                    {
                        _dcsUdpListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }


        public void ProcessRadioInfo(DCSPlayerRadioInfo message)
        {
            var update = UpdateRadio(message);

            //send to DCS UI
            SendRadioUpdateToDCS();

            Logger.Debug("Update sent to DCS");

            if (update || IsRadioInfoStale(message))
            {
                Logger.Debug("Sending Radio Info To Server - Stale");
                _clientStateSingleton.LastSent = DateTime.Now.Ticks;
                _radioUpdate();
            }
        }

        //send updated radio info back to DCS for ingame GUI
        private void SendRadioUpdateToDCS()
        {
            if (_dcsRadioUpdateSender == null)
            {
                _dcsRadioUpdateSender = new UdpClient
                {
                    ExclusiveAddressUse = false,
                    EnableBroadcast = true
                };
                _dcsRadioUpdateSender.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                    true);
                _dcsRadioUpdateSender.ExclusiveAddressUse = false;
            }

            int clientCountIngame = 0;

            foreach (KeyValuePair<string, SRClient> kvp in _clients)
            {
                if (kvp.Value.IsIngame())
                {
                    clientCountIngame++;
                }
            }

            try
            {
                var connectedClientsSingleton = ConnectedClientsSingleton.Instance;
                int[] tunedClients = new int[11];

                if (_clientStateSingleton.IsConnected
                    && _clientStateSingleton.DcsPlayerRadioInfo !=null
                    && _clientStateSingleton.DcsPlayerRadioInfo.IsCurrent())
                {

                    for (int i = 0; i < tunedClients.Length; i++)
                    {
                        var clientRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[i];
                        
                        if (clientRadio.modulation == RadioInformation.Modulation.FM ||
                            clientRadio.modulation == RadioInformation.Modulation.AM)
                        {
                            tunedClients[i] = connectedClientsSingleton.ClientsOnFreq(clientRadio.freq, clientRadio.modulation);
                        }
                    }
                }
                
                //get currently transmitting or receiving
                var combinedState = new CombinedRadioState()
                {
                    RadioInfo = _clientStateSingleton.DcsPlayerRadioInfo,
                    RadioSendingState = UdpVoiceHandler.RadioSendingState,
                    RadioReceivingState = UdpVoiceHandler.RadioReceivingState,
                    ClientCountConnected = _clients.Count,
                    ClientCountIngame = clientCountIngame,
                    TunedClients = tunedClients,
                };

                var message = JsonConvert.SerializeObject(combinedState, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }) + "\n";

                var byteData =
                    Encoding.UTF8.GetBytes(message);

                //Logger.Info("Sending Update over UDP 7080 DCS - 7082 Flight Panels: \n"+message);

                _dcsRadioUpdateSender.Send(byteData, byteData.Length,
                    new IPEndPoint(IPAddress.Parse("127.0.0.1"),
                        _globalSettings.GetNetworkSetting(GlobalSettingsKeys.OutgoingDCSUDPInfo))); //send to DCS
                _dcsRadioUpdateSender.Send(byteData, byteData.Length,
                    new IPEndPoint(IPAddress.Parse("127.0.0.1"),
                        _globalSettings.GetNetworkSetting(GlobalSettingsKeys
                            .OutgoingDCSUDPOther))); // send to Flight Control Panels
            }
            catch (Exception e)
            {
                Logger.Error(e, "Exception Sending DCS Radio Update Message");
            }
        }

        private bool UpdateRadio(DCSPlayerRadioInfo message)
        {
            var changed = false;

            var expansion = _serverSettings.GetSettingAsBool(ServerSettingsKeys.RADIO_EXPANSION);

            var playerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            //update common parts
            playerRadioInfo.name = message.name;
            playerRadioInfo.inAircraft = message.inAircraft;

            if (_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls))
            {
                message.control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS;
                playerRadioInfo.control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS;
            }
            else
            {
                playerRadioInfo.control = message.control;
            }

            playerRadioInfo.unit = message.unit;


            if (!_clientStateSingleton.ShouldUseLotATCPosition())
            {
                _clientStateSingleton.UpdatePlayerPosition(message.pos, message.latLng);
            }
            
            var overrideFreqAndVol = false;

            var newAircraft = playerRadioInfo.unitId != message.unitId || !playerRadioInfo.IsCurrent();

            if (message.unitId >= DCSPlayerRadioInfo.UnitIdOffset &&
                playerRadioInfo.unitId >= DCSPlayerRadioInfo.UnitIdOffset)
            {
                //overriden so leave as is
            }
            else
            {
                overrideFreqAndVol = playerRadioInfo.unitId != message.unitId;
                playerRadioInfo.unitId = message.unitId;
            }

            if (newAircraft)
            {
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoSelectSettingsProfile))
                {
                    _newAircraftCallback(message.unit);
                }
            }


            if (overrideFreqAndVol)
            {
                playerRadioInfo.selected = message.selected;
                changed = true;
            }

            if (playerRadioInfo.control == DCSPlayerRadioInfo.RadioSwitchControls.IN_COCKPIT)
            {
                playerRadioInfo.selected = message.selected;
            }


            //copy over radio names, min + max
            for (var i = 0; i < playerRadioInfo.radios.Length; i++)
            {
                var clientRadio = playerRadioInfo.radios[i];

                //if awacs NOT open -  disable radios over 3
                if (i >= message.radios.Length
                    || (RadioOverlayWindow.AwacsActive == false
                        && (i > 3 || i == 0)
                        // disable intercom and all radios over 3 if awacs panel isnt open and we're a spectator given by the UnitId
                        && playerRadioInfo.unitId >= DCSPlayerRadioInfo.UnitIdOffset))
                {
                    clientRadio.freq = 1;
                    clientRadio.freqMin = 1;
                    clientRadio.freqMax = 1;
                    clientRadio.secFreq = 0;
                    clientRadio.modulation = RadioInformation.Modulation.DISABLED;
                    clientRadio.name = "No Radio";

                    clientRadio.freqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.guardFreqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION;
                    clientRadio.volMode = RadioInformation.VolumeMode.COCKPIT;

                    continue;
                }

                var updateRadio = message.radios[i];


                if ((updateRadio.expansion && !expansion) ||
                    (updateRadio.modulation == RadioInformation.Modulation.DISABLED))
                {
                    //expansion radio, not allowed
                    clientRadio.freq = 1;
                    clientRadio.freqMin = 1;
                    clientRadio.freqMax = 1;
                    clientRadio.secFreq = 0;
                    clientRadio.modulation = RadioInformation.Modulation.DISABLED;
                    clientRadio.name = "No Radio";

                    clientRadio.freqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.guardFreqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION;
                    clientRadio.volMode = RadioInformation.VolumeMode.COCKPIT;
                }
                else
                {
                    //update common parts
                    clientRadio.freqMin = updateRadio.freqMin;
                    clientRadio.freqMax = updateRadio.freqMax;

                    clientRadio.name = updateRadio.name;

                    clientRadio.modulation = updateRadio.modulation;

                    //update modes
                    clientRadio.freqMode = updateRadio.freqMode;
                    clientRadio.guardFreqMode = updateRadio.guardFreqMode;

                    if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.ALLOW_RADIO_ENCRYPTION))
                    {
                        clientRadio.encMode = updateRadio.encMode;
                    }
                    else
                    {
                        clientRadio.encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION;
                    }

                    clientRadio.volMode = updateRadio.volMode;

                    if ((updateRadio.freqMode == RadioInformation.FreqMode.COCKPIT) || overrideFreqAndVol)
                    {
                        if (clientRadio.freq != updateRadio.freq)
                            changed = true;

                        if (clientRadio.secFreq != updateRadio.secFreq)
                            changed = true;

                        clientRadio.freq = updateRadio.freq;

                        if (newAircraft && updateRadio.guardFreqMode == RadioInformation.FreqMode.OVERLAY)
                        {
                            //default guard to off
                            clientRadio.secFreq = 0;
                        }
                        else
                        {
                            if (clientRadio.secFreq != 0 && updateRadio.guardFreqMode == RadioInformation.FreqMode.OVERLAY)
                            {
                                //put back
                                clientRadio.secFreq = updateRadio.secFreq;
                            }
                            else if (clientRadio.secFreq == 0 && updateRadio.guardFreqMode == RadioInformation.FreqMode.OVERLAY)
                            {
                                clientRadio.secFreq = 0;
                            }
                            else
                            {
                                clientRadio.secFreq = updateRadio.secFreq;
                            }

                        }



                        clientRadio.channel = updateRadio.channel;
                    }
                    else
                    {
                        if (clientRadio.secFreq != 0)
                        {
                            //put back
                            clientRadio.secFreq = updateRadio.secFreq;
                        }

                        //check we're not over a limit
                        if (clientRadio.freq > clientRadio.freqMax)
                        {
                            clientRadio.freq = clientRadio.freqMax;
                        }
                        else if (clientRadio.freq < clientRadio.freqMin)
                        {
                            clientRadio.freq = clientRadio.freqMin;
                        }
                    }

                    //reset encryption
                    if (overrideFreqAndVol)
                    {
                        clientRadio.enc = false;
                        clientRadio.encKey = 0;
                    }

                    //Handle Encryption
                    if (updateRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY)
                    {
                        if (clientRadio.encKey == 0)
                        {
                            clientRadio.encKey = 1;
                        }
                    }
                    else if (clientRadio.encMode ==
                             RadioInformation.EncryptionMode.ENCRYPTION_COCKPIT_TOGGLE_OVERLAY_CODE)
                    {
                        clientRadio.enc = updateRadio.enc;

                        if (clientRadio.encKey == 0)
                        {
                            clientRadio.encKey = 1;
                        }
                    }
                    else if (clientRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_FULL)
                    {
                        clientRadio.enc = updateRadio.enc;
                        clientRadio.encKey = updateRadio.encKey;
                    }
                    else
                    {
                        clientRadio.enc = false;
                        clientRadio.encKey = 0;
                    }

                    //handle volume
                    if ((updateRadio.volMode == RadioInformation.VolumeMode.COCKPIT) || overrideFreqAndVol)
                    {
                        clientRadio.volume = updateRadio.volume;
                    }

                    //handle Channels load for radios
                    if (newAircraft && i > 0)
                    {
                        if (clientRadio.freqMode == RadioInformation.FreqMode.OVERLAY)
                        {
                            var channelModel = _clientStateSingleton.FixedChannels[i - 1];
                            channelModel.Max = clientRadio.freqMax;
                            channelModel.Min = clientRadio.freqMin;
                            channelModel.Reload();
                            clientRadio.channel = -1; //reset channel

                            if (_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AutoSelectPresetChannel))
                            {
                                RadioHelper.RadioChannelUp(i);
                            }
                        }
                        else
                        {
                            _clientStateSingleton.FixedChannels[i - 1].Clear();
                            //clear
                        }
                    }
                }
            }

            //change PTT last
            if (!_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT))
            {
                playerRadioInfo.ptt = false;
            }
            else
            {
                playerRadioInfo.ptt = message.ptt;
            }

            //                }
            //            }

            //update
            playerRadioInfo.LastUpdate = DateTime.Now.Ticks;

            return changed;
        }

        private bool IsRadioInfoStale(DCSPlayerRadioInfo radioUpdate)
        {
            //send update if our metadata is nearly stale (1 tick = 100ns, 50000000 ticks = 5s stale timer)
            if (DateTime.Now.Ticks - _clientStateSingleton.LastSent < 50000000)
            {
                Logger.Debug($"Not Stale - Tick: {DateTime.Now.Ticks} Last sent: {_clientStateSingleton.LastSent} ");
                return false;
            }
            Logger.Debug($"Stale Radio - Tick: {DateTime.Now.Ticks} Last sent: {_clientStateSingleton.LastSent} ");
            return true;
        }

        public void Stop()
        {
            _stop = true;
            try
            {
                _dcsUdpListener?.Close();
            }
            catch (Exception ex)
            {
            }

            try
            {
                _dcsRadioUpdateSender?.Close();
            }
            catch (Exception ex)
            {
            }
        }

    }
}
