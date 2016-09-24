using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using Newtonsoft.Json;
using NLog;

/**
Keeps radio information in Sync Between DCS and 

**/

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class RadioDCSSyncServer
    {
        public delegate void ClientSideUpdate();

        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static volatile DCSPlayerRadioInfo DcsPlayerRadioInfo = new DCSPlayerRadioInfo();

        public static volatile DCSPlayerSideInfo DcsPlayerSideInfo = new DCSPlayerSideInfo();

        private readonly SendRadioUpdate _clientRadioUpdate;
        private readonly ConcurrentDictionary<string, SRClient> _clients;
        private readonly ClientSideUpdate _clientSideUpdate;
        private readonly string _guid;

        private UdpClient _dcsGameGuiudpListener;

        private UdpClient _dcsLOSListener;
        private UdpClient _dcsUdpListener;

        private volatile bool _stop;

        public RadioDCSSyncServer(SendRadioUpdate clientRadioUpdate, ClientSideUpdate clientSideUpdate,
            ConcurrentDictionary<string, SRClient> _clients, string guid)
        {
            _clientRadioUpdate = clientRadioUpdate;
            _clientSideUpdate = clientSideUpdate;
            this._clients = _clients;
            _guid = guid;
        }

        public static long LastSent { get; set; }


        public void Listen()
        {
            DcsListener();
        }

        private void DcsListener()
        {
            StartDcsBroadcastListener();
            StartDcsGameGuiBroadcastListener();
            StartDCSLOSBroadcastListener();
            StartDCSLOSSender();
        }

        private void StartDcsBroadcastListener()
        {
            _dcsUdpListener = new UdpClient();
            _dcsUdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _dcsUdpListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            //   var multicastaddress = IPAddress.Parse("239.255.50.10");
            //      _dcsUdpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 9084);
            _dcsUdpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;

            //reset last sent
            LastSent = 0;

            Task.Factory.StartNew(() =>
            {
                using (_dcsUdpListener)
                {
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 9084);
                        var bytes = _dcsUdpListener.Receive(ref groupEp);

                        try
                        {
                            var message =
                                JsonConvert.DeserializeObject<DCSPlayerRadioInfo>(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));

                            //  Logger.Info("Recevied Message from DCS: "+ Encoding.UTF8.GetString(
                            //          bytes, 0, bytes.Length));

                            //sync with others
                            //Radio info is marked as Stale for FC3 aircraft after every frequency change

                            if (UpdateRadio(message) || IsRadioInfoStale(message))
                            {
                                LastSent = Environment.TickCount;
                                _clientRadioUpdate();
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS  Message");
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

        private void StartDcsGameGuiBroadcastListener()
        {
            _dcsGameGuiudpListener = new UdpClient();
            _dcsGameGuiudpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            _dcsGameGuiudpListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            //    var multicastaddress = IPAddress.Parse("239.255.50.10");
            //   _dcsGameGuiudpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5068);
            _dcsGameGuiudpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;

            Task.Factory.StartNew(() =>
            {
                using (_dcsGameGuiudpListener)
                {
                    //    var count = 0;
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 5068);
                        var bytes = _dcsGameGuiudpListener.Receive(ref groupEp);

                        try
                        {
                            var playerInfo =
                                JsonConvert.DeserializeObject<DCSPlayerSideInfo>(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));

                            if (playerInfo != null)
                            {
                                //update position
                                playerInfo.Position = DcsPlayerRadioInfo.pos;
                                DcsPlayerSideInfo = playerInfo;
                                _clientSideUpdate();
                                //     count = 0;
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS GameGUI Message");
                        }
                    }

                    try
                    {
                        _dcsGameGuiudpListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        //used for the result
        private void StartDCSLOSBroadcastListener()
        {
            _dcsLOSListener = new UdpClient();
            _dcsLOSListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            _dcsLOSListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var localEp = new IPEndPoint(IPAddress.Any, 9085);
            _dcsLOSListener.Client.Bind(localEp);

            Task.Factory.StartNew(() =>
            {
                using (_dcsLOSListener)
                {
                    //    var count = 0;
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 9085);
                        var bytes = _dcsLOSListener.Receive(ref groupEp);

                        try
                        {
                            /*   Logger.Debug(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));*/
                            var playerInfo =
                                JsonConvert.DeserializeObject<DCSLosCheckResult[]>(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));

                            foreach (var player in playerInfo)
                            {
                                SRClient client;

                                if (_clients.TryGetValue(player.id, out client))
                                {
                                    client.LineOfSightLoss = player.los;

                                    Logger.Debug(client.ToString());
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS Los Result Message");
                        }
                    }

                    try
                    {
                        _dcsLOSListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS LOS Result listener ");
                    }
                }
            });
        }

        private void StartDCSLOSSender()
        {
            var _udpSocket = new UdpClient();
            var _host = new IPEndPoint(IPAddress.Loopback, 9086);


            Task.Factory.StartNew(() =>
            {
                using (_dcsLOSListener)
                {
                    while (!_stop)
                    {
                        try
                        {
                            //Chunk client list into blocks of 10 to stay below 8000 ish UDP socket limit
                            var clientsList = GenerateDcsLosCheckRequests();

                            if (clientsList.Count > 0)
                            {
                                var splitList = clientsList.ChunkBy(10);
                                foreach (var clientSubList in splitList)
                                {
                                    // Logger.Info( "Sending LOS Request: "+ JsonConvert.SerializeObject(clientSubList));
                                    var byteData =
                                        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(clientSubList) + "\n");

                                    _udpSocket.Send(byteData, byteData.Length, _host);

                                    //every 250 - Wait for the queue
                                    Thread.Sleep(250);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Sending DCS LOS Request Message");
                        }
                        //every 300 - Wait for the queue
                        Thread.Sleep(250);
                    }

                    try
                    {
                        _dcsLOSListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private List<DCSLosCheckRequest> GenerateDcsLosCheckRequests()
        {
            var clients = _clients.Values.ToList();

            var requests = new List<DCSLosCheckRequest>();

            if (ClientSync.ServerSettings[(int) ServerSettingType.LOS_ENABLED])
            {
                foreach (var client in clients)
                {
                    //only check if its worth it
                    if ((client.Position.x != 0) && (client.Position.z != 0) && (client.ClientGuid != _guid))
                    {
                        requests.Add(new DCSLosCheckRequest
                        {
                            id = client.ClientGuid,
                            x = client.Position.x,
                            y = client.Position.y,
                            z = client.Position.z
                        });
                    }
                }
            }
            return requests;
        }

        private bool UpdateRadio(DCSPlayerRadioInfo message)
        {
            var changed = false;

            var expansion = ClientSync.ServerSettings[(int) ServerSettingType.RADIO_EXPANSION];

            //update common parts
            DcsPlayerRadioInfo.name = message.name;
            DcsPlayerRadioInfo.control = message.control;
            DcsPlayerRadioInfo.unit = message.unit;
            DcsPlayerRadioInfo.pos = message.pos;

            var overrideFreqAndVol = DcsPlayerRadioInfo.unitId != message.unitId;

            if (overrideFreqAndVol)
            {
                DcsPlayerRadioInfo.selected = message.selected;
                changed = true;
            }

            if (message.control == DCSPlayerRadioInfo.RadioSwitchControls.IN_COCKPIT)
            {
                DcsPlayerRadioInfo.selected = message.selected;
            }

            DcsPlayerRadioInfo.unitId = message.unitId;

            //copy over radio names, min + max
            for (var i = 0; i < DcsPlayerRadioInfo.radios.Length; i++)
            {
                var clientRadio = DcsPlayerRadioInfo.radios[i];

                if (i >= message.radios.Length)
                {
                    clientRadio.freq = 1;
                    clientRadio.freqMin = 1;
                    clientRadio.freqMax = 1;
                    clientRadio.secFreq = 0;
                    clientRadio.modulation = RadioInformation.Modulation.DISABLED;
                    clientRadio.name = "No Radio";

                    clientRadio.freqMode = RadioInformation.FreqMode.COCKPIT;
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
                    clientRadio.encMode = updateRadio.encMode;
                    clientRadio.volMode = updateRadio.volMode;

                    if ((updateRadio.freqMode == RadioInformation.FreqMode.COCKPIT) || overrideFreqAndVol)
                    {
                        if (clientRadio.freq != updateRadio.freq)
                            changed = true;

                        if (clientRadio.secFreq != updateRadio.secFreq)
                            changed = true;

                        clientRadio.freq = updateRadio.freq;

                        //default overlay to off
                        if (updateRadio.freqMode == RadioInformation.FreqMode.OVERLAY)
                        {
                            clientRadio.secFreq = 0;
                        }
                        else
                        {
                            clientRadio.secFreq = updateRadio.secFreq;
                        }
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
                    else if (clientRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_COCKPIT_TOGGLE_OVERLAY_CODE)
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
                }
            }

            //change PTT last
            DcsPlayerRadioInfo.ptt = message.ptt;
//                }
//            }

            //update
            DcsPlayerRadioInfo.LastUpdate = Environment.TickCount;

            return changed;
        }

        private bool IsRadioInfoStale(DCSPlayerRadioInfo radioUpdate)
        {
            //send update if our metadata is nearly stale
            if (Environment.TickCount - LastSent < 5000)
            {
                return false;
            }

            return true;
        }

        public void Stop()
        {
            _stop = true;

            try
            {
                _dcsUdpListener.Close();
            }
            catch (Exception ex)
            {
            }

            try
            {
                _dcsGameGuiudpListener.Close();
            }
            catch (Exception ex)
            {
            }

            try
            {
                _dcsLOSListener.Close();
            }
            catch (Exception ex)
            {
            }
        }
    }
}