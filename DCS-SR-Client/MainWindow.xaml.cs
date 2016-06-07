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
using GitHubUpdate;
using System.Collections.Concurrent;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        ClientSync _client;

        String _guid;

        InputDeviceManager _inputManager;

        AudioManager _audioManager;

        ConcurrentDictionary<string, Common.SRClient> _clients = new ConcurrentDictionary<string, Common.SRClient>();

        AppConfiguration _appConfig;
        AudioPreview _audioPreview;

        IPAddress _resolvedIP;

        bool _stop = true;

        public MainWindow()
        {
            InitializeComponent();

            SetupLogging();

            _appConfig = new AppConfiguration();
            _guid = Guid.NewGuid().ToString();

            InitAudioInput();

            InitAudioOutput();

            _inputManager = new InputDeviceManager(this);

            LoadInputSettings();

            this.serverIp.Text = _appConfig.LastServer;
            this.microphoneBoost.Value = _appConfig.MicBoost;
            //   this.boostAmount.Content = "Boost: " + this.microphoneBoost.Value;
            _audioManager = new AudioManager(_clients);
            _audioManager.Volume = (float)this.microphoneBoost.Value;

            CheckForUpdate();

        }



        private void InitAudioInput()
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
            }

            if (WaveIn.DeviceCount >= _appConfig.AudioInputDeviceId && WaveIn.DeviceCount > 0)
            {
                mic.SelectedIndex = _appConfig.AudioInputDeviceId;
            }
            else if(WaveIn.DeviceCount > 0)
            {
                mic.SelectedIndex = 0;
            }
        }

        private void InitAudioOutput()
        {
            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                speakers.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            }

            if (WaveOut.DeviceCount >= _appConfig.AudioOutputDeviceId && WaveOut.DeviceCount > 0)
            {
                speakers.SelectedIndex = _appConfig.AudioOutputDeviceId;
            }
            else if (WaveOut.DeviceCount > 0)
            {
                speakers.SelectedIndex = 0;
            }
     
        }

        private async void CheckForUpdate()
        {
            try
            {
                var checker = new UpdateChecker("ciribob", "DCS-SimpleRadioStandalone"); // uses Application.ProductVersion

                UpdateType update = await checker.CheckUpdate();

                if (update == UpdateType.None)
                {
                    // Up to date!
                }
                else
                {
                    MessageBoxResult result = MessageBox.Show("New Version Available!\n\nDo you want to Update?", "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    // Process message box results
                    switch (result)
                    {
                        case MessageBoxResult.Yes:
                            checker.DownloadAsset("DCS-SR-Standalone.zip");
                            break;
                        case MessageBoxResult.No:

                            break;
                    }
                }
            }
            catch (Exception ex) { }
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

            if (_inputManager.InputConfig.inputDevices != null)
            {
                if (_inputManager.InputConfig.inputDevices[0] != null)
                {
                    pttCommonText.Text = _inputManager.InputConfig.inputDevices[0].Button.ToString();
                    pttCommonDevice.Text = _inputManager.InputConfig.inputDevices[0].DeviceName;
                }


                if (_inputManager.InputConfig.inputDevices[1] != null)
                {
                    ptt1Text.Text = _inputManager.InputConfig.inputDevices[1].Button.ToString();
                    ptt1Device.Text = _inputManager.InputConfig.inputDevices[1].DeviceName;
                }
                if (_inputManager.InputConfig.inputDevices[2] != null)
                {
                    ptt2Text.Text = _inputManager.InputConfig.inputDevices[2].Button.ToString();
                    ptt2Device.Text = _inputManager.InputConfig.inputDevices[2].DeviceName;
                }

                if (_inputManager.InputConfig.inputDevices[3] != null)
                {
                    ptt3Text.Text = _inputManager.InputConfig.inputDevices[3].Button.ToString();
                    ptt3Device.Text = _inputManager.InputConfig.inputDevices[3].DeviceName;
                }
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
                try
                {
                    //process hostname
                    IPAddress[] ipAddr = Dns.GetHostAddresses(this.serverIp.Text.Trim());

                    if (ipAddr.Length > 0)
                    {
                        this._resolvedIP = ipAddr[0];

                        _client = new ClientSync(_clients, _guid);
                        _client.TryConnect(new IPEndPoint(_resolvedIP, 5002), ConnectCallback);

                        startStop.Content = "Connecting...";
                        startStop.IsEnabled = false;
                        mic.IsEnabled = false;
                        speakers.IsEnabled = false;

                    }
                    else
                    {
                        //invalid ID
                        MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

            }
        }
        private void stop()
        {
            startStop.Content = "Start";
            startStop.IsEnabled = true;
            mic.IsEnabled = true;
            speakers.IsEnabled = true;
            try
            {
                _audioManager.StopEncoding();
            }
            catch (Exception ex) { }

            _stop = true;

            if (_client != null)
            {
                _client.Disconnect();
                _client = null;
            }


        }

        private void ConnectCallback(bool result)
        {
            if (result)
            {
                if (_stop)
                {
                    startStop.Content = "Disconnect";
                    startStop.IsEnabled = true;

                    //save app settings
                    _appConfig.LastServer = this.serverIp.Text.Trim();
                    _appConfig.AudioInputDeviceId = mic.SelectedIndex;
                    _appConfig.AudioOutputDeviceId = speakers.SelectedIndex;

                    _audioManager.StartEncoding(mic.SelectedIndex, speakers.SelectedIndex, _guid, _inputManager, _resolvedIP);
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

            if (_audioPreview != null)
            {
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }

        private void pttCommonButton_Click(object sender, RoutedEventArgs e)
        {
            pttCommonClear.IsEnabled = false;
            pttCommonButton.IsEnabled = false;


            _inputManager.AssignButton((InputDevice device) =>
            {
                pttCommonClear.IsEnabled = true;
                pttCommonButton.IsEnabled = true;

                pttCommonDevice.Text = device.DeviceName;
                pttCommonText.Text = device.Button.ToString();

                device.InputBind = InputDevice.InputBinding.PTT;

                _inputManager.InputConfig.inputDevices[0] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.PTT, device);

            });

        }

        private void ptt1_Click(object sender, RoutedEventArgs e)
        {
            ptt1ButtonClear.IsEnabled = false;
            ptt1ButtonClear.IsEnabled = false;


            _inputManager.AssignButton((InputDevice device) =>
            {
                ptt1ButtonClear.IsEnabled = true;
                ptt1ButtonClear.IsEnabled = true;

                ptt1Device.Text = device.DeviceName;
                ptt1Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.SWITCH_1;

                _inputManager.InputConfig.inputDevices[1] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.SWITCH_1, device);

            });

        }
        private void ptt2_Click(object sender, RoutedEventArgs e)
        {
            ptt2ButtonClear.IsEnabled = false;
            ptt2ButtonClear.IsEnabled = false;


            _inputManager.AssignButton((InputDevice device) =>
            {
                ptt2ButtonClear.IsEnabled = true;
                ptt2ButtonClear.IsEnabled = true;

                ptt2Device.Text = device.DeviceName;
                ptt2Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.SWITCH_2;

                _inputManager.InputConfig.inputDevices[2] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.SWITCH_2, device);

            });

        }

        private void ptt3_Click(object sender, RoutedEventArgs e)
        {
            ptt3ButtonClear.IsEnabled = false;
            ptt3ButtonClear.IsEnabled = false;


            _inputManager.AssignButton((InputDevice device) =>
            {
                ptt3ButtonClear.IsEnabled = true;
                ptt3ButtonClear.IsEnabled = true;

                ptt3Device.Text = device.DeviceName;
                ptt3Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.SWITCH_3;

                _inputManager.InputConfig.inputDevices[3] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.SWITCH_3, device);

            });

        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                _audioPreview = new AudioPreview();
                _audioPreview.StartPreview(mic.SelectedIndex, speakers.SelectedIndex);
                _audioPreview.Volume.Volume = (float)this.microphoneBoost.Value;
                preview.Content = "Stop Preview";
            }
            else
            {
                preview.Content = "Audio Preview";
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }

        }

        private void MicrophoneBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreview != null)
            {
                _audioPreview.Volume.Volume = (float)this.microphoneBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.Volume = (float)this.microphoneBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.MicBoost = (float)this.microphoneBoost.Value;
            }

        }
    }
}



