using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    internal class ClientSync
    {
        public delegate void ConnectCallback(bool result);

        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
        private ConnectCallback callback;
        private readonly ConcurrentDictionary<string, SRClient> clients;
        private readonly string guid;
        private IPEndPoint serverEndpoint;
        private TcpClient tcpClient;

        public ClientSync(ConcurrentDictionary<string, SRClient> clients, string guid)
        {
            this.clients = clients;
            this.guid = guid;
        }


        public void TryConnect(IPEndPoint endpoint, ConnectCallback callback)
        {
            this.callback = callback;
            serverEndpoint = endpoint;

            var tcpThread = new Thread(Connect);
            tcpThread.Start();
        }

        private void Connect()
        {
            var radioSync = new RadioSyncServer(ClientRadioUpdated, ClientSideUpdate);
            using (tcpClient = new TcpClient())
            {
                tcpClient.SendTimeout = 10;
                try
                {
                    tcpClient.Connect(serverEndpoint);

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

        private void ClientRadioUpdated()
        {
            //DO NOTHING
        }

        private void ClientSideUpdate()
        {
            SendToServer(new NetworkMessage
            {
                Client = new SRClient
                {
                    Coalition = RadioSyncServer.dcsPlayerSideInfo.side,
                    Name = RadioSyncServer.dcsPlayerSideInfo.name,
                    ClientGuid = guid
                },
                MsgType = NetworkMessage.MessageType.UPDATE
            });
        }

        private void CallOnMain(bool result)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                    new ThreadStart(delegate { callback(result); }));
            }
            catch (Exception ex)
            {
            }
        }

        private void ClientSyncLoop()
        {
            //clear the clietns list
            clients.Clear();

            using (var reader = new StreamReader(tcpClient.GetStream(), Encoding.UTF8))
            {
                try
                {
                    //start the loop off by sending a SYNC Request
                    SendToServer(new NetworkMessage
                    {
                        Client = new SRClient
                        {
                            Coalition = RadioSyncServer.dcsPlayerSideInfo.side,
                            Name = RadioSyncServer.dcsPlayerSideInfo.name,
                            ClientGuid = guid
                        },
                        MsgType = NetworkMessage.MessageType.SYNC
                    });


                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        try
                        {
                            var lastRadioTransmit = JsonConvert.DeserializeObject<NetworkMessage>(line);
                            //TODO: test malformed JSON
                            if (lastRadioTransmit != null)
                            {
                                switch (lastRadioTransmit.MsgType)
                                {
                                    case NetworkMessage.MessageType.PING:
                                        // Do nothing for now
                                        break;
                                    case NetworkMessage.MessageType.UPDATE:

                                        logger.Info("Recevied: " + NetworkMessage.MessageType.UPDATE + " From: " +
                                                    lastRadioTransmit.Client.Name + " Coalition: " +
                                                    lastRadioTransmit.Client.Coalition);

                                        if (clients.ContainsKey(lastRadioTransmit.Client.ClientGuid))
                                        {
                                            var srClient = clients[lastRadioTransmit.Client.ClientGuid];
                                            var updatedSRClient = lastRadioTransmit.Client;
                                            if (srClient != null)
                                            {
                                                srClient.LastUpdate = Environment.TickCount;
                                                srClient.Name = updatedSRClient.Name;
                                                srClient.Coalition = updatedSRClient.Coalition;
                                            }
                                        }
                                        else
                                        {
                                            var connectedClient = lastRadioTransmit.Client;
                                            connectedClient.LastUpdate = Environment.TickCount;
                                            clients[lastRadioTransmit.Client.ClientGuid] = connectedClient;
                                        }
                                        break;
                                    case NetworkMessage.MessageType.SYNC:
                                        logger.Info("Recevied: " + NetworkMessage.MessageType.SYNC);

                                        if (lastRadioTransmit.Clients != null)
                                        {
                                            foreach (var client in lastRadioTransmit.Clients)
                                            {
                                                client.LastUpdate = Environment.TickCount;
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
            //clear the clietns list
            clients.Clear();

            //disconnect callback
            CallOnMain(false);
        }


        private void SendToServer(NetworkMessage message)
        {
            try
            {
                var json = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(message) + "\n");

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
            catch (Exception ex)
            {
            }

            logger.Warn("Disconnecting from server");

            //CallOnMain(false);
        }
    }
}