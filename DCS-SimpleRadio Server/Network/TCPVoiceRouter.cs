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
using DotNetty.Buffers;
using DotNetty.Codecs;
using DotNetty.Handlers.Logging;
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

        private readonly CancellationTokenSource _serverShutdownToken = new CancellationTokenSource();

        private readonly ServerSettings _serverSettings = ServerSettings.Instance;

        public TCPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList, IEventAggregator eventAggregator)
        {
            _clientsList = clientsList;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
        }

        public void StartListening()
        {
            TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener INIT");
            var bossGroup = new MultithreadEventLoopGroup();
            var workerGroup = new MultithreadEventLoopGroup();

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
                _serverShutdownToken.Cancel();
                TCPVoiceRouter.Logger.Log(NLog.LogLevel.Info, "VOIP Listener Shutdown Requested");
            }
            catch (Exception ex)
            {
            }
        }
    }


}
