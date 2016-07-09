using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly HashSet<IPAddress> _bannedIps = new HashSet<IPAddress>();

        private readonly ConcurrentDictionary<string, SRClient> _connectedClients =
            new ConcurrentDictionary<string, SRClient>();

        private UDPVoiceRouter _serverListener;

        private ServerSync _serverSync;

        private volatile bool _stop = false;

        private ServerSettings settings = ServerSettings.Instance;

        public MainWindow()
        {
            InitializeComponent();

            SetupLogging();

            PopulateBanList();

            StartClientList();

            StartServer();

            button.Content = "Stop Server";

            UpdaterChecker.CheckForUpdate();
        }

        private void PopulateBanList()
        {
            try
            {
                var lines = File.ReadAllLines(GetCurrentDirectory() + "\\banned.txt");

                foreach (var line in lines)
                {
                    IPAddress ip = null;
                    if (IPAddress.TryParse(line.Trim(), out ip))
                    {
                        _logger.Info("Loaded Banned IP: " + line);
                        _bannedIps.Add(ip);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to read banned.txt");
            }
        }

        private void StartClientList()
        {
            Task.Run(() =>
            {
                while (!_stop)
                {
                    Application.Current.Dispatcher.Invoke(() =>

                    {
                        try
                        {
                            clientsCount.Content = _connectedClients.Count().ToString();
                        }
                        catch (Exception ex)
                        {
                        }
                    });

                    Thread.Sleep(1000);
                }
            });
        }

        private void SetupLogging()
        {
            var config = new LoggingConfiguration();

            var consoleTarget = new ColoredConsoleTarget();
            consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
            config.AddTarget("console", consoleTarget);

            var fileTarget = new FileTarget();
            fileTarget.FileName = "${basedir}/serverlog.txt";
            fileTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
            config.AddTarget("file", fileTarget);

            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Info, fileTarget);
            config.LoggingRules.Add(rule2);

            LogManager.Configuration = config;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (_serverListener != null)
            {
                stopServer();
                button.Content = "Start Server";
            }
            else
            {
                button.Content = "Stop Server";
                StartServer();
            }
        }

        private void StartServer()
        {
            _serverListener = new UDPVoiceRouter(_connectedClients);
            var listenerThread = new Thread(_serverListener.Listen);
            listenerThread.Start();


            _serverSync = new ServerSync(_connectedClients, _bannedIps);
            var serverSyncThread = new Thread(_serverSync.StartListening);
            serverSyncThread.Start();
        }

        private void stopServer()
        {
            if (_serverListener != null)
            {
                _serverSync.RequestStop();
                _serverSync = null;
                _serverListener.RequestStop();
                _serverListener = null;
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            stopServer();
        }

        private void ClientList_Click(object sender, RoutedEventArgs e)
        {
            var cw = new ClientAdminWindow(_connectedClients, _bannedIps);
            cw.ShowInTaskbar = false;
            cw.Owner = Application.Current.MainWindow;
            cw.Show();
        }


        public static string GetCurrentDirectory()
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
    }
}