using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Overlay;
using MahApps.Metro.Controls;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NLog;
using WPFCustomMessageBox;
using InputBinding = Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.InputBinding;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton);

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
        private long _toggleShowHide;

        private readonly DispatcherTimer _updateTimer;
        private MMDeviceCollection outputDeviceList;
        private ServerAddress _serverAddress;
        private readonly DelegateCommand _connectCommand;

        private readonly SettingsStore _settings = SettingsStore.Instance;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        public MainWindow()
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            InitializeComponent();

            if (IsClientRunning())
            {
                MessageBoxResult result = MessageBox.Show(
                    "Another instance of the SimpleRadio client is already running!\n\nPlease note that only one SRS client can synchronise with DCS at a time - " +
                    "all other running instances will not receive any radio frequencies from the game.\n\nExit currently started client?",
                    "Multiple SimpleRadio clients started!",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Close();
                }
            }

            // Initialize ToolTip controls
            ToolTips.Init();

            // Set up tooltips that are always defined
            InitToolTips();

            DataContext = this;

            var client = ClientStateSingleton.Instance;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.GetPositionSetting(SettingsKeys.ClientX).FloatValue;
            Top = _settings.GetPositionSetting(SettingsKeys.ClientY).FloatValue;

           

            Title = Title + " - " + UpdaterChecker.VERSION;

            if (_settings.GetClientSetting(SettingsKeys.StartMinimised).BoolValue)
            {
                Hide();
                WindowState = WindowState.Minimized;

                Logger.Info("Started DCS-SimpleRadio Client " + UpdaterChecker.VERSION + " minimized");
            }
            else
            {
                Logger.Info("Started DCS-SimpleRadio Client " + UpdaterChecker.VERSION);
            }

            _guid = _settings.GetClientSetting(SettingsKeys.CliendIdShort).StringValue;

            Analytics.Log("Client", "Startup", _settings.GetClientSetting(SettingsKeys.ClientIdLong).RawValue);

            InitSettingsScreen();

            InitInput();

            InitAudioInput();

            InitAudioOutput();
            InitMicAudioOutput();

            _connectCommand = new DelegateCommand(Connect, () => ServerAddress != null);
            FavouriteServersViewModel = new FavouriteServersViewModel(new CsvFavouriteServerStore());

            InitDefaultAddress();

        
            SpeakerBoost.Value = _settings.GetClientSetting(SettingsKeys.SpeakerBoost).DoubleValue;

            Speaker_VU.Value = -100;
            Mic_VU.Value = -100;

            ExternalAWACSModeName.Text = _settings.GetClientSetting(SettingsKeys.LastSeenName).StringValue;

            _audioManager = new AudioManager(_clients);
            _audioManager.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float) SpeakerBoost.Value);


            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(_audioManager.SpeakerBoost);
            }

            UpdaterChecker.CheckForUpdate(_settings.GetClientSetting(SettingsKeys.CheckForBetaUpdates).BoolValue);

            InitFlowDocument();

            _dcsAutoConnectListener = new DCSAutoConnectListener(AutoConnect);

            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(100)};
            _updateTimer.Tick += UpdateClientCount_VUMeters;
            _updateTimer.Start();
        }

        private bool IsClientRunning()
        {
            Process currentProcess = Process.GetCurrentProcess();
            string currentProcessName = currentProcess.ProcessName.ToLower().Trim();

            foreach (Process clsProcess in Process.GetProcesses())
            {
                if (clsProcess.Id != currentProcess.Id &&
                    clsProcess.ProcessName.ToLower().Trim() == currentProcessName)
                {
                    return true;
                }
            }

            return false;
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
            if (!string.IsNullOrEmpty(_settings.GetClientSetting(SettingsKeys.LastServer).StringValue) &&
                FavouriteServersViewModel.Addresses.Count == 0)
            {
                var oldAddress = new ServerAddress(_settings.GetClientSetting(SettingsKeys.LastServer).StringValue,
                    _settings.GetClientSetting(SettingsKeys.LastServer).StringValue, true);
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

            PTT.InputName = "Push To Talk - PTT";
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

            RadioChannelDown.InputName = "Radio Channel Down";
            RadioChannelDown.ControlInputBinding = InputBinding.RadioChannelDown;
            RadioChannelDown.InputDeviceManager = InputManager;
        }

        private void InitToolTips()
        {
            ExternalAWACSModePassword.ToolTip = ToolTips.ExternalAWACSModePassword;
            ExternalAWACSModeName.ToolTip = ToolTips.ExternalAWACSModeName;
            ConnectExternalAWACSMode.ToolTip = ToolTips.ExternalAWACSMode;
        }

        public InputDeviceManager InputManager { get; set; }

        public FavouriteServersViewModel FavouriteServersViewModel { get; }

        public ServerAddress ServerAddress
        {
            get { return _serverAddress; }
            set
            {
                _serverAddress = value;
                if (value != null)
                {
                    ServerIp.Text = value.Address;
                }
                
                _connectCommand.RaiseCanExecuteChanged();
            }
        }

        public ICommand ConnectCommand => _connectCommand;

        private void InitAudioInput()
        {
            Logger.Info("Audio Input - Saved ID " +
                        _settings.GetClientSetting(SettingsKeys.AudioInputDeviceId).StringValue);

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

                Logger.Info("Audio Input - " + item.ProductName + " " + item.ProductGuid.ToString() + " - Name GUID" +
                            item.NameGuid + " - CHN:" + item.Channels);

                if (item.ProductName.Trim().StartsWith(_settings.GetClientSetting(SettingsKeys.AudioInputDeviceId).StringValue.Trim()))
                {
                    Mic.SelectedIndex = i;
                    Logger.Info("Audio Input - Found Saved ");
                }
            }

            // No microphone is available - users can still connect/listen, but audio input controls are disabled and sending is prevented
            if (WaveIn.DeviceCount == 0 || Mic.SelectedIndex < 0)
            {
                Logger.Info("Audio Input - No audio input devices available, disabling mic preview");

                _clientStateSingleton.MicrophoneAvailable = false;

                Preview.IsEnabled = false;

                Preview.ToolTip = ToolTips.NoMicAvailable;
                StartStop.ToolTip = ToolTips.NoMicAvailable;
                Mic.ToolTip = ToolTips.NoMicAvailable;
                Mic_VU.ToolTip = ToolTips.NoMicAvailable;
            }
            else
            {
                Logger.Info("Audio Input - " + WaveIn.DeviceCount + " audio input devices available, configuring as usual");

                _clientStateSingleton.MicrophoneAvailable = true;

                Preview.IsEnabled = true;

                Preview.ToolTip = null;
                StartStop.ToolTip = null;
                Mic.ToolTip = null;
                Mic_VU.ToolTip = null;
            }
        }

        private void InitAudioOutput()
        {
            Logger.Info("Audio Output - Saved ID " +
                        _settings.GetClientSetting(SettingsKeys.AudioOutputDeviceId).RawValue);

            var enumerator = new MMDeviceEnumerator();
            outputDeviceList = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            var i = 0;
            foreach (var device in outputDeviceList)
            {
                Speakers.Items.Add(new AudioDeviceListItem()
                {
                    Text = device.FriendlyName,
                    Value = device
                });

                Logger.Info("Audio Output - " + device.DeviceFriendlyName + " " + device.ID + " CHN:" +
                            device.AudioClient.MixFormat.Channels + " Rate:" +
                            device.AudioClient.MixFormat.SampleRate.ToString());

                //first time round the loop, select first item
                if (i == 0)
                {
                    Speakers.SelectedIndex = 0;
                }

                if (device.ID == _settings.GetClientSetting(SettingsKeys.AudioOutputDeviceId).RawValue)
                {
                    Speakers.SelectedIndex = i; //this one
                }

                i++;
            }
        }

        private void InitMicAudioOutput()
        {
            Logger.Info("Mic Audio Output - Saved ID " +
                        _settings.GetClientSetting(SettingsKeys.MicAudioOutputDeviceId).RawValue);

            var i = 0;

            MicOutput.Items.Add(new AudioDeviceListItem()
            {
                Text = "NO MIC OUTPUT / PASSTHROUGH",
                Value = null
            });
            foreach (var device in outputDeviceList)
            {
                MicOutput.Items.Add(new AudioDeviceListItem()
                {
                    Text = device.FriendlyName,
                    Value = device
                });

                Logger.Info("Mic Audio Output - " + device.DeviceFriendlyName + " " + device.ID + " CHN:" +
                            device.AudioClient.MixFormat.Channels + " Rate:" +
                            device.AudioClient.MixFormat.SampleRate.ToString());

                //first time round the loop, select first item
                if (i == 0)
                {
                    MicOutput.SelectedIndex = 0;
                }

                if (device.ID == _settings.GetClientSetting(SettingsKeys.MicAudioOutputDeviceId).RawValue)
                {
                    MicOutput.SelectedIndex = i; //this one
                }

                i++;
            }
        }

        private void UpdateClientCount_VUMeters(object sender, EventArgs e)
        {
            int clientCountIngame = 0;

            foreach (KeyValuePair<string, SRClient> kvp in _clients)
            {
                if (kvp.Value.IsIngame())
                {
                    clientCountIngame++;
                }
            }

            ClientCount.Content = $"{_clients.Count} ({clientCountIngame} ingame)";

            if (_audioPreview != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (_clientStateSingleton.MicrophoneAvailable)
                {
                    Mic_VU.Value = _audioPreview.MicMax;
                }
                Speaker_VU.Value = _audioPreview.SpeakerMax;
            }
            else if (_audioManager != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (_clientStateSingleton.MicrophoneAvailable)
                {
                    Mic_VU.Value = _audioManager.MicMax;
                }
                Speaker_VU.Value = _audioManager.SpeakerMax;
            }
            else
            {
                Mic_VU.Value = -100;
                Speaker_VU.Value = -100;
            }
        }


        private void InitSettingsScreen()
        {
            RadioEncryptionEffectsToggle.IsChecked =
                _settings.GetClientSetting(SettingsKeys.RadioEncryptionEffects).BoolValue;
            RadioSwitchIsPTT.IsChecked =
                _settings.GetClientSetting(SettingsKeys.RadioSwitchIsPTT).BoolValue;
            AutoConnectPromptToggle.IsChecked = _settings.GetClientSetting(SettingsKeys.AutoConnectPrompt).BoolValue;
            RadioOverlayTaskbarItem.IsChecked =
                _settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue;
            RefocusDCS.IsChecked = _settings.GetClientSetting(SettingsKeys.RefocusDCS).BoolValue;
            ExpandInputDevices.IsChecked = _settings.GetClientSetting(SettingsKeys.ExpandControls).BoolValue;
            RadioTxStartToggle.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioTxEffects_Start).BoolValue;
            RadioTxEndToggle.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioTxEffects_End).BoolValue;
            
            RadioRxStartToggle.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioRxEffects_Start).BoolValue;
            RadioRxEndToggle.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioRxEffects_Start).BoolValue;

            MinimiseToTray.IsChecked = _settings.GetClientSetting(SettingsKeys.MinimiseToTray).BoolValue;
            StartMinimised.IsChecked = _settings.GetClientSetting(SettingsKeys.StartMinimised).BoolValue;

            RadioSoundEffects.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioEffects).BoolValue;
            RadioSoundEffectsClipping.IsChecked = _settings.GetClientSetting(SettingsKeys.RadioEffectsClipping).BoolValue;
            AutoSelectChannel.IsChecked = _settings.GetClientSetting(SettingsKeys.AutoSelectPresetChannel).BoolValue;

            AlwaysAllowHotas.IsChecked = _settings.GetClientSetting(SettingsKeys.AlwaysAllowHotasControls).BoolValue;
            AllowDCSPTT.IsChecked = _settings.GetClientSetting(SettingsKeys.AllowDCSPTT).BoolValue;

            CheckForBetaUpdates.IsChecked = _settings.GetClientSetting(SettingsKeys.CheckForBetaUpdates).BoolValue;
        }

        private void Connect()
        {
            if (!_stop)
            {
                Stop();
            }
            else
            {
                SaveSelectedInputAndOutput();

                try
                {
                    //process hostname
                    var ipAddr = Dns.GetHostAddresses(GetAddressFromTextBox());

                    if (ipAddr.Length > 0)
                    {
                        _resolvedIp = ipAddr[0];
                        _port = GetPortFromTextBox();

                        _client = new ClientSync(_clients, _guid, UpdateUICallback);
                        _client.TryConnect(new IPEndPoint(_resolvedIp, _port), ConnectCallback);

                        StartStop.Content = "Connecting...";
                        StartStop.IsEnabled = false;
                        Mic.IsEnabled = false;
                        Speakers.IsEnabled = false;
                        MicOutput.IsEnabled = false;
                        Preview.IsEnabled = false;

                        if (_audioPreview != null)
                        {
                            Preview.Content = "Audio Preview";
                            _audioPreview.StopEncoding();
                            _audioPreview = null;
                        }
                    }
                    else
                    {
                        //invalid ID
                        MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        _clientStateSingleton.IsConnected = false;
                        ToggleServerSettings.IsEnabled = false;
                    }
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    _clientStateSingleton.IsConnected = false;
                    ToggleServerSettings.IsEnabled = false;
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
            MicOutput.IsEnabled = true;
            Preview.IsEnabled = true;
            _clientStateSingleton.IsConnected = false;
            ToggleServerSettings.IsEnabled = false;

            ExternalAWACSModePassword.IsEnabled = false;
            ExternalAWACSModePasswordLabel.IsEnabled = false;
            ExternalAWACSModeName.IsEnabled = false;
            ExternalAWACSModeNameLabel.IsEnabled = false;
            ConnectExternalAWACSMode.IsEnabled = false;
            ConnectExternalAWACSMode.Content = "Connect External AWACS MODE (EAM)";

            if (!string.IsNullOrWhiteSpace(_clientStateSingleton.LastSeenName) &&
                _settings.GetClientSetting(SettingsKeys.LastSeenName).StringValue != _clientStateSingleton.LastSeenName)
            {
                _settings.SetClientSetting(SettingsKeys.LastSeenName, _clientStateSingleton.LastSeenName);
            }

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

            _clientStateSingleton.DcsPlayerRadioInfo.Reset();
            _clientStateSingleton.DcsPlayerSideInfo.Reset();
        }

        private void SaveSelectedInputAndOutput()
        {

            var output = outputDeviceList[Speakers.SelectedIndex];

          
            //save app settings
            // Only save selected microphone if one is actually available, resulting in a crash otherwise
            if (_clientStateSingleton.MicrophoneAvailable)
            {
                _settings.SetClientSetting(SettingsKeys.AudioInputDeviceId, ((WaveInCapabilities)((AudioDeviceListItem)Mic.SelectedItem).Value).ProductName);
            }

            _settings.SetClientSetting(SettingsKeys.AudioOutputDeviceId, output.ID);

            //check if we have optional output
            if (MicOutput.SelectedIndex - 1 >= 0)
            {
                var micOutput = outputDeviceList[MicOutput.SelectedIndex - 1];
                //save settings
                _settings.SetClientSetting(SettingsKeys.MicAudioOutputDeviceId, micOutput.ID);
            }
            else
            {
                //save settings as none
                _settings.SetClientSetting(SettingsKeys.MicAudioOutputDeviceId, "");
            }
        }

        private void ConnectCallback(bool result)
        {
            if (result)
            {
                if (_stop)
                    try
                    {

                        var inputId = Mic.SelectedIndex;
                        var output = outputDeviceList[Speakers.SelectedIndex];

                        //check if we have optional output
                        MMDevice micOutput = null;
                        if (MicOutput.SelectedIndex - 1 >= 0)
                        {
                            micOutput = outputDeviceList[MicOutput.SelectedIndex - 1];
                        }

                        StartStop.Content = "Disconnect";
                        StartStop.IsEnabled = true;

                        _clientStateSingleton.IsConnected = true;

                        _settings.SetClientSetting(SettingsKeys.LastServer, ServerIp.Text);

                        _audioManager.StartEncoding(inputId, output, _guid, InputManager,
                            _resolvedIp, _port, micOutput);
                        _stop = false;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex,
                            "Unable to get audio device - likely output device error - Pick another. Error:" +
                            ex.Message);
                        Stop();

                        var messageBoxResult = CustomMessageBox.ShowYesNo(
                            "Problem initialising Audio Output!\n\nTry a different Output device and please post your clientlog.txt to the support Discord server.\n\nJoin support Discord server now?",
                            "Audio Output Error",
                            "OPEN PRIVACY SETTINGS",
                            "JOIN DISCORD SERVER",
                            MessageBoxImage.Error);

                        if (messageBoxResult == MessageBoxResult.Yes) Process.Start("https://discord.gg/baw7g3t");
                    }
            }
            else
            {
                Stop();
            }
        }


        protected override void OnClosing(CancelEventArgs e)
        {
            _settings.SetPositionSetting(SettingsKeys.ClientX, Left);
            _settings.SetPositionSetting(SettingsKeys.ClientY, Top);

            if (!string.IsNullOrWhiteSpace(_clientStateSingleton.LastSeenName) &&
                _settings.GetClientSetting(SettingsKeys.LastSeenName).StringValue != _clientStateSingleton.LastSeenName)
            {
                _settings.SetClientSetting(SettingsKeys.LastSeenName, _clientStateSingleton.LastSeenName);
            }

            //save window position
            base.OnClosing(e);

            //stop timer
            _updateTimer?.Stop();

            Stop();

            _audioPreview?.StopEncoding();
            _audioPreview = null;

            _radioOverlayWindow?.Close();
            _radioOverlayWindow = null;

            _awacsRadioOverlay?.Close();
            _awacsRadioOverlay = null;

            _dcsAutoConnectListener?.Stop();
            _dcsAutoConnectListener = null;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _settings.GetClientSetting(SettingsKeys.MinimiseToTray).BoolValue)
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                if (!_clientStateSingleton.MicrophoneAvailable)
                {
                    Logger.Info("Unable to preview audio, no valid audio input device available or selected");
                    return;
                }

                //get device
                try
                {
                    var inputId = Mic.SelectedIndex;
                    var output = outputDeviceList[Speakers.SelectedIndex];

                    SaveSelectedInputAndOutput();


                    _audioPreview = new AudioPreview();
                    _audioPreview.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value); 
                    _audioPreview.StartPreview(inputId, output);
                  
                    Preview.Content = "Stop Preview";
                }
                catch (Exception ex)
                {
                    Logger.Error(ex,
                        "Unable to preview audio - likely output device error - Pick another. Error:" + ex.Message);

                }
            }
            else
            {
                Preview.Content = "Audio Preview";
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }

        private void UpdateUICallback()
        {
            if (_clientStateSingleton.IsConnected)
            {
                ToggleServerSettings.IsEnabled = true;

                bool eamEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);

                ExternalAWACSModePassword.IsEnabled = eamEnabled && !_clientStateSingleton.InExternalAWACSMode;
                ExternalAWACSModePasswordLabel.IsEnabled = eamEnabled;
                ExternalAWACSModeName.Text = _clientStateSingleton.LastSeenName;
                ExternalAWACSModeName.IsEnabled = eamEnabled && !_clientStateSingleton.InExternalAWACSMode;
                ExternalAWACSModeNameLabel.IsEnabled = eamEnabled;
                ConnectExternalAWACSMode.IsEnabled = eamEnabled;
                ConnectExternalAWACSMode.Content = _clientStateSingleton.InExternalAWACSMode ? "Disconnect External AWACS MODE (EAM)" : "Connect External AWACS MODE (EAM)";
            }
            else
            {
                ToggleServerSettings.IsEnabled = false;
                ExternalAWACSModePassword.IsEnabled = false;
                ExternalAWACSModePasswordLabel.IsEnabled = false;
                ExternalAWACSModeName.Text = _clientStateSingleton.LastSeenName;
                ExternalAWACSModeName.IsEnabled = false;
                ExternalAWACSModeNameLabel.IsEnabled = false;
                ConnectExternalAWACSMode.IsEnabled = false;
                ConnectExternalAWACSMode.Content = "Connect External AWACS MODE (EAM)";
            }
        }

        private void SpeakerBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var convertedValue = VolumeConversionHelper.ConvertVolumeSliderToScale((float) SpeakerBoost.Value);

            if (_audioPreview != null)
            {
                _audioPreview.SpeakerBoost = convertedValue;
            }
            if (_audioManager != null)
            {
                _audioManager.SpeakerBoost = convertedValue;
            }

            _settings.SetClientSetting(SettingsKeys.SpeakerBoost,
                SpeakerBoost.Value.ToString(CultureInfo.InvariantCulture));


            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(convertedValue);
            }
        }

        private void RadioEncryptionEffects_Click(object sender, RoutedEventArgs e)
        {
            _settings.SetClientSetting(SettingsKeys.RadioEncryptionEffects,
                (string) RadioEncryptionEffectsToggle.Content);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            _settings.SetClientSetting(SettingsKeys.RadioSwitchIsPTT, (string) RadioSwitchIsPTT.Content);
        }

        private void ShowOverlay_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true);
        }

        private void ToggleOverlay(bool uiButton)
        {
            //debounce show hide (1 tick = 100ns, 6000000 ticks = 600ms debounce)
            if ((DateTime.Now.Ticks - _toggleShowHide > 6000000) || uiButton)
            {
                _toggleShowHide = DateTime.Now.Ticks;
                if ((_radioOverlayWindow == null) || !_radioOverlayWindow.IsVisible ||
                    (_radioOverlayWindow.WindowState == WindowState.Minimized))
                {
                    //hide awacs panel
                    _awacsRadioOverlay?.Close();
                    _awacsRadioOverlay = null;

                    _radioOverlayWindow?.Close();

                    _radioOverlayWindow = new Overlay.RadioOverlayWindow();


                    _radioOverlayWindow.ShowInTaskbar =
                        !_settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue;
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
                    !_settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue;
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
                var autoConnect = _settings.GetClientSetting(SettingsKeys.AutoConnectPrompt).BoolValue;

                var connection = $"{address}:{port}";
                if (autoConnect)
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


            _settings.GetPositionSetting(SettingsKeys.RadioX).DoubleValue = 100;
            _settings.GetPositionSetting(SettingsKeys.RadioY).DoubleValue = 100;

            _settings.GetPositionSetting(SettingsKeys.RadioWidth).DoubleValue = 122;
            _settings.GetPositionSetting(SettingsKeys.RadioHeight).DoubleValue = 270;

            _settings.GetPositionSetting(SettingsKeys.RadioOpacity).DoubleValue = 1.0;

            _settings.Save();
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
            _settings.GetClientSetting(SettingsKeys.AutoConnectPrompt).BoolValue =
                (bool) AutoConnectPromptToggle.IsChecked;
            _settings.Save();
        }

        private void RadioOverlayTaskbarItem_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue =
                (bool) RadioOverlayTaskbarItem.IsChecked;
            _settings.Save();

            if (_radioOverlayWindow != null)
                _radioOverlayWindow.ShowInTaskbar = !_settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue;
            else if (_awacsRadioOverlay != null) _awacsRadioOverlay.ShowInTaskbar = !_settings.GetClientSetting(SettingsKeys.RadioOverlayTaskbarHide).BoolValue;
        }

        private void DCSRefocus_OnClick_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RefocusDCS).BoolValue =
                (bool) RefocusDCS.IsChecked;
            _settings.Save();
        }

        private void ExpandInputDevices_OnClick_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "You must restart SRS for this setting to take effect.\n\nTurning this on will allow almost any DirectX device to be used as input expect a Mouse but may cause issues with other devices being detected",
                "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _settings.GetClientSetting(SettingsKeys.ExpandControls).BoolValue =
                (bool) ExpandInputDevices.IsChecked;
            _settings.Save();
        }

        private void LaunchAddressTab(object sender, RoutedEventArgs e)
        {
            TabControl.SelectedItem = FavouritesSeversTab;
        }

        private void RadioSoundEffects_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioEffects).BoolValue =
                (bool) RadioSoundEffects.IsChecked;
            _settings.Save();
        }

        private void RadioTxStart_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioTxEffects_Start).BoolValue =
                (bool) RadioTxStartToggle.IsChecked;
            _settings.Save();
        }

        private void RadioTxEnd_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioTxEffects_End).BoolValue =
                (bool) RadioTxEndToggle.IsChecked;
            _settings.Save();
        }

        private void RadioRxStart_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioRxEffects_Start).BoolValue =
                (bool) RadioRxStartToggle.IsChecked;
            _settings.Save();
        }

        private void RadioRxEnd_Click(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioRxEffects_End).BoolValue =
                (bool) RadioRxEndToggle.IsChecked;
            _settings.Save();
        }

        private void AudioSelectChannel_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.AutoSelectPresetChannel).BoolValue =
                (bool) AutoSelectChannel.IsChecked;
            _settings.Save();
        }

        private void RadioSoundEffectsClipping_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.RadioEffectsClipping).BoolValue =
                (bool)RadioSoundEffectsClipping.IsChecked;
            _settings.Save();

        }

        private void MinimiseToTray_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.MinimiseToTray).BoolValue =
                (bool)MinimiseToTray.IsChecked;
            _settings.Save();
        }

        private void StartMinimised_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.StartMinimised).BoolValue =
                (bool)StartMinimised.IsChecked;
            _settings.Save();
        }

        private void AllowDCSPTT_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.AllowDCSPTT).BoolValue =
                (bool)AllowDCSPTT.IsChecked;
            _settings.Save();
        }

        private void AlwaysAllowHotas_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.AlwaysAllowHotasControls).BoolValue =
                (bool)AlwaysAllowHotas.IsChecked;
            _settings.Save();

        }

        private void CheckForBetaUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            _settings.GetClientSetting(SettingsKeys.CheckForBetaUpdates).BoolValue =
                (bool)CheckForBetaUpdates.IsChecked;
            _settings.Save();
        }

        private void ConnectExternalAWACSMode_OnClick(object sender, RoutedEventArgs e)
        {
            if (_client == null || 
                !_clientStateSingleton.IsConnected ||
                !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE) ||
                (!_clientStateSingleton.InExternalAWACSMode &&
                string.IsNullOrWhiteSpace(ExternalAWACSModePassword.Text)))
            {
                return;
            }

            _clientStateSingleton.LastSeenName = ExternalAWACSModeName.Text;

            // Already connected, disconnect
            if (_clientStateSingleton.InExternalAWACSMode)
            {
                _client.DisconnectExternalAWACSMode();
            }
            else
            {
                _client.ConnectExternalAWACSMode(ExternalAWACSModePassword.Text.Trim(), ExternalAWACSModeConnectionChanged);
            }
        }

        private void ExternalAWACSModeConnectionChanged(bool result, int coalition)
        {
            if (result)
            {
                _clientStateSingleton.InExternalAWACSMode = true;
                _clientStateSingleton.DcsPlayerSideInfo.side = coalition;
                _clientStateSingleton.DcsPlayerSideInfo.name = _clientStateSingleton.LastSeenName;
                _clientStateSingleton.DcsPlayerRadioInfo.name = _clientStateSingleton.LastSeenName;

                ConnectExternalAWACSMode.Content = "Disconnect External AWACS MODE (EAM)";
                ExternalAWACSModePassword.IsEnabled = false;
                ExternalAWACSModeName.IsEnabled = false;
            }
            else
            {
                _clientStateSingleton.InExternalAWACSMode = false;
                _clientStateSingleton.DcsPlayerSideInfo.side = 0;
                _clientStateSingleton.DcsPlayerSideInfo.name = "";
                _clientStateSingleton.DcsPlayerRadioInfo.name = "";
                _clientStateSingleton.DcsPlayerRadioInfo.LastUpdate = 0;
                _clientStateSingleton.LastSent = 0;

                ConnectExternalAWACSMode.Content = "Connect External AWACS MODE (EAM)";
                ExternalAWACSModePassword.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
                ExternalAWACSModeName.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
            }
        }
    }
}