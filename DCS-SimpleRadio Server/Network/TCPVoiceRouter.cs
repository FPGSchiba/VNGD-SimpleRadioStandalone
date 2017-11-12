using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    // State object for reading client data asynchronously
    public class ClientStateObject
    {
        // Size of receive buffer.
        public const int BufferSize = 4096;
        
        // Receive buffer.
        public byte[] ByteBuffer { get; } = new byte[BufferSize];

        public string ClientGUID { get; set; }

        // Client  socket.

        public Socket WorkSocket { get; set; }

        public ushort CurrentPacketLength { get; set; }
        public int CurrentBufferOffset { get; set; }
    }


    public class TCPVoiceRouter
    {

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;

        private readonly ConcurrentDictionary<string, ClientStateObject> _voipClients = new ConcurrentDictionary<string, ClientStateObject>();

        private readonly IEventAggregator _eventAggregator;

        private readonly BlockingCollection<OutgoingVoice> _outGoing = new BlockingCollection<OutgoingVoice>();
        private readonly CancellationTokenSource _outgoingCancellationToken = new CancellationTokenSource();

        private readonly CancellationTokenSource _pendingProcessingCancellationToken = new CancellationTokenSource();

        private readonly BlockingCollection<PendingPacket> _pendingProcessingPackets =
            new BlockingCollection<PendingPacket>();

        private readonly ServerSettings _serverSettings = ServerSettings.Instance;

        private Socket listener;

        private volatile bool _stop;

        public static ManualResetEvent _allDone = new ManualResetEvent(false);

        public TCPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList, IEventAggregator eventAggregator)
        {
            _clientsList = clientsList;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
        }

        public void StartListening()
        {
            var ipAddress = new IPAddress(0);
            var localEndPoint = new IPEndPoint(ipAddress, _serverSettings.ServerListeningPort()+1);

            //start threads
            //packets that need processing
            new Thread(ProcessPackets).Start();
            //outgoing packets
            new Thread(SendPendingPackets).Start();

            // Create a TCP/IP socket.
            listener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            listener.NoDelay = true;

            // Bind the socket to the local endpoint and listen for incoming connections.
            try
            {
                listener.Bind(localEndPoint);
                listener.Listen(100);
                listener.NoDelay = true;
                while (true)
                {
                    // Set the event to nonsignaled state.
                    _allDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    Logger.Info($"Waiting for a VOIP connection on {_serverSettings.ServerListeningPort()+1}...");
                    listener.BeginAccept(
                        AcceptCallback,
                        listener);

                    // Wait until a connection is made before continuing.
                    _allDone.WaitOne();
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, "Server Listen error: " + e.Message);
            }
        }

        public void AcceptCallback(IAsyncResult ar)
        {
            // Signal the main thread to continue.
            _allDone.Set();

            try
            {
                // Get the socket that handles the client request.
                var listener = (Socket)ar.AsyncState;
                var handler = listener.EndAccept(ar);
                handler.NoDelay = true;

                // Create the state object.
                var state = new ClientStateObject();
                state.WorkSocket = handler;

                handler.BeginReceive(state.ByteBuffer, 0, 22, 0,
                    ReadCallback, state);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error accepting socket");
            }
        }

        public void HandleDisconnect(ClientStateObject state)
        {
            Logger.Info("Disconnecting Client");

            if ((state != null) && (state.ClientGUID != null))
            {
                 ClientStateObject returned;
                _voipClients.TryRemove(state.ClientGUID,out returned);
            }

            try
            {
                state.WorkSocket.Close();
            }
            catch (Exception ex)
            {
                Logger.Info(ex, "Exception closing socket after disconnect");
            }
        }

        public void ReadCallback(IAsyncResult ar)
        {
            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            var state = (ClientStateObject)ar.AsyncState;
            var handler = state.WorkSocket;

            try
            {
                // Read data from the client socket. 
                var bytesRead = handler.EndReceive(ar);

                //first message is GUID followed by arbitrary bytes
                if (state.ClientGUID == null && bytesRead >=22 )
                {
                    //read 22 bytes for GUID
                    var guid = Encoding.ASCII.GetString(state.ByteBuffer, 0, 22);
                    state.ClientGUID = guid;

                    //clean up buffer
                    Buffer.BlockCopy(state.ByteBuffer, 22, state.ByteBuffer, 0, bytesRead - 22);
                    state.CurrentBufferOffset += (bytesRead - 22);
                    
                    handler.BeginReceive(state.ByteBuffer, 0, StateObject.BufferSize, 0,
                        ReadCallback, state);
                }
                else if (bytesRead > 0)
                {
                    //first add to length offset
                    state.CurrentBufferOffset += bytesRead;

                    if (state.CurrentBufferOffset >= 2)
                    {
                        //read the first 2 bytes
                        int voipLength = BitConverter.ToInt16(state.ByteBuffer, 0);

                        //- 2 to discount length 
                        if (state.CurrentBufferOffset-2 >= voipLength)
                        {
                            //great generate packet
                            byte[] voipBytes = new byte[voipLength];

                            //offset by two to skip length
                            Buffer.BlockCopy(state.ByteBuffer, 2, voipBytes, 0,voipLength);

                            //horray voip bytes -- decode!
                            HandleMessage(state,voipBytes);

                            //reset buffers

                            //clean up buffer -- shift rest of buffer back overwriting removed bytes 
                            state.CurrentBufferOffset = state.CurrentBufferOffset - (voipLength + 2);
                            Buffer.BlockCopy(state.ByteBuffer, voipLength+2, state.ByteBuffer, 0, state.CurrentBufferOffset);
                        }

                    }
                  
                    //continue receiving more
                    handler.BeginReceive(state.ByteBuffer, state.CurrentBufferOffset, StateObject.BufferSize, SocketFlags.None,
                        ReadCallback, state);
                }
                else
                {
                    HandleDisconnect(state);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reading from socket. Disconnecting ");

                HandleDisconnect(state);
            }
        }

        public void HandleMessage(ClientStateObject state, byte[] voip)
        {
            _pendingProcessingPackets.Add(new PendingPacket()
            {
                RawBytes = voip,
                ReceivedFrom = state.ClientGUID,
                ConnectedClient = state.WorkSocket
            });
        }


        private void ProcessPackets()
        {
            while (!_stop)
                try
                {
                    PendingPacket udpPacket = null;
                    _pendingProcessingPackets.TryTake(out udpPacket, 100000, _pendingProcessingCancellationToken.Token);

                    if (udpPacket != null)
                    {
                        //last 36 bytes are guid!
                        var guid = Encoding.ASCII.GetString(
                            udpPacket.RawBytes, udpPacket.RawBytes.Length - 22, 22);

                        if (_clientsList.ContainsKey(guid))
                        {
                            var client = _clientsList[guid];
                            client.VoipPort = udpPacket.ConnectedClient;

                            var spectatorAudioDisabled =
                                _serverSettings.ServerSetting[(int)ServerSettingType.SPECTATORS_AUDIO_DISABLED];

                            if ((client.Coalition == 0) && spectatorAudioDisabled)
                            {
                                // IGNORE THE AUDIO
                            }
                            else
                            {
                                try
                                {
                                    //decode
                                    var udpVoicePacket = UDPVoicePacket.DecodeVoicePacket(udpPacket.RawBytes);

                                    if ((udpVoicePacket != null) && (udpVoicePacket.Modulation != 4))
                                    //magical ignore message 4
                                    {
                                        var outgoingVoice = GenerateOutgoingPacket(udpVoicePacket, udpPacket, client);

                                        if (outgoingVoice != null)
                                            _outGoing.Add(outgoingVoice);
                                    }
                                }
                                catch (Exception)
                                {
                                    //Hide for now, slows down loop to much....
                                }
                            }
                        }
                        else
                        {
                            SRClient value;
                            _clientsList.TryRemove(guid, out value);
                            //  logger.Info("Removing  "+guid+" From UDP pool");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info("Failed to Process UDP Packet: " + ex.Message);
                }
        }

        private OutgoingVoice GenerateOutgoingPacket(UDPVoicePacket udpVoice, PendingPacket pendingPacket,
         SRClient fromClient)
        {
            var outgoingList = new List<Socket>();
            var outgoingVoice = new OutgoingVoice
            {
                OutgoingEndPoints = outgoingList,
                ReceivedPacket = pendingPacket.RawBytes
            };

            var coalitionSecurity =
                _serverSettings.ServerSetting[(int)ServerSettingType.COALITION_AUDIO_SECURITY];
            var guid = fromClient.ClientGuid;

            foreach (var client in _clientsList)
            {
                if (!client.Key.Equals(guid))
                {
                    var ip = client.Value.VoipPort;

                    // check that either coalition radio security is disabled OR the coalitions match
                    if ((ip != null) && (!coalitionSecurity || (client.Value.Coalition == fromClient.Coalition)))
                    {
                        var radioInfo = client.Value.RadioInfo;

                        if (radioInfo != null)
                        {
                            RadioReceivingState radioReceivingState = null;
                            var receivingRadio = radioInfo.CanHearTransmission(udpVoice.Frequency,
                                (RadioInformation.Modulation)udpVoice.Modulation,
                                udpVoice.UnitId, out radioReceivingState);

                            //only send if we can hear!
                            if (receivingRadio != null)
                                outgoingList.Add(ip);
                        }
                    }
                }
                else
                {
                    var ip = client.Value.VoipPort;

                    if (ip != null)
                    {
                         outgoingList.Add(ip);
                    }
                }
            }

            return outgoingList.Count > 0 ? outgoingVoice : null;
        }

        private
            void SendPendingPackets()
        {
            //_listener.Send(bytes, bytes.Length, ip);
            while (!_stop)
                try
                {
                    OutgoingVoice udpPacket = null;
                    _outGoing.TryTake(out udpPacket, 100000, _pendingProcessingCancellationToken.Token);

                    if (udpPacket != null)
                    {
                        var bytes = udpPacket.ReceivedPacket;
                        var bytesLength = bytes.Length;
                        foreach (var outgoingEndPoint in udpPacket.OutgoingEndPoints)
                        {
                            try
                            {
                                outgoingEndPoint.Send(BitConverter.GetBytes(Convert.ToUInt16(bytesLength)));
                               
                                outgoingEndPoint.Send(bytes, bytesLength, SocketFlags.None);
                            }
                            catch (Exception ex)
                            {
                                //dont log, slows down too much...
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info("Error processing Sending Queue UDP Packet: " + ex.Message);
                }
        }

        public void RequestStop()
        {
            try
            {
//                foreach (var client in _clients)
//                {
//                    try
//                    {
//                        client.Value.ClientSocket.Close();
//                    }
//                    catch (Exception ex)
//                    {
//                    }
//                }
                listener.Close();

          //      _clients.Clear();
            }
            catch (Exception ex)
            {
            }
        }
    }


}
