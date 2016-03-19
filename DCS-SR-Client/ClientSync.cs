using Ciribob;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Collections.Concurrent;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class ClientSync
    {
        public delegate void ConnectCallback(Boolean result);
        private static Logger logger = LogManager.GetCurrentClassLogger();
        TcpClient tcpClient;
        ConnectCallback callback;
        IPEndPoint serverEndpoint;
        String guid;
        private ConcurrentDictionary<string, SRClient> clients;

        public ClientSync(ConcurrentDictionary<string, SRClient> clients, string guid)
        {
            this.clients = clients;
            this.guid = guid;
        }

        

        public void TryConnect(IPEndPoint endpoint, ConnectCallback callback)
        {

            this.callback = callback;
            this.serverEndpoint = endpoint;

            Thread tcpThread = new Thread(Connect);
            tcpThread.Start();
        }

        private void Connect()
        {
            RadioSyncServer radioSync = new RadioSyncServer(ClientRadioUpdated);
            using (tcpClient = new TcpClient())
            {
                tcpClient.SendTimeout = 10;
                try
                {
                    tcpClient.Connect(this.serverEndpoint);

                    if (tcpClient.Connected)
                    {

                        radioSync.Listen();

                        tcpClient.NoDelay = true;

                        CallOnMain(true);
                        ClientSyncLoop();
                    }
                }
                catch (SocketException ex)
                {
              
                    logger.Error(ex, "error connecting to server");
                }

            }

            radioSync.Stop();

            //disconnect callback
            CallOnMain(false);

        }

        private void ClientRadioUpdated(DCSRadios radio)
        {
            SendToServer(new NetworkMessage() { ClientGuid = guid, MsgType = NetworkMessage.MessageType.RADIO_UPDATE, ClientRadioUpdate = radio });

        }

        private void CallOnMain(bool result)
        {
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new ThreadStart(delegate
            {
                callback(result);

            }));
        }

        private void ClientSyncLoop()
        {
            using (StreamReader reader = new StreamReader(tcpClient.GetStream(), Encoding.UTF8))
            {

                try
                {


                    //start the loop off by sending a SYNC Request
                    SendToServer(new NetworkMessage() { ClientGuid = guid, MsgType = NetworkMessage.MessageType.SYNC, ClientRadioUpdate = new DCSRadios() });


                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {

                        try
                        {
                            NetworkMessage lastRadioTransmit = JsonConvert.DeserializeObject<NetworkMessage>(line);
                            //TODO: test malformed JSON
                            if (lastRadioTransmit != null)
                            {
                                switch (lastRadioTransmit.MsgType)
                                {
                                    case NetworkMessage.MessageType.RADIO_UPDATE:
                                        logger.Info("Recevied: " + NetworkMessage.MessageType.RADIO_UPDATE);

                                        if(clients.ContainsKey(lastRadioTransmit.ClientGuid))
                                        {
                                            SRClient srClient = clients[lastRadioTransmit.ClientGuid];
                                            srClient.ClientRadios = lastRadioTransmit.ClientRadioUpdate;
                                            srClient.LastUpdate = System.Environment.TickCount;
                                          
                                        }


                                  

                                        break;
                                    case NetworkMessage.MessageType.SYNC:
                                        logger.Info("Recevied: " + NetworkMessage.MessageType.SYNC);

                                        if (lastRadioTransmit.Clients != null)
                                        {
                                            foreach (var client in lastRadioTransmit.Clients)
                                            {
                                                client.LastUpdate = System.Environment.TickCount;
                                                
                                                clients[client.ClientGuid] = client;
                                            }
                                        }

                                        break;
                                    default:
                                        logger.Warn("Recevied unknown " + line);
                                        break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Client exception reading from socket ");
                       
                        }

                        // do something with line

                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Client exception reading - Disconnecting ");
                  
                }
            }
        }

   

        private void SendToServer(NetworkMessage message)
        {
            try
            {
                byte[] json = System.Text.Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(message) + "\n");

                tcpClient.GetStream().Write(json, 0, json.Length);
                //Need to flush?
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Client exception sending so server");
               
                Disconnect();
            }
        }

        //implement IDispose? To close stuff properly?
        public void Disconnect()
        {
            try
            {
                if (tcpClient != null)
                {

                    tcpClient.Close(); // this'll stop the socket blocking
                }

            }
            catch (Exception ex) { }

            logger.Warn("Disconnecting from server");

            //CallOnMain(false);
        }
    }
}
