using Ciribob;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
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

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    class ClientSync
    {

        TcpClient tcpClient;
        ConnectCallback callback;
        IPEndPoint serverEndpoint;
        String guid;

        public delegate void ConnectCallback(Boolean result);

        public void TryConnect(IPEndPoint endpoint, ConnectCallback callback)
        {

            //set our client GUID
            guid = Guid.NewGuid().ToString();

            this.callback = callback;
            this.serverEndpoint = endpoint;

            Thread tcpThread = new Thread(Connect);
            tcpThread.Start();
        }

        private void Connect()
        {
            using (tcpClient = new TcpClient())
            {
                tcpClient.Connect(this.serverEndpoint);

                if(tcpClient.Connected)
                {
                    tcpClient.NoDelay = true;

                    CallOnMain(true);
                    ClientSyncLoop();
                }
             
            }
            //disconnect callback
            CallOnMain(false);

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
                //start the loop off by sending a SYNC Request
                SendToServer(new NetworkMessage() { ClientGuid = guid, MsgType = NetworkMessage.MessageType.SYNC});
               
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                   
                    NetworkMessage lastRadioTransmit = JsonConvert.DeserializeObject<NetworkMessage>(line);
                    //TODO: test malformed JSON
                    if (lastRadioTransmit != null)
                    {
                        switch (lastRadioTransmit.MsgType)
                        {
                            case NetworkMessage.MessageType.RADIO_UPDATE:
                                Console.WriteLine("Recevied: " + NetworkMessage.MessageType.RADIO_UPDATE);
                                break;
                            case NetworkMessage.MessageType.SYNC:
                                Console.WriteLine("Recevied: " + NetworkMessage.MessageType.SYNC);
                                break;
                            default:
                                Console.WriteLine("Recevied unknown "+line);
                                break;
                        }
                    }

                    // do something with line

                }
            }
        }

        private void SendToServer(NetworkMessage message)
        {
            try
            {
                byte[] json = System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)+"\n");

                tcpClient.GetStream().Write(json,0,json.Length);
                //Need to flush?
            }
            catch(Exception ex)
            {
                Console.WriteLine("Error sending to server " + ex.Message);
                Disconnect();
            }
        }

        //implement IDispose? To close stuff properly?
        public void Disconnect()
        {
            try
            {
                if(tcpClient!=null)
                {
                   
                    tcpClient.Close(); // this'll stop the socket blocking
                }
                
            }catch(Exception ex) { }

            CallOnMain(false);
        }
    }
}
