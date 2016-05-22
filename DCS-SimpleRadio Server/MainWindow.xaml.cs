using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using NLog.Config;
using NLog.Targets;
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

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();
        UDPVoiceRouter serverListener;

        ConcurrentDictionary<String, SRClient> connectedClients = new ConcurrentDictionary<string, SRClient>();
        SRClient[] clientList = new SRClient[1024 * 1024];
        private ServerSync serverSync;

        volatile bool stop = false;

        public MainWindow()
        {
            InitializeComponent();

            SetupLogging();

            StartClientList();

        }

        private void StartClientList()
        {
            Task.Run(() =>
            {
                while (!stop)
                {
                    Application.Current.Dispatcher.Invoke(new Action(() =>

                    {

                        try
                        {

                            this.clientsCount.Content = connectedClients.Count().ToString();


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

            // Step 1. Create configuration object 
            var config = new LoggingConfiguration();

            // Step 2. Create targets and add them to the configuration 
            var consoleTarget = new ColoredConsoleTarget();
            config.AddTarget("console", consoleTarget);

            var fileTarget = new FileTarget();
            config.AddTarget("file", fileTarget);

            // Step 3. Set target properties 
            consoleTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";
            fileTarget.FileName = "${basedir}/serverlog.txt";
            fileTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";

            // Step 4. Define rules
            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule2);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (serverListener != null)
            {
                stopServer();
                button.Content = "Start Server";
            }
            else
            {

                button.Content = "Stop Server";
                serverListener = new UDPVoiceRouter(connectedClients, clientList);
                Thread listenerThread = new Thread(serverListener.Listen);
                listenerThread.Start();


                serverSync = new ServerSync(connectedClients, clientList);
                Thread serverSyncThread = new Thread(serverSync.StartListening);
                serverSyncThread.Start();
            }
        }
        private void stopServer()
        {
            if (serverListener != null)
            {
                serverSync.RequestStop();
                serverSync = null;
                serverListener.RequestStop();
                serverListener = null;

            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            stopServer();

        }
    }






}
