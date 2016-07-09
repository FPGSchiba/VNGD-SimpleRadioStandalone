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

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public static volatile DCSPlayerRadioInfo dcsPlayerRadioInfo = new DCSPlayerRadioInfo();

        public static volatile DCSPlayerSideInfo dcsPlayerSideInfo = new DCSPlayerSideInfo();
        private volatile bool _stop;

        private readonly SendRadioUpdate clientRadioUpdate;
        private readonly ClientSideUpdate clientSideUpdate;
        private UdpClient dcsGameGUIUDPListener;

        private UdpClient dcsUDPListener;

        private long lastSent;
        private UdpClient radioCommandUDPListener;

        public RadioSyncServer(SendRadioUpdate clientRadioUpdate, ClientSideUpdate clientSideUpdate)
        {
            this.clientRadioUpdate = clientRadioUpdate;
            this.clientSideUpdate = clientSideUpdate;
        }

        public void Listen()
        {
            DCSListener();
        }

        private void DCSListener()
        {
            StartDCSMulticastListener();
            StartDCSGameGUIMulticastListener();
            StartRadioOverlayListener();
        }

        private void StartRadioOverlayListener()
        {
            //START GUI LISTENER


            radioCommandUDPListener = new UdpClient();
            radioCommandUDPListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            radioCommandUDPListener.ExclusiveAddressUse = false;
            // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            radioCommandUDPListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5070);
            radioCommandUDPListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (radioCommandUDPListener)
                {
                    while (!_stop)
                    {
                        var groupEP = new IPEndPoint(IPAddress.Any, 5070);
                        var bytes = radioCommandUDPListener.Receive(ref groupEP);


                        try
                        {
                            var message =
                                JsonConvert.DeserializeObject<RadioCommand>(Encoding.ASCII.GetString(
                                    bytes, 0, bytes.Length));

                            HandleRadioCommand(message);
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Exception Handling DCS  Message");
                        }
                    }

                    try
                    {
                        radioCommandUDPListener.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private void HandleRadioCommand(RadioCommand radioCommand)
        {
            if (radioCommand.cmdType == RadioCommand.CmdType.SELECT)
            {
                if (dcsPlayerRadioInfo.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    dcsPlayerRadioInfo.selected = (short) radioCommand.radio;
                }
            }
            else if (radioCommand.cmdType == RadioCommand.CmdType.FREQUENCY)
            {
                if (dcsPlayerRadioInfo.radioType == DCSPlayerRadioInfo.AircraftRadioType.NO_COCKPIT_INTEGRATION
                    && radioCommand.radio >= 0
                    && radioCommand.radio < 3)
                {
                    //sort out the frequencies
                    var clientRadio = dcsPlayerRadioInfo.radios[radioCommand.radio];
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
                if (dcsPlayerRadioInfo.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION
                    && radioCommand.radio >= 0
                    && radioCommand.radio < 3)
                {
                    var clientRadio = dcsPlayerRadioInfo.radios[radioCommand.radio];

                    clientRadio.volume = radioCommand.volume;
                }
            }
        }

        private void StartDCSMulticastListener()
        {
            dcsUDPListener = new UdpClient();
            dcsUDPListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            dcsUDPListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            dcsUDPListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5067);
            dcsUDPListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (dcsUDPListener)
                {
                    while (!_stop)
                    {
                        var groupEP = new IPEndPoint(IPAddress.Any, 5067);
                        var bytes = dcsUDPListener.Receive(ref groupEP);

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
                                lastSent = Environment.TickCount;
                                clientRadioUpdate();
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Exception Handling DCS  Message");
                        }
                    }

                    try
                    {
                        dcsUDPListener.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private void StartDCSGameGUIMulticastListener()
        {
            dcsGameGUIUDPListener = new UdpClient();
            dcsGameGUIUDPListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            dcsGameGUIUDPListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            dcsGameGUIUDPListener.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, 5068);
            dcsGameGUIUDPListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;

            Task.Factory.StartNew(() =>
            {
                using (dcsGameGUIUDPListener)
                {
                    var count = 0;
                    while (!_stop)
                    {
                        var groupEP = new IPEndPoint(IPAddress.Any, 5068);
                        var bytes = dcsGameGUIUDPListener.Receive(ref groupEP);

                        try
                        {
                            var playerInfo =
                                JsonConvert.DeserializeObject<DCSPlayerSideInfo>(Encoding.ASCII.GetString(
                                    bytes, 0, bytes.Length));

                            if (dcsPlayerSideInfo.name != playerInfo.name || dcsPlayerSideInfo.side != playerInfo.side ||
                                count > 3)
                            {
                                dcsPlayerSideInfo = playerInfo;
                                clientSideUpdate();
                                count = 0;
                            }
                            else
                            {
                                count++;
                                dcsPlayerSideInfo = playerInfo;
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Exception Handling DCS GameGUI Message");
                        }
                    }

                    try
                    {
                        dcsGameGUIUDPListener.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private void UpdateRadio(DCSPlayerRadioInfo message)
        {
            if (message.radioType == DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                // Full radio, all from DCS
            {
                dcsPlayerRadioInfo = message;
            }
            else if (message.radioType == DCSPlayerRadioInfo.AircraftRadioType.PARTIAL_COCKPIT_INTEGRATION)
                // Partial radio - can select radio but the rest is from DCS
            {
                //update common parts
                dcsPlayerRadioInfo.name = message.name;
                dcsPlayerRadioInfo.radioType = message.radioType;
                dcsPlayerRadioInfo.unit = message.unit;
                dcsPlayerRadioInfo.unitId = message.unitId;

                //copy over the radios
                dcsPlayerRadioInfo.radios = message.radios;
            }
            else // FC3 Radio - Take nothing from DCS, just update the last tickcount
            {
                //update common parts
                dcsPlayerRadioInfo.name = message.name;
                dcsPlayerRadioInfo.radioType = message.radioType;
                dcsPlayerRadioInfo.unit = message.unit;
                dcsPlayerRadioInfo.unitId = message.unitId;


                //copy over radio names, min + max
                for (var i = 0; i < dcsPlayerRadioInfo.radios.Length; i++)
                {
                    var updateRadio = message.radios[i];

                    var clientRadio = dcsPlayerRadioInfo.radios[i];

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
            dcsPlayerRadioInfo.lastUpdate = Environment.TickCount;

            SendUpdateToGUI();
        }

        private bool ShouldSendUpdate(DCSPlayerRadioInfo radioUpdate)
        {
            //send update if our metadata is nearly stale
            if (Environment.TickCount - lastSent < 4000)
            {
                return false;
            }

            return true;
        }


        private void SendUpdateToGUI()
        {
            var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(dcsPlayerRadioInfo) + "\n");
            //multicast
            send("239.255.50.10", 35034, bytes);
            //unicast
            //  send("127.0.0.1", 5061, bytes);
        }

        private void send(string ipStr, int port, byte[] bytes)
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
                dcsUDPListener.Close();
            }
            catch (Exception ex)
            {
            }
            try
            {
                radioCommandUDPListener.Close();
            }
            catch (Exception ex)
            {
            }
        }
    }
}