using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    // State object for reading client data asynchronously
    public class StateObject
    {
        // Size of receive buffer.
        public const int BufferSize = 1024;
        // Receive buffer.
        public byte[] buffer = new byte[BufferSize];

        public string guid;
        // Received data string.
        public StringBuilder sb = new StringBuilder();
        // Client  socket.
        public Socket workSocket;
    }

    internal class ServerSync
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        // Thread signal.
        public static ManualResetEvent _allDone = new ManualResetEvent(false);
        private readonly HashSet<IPAddress> _bannedIps;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();

        private Socket listener;

        public ServerSync(ConcurrentDictionary<string, SRClient> connectedClients, HashSet<IPAddress> _bannedIps)
        {
            _clients = connectedClients;
            this._bannedIps = _bannedIps;
        }

        public void StartListening()
        {
            var ipAddress = new IPAddress(0);
            var localEndPoint = new IPEndPoint(ipAddress, 5002);

            // Create a TCP/IP socket.
            listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);

                while (true)
                {
                    // Set the event to nonsignaled state.
                    _allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    _logger.Info("Waiting for a connection...");
                    listener.BeginAccept(
                        AcceptCallback,
                        listener);

                    // Wait until a connection is made before continuing.
                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Server Listen error");
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            _allDone.Set();

            try
            {
                // Get the socket that handles the client request.
                var listener = (Socket) ar.AsyncState;
                var handler = listener.EndAccept(ar);
                handler.NoDelay = true;

                // Create the state object.
                var state = new StateObject();
                state.workSocket = handler;
                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    ReadCallback, state);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error accepting socket");
            }
        }

        public void HandleDisconnect(StateObject state)
        {
            _logger.Info("Disconnecting Client");

            if (state != null && state.guid != null)
            {
                //removed
                SRClient client;
                _clients.TryRemove(state.guid, out client);

                if (client != null)
                {
                    _logger.Info("Removed Client " + state.guid);
                }
            }

            try
            {
                state.workSocket.Close();
            }
            catch (Exception ex)
            {
                _logger.Info(ex, "Exception closing socket after disconnect");
            }
        }

        public void ReadCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            var state = (StateObject) ar.AsyncState;
            var handler = state.workSocket;

            try
            {
                // Read data from the client socket. 
                var bytesRead = handler.EndReceive(ar);

                if (bytesRead > 0)
                {
                    // There  might be more data, so store the data received so far.
                    state.sb.Append(Encoding.ASCII.GetString(
                        state.buffer, 0, bytesRead));

                    var content = state.sb.ToString();
                    if (content.EndsWith("\n"))
                    {
                        //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}",
                        //   content.Length, content);

                        try
                        {
                            var message = JsonConvert.DeserializeObject<NetworkMessage>(content);

                            HandleMessage(state, message);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Server - Client Exception reading");
                        }

                        //clear the state buffer
                        state.sb.Clear();

                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            ReadCallback, state);
                    }
                    else
                    {
                        // Not all data received. Get more.
                        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                            ReadCallback, state);
                    }
                }
                else
                {
                    HandleDisconnect(state);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error reading from socket. Disconnecting ");

                HandleDisconnect(state);
            }
        }

        public void HandleMessage(StateObject state, NetworkMessage message)
        {
            try
            {
                var clientIp = (IPEndPoint) state.workSocket.RemoteEndPoint;

                if (_bannedIps.Contains(clientIp.Address))
                {
                    state.workSocket.Disconnect(true);

                    _logger.Warn("Disconnecting Banned Client -  " + clientIp.Address + " " + clientIp.Port);
                    return;
                }

                //  logger.Info("Received From " + clientIp.Address + " " + clientIp.Port);
                // logger.Info("Recevied: " + message.MsgType);

                switch (message.MsgType)
                {
                    //synonymous for now
                    case NetworkMessage.MessageType.PING:
                        // Do nothing for now
                        break;
                    case NetworkMessage.MessageType.UPDATE:
                        HandleRadioUpdate(message);
                        break;
                    case NetworkMessage.MessageType.SYNC:

                        var srClient = message.Client;
                        if (!_clients.ContainsKey(srClient.ClientGuid))
                        {
                            srClient.ClientSocket = state.workSocket;

                            //add to proper list
                            _clients[srClient.ClientGuid] = srClient;

                            state.guid = srClient.ClientGuid;
                        }

                        HandleRadioSync(clientIp, state.workSocket, message);

                        break;
                    default:
                        _logger.Warn("Recevied unknown message type");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception Handling Message " + ex.Message);
            }
        }

        private void HandleRadioUpdate(NetworkMessage message)
        {
            if (_clients.ContainsKey(message.Client.ClientGuid))
            {
                var client = _clients[message.Client.ClientGuid];

                if (client != null)
                {
                    client.LastUpdate = Environment.TickCount;
                    client.Name = message.Client.Name;
                    client.Coalition = message.Client.Coalition;
                    //send update to everyone

                    var replyMessage = new NetworkMessage();
                    replyMessage.MsgType = NetworkMessage.MessageType.UPDATE;
                    replyMessage.Client = client;

                    foreach (var clientToSent in _clients)
                    {
                        Send(clientToSent.Value.ClientSocket, replyMessage);
                    }
                }
            }
        }

        private void HandleRadioSync(IPEndPoint clientIp, Socket clientSocket, NetworkMessage message)
        {
            //store new client
            var replyMessage = new NetworkMessage();
            replyMessage.MsgType = NetworkMessage.MessageType.SYNC;

            replyMessage.Clients = new List<SRClient>();

            foreach (var clientToSent in _clients)
            {
                replyMessage.Clients.Add(clientToSent.Value);
            }

            Send(clientSocket, replyMessage);
        }

        private static void Send(Socket handler, NetworkMessage message)
        {
            try
            {
                // Convert the string data to byte data using ASCII encoding.
                var byteData = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(message) + "\n");

                // Begin sending the data to the remote device.
                handler.BeginSend(byteData, 0, byteData.Length, 0,
                    SendCallback, handler);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception Sending Message " + ex.Message);
            }
        }

        private static void SendCallback(IAsyncResult ar)
        {
            try
            {
                // Retrieve the socket from the state object.
                var handler = (Socket) ar.AsyncState;

                // Complete sending the data to the remote device.
                var bytesSent = handler.EndSend(ar);
                //  Console.WriteLine("Sent {0} bytes to client.", bytesSent);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Exception SendCallback ");
            }
        }

        public void RequestStop()
        {
            try
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        client.Value.ClientSocket.Close();
                    }
                    catch (Exception ex)
                    {
                    }
                }
                listener.Close();

                _clients.Clear();
            }
            catch (Exception ex)
            {
            }
        }
    }
}