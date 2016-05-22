using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using NAudio.Wave;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;
using FragLabs.Audio.Codecs;
using System.Threading;
using NLog.Config;
using NLog.Targets;
using NLog;
using System.ComponentModel;
using SharpDX.DirectInput;
using SharpDX.Multimedia;
using NAudio.Wave.SampleProviders;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        ClientSync client;

        String guid;

        InputDeviceManager inputManager;

        AudioManager audioManager;

        System.Collections.Concurrent.ConcurrentDictionary<string, Common.SRClient> clients = new System.Collections.Concurrent.ConcurrentDictionary<string, Common.SRClient>();

        bool _stop = true;

        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
            }
            if (WaveIn.DeviceCount > 0)
                mic.SelectedIndex = 0;
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                speakers.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            }
            if (WaveOut.DeviceCount > 0)
                speakers.SelectedIndex = 0;

            guid = Guid.NewGuid().ToString();
            SetupLogging();
           

            inputManager = new InputDeviceManager(this);
            LoadInputSettings();

            audioManager = new AudioManager(clients);

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
            fileTarget.FileName = "${basedir}/clientlog.txt";
            fileTarget.Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}";

            // Step 4. Define rules
            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Debug, fileTarget);
            config.LoggingRules.Add(rule2);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        void LoadInputSettings()
        {
            //TODO load input settings

            if(inputManager.InputConfig.PTTCommon !=null)
            {
                pttCommonText.Text = inputManager.InputConfig.PTTCommon.Button.ToString();
                pttCommonDevice.Text = inputManager.InputConfig.PTTCommon.DeviceName;
            }
           
        }



        private void startStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_stop)
            {
                stop();
            }
            else
            {
                IPAddress ipAddr;

                if (IPAddress.TryParse(this.serverIp.Text.Trim(), out ipAddr))
                {
                    client = new ClientSync(clients, guid);
                    client.TryConnect(new IPEndPoint(ipAddr, 5002), ConnectCallback);
                   

                    startStop.Content = "Connecting...";

                }
                else
                {
                    //invalid ID

                }

            }
        }
        private void stop()
        {
            startStop.Content = "Start";
            try
            {
                audioManager.StopEncoding();
            }
            catch (Exception ex) { }

            _stop = true;

            if (client != null)
            {
                client.Disconnect();
                client = null;
            }


        }

        private void ConnectCallback(bool result)
        {
            if (result)
            {
                if (_stop)
                {
                    startStop.Content = "Disconnect";
                   
                        audioManager.StartEncoding(mic.SelectedIndex,speakers.SelectedIndex, guid,inputManager, IPAddress.Parse(this.serverIp.Text.Trim()));
                    _stop = false;
                }

            }
            else
            {
                stop();
            }

        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            stop();

        }

        private void pttCommonButton_Click(object sender, RoutedEventArgs e)
        {
            pttCommonClear.IsEnabled = false;
            pttCommonButton.IsEnabled = false;


            inputManager.AssignButton((InputDevice device) =>
            {
                pttCommonClear.IsEnabled = true;
                pttCommonButton.IsEnabled = true;

                pttCommonDevice.Text = device.DeviceName;
                pttCommonText.Text = device.Button.ToString();

                inputManager.InputConfig.PTTCommon = device;
                inputManager.InputConfig.WriteInputRegistry("common", device);
               
            //    Console.WriteLine(device.Button + " " + device.Device);

                //inputManager.StartDetectPTT(device, (bool pressed) => {
                //    Console.WriteLine("PTT: "+pressed);

                //});

            });

        }
    }
}



