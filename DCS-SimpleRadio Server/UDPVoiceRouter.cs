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
        // private static Logger logger = LogManager.GetCurrentClassLogger();
        UdpClient listener;

        ConcurrentDictionary<IPEndPoint, int> udpClients = new ConcurrentDictionary<IPEndPoint, int>();

        private volatile bool stop;
        private ConcurrentDictionary<String, SRClient> clientsList;

        public UDPVoiceRouter(ConcurrentDictionary<String, SRClient> clientsList)
        {
            this.clientsList = clientsList;
        }

        public void Listen()
        {
            listener = new UdpClient(5010);
            listener.AllowNatTraversal(true);

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
                        udpClients[groupEP] = groupEP.Port;
                        SendToOthers(rawBytes, groupEP);
                    }
                    else
                    {
                        int port;
                        udpClients.TryRemove(groupEP, out port);
                    }
                }
                catch (Exception e) { }
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


        private void SendToOthers(byte[] bytes, IPEndPoint ignoreEndpoint)
        {
            try
            {
                //      Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
                //      {
                //          this.clientsList.Text += ("\n " + address.ToString() + ":" + port);
                //          //Update UI here
                //      }));

                foreach (var client in udpClients)
                {
                    if (!client.Key.Equals(ignoreEndpoint))
                    {
                        IPEndPoint ip = client.Key;
                        listener.Send(bytes, bytes.Length, ip);
                    }
                    else
                    {
                        //DEBUG@
                        IPEndPoint ip = client.Key;
                        listener.Send(bytes, bytes.Length, ip);

                    }

                }


                //  listener.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine("Error sending audio UDP " + e.Message);
            }

            //    Console.WriteLine("sent");
        }
    }
}


