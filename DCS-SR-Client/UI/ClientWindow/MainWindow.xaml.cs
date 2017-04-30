using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.Favourites;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Overlay;
using MahApps.Metro.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLog;
using NLog.Config;
using NLog.Targets;
using InputBinding = Ciribob.DCS.SimpleRadio.Standalone.Client.Input.InputBinding;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton);

        private readonly AppConfiguration _appConfig;

        private readonly AudioManager _audioManager;

        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();

        private readonly string _guid;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private AudioPreview _audioPreview;
        private ClientSync _client;
        private DCSAutoConnectListener _dcsAutoConnectListener;
        private int _port = 5002;

        private Overlay.RadioOverlayWindow _radioOverlayWindow;
        private AwacsRadioOverlayWindow.RadioOverlayWindow _awacsRadioOverlay;

        private IPAddress _resolvedIp;
        private ServerSettingsWindow _serverSettingsWindow;

        private bool _stop = true;

        //used to debounce toggle
        private double _toggleShowHide;

        private readonly DispatcherTimer _updateTimer;
        private MMDeviceCollection outputDeviceList;
        private ServerAddress _serverAddress;
        private readonly DelegateCommand _connectCommand;

        public MainWindow()
        {
            InitializeComponent();

            DataContext = this;

            var client = ClientStateSingleton.Instance;

            this.WindowStartupLocation = WindowStartupLocation.Manual;
            this.Left = AppConfiguration.Instance.ClientX;
            this.Top = AppConfiguration.Instance.ClientY; 


            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            SetupLogging();

            Title = Title + " - " + UpdaterChecker.VERSION;

            Logger.Info("Started DCS-SimpleRadio Client " + UpdaterChecker.VERSION);

            InitExpandControls();

            InitInput();

            _appConfig = AppConfiguration.Instance;
            _guid = ShortGuid.NewGuid().ToString();

            InitAudioInput();

            InitAudioOutput();

            _connectCommand = new DelegateCommand(Connect, () => ServerAddress != null);
            FavouriteServersViewModel = new FavouriteServersViewModel(new CsvFavouriteServerStore());

            InitDefaultAddress();

            MicrophoneBoost.Value = _appConfig.MicBoost;
            SpeakerBoost.Value = _appConfig.SpeakerBoost;

            _audioManager = new AudioManager(_clients);
            _audioManager.MicBoost = (float) MicrophoneBoost.Value;
            _audioManager.SpeakerBoost = (float) SpeakerBoost.Value;

            if ((BoostLabel != null) && (MicrophoneBoost != null))
            {
                BoostLabel.Content = (int) (MicrophoneBoost.Value*100) - 100 + "%";
            }

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = (int) (SpeakerBoost.Value*100) - 100 + "%";
            }

            UpdaterChecker.CheckForUpdate();

            InitRadioSwitchIsPTT();

            InitRadioRxEffectsToggle();

            InitRadioTxEffectsToggle();

            InitRadioEncryptionEffectsToggle();

            InitRadioSoundEffects();

            InitAutoConnectPrompt();

            InitRadioOverlayTaskbarHide();

            InitRefocusDCS();

            InitFlowDocument();

            _dcsAutoConnectListener = new DCSAutoConnectListener(AutoConnect);

            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(100)};
            _updateTimer.Tick += UpdateClientCount_VUMeters;
            _updateTimer.Start();
        }

        private void InitFlowDocument()
        {
            //make hyperlinks work
            var hyperlinks = WPFElementHelper.GetVisuals(AboutFlowDocument).OfType<Hyperlink>();
            foreach (var link in hyperlinks)
                link.RequestNavigate += new System.Windows.Navigation.RequestNavigateEventHandler((sender, args) =>
                {
                    Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri));
                    args.Handled = true;
                });

        }

        private void InitDefaultAddress()
        {
            // legacy setting migration
            if (!string.IsNullOrEmpty(_appConfig.LastServer) && FavouriteServersViewModel.Addresses.Count == 0)
            {
                var oldAddress = new ServerAddress(_appConfig.LastServer, _appConfig.LastServer, true);
                FavouriteServersViewModel.Addresses.Add(oldAddress);
            }

            ServerAddress = FavouriteServersViewModel.DefaultServerAddress;
        }

        private void InitInput()
        {
            InputManager = new InputDeviceManager(this, ToggleOverlay);

            Radio1.InputName = "Radio 1";
            Radio1.ControlInputBinding = InputBinding.Switch1;
            Radio1.InputDeviceManager = InputManager;

            Radio2.InputName = "Radio 2";
            Radio2.ControlInputBinding = InputBinding.Switch2;
            Radio2.InputDeviceManager = InputManager;

            Radio3.InputName = "Radio 3";
            Radio3.ControlInputBinding = InputBinding.Switch3;
            Radio3.InputDeviceManager = InputManager;

            PTT.InputName = "Common PTT";
            PTT.ControlInputBinding = InputBinding.Ptt;
            PTT.InputDeviceManager = InputManager;

            Intercom.InputName = "Intercom Select";
            Intercom.ControlInputBinding = InputBinding.Intercom;
            Intercom.InputDeviceManager = InputManager;

            RadioOverlay.InputName = "Overlay Toggle";
            RadioOverlay.ControlInputBinding = InputBinding.OverlayToggle;
            RadioOverlay.InputDeviceManager = InputManager;

            Radio4.InputName = "Radio 4";
            Radio4.ControlInputBinding = InputBinding.Switch4;
            Radio4.InputDeviceManager = InputManager;

            Radio5.InputName = "Radio 5";
            Radio5.ControlInputBinding = InputBinding.Switch5;
            Radio5.InputDeviceManager = InputManager;

            Radio6.InputName = "Radio 6";
            Radio6.ControlInputBinding = InputBinding.Switch6;
            Radio6.InputDeviceManager = InputManager;

            Radio7.InputName = "Radio 7";
            Radio7.ControlInputBinding = InputBinding.Switch7;
            Radio7.InputDeviceManager = InputManager;

            Radio8.InputName = "Radio 8";
            Radio8.ControlInputBinding = InputBinding.Switch8;
            Radio8.InputDeviceManager = InputManager;

            Radio9.InputName = "Radio 9";
            Radio9.ControlInputBinding = InputBinding.Switch9;
            Radio9.InputDeviceManager = InputManager;

            Radio10.InputName = "Radio 10";
            Radio10.ControlInputBinding = InputBinding.Switch10;
            Radio10.InputDeviceManager = InputManager;

            Up100.InputName = "Up 100MHz";
            Up100.ControlInputBinding = InputBinding.Up100;
            Up100.InputDeviceManager = InputManager;

            Up10.InputName = "Up 10MHz";
            Up10.ControlInputBinding = InputBinding.Up10;
            Up10.InputDeviceManager = InputManager;

            Up1.InputName = "Up 1MHz";
            Up1.ControlInputBinding = InputBinding.Up1;
            Up1.InputDeviceManager = InputManager;

            Up01.InputName = "Up 0.1MHz";
            Up01.ControlInputBinding = InputBinding.Up01;
            Up01.InputDeviceManager = InputManager;

            Up001.InputName = "Up 0.01MHz";
            Up001.ControlInputBinding = InputBinding.Up001;
            Up001.InputDeviceManager = InputManager;

            Up0001.InputName = "Up 0.001MHz";
            Up0001.ControlInputBinding = InputBinding.Up0001;
            Up0001.InputDeviceManager = InputManager;


            Down100.InputName = "Down 100MHz";
            Down100.ControlInputBinding = InputBinding.Down100;
            Down100.InputDeviceManager = InputManager;

            Down10.InputName = "Down 10MHz";
            Down10.ControlInputBinding = InputBinding.Down10;
            Down10.InputDeviceManager = InputManager;

            Down1.InputName = "Down 1MHz";
            Down1.ControlInputBinding = InputBinding.Down1;
            Down1.InputDeviceManager = InputManager;

            Down01.InputName = "Down 0.1MHz";
            Down01.ControlInputBinding = InputBinding.Down01;
            Down01.InputDeviceManager = InputManager;

            Down001.InputName = "Down 0.01MHz";
            Down001.ControlInputBinding = InputBinding.Down001;
            Down001.InputDeviceManager = InputManager;

            Down0001.InputName = "Down 0.001MHz";
            Down0001.ControlInputBinding = InputBinding.Down0001;
            Down0001.InputDeviceManager = InputManager;

            ToggleGuard.InputName = "Toggle Guard";
            ToggleGuard.ControlInputBinding = InputBinding.ToggleGuard;
            ToggleGuard.InputDeviceManager = InputManager;

            NextRadio.InputName = "Select Next Radio";
            NextRadio.ControlInputBinding = InputBinding.NextRadio;
            NextRadio.InputDeviceManager = InputManager;

            PreviousRadio.InputName = "Select Previous Radio";
            PreviousRadio.ControlInputBinding = InputBinding.PreviousRadio;
            PreviousRadio.InputDeviceManager = InputManager;

            ToggleEncryption.InputName = "Toggle Encryption";
            ToggleEncryption.ControlInputBinding = InputBinding.ToggleEncryption;
            ToggleEncryption.InputDeviceManager = InputManager;

            EncryptionKeyIncrease.InputName = "Encryption Key Up";
            EncryptionKeyIncrease.ControlInputBinding = InputBinding.EncryptionKeyIncrease;
            EncryptionKeyIncrease.InputDeviceManager = InputManager;

            EncryptionKeyDecrease.InputName = "Encryption Key Down";
            EncryptionKeyDecrease.ControlInputBinding = InputBinding.EncryptionKeyDecrease;
            EncryptionKeyDecrease.InputDeviceManager = InputManager;

            RadioChannelUp.InputName = "Radio Channel Up";
            RadioChannelUp.ControlInputBinding = InputBinding.RadioChannelUp;
            RadioChannelUp.InputDeviceManager = InputManager;

            RadioChannelDown.InputName = "Encryption Key Down";
            RadioChannelDown.ControlInputBinding = InputBinding.RadioChannelDown;
            RadioChannelDown.InputDeviceManager = InputManager;
        }

        public InputDeviceManager InputManager { get; set; }

        public FavouriteServersViewModel FavouriteServersViewModel { get; }

        public ServerAddress ServerAddress
        {
            get { return _serverAddress; }
            set
            {
                _serverAddress = value;
                ServerIp.Text = value.Address;
                _connectCommand.RaiseCanExecuteChanged();
            }
        }

        public ICommand ConnectCommand => _connectCommand;

        private void InitAudioInput()
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                //first time round
                if (i == 0)
                {
                    Mic.SelectedIndex = 0;
                }

                var item = WaveIn.GetCapabilities(i);
                Mic.Items.Add(new AudioDeviceListItem()
                {
                    Text = item.ProductName,
                    Value = item
                });

                if (item.ProductGuid.ToString() == _appConfig.AudioInputDeviceId)
                {
                    Mic.SelectedIndex = i;
                }

            }
           
        }

        private void InitAudioOutput()
        {

            var enumerator = new MMDeviceEnumerator();
            outputDeviceList = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            int i = 0;
            foreach (var device in outputDeviceList)
            {
               
                Speakers.Items.Add(new AudioDeviceListItem()
                {
                    Text = device.DeviceFriendlyName,
                    Value = device
                });

                //first time round the loop, select first item
                if (i == 0)
                {
                    Speakers.SelectedIndex = 0;
                }

                if (device.DeviceFriendlyName == _appConfig.AudioOutputDeviceId)
                {
                    Speakers.SelectedIndex = i; //this one
                }

                i++;
            }

        }

        private void UpdateClientCount_VUMeters(object sender, EventArgs e)
        {
            ClientCount.Content = _clients.Count;

            if (_audioPreview != null)
            {
                Mic_VU.Value = _audioPreview.MicMax;
                Speaker_VU.Value = _audioPreview.SpeakerMax;
            }
            else if (_audioManager != null)
            {
                Mic_VU.Value = _audioManager.MicMax;
                Speaker_VU.Value = _audioManager.SpeakerMax;
            } 
        }

        private void InitRadioRxEffectsToggle()
        {
     
            RadioRxStartToggle.IsChecked = SettingsStore.Instance.UserSettings[(int)SettingType.RadioRxEffects_Start] == "ON";
            RadioRxEndToggle.IsChecked = SettingsStore.Instance.UserSettings[(int)SettingType.RadioRxEffects_End] == "ON";
        }


        private void InitRadioTxEffectsToggle()
        {
            RadioTxStartToggle.IsChecked = SettingsStore.Instance.UserSettings[(int)SettingType.RadioTxEffects_Start] == "ON";
            RadioTxEndToggle.IsChecked = SettingsStore.Instance.UserSettings[(int)SettingType.RadioTxEffects_End] == "ON";
        }

        private void InitRadioEncryptionEffectsToggle()
        {
            var radioEffects = Settings.SettingsStore.Instance.UserSettings[(int) SettingType.RadioEncryptionEffects];
            if (radioEffects == "ON")
            {
                RadioEncryptionEffectsToggle.IsChecked = true;
            }
            else
            {
                RadioEncryptionEffectsToggle.IsChecked = false;
            }
        }

        private void InitRadioSwitchIsPTT()
        {
            var switchIsPTT = Settings.SettingsStore.Instance.UserSettings[(int) SettingType.RadioSwitchIsPTT];
            if (switchIsPTT == "ON")
            {
                RadioSwitchIsPTT.IsChecked = true;
            }
            else
            {
                RadioSwitchIsPTT.IsChecked = false;
            }
        }

        private void InitAutoConnectPrompt()
        {
            var autoConnect = Settings.SettingsStore.Instance.UserSettings[(int) SettingType.AutoConnectPrompt];
            if (autoConnect == "ON")
            {
                AutoConnectPromptToggle.IsChecked = true;
            }
            else
            {
                AutoConnectPromptToggle.IsChecked = false;
            }
        }

        private void InitRadioOverlayTaskbarHide()
        {
            var autoConnect = Settings.SettingsStore.Instance.UserSettings[(int) SettingType.RadioOverlayTaskbarHide];
            if (autoConnect == "ON")
            {
                RadioOverlayTaskbarItem.IsChecked = true;
            }
            else
            {
                RadioOverlayTaskbarItem.IsChecked = false;
            }
        }

        private void InitRefocusDCS()
        {
            var refocus = Settings.SettingsStore.Instance.UserSettings[(int)SettingType.RefocusDCS];
            if (refocus == "ON")
            {
                RefocusDCS.IsChecked = true;
            }
            else
            {
                RefocusDCS.IsChecked = false;
            }
        }


        private void InitExpandControls()
        {
            var expand = Settings.SettingsStore.Instance.UserSettings[(int)SettingType.ExpandControls];
            if (expand == "ON")
            {
                ExpandInputDevices.IsChecked = true;
            }
            else
            {
                ExpandInputDevices.IsChecked = false;
            }
        }

        private void InitRadioSoundEffects()
        {
            var radioEffects = Settings.SettingsStore.Instance.UserSettings[(int)SettingType.RadioEffects];
            if (radioEffects == "ON")
            {
                RadioSoundEffects.IsChecked = true;
            }
            else
            {
                RadioSoundEffects.IsChecked = false;
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
//            var rule1 = new LoggingRule("*", LogLevel.Debug, consoleTarget);
//            config.LoggingRules.Add(rule1);

            var rule2 = new LoggingRule("*", LogLevel.Info, fileTarget);
            config.LoggingRules.Add(rule2);

            // Step 5. Activate the configuration
            LogManager.Configuration = config;
        }

        private void Connect()
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
                    var ipAddr = Dns.GetHostAddresses(GetAddressFromTextBox());

                    if (ipAddr.Length > 0)
                    {
                        _resolvedIp = ipAddr[0];
                        _port = GetPortFromTextBox();

                        _client = new ClientSync(_clients, _guid);
                        _client.TryConnect(new IPEndPoint(_resolvedIp, _port), ConnectCallback);

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

        private string GetAddressFromTextBox()
        {
            var addr = ServerIp.Text.Trim();

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private int GetPortFromTextBox()
        {
            var addr = ServerIp.Text.Trim();

            if (addr.Contains(":"))
            {
                int port;
                if (int.TryParse(addr.Split(':')[1], out port))
                {
                    return port;
                }
                throw new ArgumentException("specified port is not valid");
            }

            return 5002;
        }

        private void Stop()
        {
            StartStop.Content = "Connect";
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
                    try
                    {
                        var output = outputDeviceList[Speakers.SelectedIndex];

                        StartStop.Content = "Disconnect";
                        StartStop.IsEnabled = true;


                        //save app settings
                        _appConfig.AudioInputDeviceId = ((WaveInCapabilities)((AudioDeviceListItem)Mic.SelectedItem).Value).ProductGuid.ToString();
                        _appConfig.AudioOutputDeviceId = output.DeviceFriendlyName;

                        _audioManager.StartEncoding(Mic.SelectedIndex, output, _guid, InputManager,
                            _resolvedIp, _port);
                        _stop = false;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex,
                            "Unable to get audio device - likely output device error - Pick another. Error:" +
                            ex.Message);
                        Stop();

                        MessageBox.Show($"Problem Initialising Audio Output! Try selecting a different Output device.", "Audio Output Error", MessageBoxButton.OK,
                         MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                Stop();
            }
        }

        

        protected override void OnClosing(CancelEventArgs e)
        {
            AppConfiguration.Instance.ClientX = this.Left;
            AppConfiguration.Instance.ClientY = this.Top;

            //save window position
            base.OnClosing(e);

            //stop timer
            _updateTimer.Stop();

            Stop();

            if (_audioPreview != null)
            {
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }

            _radioOverlayWindow?.Close();
            _radioOverlayWindow = null;

            _dcsAutoConnectListener.Stop();
            _dcsAutoConnectListener = null;
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                //get device
                try
                {
                    var output = outputDeviceList[Speakers.SelectedIndex];

                    //save settings
                    _appConfig.AudioInputDeviceId = (( WaveInCapabilities )((AudioDeviceListItem)Mic.SelectedItem).Value).ProductGuid.ToString();
                    _appConfig.AudioOutputDeviceId = output.DeviceFriendlyName;

                    _audioPreview = new AudioPreview();

                    _audioPreview.StartPreview(Mic.SelectedIndex, output);
                    _audioPreview.SpeakerBoost = (float) SpeakerBoost.Value;
                    _audioPreview.MicBoost = (float) MicrophoneBoost.Value;
                    Preview.Content = "Stop Preview";
                }
                catch (Exception ex)
                {
                    Logger.Error(ex,"Unable to preview audio - likely output device error - Pick another. Error:"+ex.Message);
                }
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
                _audioPreview.MicBoost = (float) MicrophoneBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.MicBoost = (float) MicrophoneBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.MicBoost = (float) MicrophoneBoost.Value;
            }

            if ((BoostLabel != null) && (MicrophoneBoost != null))
            {
                BoostLabel.Content = (int) (MicrophoneBoost.Value*100) - 100 + "%";
            }
        }

        private void SpeakerBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreview != null)
            {
                _audioPreview.SpeakerBoost = (float) SpeakerBoost.Value;
            }
            if (_audioManager != null)
            {
                _audioManager.SpeakerBoost = (float) SpeakerBoost.Value;
            }
            if (_appConfig != null)
            {
                _appConfig.SpeakerBoost = (float) SpeakerBoost.Value;
            }

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = (int) (SpeakerBoost.Value*100) - 100 + "%";
            }
        }

        private void RadioEncryptionEffects_Click(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.RadioEncryptionEffects,
                (string) RadioEncryptionEffectsToggle.Content);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.RadioSwitchIsPTT, (string) RadioSwitchIsPTT.Content);
        }

        private void ShowOverlay_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true);
        }

        private void ToggleOverlay(bool uiButton)
        {
            //debounce show hide
            if ((Environment.TickCount - _toggleShowHide > 600) || uiButton)
            {
                _toggleShowHide = Environment.TickCount;
                if ((_radioOverlayWindow == null) || !_radioOverlayWindow.IsVisible ||
                    (_radioOverlayWindow.WindowState == WindowState.Minimized))
                {
                    //hide awacs panel
                    _awacsRadioOverlay?.Close();
                    _awacsRadioOverlay = null;

                    _radioOverlayWindow?.Close();

                    _radioOverlayWindow = new Overlay.RadioOverlayWindow();
                    _radioOverlayWindow.ShowInTaskbar =
                        Settings.SettingsStore.Instance.UserSettings[(int) SettingType.RadioOverlayTaskbarHide] != "ON";
                    _radioOverlayWindow.Show();

                   
                }
                else
                {
                    _radioOverlayWindow?.Close();
                    _radioOverlayWindow = null;
                }
            }
        }

        private void ShowAwacsOverlay_OnClick(object sender, RoutedEventArgs e)
        {
            
            if ((_awacsRadioOverlay == null) || !_awacsRadioOverlay.IsVisible ||
                (_awacsRadioOverlay.WindowState == WindowState.Minimized))
            {
                //close normal overlay
                _radioOverlayWindow?.Close();
                _radioOverlayWindow = null;

                _awacsRadioOverlay?.Close();

                _awacsRadioOverlay = new AwacsRadioOverlayWindow.RadioOverlayWindow();
                _awacsRadioOverlay.ShowInTaskbar =
                    Settings.SettingsStore.Instance.UserSettings[(int)SettingType.RadioOverlayTaskbarHide] != "ON";
                _awacsRadioOverlay.Show();
            }
            else
            {
                _awacsRadioOverlay?.Close();
                _awacsRadioOverlay = null;
            }
            
        }

        private void AutoConnect(string address, int port)
        {
            Logger.Info("Received AutoConnect " + address);

            if (StartStop.Content.ToString().ToLower() == "connect")
            {
                var autoConnect = Settings.SettingsStore.Instance.UserSettings[(int) SettingType.AutoConnectPrompt];

                var connection = $"{address}:{port}";
                if (autoConnect == "ON")
                {
                    WindowHelper.BringProcessToFront(Process.GetCurrentProcess());

                    var result = MessageBox.Show(this,
                        $"Would you like to try to Auto-Connect to DCS-SRS @ {address}:{port}? ", "Auto Connect",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if ((result == MessageBoxResult.Yes) && (StartStop.Content.ToString().ToLower() == "connect"))
                    {
                        ServerIp.Text = connection;
                        Connect();
                    }
                }
                else
                {
                    ServerIp.Text = connection;
                    Connect();
                }
            }
        }

        private void ResetRadioWindow_Click(object sender, RoutedEventArgs e)
        {
            //close overlay
            _radioOverlayWindow?.Close();
            _radioOverlayWindow = null;

            AppConfiguration.Instance.RadioX = 100;
            AppConfiguration.Instance.RadioY = 100;

            AppConfiguration.Instance.RadioWidth = 122;
            AppConfiguration.Instance.RadioHeight = 270;

            AppConfiguration.Instance.RadioOpacity = 1.0;
        }

        private void ToggleServerSettings_OnClick(object sender, RoutedEventArgs e)
        {
            if ((_serverSettingsWindow == null) || !_serverSettingsWindow.IsVisible ||
                (_serverSettingsWindow.WindowState == WindowState.Minimized))
            {
                _serverSettingsWindow?.Close();

                _serverSettingsWindow = new ServerSettingsWindow();
                _serverSettingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _serverSettingsWindow.Owner = this;
                _serverSettingsWindow.ShowDialog();

              
            }
            else
            {
                _serverSettingsWindow?.Close();
                _serverSettingsWindow = null;
            }
        }

        private void AutoConnectPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.AutoConnectPrompt, (string) AutoConnectPromptToggle.Content);
        }

        private void RadioOverlayTaskbarItem_Click(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.RadioOverlayTaskbarHide, (string) RadioOverlayTaskbarItem.Content);
        }


        private void DCSRefocus_OnClick_Click(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.RefocusDCS, (string)RefocusDCS.Content);
        }

        private void ExpandInputDevices_OnClick_Click(object sender, RoutedEventArgs e)
        {

            MessageBox.Show("You must restart SRS for this setting to take effect.\n\nTurning this on will allow almost any DirectX device to be used as input expect a Mouse but may cause issues with other devices being detected", "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                           MessageBoxImage.Warning);

            Settings.SettingsStore.Instance.WriteSetting(SettingType.ExpandControls, (string)ExpandInputDevices.Content);
        }

        private void LaunchAddressTab(object sender, RoutedEventArgs e)
        {
            TabControl.SelectedItem = FavouritesSeversTab;
        }

        private void RadioSoundEffects_OnClick(object sender, RoutedEventArgs e)
        {
            Settings.SettingsStore.Instance.WriteSetting(SettingType.RadioEffects, (string)RadioSoundEffects.Content);
        }

        private void RadioTxStart_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Instance.WriteSetting(SettingType.RadioTxEffects_Start, (string)RadioTxStartToggle.Content);
        }

        private void RadioTxEnd_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Instance.WriteSetting(SettingType.RadioTxEffects_End, (string) RadioTxEndToggle.Content);
        }

        private void RadioRxStart_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Instance.WriteSetting(SettingType.RadioRxEffects_Start, (string)RadioRxStartToggle.Content);
        }

        private void RadioRxEnd_Click(object sender, RoutedEventArgs e)
        {

            SettingsStore.Instance.WriteSetting(SettingType.RadioRxEffects_End, (string)RadioRxEndToggle.Content);
        }
    }
}