using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
using NLog;

/**
Keeps radio information in Sync Between DCS and 

**/

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    public class RadioSyncServer
    {
        public delegate void ClientSideUpdate();

        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static volatile DCSPlayerRadioInfo DcsPlayerRadioInfo = new DCSPlayerRadioInfo();

        public static volatile DCSPlayerSideInfo DcsPlayerSideInfo = new DCSPlayerSideInfo();
        private volatile bool _stop;

        private readonly SendRadioUpdate _clientRadioUpdate;
        private readonly ClientSideUpdate _clientSideUpdate;
        private UdpClient _dcsGameGuiudpListener;

        private UdpClient _dcsUdpListener;

        private long _lastSent;
        private UdpClient _radioCommandUdpListener;

        public RadioSyncServer(SendRadioUpdate clientRadioUpdate, ClientSideUpdate clientSideUpdate)
        {
            this._clientRadioUpdate = clientRadioUpdate;
            this._clientSideUpdate = clientSideUpdate;
        }

        public void Listen()
        {
            DcsListener();
        }

        private void DcsListener()
        {
            StartDcsMulticastListener();
            StartDcsGameGuiMulticastListener();
            StartRadioOverlayListener();
        }

        private void StartRadioOverlayListener()
        {
            //START GUI LISTENER


            _radioCommandUdpListener = new UdpClient();
            _radioCommandUdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            _radioCommandUdpListener.ExclusiveAddressUse = false;
            // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            _radioCommandUdpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5070);
            _radioCommandUdpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (_radioCommandUdpListener)
                {
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 5070);
                        var bytes = _radioCommandUdpListener.Receive(ref groupEp);


                        try
                        {
                            var message =
                                JsonConvert.DeserializeObject<RadioCommand>(Encoding.ASCII.GetString(
                                    bytes, 0, bytes.Length));

                            HandleRadioCommand(message);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS  Message");
                        }
                    }

                    try
                    {
                        _radioCommandUdpListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private void HandleRadioCommand(RadioCommand radioCommand)
        {
            if (radioCommand.cmdType == RadioCommand.CmdType.SELECT)
            {
                if (DcsPlayerRadioInfo.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    DcsPlayerRadioInfo.selected = (short) radioCommand.radio;
                }
            }
            else if (radioCommand.cmdType == RadioCommand.CmdType.FREQUENCY)
            {
                if (DcsPlayerRadioInfo.radioType == DCSPlayerRadioInfo.AircraftRadioType.NO_COCKPIT_INTEGRATION
                    && radioCommand.radio >= 0
                    && radioCommand.radio < 3)
                {
                    //sort out the frequencies
                    var clientRadio = DcsPlayerRadioInfo.radios[radioCommand.radio];
                    clientRadio.frequency += radioCommand.freq;

                    //make sure we're not over or under a limit
                    if (clientRadio.frequency > clientRadio.freqMax)
                    {
                        clientRadio.frequency = clientRadio.freqMax;
                    }
                    else if (clientRadio.frequency < clientRadio.freqMin)
                    {
                        clientRadio.frequency = clientRadio.freqMin;
                    }
                }
            }
            else if (radioCommand.cmdType == RadioCommand.CmdType.VOLUME)
            {
                if (DcsPlayerRadioInfo.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION
                    && radioCommand.radio >= 0
                    && radioCommand.radio < 3)
                {
                    var clientRadio = DcsPlayerRadioInfo.radios[radioCommand.radio];

                    clientRadio.volume = radioCommand.volume;
                }
            }
        }

        private void StartDcsMulticastListener()
        {
            _dcsUdpListener = new UdpClient();
            _dcsUdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _dcsUdpListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            _dcsUdpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5067);
            _dcsUdpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (_dcsUdpListener)
                {
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 5067);
                        var bytes = _dcsUdpListener.Receive(ref groupEp);

                        try
                        {
                            var message =
                                JsonConvert.DeserializeObject<DCSPlayerRadioInfo>(Encoding.ASCII.GetString(
                                    bytes, 0, bytes.Length));

                            //update internal radio
                            UpdateRadio(message);

                            //sync with others
                            if (ShouldSendUpdate(message))
                            {
                                _lastSent = Environment.TickCount;
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

        private void StartDcsGameGuiMulticastListener()
        {
            _dcsGameGuiudpListener = new UdpClient();
            _dcsGameGuiudpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            _dcsGameGuiudpListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            _dcsGameGuiudpListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5068);
            _dcsGameGuiudpListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;

            Task.Factory.StartNew(() =>
            {
                using (_dcsGameGuiudpListener)
                {
                    var count = 0;
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 5068);
                        var bytes = _dcsGameGuiudpListener.Receive(ref groupEp);

                        try
                        {
                            var playerInfo =
                                JsonConvert.DeserializeObject<DCSPlayerSideInfo>(Encoding.ASCII.GetString(
                                    bytes, 0, bytes.Length));

                            if (DcsPlayerSideInfo.name != playerInfo.name || DcsPlayerSideInfo.side != playerInfo.side ||
                                count > 3)
                            {
                                DcsPlayerSideInfo = playerInfo;
                                _clientSideUpdate();
                                count = 0;
                            }
                            else
                            {
                                count++;
                                DcsPlayerSideInfo = playerInfo;
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

        private void UpdateRadio(DCSPlayerRadioInfo message)
        {
            if (message.radioType == DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                // Full radio, all from DCS
            {
                DcsPlayerRadioInfo = message;
            }
            else if (message.radioType == DCSPlayerRadioInfo.AircraftRadioType.PARTIAL_COCKPIT_INTEGRATION)
                // Partial radio - can select radio but the rest is from DCS
            {
                //update common parts
                DcsPlayerRadioInfo.name = message.name;
                DcsPlayerRadioInfo.radioType = message.radioType;
                DcsPlayerRadioInfo.unit = message.unit;
                DcsPlayerRadioInfo.unitId = message.unitId;

                //copy over the radios
                DcsPlayerRadioInfo.radios = message.radios;
            }
            else // FC3 Radio - Take nothing from DCS, just update the last tickcount
            {
                //update common parts
                DcsPlayerRadioInfo.name = message.name;
                DcsPlayerRadioInfo.radioType = message.radioType;
                DcsPlayerRadioInfo.unit = message.unit;
                DcsPlayerRadioInfo.unitId = message.unitId;


                //copy over radio names, min + max
                for (var i = 0; i < DcsPlayerRadioInfo.radios.Length; i++)
                {
                    var updateRadio = message.radios[i];

                    var clientRadio = DcsPlayerRadioInfo.radios[i];

                    clientRadio.freqMin = updateRadio.freqMin;
                    clientRadio.freqMax = updateRadio.freqMax;

                    clientRadio.name = updateRadio.name;
                    clientRadio.secondaryFrequency = updateRadio.secondaryFrequency;

                    clientRadio.modulation = updateRadio.modulation;

                    //check we're not over a limit

                    if (clientRadio.frequency > clientRadio.freqMax)
                    {
                        clientRadio.frequency = clientRadio.freqMax;
                    }
                    else if (clientRadio.frequency < clientRadio.freqMin)
                    {
                        clientRadio.frequency = clientRadio.freqMin;
                    }
                }
            }

            //update
            DcsPlayerRadioInfo.lastUpdate = Environment.TickCount;

            SendUpdateToGui();
        }

        private bool ShouldSendUpdate(DCSPlayerRadioInfo radioUpdate)
        {
            //send update if our metadata is nearly stale
            if (Environment.TickCount - _lastSent < 4000)
            {
                return false;
            }

            return true;
        }


        private void SendUpdateToGui()
        {
            var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(DcsPlayerRadioInfo) + "\n");
            //multicast
            Send("239.255.50.10", 35034, bytes);
            //unicast
            //  send("127.0.0.1", 5061, bytes);
        }

        private void Send(string ipStr, int port, byte[] bytes)
        {
            try
            {
                var client = new UdpClient();
                var ip = new IPEndPoint(IPAddress.Parse(ipStr), port);

                client.Send(bytes, bytes.Length, ip);
                client.Close();
            }
            catch (Exception e)
            {
            }
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
                _radioCommandUdpListener.Close();
            }
            catch (Exception ex)
            {
            }
        }
    }
}