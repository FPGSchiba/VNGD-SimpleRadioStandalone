using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

/**
Keeps radio information in Sync Between DCS and 

**/

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    public class RadioSyncServer
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private UdpClient dcsUDPListener;
        private UdpClient radioCommandUDPListener;
        private volatile bool _stop = false;

        public static volatile DCSPlayerRadioInfo dcsPlayerRadioInfo = new DCSPlayerRadioInfo();

        private SendRadioUpdate updateDelegate;

        public delegate void SendRadioUpdate();

        private long lastSent = 0;

        public RadioSyncServer(SendRadioUpdate send)
        {
            updateDelegate = send;
        }

        public void Listen()
        {
            DCSListener();
        }

        private void DCSListener()
        {
            StartDCSMulticastListener();
            StartRadioGUIListener();
        }

        private void StartRadioGUIListener()
        {
            //START GUI LISTENER


            this.radioCommandUDPListener = new UdpClient();
            this.radioCommandUDPListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            this.radioCommandUDPListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            IPAddress multicastaddress = IPAddress.Parse("239.255.50.10");
            this.radioCommandUDPListener.JoinMulticastGroup(multicastaddress);

            IPEndPoint localEp = new IPEndPoint(IPAddress.Any, 5070);
            this.radioCommandUDPListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (radioCommandUDPListener)
                {
                    while (!_stop)
                    {
                        IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5070);
                        byte[] bytes = radioCommandUDPListener.Receive(ref groupEP);

                        int length = 0;
                        try
                        {
                            RadioCommand message = JsonConvert.DeserializeObject<RadioCommand>((Encoding.ASCII.GetString(
                                                                                             bytes, 0, bytes.Length)));

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
                    dcsPlayerRadioInfo.selected = (short)radioCommand.radio;
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
            this.dcsUDPListener = new UdpClient();
            this.dcsUDPListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            this.dcsUDPListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            IPAddress multicastaddress = IPAddress.Parse("239.255.50.10");
            this.dcsUDPListener.JoinMulticastGroup(multicastaddress);

            IPEndPoint localEp = new IPEndPoint(IPAddress.Any, 5067);
            this.dcsUDPListener.Client.Bind(localEp);
            //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (dcsUDPListener)
                {
                    while (!_stop)
                    {
                        IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5067);
                        byte[] bytes = dcsUDPListener.Receive(ref groupEP);

                        try
                        {
                            DCSPlayerRadioInfo message = JsonConvert.DeserializeObject<DCSPlayerRadioInfo>((Encoding.ASCII.GetString(
                                                                                                         bytes, 0, bytes.Length)));

                      //update internal radio
                      UpdateRadio(message);

                      //sync with others
                      if (ShouldSendUpdate(message))
                            {
                                lastSent = System.Environment.TickCount;
                                this.updateDelegate();
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
                for (int i = 0; i < dcsPlayerRadioInfo.radios.Length; i++)
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
            dcsPlayerRadioInfo.lastUpdate = System.Environment.TickCount;

            SendUpdateToGUI();
        }

        private bool ShouldSendUpdate(DCSPlayerRadioInfo radioUpdate)
        {
            //send update if our metadata is nearly stale
            if (System.Environment.TickCount - lastSent < 4000)
            {
                return false;
            }

            return true;
        }


        private void SendUpdateToGUI()
        {
            byte[] bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(dcsPlayerRadioInfo) + "\n");
            //multicast
            send("239.255.50.10", 35034, bytes);
            //unicast
            //  send("127.0.0.1", 5061, bytes);
        }

        private void send(String ipStr, int port, byte[] bytes)
        {
            try
            {
                UdpClient client = new UdpClient();
                IPEndPoint ip = new IPEndPoint(IPAddress.Parse(ipStr), port);

                client.Send(bytes, bytes.Length, ip);
                client.Close();
            }
            catch (Exception e)
            {
            }
        }

        public void Stop()
        {
            this._stop = true;

            try
            {
                this.dcsUDPListener.Close();
            }
            catch (Exception ex)
            {
            }
            try
            {
                this.radioCommandUDPListener.Close();
            }
            catch (Exception ex)
            {
            }
        }
    }
}