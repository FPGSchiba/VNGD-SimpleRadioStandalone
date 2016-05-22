using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Concurrent;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace DCS_SimpleRadio_Server
{
    public sealed class UDPVoiceRouterV2
    {
     
        Socket listener;
        EndPoint ipeSender;
        private ConcurrentDictionary<String, SRClient> clientsList;

        private UDPVoiceRouterV2(ConcurrentDictionary<String, SRClient> clientsList)
        {
            this.clientsList = clientsList;
        }


        public void Start()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 5010);

            listener = new Socket(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp); 
            listener.Bind(endpoint);

            ClientSocketState connection = new ClientSocketState();
            connection.Buffer = new byte[ClientSocketState.BufferSize];
            connection.Socket = listener;

            ipeSender = new IPEndPoint(IPAddress.Any, 5010);

            listener.BeginReceiveFrom(connection.Buffer, 0, connection.Buffer.Length, SocketFlags.None, ref ipeSender, new AsyncCallback(DataReceived), connection);
                
        }

        public void Stop()
        {
            try
            {
                listener.Close();
            }
            catch (Exception ex) { }
        }

        internal void ClientConnected(IAsyncResult asyncResult)
        {
            ClientSocketState connection = new ClientSocketState();
            connection.Buffer = new byte[ClientSocketState.BufferSize];
      
            Socket asyncListener = (Socket)asyncResult.AsyncState;
            Socket asyncClient = asyncListener.EndAccept(asyncResult);

            // Set the SocketConnectionInformations socket to the current client
            connection.Socket = asyncClient;
          
            asyncClient.BeginReceiveFrom(connection.Buffer, 0, connection.Buffer.Length, SocketFlags.None, ref ipeSender, new AsyncCallback(DataReceived), connection);
            
            listener.BeginAccept(new AsyncCallback(ClientConnected), listener);
        }

        internal void DataReceived(IAsyncResult asyncResult)
        {
            try
            {
                ClientSocketState connection = (ClientSocketState)asyncResult.AsyncState;
                int bytesRead = 0;
                try
                {
                    bytesRead = connection.Socket.EndReceiveFrom(asyncResult, ref ipeSender);

                }
                catch (Exception ex) { }

                byte[] copy = new byte[bytesRead];

                if (bytesRead >0 )
                {
                    Buffer.BlockCopy(connection.Buffer, 0, copy, 0, bytesRead);
                        
                }

                //ready to recieve again
                connection.Socket.BeginReceiveFrom(connection.Buffer, 0, connection.Buffer.Length, SocketFlags.None, ref ipeSender, new AsyncCallback(DataReceived), connection);

                // If we have read no more bytes, raise the data received event
                if (bytesRead == 0 || (bytesRead > 0 && bytesRead < ClientSocketState.BufferSize))
                {
                    byte[] buffer = connection.Buffer;
                    Int32 totalBytesRead = connection.BytesRead;
                    // Setup the connection info again ready for another packet
                    connection = new ClientSocketState();
                    connection.Buffer = new byte[ClientSocketState.BufferSize];
                    connection.Socket = ((ClientSocketState)asyncResult.AsyncState).Socket;
                    // Fire off the receive event as quickly as possible, then we can process the data...
                      
                    connection.Socket.BeginReceiveFrom(connection.Buffer, 0, connection.Buffer.Length, SocketFlags.None, ref ipeSender, new AsyncCallback(DataReceived), connection);
                        
                  
              
                
                }
                   
                
            
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

    
    }

    public class ClientSocketState
    {
        public const Int32 BufferSize = 2048;
        public Socket Socket;
        public byte[] Buffer;
        public Int32 BytesRead { get; set; }
    }


}