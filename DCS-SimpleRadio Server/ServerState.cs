using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    public class ServerState:IHandle<StartServerMessage>, IHandle<StopServerMessage>, IHandle<KickClientMessage>, IHandle<BanClientMessage>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IEventAggregator _eventAggregator;
        private UDPVoiceRouter _serverListener;
        private ServerSync _serverSync;

        private readonly HashSet<IPAddress> _bannedIps = new HashSet<IPAddress>();

        private readonly ConcurrentDictionary<string, SRClient> _connectedClients =
            new ConcurrentDictionary<string, SRClient>();

        public ServerState(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
          

            StartServer();
        }

        private void PopulateBanList()
        {
            try
            {
                _bannedIps.Clear();
                var lines = File.ReadAllLines(GetCurrentDirectory() + "\\banned.txt");

                foreach (var line in lines)
                {
                    IPAddress ip = null;
                    if (IPAddress.TryParse(line.Trim(), out ip))
                    {
                        Logger.Info("Loaded Banned IP: " + line);
                        _bannedIps.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to read banned.txt");
            }
        }

        private static string GetCurrentDirectory()
        {
            //To get the location the assembly normally resides on disk or the install directory
            var currentPath = Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            var currentDirectory = Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }

            return currentDirectory;
        }

        public void Handle(StartServerMessage message)
        { 
            StartServer();
           _eventAggregator.PublishOnUIThread(new ServerStateMessage(true, new List<SRClient>(_connectedClients.Values)));
        }

        public void Handle(StopServerMessage message)
        {
            StopServer();
            _eventAggregator.PublishOnUIThread(new ServerStateMessage(false, new List<SRClient>(_connectedClients.Values)));
        }

        private void StartServer()
        {
            if (_serverListener == null)
            {
                PopulateBanList();

                _serverListener = new UDPVoiceRouter(_connectedClients, _eventAggregator);
                var listenerThread = new Thread(_serverListener.Listen);
                listenerThread.Start();

                _serverSync = new ServerSync(_connectedClients, _bannedIps, _eventAggregator);
                var serverSyncThread = new Thread(_serverSync.StartListening);
                serverSyncThread.Start();
            }
        }

        public void StopServer()
        {
            if (_serverListener != null)
            {
                _serverSync.RequestStop();
                _serverSync = null;
                _serverListener.RequestStop();
                _serverListener = null;
            }
        }

        public void Handle(KickClientMessage message)
        {
            var client = message.Client;
            KickClient(client);
        }

        private void KickClient(SRClient client)
        {
            if (client != null)
            {
                try
                {
                    client.ClientSocket.Disconnect(false);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "Error kicking client");
                }
            }
        }

        public void Handle(BanClientMessage message)
        {
            WriteBanIP(message.Client);

            KickClient(message.Client);
        }

        private void WriteBanIP(SRClient client)
        {
            try
            {
                var remoteIpEndPoint = client.ClientSocket.RemoteEndPoint as IPEndPoint;

                _bannedIps.Add(remoteIpEndPoint.Address);

                File.AppendAllText(GetCurrentDirectory() + "\\banned.txt",
                    remoteIpEndPoint.Address + "\r\n");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error saving banned client");
            }
        }

    }
}
