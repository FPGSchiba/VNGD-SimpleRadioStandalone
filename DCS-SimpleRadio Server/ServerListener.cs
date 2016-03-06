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
    class ServerListener
    {
        UdpClient listener;

        ConcurrentDictionary<IPEndPoint, int> clients = new ConcurrentDictionary<IPEndPoint, int>();
        
        public void Listen()
        {
            listener = new UdpClient(5000);
            listener.AllowNatTraversal(true);

            while (!stop)
            {
                try
                {
                    IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5000);
                    byte[] bytes = listener.Receive(ref groupEP);

                    clients[groupEP] = groupEP.Port;

                    SendToOthers(bytes, groupEP);
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

        private volatile bool stop;
        private TextBox clientsList;

        public ServerListener(TextBox clientsList)
        {
            this.clientsList = clientsList;
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

                foreach (var client in clients)
                {
                    if (!client.Key.Equals(ignoreEndpoint))
                    {
                        IPEndPoint ip = client.Key;
                        listener.Send(bytes, bytes.Length, ip);
                    }
                    else
                    {
               
                    }

                }


                //  listener.Close();
            }
            catch (Exception e) { }

            //    Console.WriteLine("sent");
        }
    }
}


