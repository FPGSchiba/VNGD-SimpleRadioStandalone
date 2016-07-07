using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using NLog.Config;
using NLog.Targets;
using NBug;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        UDPVoiceRouter _serverListener;

        ConcurrentDictionary<String, SRClient> _connectedClients = new ConcurrentDictionary<string, SRClient>();

        private HashSet<IPAddress> _bannedIps = new HashSet<IPAddress>();

        private ServerSync _serverSync;

        ServerSettings settings = ServerSettings.Instance;

        volatile bool _stop = false;

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
                string[] lines =  File.ReadAllLines(GetCurrentDirectory() + "\\banned.txt");

                foreach(var line in lines)
                {
                    IPAddress ip = null;
                    if(IPAddress.TryParse(line.Trim(), out ip))
                    {
                        _logger.Info("Loaded Banned IP: "+line);
                        _bannedIps.Add(ip);

                    }
                }

            }
            catch(Exception ex)
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
                    System.Windows.Application.Current.Dispatcher.Invoke(new Action(() =>

              {
                    try
                    {
                        this.clientsCount.Content = _connectedClients.Count().ToString();
                    }
                    catch (Exception ex)
                    {
                    }
                }));

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
            Thread listenerThread = new Thread(_serverListener.Listen);
            listenerThread.Start();


            _serverSync = new ServerSync(_connectedClients,_bannedIps);
            Thread serverSyncThread = new Thread(_serverSync.StartListening);
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
            ClientAdminWindow cw = new ClientAdminWindow(_connectedClients, _bannedIps);
            cw.ShowInTaskbar = false;
            cw.Owner = Application.Current.MainWindow;
            cw.Show();
        }



        public static string GetCurrentDirectory()
        {
            //To get the location the assembly normally resides on disk or the install directory
            var currentPath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            var currentDirectory = System.IO.Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }

            return currentDirectory;

        }
    }
}