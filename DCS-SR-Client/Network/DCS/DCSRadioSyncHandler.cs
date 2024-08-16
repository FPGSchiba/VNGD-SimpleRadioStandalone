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
using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Newtonsoft.Json;
using NLog;
using System.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class DCSRadioSyncHandler
    {
        private readonly DCSRadioSyncManager.SendRadioUpdate _radioUpdate;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConnectedClientsSingleton _clients = ConnectedClientsSingleton.Instance;

        private static readonly int RADIO_UPDATE_PING_INTERVAL = 60; //send update regardless of change every X seconds


        private UdpClient _dcsUdpListener;
        private UdpClient _dcsRadioUpdateSender;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private volatile bool _stop;

        public delegate void NewAircraft(string name, int seat);

        private readonly NewAircraft _newAircraftCallback;

        private long _identStart = 0;

        public DCSRadioSyncHandler(DCSRadioSyncManager.SendRadioUpdate radioUpdate, NewAircraft _newAircraft)
        {
            _radioUpdate = radioUpdate;
            _newAircraftCallback = _newAircraft;
        }


        public void ProcessRadioInfo(DCSPlayerRadioInfo message)
        {
          
            // determine if its changed by comparing old to new
            var update = UpdateRadio(message);

            var diff = new TimeSpan( DateTime.Now.Ticks - _clientStateSingleton.LastSent);

            if (update 
                || _clientStateSingleton.LastSent < 1 
                || diff.TotalSeconds > 60)
            {
                Logger.Debug("Sending Radio Info To Server - Update");
                _clientStateSingleton.LastSent = DateTime.Now.Ticks;
                _radioUpdate();
            }
        }

        private bool UpdateRadio(DCSPlayerRadioInfo message)
        {
            var expansion = _serverSettings.GetSettingAsBool(ServerSettingsKeys.RADIO_EXPANSION);

            var playerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            //copy and compare to look for changes
            var beforeUpdate = playerRadioInfo.DeepClone();

            //update common parts
            playerRadioInfo.name = message.name;
            playerRadioInfo.intercomHotMic = message.intercomHotMic; // This setting Comes via -> UDP Connection Socket with DCS (Backtrace function)
            playerRadioInfo.capabilities = message.capabilities;

            if (_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls))
            {
                message.control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS;
                playerRadioInfo.control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS;
            }
            else
            {
                playerRadioInfo.control = message.control;
            }

            playerRadioInfo.simultaneousTransmissionControl = message.simultaneousTransmissionControl;

            playerRadioInfo.unit = message.unit;

            var overrideFreqAndVol = false;

            var newAircraft = playerRadioInfo.unitId != message.unitId || playerRadioInfo.seat != message.seat || !playerRadioInfo.IsCurrent();

            overrideFreqAndVol = playerRadioInfo.unitId != message.unitId;
            
            //save unit id
            playerRadioInfo.unitId = message.unitId;
            playerRadioInfo.seat = message.seat;
            

            if (newAircraft)
            {
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoSelectSettingsProfile))
                {
                    _newAircraftCallback(message.unit, message.seat);
                }

                playerRadioInfo.iff = message.iff;
            }

            if (overrideFreqAndVol)
            {
                playerRadioInfo.selected = message.selected;
            }

            if (playerRadioInfo.control == DCSPlayerRadioInfo.RadioSwitchControls.IN_COCKPIT)
            {
                playerRadioInfo.selected = message.selected;
            }

            bool simul = false;


            //copy over radio names, min + max
            for (var i = 0; i < playerRadioInfo.radios.Length; i++)
            {
                var clientRadio = playerRadioInfo.radios[i];

                //if we have more radios than the message has
                if (i >= message.radios.Length)
                {
                    clientRadio.freq = 1;
                    clientRadio.freqMin = 1;
                    clientRadio.freqMax = 1;
                    clientRadio.secFreq = 0;
                    clientRadio.retransmit = false;
                    clientRadio.modulation = RadioInformation.Modulation.DISABLED;
                    clientRadio.name = "No Radio";
                    clientRadio.rtMode = RadioInformation.RetransmitMode.DISABLED;
                    clientRadio.retransmit = false;

                    clientRadio.freqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.guardFreqMode = RadioInformation.FreqMode.COCKPIT;
                    clientRadio.encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION;
                    clientRadio.volMode = RadioInformation.VolumeMode.COCKPIT;

                    continue;
                }

                var updateRadio = message.radios[i];


                if ((updateRadio.expansion && !expansion))
                {
                    //expansion radio, not allowed
                    clientRadio.freq = 1;
                    clientRadio.freqMin = 1;
                    clientRadio.freqMax = 1;
                    clientRadio.secFreq = 0;
                    clientRadio.retransmit = false;
                    clientRadio.modulation = RadioInformation.Modulation.DISABLED;
                    clientRadio.name = "No Radio";
                    clientRadio.rtMode = RadioInformation.RetransmitMode.DISABLED;
                    clientRadio.retransmit = false;

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

                    if (playerRadioInfo.simultaneousTransmissionControl == DCSPlayerRadioInfo.SimultaneousTransmissionControl.EXTERNAL_DCS_CONTROL)
                    { 
                        clientRadio.simul = updateRadio.simul;
                    }

                    if (updateRadio.simul)
                    {
                        simul = true;
                    }

                    clientRadio.name = updateRadio.name;

                    if (overrideFreqAndVol)
                    {
                        clientRadio.modulation = updateRadio.modulation;
                    }

                    //update modes
                    clientRadio.freqMode = updateRadio.freqMode;
                    clientRadio.guardFreqMode = updateRadio.guardFreqMode;
                    clientRadio.rtMode = updateRadio.rtMode;

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

                    //handle Retransmit mode
                    if ((updateRadio.rtMode == RadioInformation.RetransmitMode.COCKPIT))
                    {
                        clientRadio.rtMode = updateRadio.rtMode;
                        clientRadio.retransmit = updateRadio.retransmit;
                    }else if (updateRadio.rtMode == RadioInformation.RetransmitMode.DISABLED)
                    {
                        clientRadio.rtMode = updateRadio.rtMode;
                        clientRadio.retransmit = false;
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


            if (playerRadioInfo.simultaneousTransmissionControl ==
                DCSPlayerRadioInfo.SimultaneousTransmissionControl.EXTERNAL_DCS_CONTROL)
            {
                playerRadioInfo.simultaneousTransmission = simul;
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

            //HANDLE IFF/TRANSPONDER UPDATE
            //TODO tidy up the IFF/Transponder handling and this giant function in general as its silly big :(


            if (_globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys
                .AlwaysAllowTransponderOverlay))
            {
                if (message.iff.control != Transponder.IFFControlMode.DISABLED)
                {
                    playerRadioInfo.iff.control = Transponder.IFFControlMode.OVERLAY;
                    message.iff.control = Transponder.IFFControlMode.OVERLAY;
                }
            }

            if (message.iff.control == Transponder.IFFControlMode.COCKPIT)
            {
                playerRadioInfo.iff = message.iff;
            }


            //HANDLE MIC IDENT
            if (!playerRadioInfo.ptt && playerRadioInfo.iff.mic >0 && _clientStateSingleton.RadioSendingState.IsSending)
            {
                if (_clientStateSingleton.RadioSendingState.SendingOn == playerRadioInfo.iff.mic)
                {
                    playerRadioInfo.iff.status = Transponder.IFFStatus.IDENT;
                }
            }

            //Handle IDENT only lasting for 40 seconds at most - need to toggle it
            if (playerRadioInfo.iff.status == Transponder.IFFStatus.IDENT)
            {
                if (_identStart == 0)
                {
                    _identStart = DateTime.Now.Ticks;
                }

                if (TimeSpan.FromTicks(DateTime.Now.Ticks -_identStart).TotalSeconds > 40)
                {
                    playerRadioInfo.iff.status = Transponder.IFFStatus.NORMAL;
                }

            }
            else
            {
                _identStart = 0;
            }

            //                }
            //            }

            //update
            playerRadioInfo.LastUpdate = DateTime.Now.Ticks;

            return !beforeUpdate.Equals(playerRadioInfo);
        }

        public void Stop()
        {
            _stop = true;
            try
            {
                _dcsUdpListener?.Close();
            }
            catch (Exception)
            {
            }

            try
            {
                _dcsRadioUpdateSender?.Close();
            }
            catch (Exception)
            {
                //IGNORE
            }

            _clientStateSingleton.DcsExportLastReceived = -1;
        }

    }
}
