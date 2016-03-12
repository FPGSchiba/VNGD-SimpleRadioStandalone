using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    class UDPVoiceRouter
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        UdpClient listener;

    //    ConcurrentDictionary<String, IPEndPoint> udpClients = new ConcurrentDictionary<String, IPEndPoint>();

        private volatile bool stop;
        private ConcurrentDictionary<String, SRClient> clientsList;

        public UDPVoiceRouter(ConcurrentDictionary<String, SRClient> clientsList)
        {
            this.clientsList = clientsList;
        }

        public void Listen()
        {

            listener = new UdpClient();
            listener.AllowNatTraversal(true);
            listener.ExclusiveAddressUse = false;
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, 5010));
            while (!stop)
            {
                try
                {
                  
                    IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5010);
                    byte[] rawBytes = listener.Receive(ref groupEP);

                    //last 36 bytes are guid!
                    String guid = Encoding.ASCII.GetString(
                    rawBytes, rawBytes.Length - 36, 36);

                    if (clientsList.ContainsKey(guid))
                    {
                        clientsList[guid].voipPort = groupEP;
                      
                        SendToOthers(rawBytes, guid);
                    }
                    else
                    {
                       
                      //  logger.Info("Removing  "+guid+" From UDP pool");
                    }
                  
                }
                catch (Exception e) {
                  //  logger.Error(e,"Error receving audio UDP for client " + e.Message);
                }
            }

            try
            {
                listener.Close();
            }
            catch (Exception e) { }
        }
        public void RequestStop()
        {
            stop = true;
            try
            {
                listener.Close();
            }
            catch (Exception e) { }
        }


        private void SendToOthers(byte[] bytes, String guid)
        {


            //      Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
            //      {
            //          this.clientsList.Text += ("\n " + address.ToString() + ":" + port);
            //          //Update UI here
            //      }));

            foreach (var client in clientsList)
            {
                try
                {
                    if (!client.Key.Equals(guid))
                    {
                        IPEndPoint ip = client.Value.voipPort;

                        if(ip != null)
                        {
                            listener.Send(bytes, bytes.Length, ip);
                        }
                        
                    }
                    else
                    {

                        IPEndPoint ip = client.Value.voipPort;

                        if (ip != null)
                        {
                            listener.Send(bytes, bytes.Length, ip);
                        }
                    }
                }
                catch (Exception e)
                {
                    //      IPEndPoint ip = client.Value;
                 //   logger.Error(e, "Error sending audio UDP for client " + e.Message);
                    //  udpClients.TryRemove(guid, out port);


                }
            }

            //  listener.Close();



            //    Console.WriteLine("sent");
        }
    }
}


