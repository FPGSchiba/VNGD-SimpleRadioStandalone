using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using MahApps.Metro.Controls;
using NAudio.Wave;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private readonly AppConfiguration _appConfig;

        private readonly AudioManager _audioManager;
        private AudioPreview _audioPreview;
        private ClientSync _client;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();

        private readonly string _guid;

        private readonly InputDeviceManager _inputManager;

        private IPAddress _resolvedIp;

        private bool _stop = true;

        public MainWindow()
        {
            InitializeComponent();

            SetupLogging();

            _appConfig = new AppConfiguration();
            _guid = ShortGuid.NewGuid().ToString();

            InitAudioInput();

            InitAudioOutput();

            _inputManager = new InputDeviceManager(this);

            LoadInputSettings();

            ServerIp.Text = _appConfig.LastServer;
            MicrophoneBoost.Value = _appConfig.MicBoost;
            //   this.boostAmount.Content = "Boost: " + this.microphoneBoost.Value;
            _audioManager = new AudioManager(_clients);
            _audioManager.Volume = (float) MicrophoneBoost.Value;

            UpdaterChecker.CheckForUpdate();

            InitRadioEffectsToggle();

            InitRadioSwitchIsPTT();
        }


        private void InitAudioInput()
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                Mic.Items.Add(WaveIn.GetCapabilities(i).ProductName);
            }

            if (WaveIn.DeviceCount >= _appConfig.AudioInputDeviceId && WaveIn.DeviceCount > 0)
            {
                Mic.SelectedIndex = _appConfig.AudioInputDeviceId;
            }
            else if (WaveIn.DeviceCount > 0)
            {
                Mic.SelectedIndex = 0;
            }
        }

        private void InitAudioOutput()
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                Speakers.Items.Add(WaveOut.GetCapabilities(i).ProductName);
            }

            if (WaveOut.DeviceCount >= _appConfig.AudioOutputDeviceId && WaveOut.DeviceCount > 0)
            {
                Speakers.SelectedIndex = _appConfig.AudioOutputDeviceId;
            }
            else if (WaveOut.DeviceCount > 0)
            {
                Speakers.SelectedIndex = 0;
            }
        }

        private void InitRadioEffectsToggle()
        {
            var radioEffects = Settings.Instance.UserSettings[(int) SettingType.RadioEffects];
            if (radioEffects == "ON")
            {
                RadioEffectsToggle.IsChecked = true;
            }
            else
            {
                RadioEffectsToggle.IsChecked = false;
            }
        }

        private void InitRadioSwitchIsPTT()
        {
            var switchIsPTT = Settings.Instance.UserSettings[(int)SettingType.RadioSwitchIsPTT];
            if (switchIsPTT == "ON")
            {
                RadioSwitchIsPTT.IsChecked = true;
            }
            else
            {
                RadioSwitchIsPTT.IsChecked = false;
            }
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

        private void LoadInputSettings()
        {
            //TODO load input settings

            if (_inputManager.InputConfig.InputDevices != null)
            {
                if (_inputManager.InputConfig.InputDevices[0] != null)
                {
                    PttCommonText.Text = _inputManager.InputConfig.InputDevices[0].Button.ToString();
                    PttCommonDevice.Text = _inputManager.InputConfig.InputDevices[0].DeviceName;
                }


                if (_inputManager.InputConfig.InputDevices[1] != null)
                {
                    Ptt1Text.Text = _inputManager.InputConfig.InputDevices[1].Button.ToString();
                    Ptt1Device.Text = _inputManager.InputConfig.InputDevices[1].DeviceName;
                }
                if (_inputManager.InputConfig.InputDevices[2] != null)
                {
                    Ptt2Text.Text = _inputManager.InputConfig.InputDevices[2].Button.ToString();
                    Ptt2Device.Text = _inputManager.InputConfig.InputDevices[2].DeviceName;
                }

                if (_inputManager.InputConfig.InputDevices[3] != null)
                {
                    Ptt3Text.Text = _inputManager.InputConfig.InputDevices[3].Button.ToString();
                    Ptt3Device.Text = _inputManager.InputConfig.InputDevices[3].DeviceName;
                }
            }
        }


        private void startStop_Click(object sender, RoutedEventArgs e)
        {
            if (!_stop)
            {
                Stop();
            }
            else
            {
                try
                {
                    //process hostname
                    var ipAddr = Dns.GetHostAddresses(ServerIp.Text.Trim());

                    if (ipAddr.Length > 0)
                    {
                        _resolvedIp = ipAddr[0];

                        _client = new ClientSync(_clients, _guid);
                        _client.TryConnect(new IPEndPoint(_resolvedIp, 5002), ConnectCallback);

                        StartStop.Content = "Connecting...";
                        StartStop.IsEnabled = false;
                        Mic.IsEnabled = false;
                        Speakers.IsEnabled = false;
                    }
                    else
                    {
                        //invalid ID
                        MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void Stop()
        {
            StartStop.Content = "Start";
            StartStop.IsEnabled = true;
            Mic.IsEnabled = true;
            Speakers.IsEnabled = true;
            try
            {
                _audioManager.StopEncoding();
            }
            catch (Exception ex)
            {
            }

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
                    StartStop.Content = "Disconnect";
                    StartStop.IsEnabled = true;

                    //save app settings
                    _appConfig.LastServer = ServerIp.Text.Trim();
                    _appConfig.AudioInputDeviceId = Mic.SelectedIndex;
                    _appConfig.AudioOutputDeviceId = Speakers.SelectedIndex;

                    _audioManager.StartEncoding(Mic.SelectedIndex, Speakers.SelectedIndex, _guid, _inputManager,
                        _resolvedIp);
                    _stop = false;
                }
            }
            else
            {
                Stop();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            Stop();

            if (_audioPreview != null)
            {
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }

        private void pttCommonButton_Click(object sender, RoutedEventArgs e)
        {
            PttCommonClear.IsEnabled = false;
            PttCommonButton.IsEnabled = false;


            _inputManager.AssignButton(device =>
            {
                PttCommonClear.IsEnabled = true;
                PttCommonButton.IsEnabled = true;

                PttCommonDevice.Text = device.DeviceName;
                PttCommonText.Text = device.Button.ToString();

                device.InputBind = InputDevice.InputBinding.Ptt;

                _inputManager.InputConfig.InputDevices[0] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.Ptt, device);
            });
        }

        private void ptt1_Click(object sender, RoutedEventArgs e)
        {
            Ptt1ButtonClear.IsEnabled = false;
            Ptt1ButtonClear.IsEnabled = false;


            _inputManager.AssignButton(device =>
            {
                Ptt1ButtonClear.IsEnabled = true;
                Ptt1ButtonClear.IsEnabled = true;

                Ptt1Device.Text = device.DeviceName;
                Ptt1Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.Switch1;

                _inputManager.InputConfig.InputDevices[1] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.Switch1, device);
            });
        }

        private void ptt2_Click(object sender, RoutedEventArgs e)
        {
            Ptt2ButtonClear.IsEnabled = false;
            Ptt2ButtonClear.IsEnabled = false;


            _inputManager.AssignButton(device =>
            {
                Ptt2ButtonClear.IsEnabled = true;
                Ptt2ButtonClear.IsEnabled = true;

                Ptt2Device.Text = device.DeviceName;
                Ptt2Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.Switch2;

                _inputManager.InputConfig.InputDevices[2] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.Switch2, device);
            });
        }

        private void ptt3_Click(object sender, RoutedEventArgs e)
        {
            Ptt3ButtonClear.IsEnabled = false;
            Ptt3ButtonClear.IsEnabled = false;


            _inputManager.AssignButton(device =>
            {
                Ptt3ButtonClear.IsEnabled = true;
                Ptt3ButtonClear.IsEnabled = true;

                Ptt3Device.Text = device.DeviceName;
                Ptt3Text.Text = device.Button.ToString();
                device.InputBind = InputDevice.InputBinding.Switch3;

                _inputManager.InputConfig.InputDevices[3] = device;
                _inputManager.InputConfig.WriteInputRegistry(InputDevice.InputBinding.Switch3, device);
            });
        }

        private void pttCommonButtonClear_Click(object sender, RoutedEventArgs e)
        {
            _inputManager.InputConfig.ClearInputRegistry(InputDevice.InputBinding.Ptt);
            _inputManager.InputConfig.InputDevices[0] = null;

            PttCommonDevice.Text = "None";
            PttCommonText.Text = "None";
        }

        private void ptt1Clear_Click(object sender, RoutedEventArgs e)
        {
            _inputManager.InputConfig.ClearInputRegistry(InputDevice.InputBinding.Switch1);
            _inputManager.InputConfig.InputDevices[1] = null;

            Ptt1Device.Text = "None";
            Ptt1Text.Text = "None";
        }

        private void ptt2Clear_Click(object sender, RoutedEventArgs e)
        {
            _inputManager.InputConfig.ClearInputRegistry(InputDevice.InputBinding.Switch2);
            _inputManager.InputConfig.InputDevices[2] = null;

            Ptt2Device.Text = "None";
            Ptt2Text.Text = "None";
        }

        private void ptt3Clear_Click(object sender, RoutedEventArgs e)
        {
            _inputManager.InputConfig.ClearInputRegistry(InputDevice.InputBinding.Switch3);
            _inputManager.InputConfig.InputDevices[3] = null;

            Ptt3Device.Text = "None";
            Ptt3Text.Text = "None";
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                _audioPreview = new AudioPreview();
                _audioPreview.StartPreview(Mic.SelectedIndex, Speakers.SelectedIndex);
                _audioPreview.Volume.Volume = (float) MicrophoneBoost.Value;
                Preview.Content = "Stop Preview";
            }
            else
            {
                Preview.Content = "Audio Preview";
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }

        private void MicrophoneBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreview != null)
            {
                _audioPreview.Volume.Volume = (float) MicrophoneBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.Volume = (float) MicrophoneBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.MicBoost = (float) MicrophoneBoost.Value;
            }
        }

        private void RadioEffects_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioEffects, (string) RadioEffectsToggle.Content);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            Settings.Instance.WriteSetting(SettingType.RadioSwitchIsPTT, (string)RadioSwitchIsPTT.Content);
        }
    }
}