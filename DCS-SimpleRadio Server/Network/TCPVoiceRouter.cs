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
<<<<<<< HEAD
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Handlers.Logging;
=======
using DotNetty.Codecs;
>>>>>>> 6b32cab90359db179f0c214ad5306146f513e9db
using DotNetty.Transport.Bootstrapping;
using DotNetty.Transport.Channels;
using DotNetty.Transport.Channels.Sockets;
using Newtonsoft.Json;
using NLog;
using LogLevel = DotNetty.Handlers.Logging.LogLevel;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    public class TCPVoiceRouter
    {

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;

        private readonly IEventAggregator _eventAggregator;

<<<<<<< HEAD
        private readonly CancellationTokenSource _serverShutdownToken = new CancellationTokenSource();
=======
        private readonly BlockingCollection<OutgoingVoice> _outGoing = new BlockingCollection<OutgoingVoice>();
        private readonly CancellationTokenSource _serverShutdownToken = new CancellationTokenSource();

        private readonly CancellationTokenSource _pendingProcessingCancellationToken = new CancellationTokenSource();

        private readonly BlockingCollection<PendingPacket> _pendingProcessingPackets =
            new BlockingCollection<PendingPacket>();
>>>>>>> 6b32cab90359db179f0c214ad5306146f513e9db

        private readonly ServerSettings _serverSettings = ServerSettings.Instance;

        public TCPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList, IEventAggregator eventAggregator)
        {
            _clientsList = clientsList;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
        }

        public void StartListening()
        {
<<<<<<< HEAD
            TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener INIT");
            var bossGroup = new MultithreadEventLoopGroup();
            var workerGroup = new MultithreadEventLoopGroup();
=======

            var bossGroup = new MultithreadEventLoopGroup();
            var workerGroup = new MultithreadEventLoopGroup();

            try
            {
                var bootstrap = new ServerBootstrap();
                bootstrap
                    .Group(bossGroup, workerGroup)
                    .Channel<TcpServerSocketChannel>()
                    .Option(ChannelOption.SoBacklog, 100)
                    .Handler(new ActionChannelInitializer<ISocketChannel>(channel =>
                    {
                        IChannelPipeline pipeline = channel.Pipeline;
   

                        pipeline.AddLast(new LengthFieldBasedFrameDecoder(1024, 0,2,0,0,true));

                        pipeline.AddLast(new VOIPPacketHandler());
                    }));

                Task<IChannel> bootstrapChannel = bootstrap.BindAsync(_serverSettings.ServerListeningPort() + 1);

                bootstrapChannel.Wait(5000);

                //wait here for shutdown
                _serverShutdownToken.Token.WaitHandle.WaitOne();

                 var task =bootstrapChannel.Result.CloseAsync();

                task.Wait(10000);
            }
            finally
            {
                Task.WaitAll(bossGroup.ShutdownGracefullyAsync(), workerGroup.ShutdownGracefullyAsync());
            }

//            var ipAddress = new IPAddress(0);
//            var localEndPoint = new IPEndPoint(ipAddress, _serverSettings.ServerListeningPort()+1);
//
//            //start threads
//            //packets that need processing
//            new Thread(ProcessPackets).Start();
//            //outgoing packets
//            new Thread(SendPendingPackets).Start();
//
//            // Create a TCP/IP socket.
//            listener = new Socket(AddressFamily.InterNetwork,
//                SocketType.Stream, ProtocolType.Tcp);
//            listener.NoDelay = true;
//
//            // Bind the socket to the local endpoint and listen for incoming connections.
//            try
//            {
//                listener.Bind(localEndPoint);
//                listener.Listen(100);
//                listener.NoDelay = true;
//                while (true)
//                {
//                    // Set the event to nonsignaled state.
//                    _allDone.Reset();
//
//                    // Start an asynchronous socket to listen for connections.
//                    Logger.Info($"Waiting for a VOIP connection on {_serverSettings.ServerListeningPort()+1}...");
//                    listener.BeginAccept(
//                        AcceptCallback,
//                        listener);
//
//                    // Wait until a connection is made before continuing.
//                    _allDone.WaitOne();
//                }
//            }
//            catch (Exception e)
//            {
//                Logger.Error(e, "Server Listen error: " + e.Message);
//            }
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

                Logger.Info("Disconnected Client "+state.ClientGUID);
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
>>>>>>> 6b32cab90359db179f0c214ad5306146f513e9db

            try
            {
                var bootstrap = new ServerBootstrap();
                bootstrap
                    .Group(bossGroup, workerGroup)
                    .Channel<TcpServerSocketChannel>()
                    .Option(ChannelOption.SoBacklog, 100)
                    .Option(ChannelOption.TcpNodelay, true)
                    .Handler(new LoggingHandler(LogLevel.ERROR))
                    .ChildHandler(new ActionChannelInitializer<ISocketChannel>(channel =>
                    {
                        IChannelPipeline pipeline = channel.Pipeline;

                        pipeline.AddLast(new LengthFieldBasedFrameDecoder(ByteOrder.LittleEndian, 1024, 0, 2, -2, 0,
                            true));
                        pipeline.AddLast(new VOIPPacketHandler(_clientsList));

                    })).ChildOption(ChannelOption.TcpNodelay, true);

                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Binding to "+ _serverSettings.ServerListeningPort() + 1);
                Task<IChannel> bootstrapChannel = bootstrap.BindAsync(_serverSettings.ServerListeningPort() + 1);

                bootstrapChannel.Wait(5000);


                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Bound to " + _serverSettings.ServerListeningPort() + 1);

                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Waiting for Shutdown...");
                //wait here for shutdown
                _serverShutdownToken.Token.WaitHandle.WaitOne();
                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Waiting for Shutting down...");

                var task = bootstrapChannel.Result.CloseAsync();

                task.Wait(10000);
            }
            finally
            {
                Task.WaitAll(bossGroup.ShutdownGracefullyAsync(), workerGroup.ShutdownGracefullyAsync());
            }

            TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Shutdown");
        }

        public void RequestStop()
        {
            try
            {
<<<<<<< HEAD
                _serverShutdownToken.Cancel();
                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Shutdown Requested");
=======

                _serverShutdownToken.Cancel();

                _pendingProcessingCancellationToken.Cancel();

                foreach (var client in _voipClients)
                {
                    try
                    {
                        client.Value.WorkSocket.Close();
                    }
                    catch (Exception ex)
                    {
                    }
                }
                listener.Close();

                _voipClients.Clear();
>>>>>>> 6b32cab90359db179f0c214ad5306146f513e9db
            }
            catch (Exception ex)
            {
            }
        }
    }


}
