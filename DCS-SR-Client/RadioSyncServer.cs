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

        private UdpClient listener;
        private volatile bool _stop = false;

        public static volatile DCSRadios clientRadio = new DCSRadios();

        private SendRadioUpdate updateDelegate;

        public delegate void SendRadioUpdate(DCSRadios clientRadio);

        //TODo pass in callback to send updated stuff over to clients

        //
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

            listener = new UdpClient(); //was  new UdpClient(5000);
                                        //setup UDP
            this.listener = new UdpClient();
            this.listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            this.listener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            IPAddress multicastaddress = IPAddress.Parse("239.255.50.10");
            this.listener.JoinMulticastGroup(multicastaddress);

            IPEndPoint localEp = new IPEndPoint(IPAddress.Any, 5057);
            this.listener.Client.Bind(localEp);
         //   activeRadioUdpClient.Client.ReceiveTimeout = 10000;


            Task.Factory.StartNew(() =>
            {
                using (listener)
                {
                    while (!_stop)
                    {
                        IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5057);
                        byte[] bytes = listener.Receive(ref groupEP);

                        int length = 0;
                        try
                        {

                            DCSRadios message = JsonConvert.DeserializeObject<DCSRadios>((Encoding.ASCII.GetString(
               bytes, 0, bytes.Length)));

                            //TODO - Handle volume levels
                            //TODO - Select Radio
                            //TODO - 

                            if(ShouldSendUpdate(message))
                            {
                                //update last received
                                message.lastUpdate = System.Environment.TickCount;
                                clientRadio = message;

                                this.updateDelegate(message);

                                //send to GUI
                                SendUpdateToGUI(message);

                            }


                        }
                        catch (Exception e)
                        {
                            logger.Error(e, "Exception Handling DCS  Message");
                          
                        }
                    }

                    try
                    {
                        listener.Close();
                    }
                    catch (Exception e)
                    {
                        logger.Error(e, "Exception stoping DCS listener ");
                      
                    }

                }

            });
        }

        private bool ShouldSendUpdate(DCSRadios radioUpdate)
        {
            //only send radio to all clients if not equal to current
            if(!clientRadio.Equals(radioUpdate))
            {
                return true;
            }

            //send update if our metadata is nearly stale
            if(System.Environment.TickCount - clientRadio.lastUpdate < 8000)
            {
                return false;
            }

            return true;
        }


        private void SendUpdateToGUI(DCSRadios update)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(update) + "\n");
            //multicast
            send("239.255.50.10", 35024, bytes);
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
            catch (Exception e) { }

        }

        public void Stop()
        {
            this._stop = true;

            try
            {
                this.listener.Close();
            }
            catch (Exception ex) { }

        }
    }
}
