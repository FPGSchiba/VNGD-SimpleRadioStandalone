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

        private volatile bool stop;
        private ConcurrentDictionary<String, SRClient> clientsList;
        private SRClient[] clientListLookup;

        public UDPVoiceRouter(ConcurrentDictionary<String, SRClient> clientsList, SRClient[] clientListLookup)
        {
            this.clientsList = clientsList;
            this.clientListLookup = clientListLookup;
        }

        public void Listen()
        {

            listener = new UdpClient();
            listener.AllowNatTraversal(true);
            listener.ExclusiveAddressUse = true;
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, 5010));
            startPing();
            while (!stop)
            {
                try
                {
                  
                    IPEndPoint groupEP = new IPEndPoint(IPAddress.Any, 5010);
                     byte[] rawBytes = listener.Receive(ref groupEP);
                    if (rawBytes.Length > 36)
                    {
                        Task.Run(() =>
                        {

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
                                SRClient value;
                                clientsList.TryRemove(guid, out value);
                                //  logger.Info("Removing  "+guid+" From UDP pool");
                            }
                        });
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

        private void startPing()
        {
            Task.Run(() =>
            {
                byte[] message = { 1, 2, 3, 4, 5 };
                while (!stop)
                {

                    logger.Info("Pinging Clients");
                    try
                    {
                        foreach (var client in clientsList)
                        {
                            try
                            {

                                IPEndPoint ip = client.Value.voipPort;

                                if (ip != null)
                                {
                                    listener.Send(message, message.Length, ip);
                                    //      listener.Send(bytes, bytes.Length, ip);
                                }

                            }
                            catch (Exception e)
                            {
                                //      IPEndPoint ip = client.Value;
                                //   logger.Error(e, "Error sending audio UDP for client " + e.Message);
                                //  udpClients.TryRemove(guid, out port);
                                SRClient value;
                                clientsList.TryRemove(client.Key, out value);
                            }
                        }
                    }
                    catch (Exception e)
                    {

                    }

                    Thread.Sleep(10 * 1000);
                    
                }
            });
        }


        private void SendToOthers(byte[] bytes, String guid)
        { 

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
                       
                      //      listener.Send(bytes, bytes.Length, ip);
                        }
                    }
                }
                catch (Exception e)
                {
                    //      IPEndPoint ip = client.Value;
                    //   logger.Error(e, "Error sending audio UDP for client " + e.Message);
                    SRClient value;
                    clientsList.TryRemove(guid,out value);
                }
            }

        }
    }
}


