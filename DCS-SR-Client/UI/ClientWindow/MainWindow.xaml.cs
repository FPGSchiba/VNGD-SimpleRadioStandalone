using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.ClientList;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.Favourites;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Overlay;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NLog;
using WPFCustomMessageBox;
using InputBinding = Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.InputBinding;
using Sentry;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPage;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    enum SrsTeam
    {
        RedTeam,
        BlueTeam
    }

    public enum LoginType
    {
        Guest,
        Member,
        Officer,
        Administrator
    }

    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton, int switchTo);
        public delegate void UpdateChannelCallback(ProfileSettingsKeys channel, float balance);

        private readonly AudioManager _audioManager;

        private readonly string _guid;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private AudioPreview _audioPreview;
        private SrsClientSyncHandler _client;
        private DCSAutoConnectHandler _dcsAutoConnectListener;
        private int _port = 5002;

        private const int NoWindowOpen = 17;  // Update when adding new panel
        private int _windowOpen = NoWindowOpen;

        // State
        public bool LoggedIn
        {
            get { return (bool)this.GetValue(LOGGED_IN_PROPERTY); }
            set { this.SetValue(LOGGED_IN_PROPERTY, value); }
        }
        private string _playerName = "";
        private string _coalitionPassword = "";
        public LoginType LoginType { get; private set; }
        public DateTime ConnectedAt { get; private set; }

        private int OpenPage
        {
            get { return (int)this.GetValue(OPEN_PAGE_PROPERTY); }
            set { this.SetValue(OPEN_PAGE_PROPERTY, value); }
        }

        private int _oldOpenSupportPage;
        private int _oldOpenSettingsPage;

        public static readonly DependencyProperty OPEN_PAGE_PROPERTY =
            DependencyProperty.Register(nameof(OpenPage),
                typeof(int),
                typeof(MainWindow),
                new PropertyMetadata(-1, OpenPagePropertyChanged));
        
        public static readonly DependencyProperty LOGGED_IN_PROPERTY = DependencyProperty.Register(nameof(LoggedIn), typeof(bool), typeof(MainWindow), new PropertyMetadata(false,
            (o, args) =>
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                
                if ((bool)args.NewValue && mainWindow != null)
                {
                    mainWindow.HomeNavigation.IsEnabled = false;
                    mainWindow.HomeNavigation.Visibility = Visibility.Hidden;
                }
                else if (mainWindow != null)
                {
                    mainWindow.HomeNavigation.IsEnabled = true;
                    mainWindow.HomeNavigation.Visibility = Visibility.Visible;
                }
            }));

        // Pages (Page & Index)
        private WelcomePage _welcomePage;
        private const int WelcomeIndex = 0;

        private SupportPage _supportPage;
        private const int SupportIndex = 1;

        private LoginPage _loginPage;
        private const int LoginIndex = 2;

        private GuestPage _guestPage;
        private const int GuestIndex = 3;

        private GuestSuccess _guestSuccessPage;
        private const int GuestSuccessIndex = 4;

        private HomePage _homePage;
        private const int HomePageIndex = 5;
        
        private SettingsPage _settingsPage;
        private const int SettingsIndex = 6;

        // Sentry Transactions
        private ITransactionTracer _connectionTransaction;
        private ISpan _connectioNetworkSpan;
        private ISpan _connectionAwacsSpan;
        
        // Statics
        private const string RegistryPath = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone";

        // Menu Radio Overlays
        private RadioOverlayMenuSelect _radioOverlayMenuSelect;
        public const int MenuSelectIndex = 15;

        // Vertical Radio-Overlays 
        private RadioOverlayWindowOneVertical _radioOverlayWindowOneVertical;
        public const int OneVerticalIndex = 8;
        private RadioOverlayWindowTwoVertical _radioOverlayWindowTwoVertical;
        public const int TwoVerticalIndex = 0;
        private RadioOverlayWindowThreeVertical _radioOverlayWindowThreeVertical;
        public const int ThreeVerticalIndex = 1;
        private RadioOverlayWindowFiveVertical _radioOverlayWindowFiveVertical;
        public const int FiveVerticalIndex = 2;
        private RadioOverlayWindowTenVertical _radioOverlayWindowTenVertical;
        public const int TenVerticalIndex = 3;
        private RadioOverlayWindowTenVerticalLong _radioOverlayWindowTenVerticalLong;
        public const int TenVerticalLongIndex = 10;
        private RadioOverlayWindowTenTransparent _radioOverlayWindowTenTransparent;
        public const int TransparentIndex = 12;
        private RadioOverlayWindowTenSwitch _radioOverlayWindowTenSwitch;
        public const int SwitchIndex = 13;
        private RadioOverlayWindowEngineering _radioOverlayWindowEngineering;
        public const int EngineeringIndex = 16;

        // Horizontal Radio-Overlays
        private RadioOverlayWindowOneHorizontal _radioOverlayWindowOneHorizontal;
        public const int OneHorizontalIndex = 9;
        private RadioOverlayWindowTwoHorizontal _radioOverlayWindowTwoHorizontal;
        public const int TwoHorizontalIndex = 4;
        private RadioOverlayWindowThreeHorizontal _radioOverlayWindowThreeHorizontal;
        public const int ThreeHorizontalIndex = 5;
        private RadioOverlayWindowFiveHorizontal _radioOverlayWindowFiveHorizontal;
        public const int FiveHorizontalIndex = 6;
        private RadioOverlayWindowTenHorizontal _radioOverlayWindowTenHorizontal;
        public const int TenHorizontalIndex = 7;
        private RadioOverlayWindowTenHorizontalWide _radioOverlayWindowTenHorizontalWide;
        public const int TenHorizontalWideIndex = 11;

        // Dragable Radio-Overlay
        private RadioOverlayWindowDragable _radioOverlayWindowDragable;
        public const int DragableIndex = 14;

        // Windows array
        private readonly Window[] _windows = new Window[NoWindowOpen];

        private IPAddress _resolvedIp;
        private ServerSettingsWindow _serverSettingsWindow;

        private ClientListWindow _clientListWindow;

        //used to debounce toggle
        private long _toggleShowHide;
        private readonly DispatcherTimer _updateTimer;
        private ServerAddress _serverAddress;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        /// <remarks>Used in the XAML for DataBinding many things</remarks>
        public ClientStateSingleton ClientState { get; } = ClientStateSingleton.Instance;

        /// <remarks>Used in the XAML for DataBinding the connected client count</remarks>
        public ConnectedClientsSingleton Clients { get; } = ConnectedClientsSingleton.Instance;

        /// <remarks>Used in the XAML for DataBinding input audio related UI elements</remarks>
        public AudioInputSingleton AudioInput { get; } = AudioInputSingleton.Instance;

        /// <remarks>Used in the XAML for DataBinding output audio related UI elements</remarks>
        public AudioOutputSingleton AudioOutput { get; } = AudioOutputSingleton.Instance;

        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        public MainWindow()
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            InitializeComponent();

            // Initialize Pages
            InitPages();

            DisplayFrame.Content = _welcomePage;

            // Initialize ToolTip controls
            ToolTips.Init();

            // Initialize images/icons
            Images.Init();

            // Initialise sounds
            Sounds.Init();

            // Set up tooltips that are always defined
            InitToolTips();

            DataContext = this;

            _windows[TwoVerticalIndex] = _radioOverlayWindowTwoVertical;
            _windows[ThreeVerticalIndex] = _radioOverlayWindowThreeVertical;
            _windows[FiveVerticalIndex] = _radioOverlayWindowFiveVertical;
            _windows[TenVerticalIndex] = _radioOverlayWindowTenVertical;
            _windows[TwoHorizontalIndex] = _radioOverlayWindowTwoHorizontal;
            _windows[ThreeHorizontalIndex] = _radioOverlayWindowThreeHorizontal;
            _windows[FiveHorizontalIndex] = _radioOverlayWindowFiveHorizontal;
            _windows[TenHorizontalIndex] = _radioOverlayWindowTenHorizontal;
            _windows[OneVerticalIndex] = _radioOverlayWindowOneVertical;
            _windows[OneHorizontalIndex] = _radioOverlayWindowOneHorizontal;
            _windows[TenVerticalLongIndex] = _radioOverlayWindowTenVerticalLong;
            _windows[TenHorizontalWideIndex] = _radioOverlayWindowTenHorizontalWide;
            _windows[TransparentIndex] = _radioOverlayWindowTenTransparent;
            _windows[SwitchIndex] = _radioOverlayWindowTenSwitch;
            _windows[EngineeringIndex] = _radioOverlayWindowEngineering;
            _windows[DragableIndex] = _radioOverlayWindowDragable;
            _windows[MenuSelectIndex] = _radioOverlayMenuSelect;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientY).DoubleValue;
            Assembly assembly = Assembly.GetExecutingAssembly();

            string version = Regex.Replace(AssemblyName.GetAssemblyName(assembly.Location).Version.ToString(), @"(?<=\d\.\d\.\d)(.*)(?=)", "");

            Title = "VCS-SRS - v" + version; //UpdaterChecker.VERSION
            SRSVersionText.Text = "VCS-SRS v" + version;

            CheckWindowVisibility();

            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.StartMinimised))
            {
                Hide();
                WindowState = WindowState.Minimized;

                _logger.Info("Started DCS-SimpleRadio Client " + version + " minimized"); //UpdaterChecker.VERSION
            }
            else
            {
                _logger.Info("Started DCS-SimpleRadio Client " + version); //UpdaterChecker.VERSION
            }

            _guid = ClientStateSingleton.Instance.ShortGUID;
            Analytics.Log("Client", "Startup", _globalSettings.GetClientSetting(GlobalSettingsKeys.ClientIdLong).RawValue);

            InitSettingsScreen();

            InitSettingsProfiles();
            ReloadProfile();

            InitInput();

            FavouriteServersViewModel = new FavouriteServersViewModel(new CsvFavouriteServerStore());

            InitDefaultAddress();

            SpeakerBoost.Value = _globalSettings.GetClientSetting(GlobalSettingsKeys.SpeakerBoost).DoubleValue;

            Speaker_VU.Value = -100;
            Mic_VU.Value = -100;

            ExternalAWACSModeName.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).RawValue;

            _audioManager = new AudioManager(AudioOutput.WindowsN);
            _audioManager.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);

            if (SpeakerBoostLabel != null)
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(_audioManager.SpeakerBoost);
            }

            // Use Update Checker for automatic Updates, needs rewrite of the UpdateCheker

            InitFlowDocument();

            _dcsAutoConnectListener = new DCSAutoConnectHandler(AutoConnect);

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdatePlayerLocationAndVuMeters;
            _updateTimer.Start();
        }

        public String GetPlayerName()
        {
            if (LoggedIn)
            {
                return _playerName;
            }

            return null;
        }

        private void CheckWindowVisibility()
        {
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.DisableWindowVisibilityCheck))
            {
                _logger.Info("Window visibility check is disabled, skipping");
                return;
            }
            
            bool radioWindowVisible = false;
        
            int mainWindowX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientX).DoubleValue;
            int mainWindowY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientY).DoubleValue;
        
            _logger.Trace($"Checking window visibility for main client window {{X={mainWindowX},Y={mainWindowY}}}");
        
            bool mainWindowVisible = CheckWindowVisibilityForPanels(mainWindowX, mainWindowY, ref radioWindowVisible);
        
            if (!mainWindowVisible)
            {
                ResetMainWindowPosition(mainWindowX, mainWindowY);
            }
        
            if (!radioWindowVisible)
            {
                ResetRadioWindowPositions();
            }
        }
        
        private bool CheckWindowVisibilityForPanels(int mainWindowX, int mainWindowY, ref bool radioWindowVisible)
        {
            bool mainWindowVisible = false;
        
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                var primary = "primary ";
                _logger.Trace($"Checking {(screen.Primary ? primary : "")}screen {screen.DeviceName} with bounds {screen.Bounds} for window visibility");
        
                if (screen.Bounds.Contains(mainWindowX, mainWindowY))
                {
                    _logger.Trace($"Main client window {{X={mainWindowX},Y={mainWindowY}}} is visible on {(screen.Primary ? primary : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    mainWindowVisible = true;
                }
        
                radioWindowVisible = CheckRadioWindowVisibility(screen, radioWindowVisible);
            }
        
            return mainWindowVisible;
        }
        
        private bool CheckRadioWindowVisibility(System.Windows.Forms.Screen screen, bool radioWindowVisible)
        {
            int[] radioWindowX = {
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneVerticalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenVerticalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalX).DoubleValue
            };
        
            int[] radioWindowY = {
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneVerticalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenVerticalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalY).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalY).DoubleValue
            };
        
            for (int i = 0; i < radioWindowX.Length; i++)
            {
                if (screen.Bounds.Contains(radioWindowX[i], radioWindowY[i]))
                {
                    _logger.Trace($"Radio overlay {{X={radioWindowX[i]},Y={radioWindowY[i]}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
            }
        
            return radioWindowVisible;
        }
        
        private void ResetMainWindowPosition(int mainWindowX, int mainWindowY)
        {
            MessageBox.Show(this,
                "The SRS client window is no longer visible likely due to a monitor reconfiguration.\n\nThe position will be reset to default to fix this issue.",
                "SRS window position reset",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        
            _logger.Warn($"Main client window outside visible area of monitors, resetting position ({mainWindowX},{mainWindowY}) to defaults");
        
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientX, 200);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientY, 200);
        
            Left = 200;
            Top = 200;
        }
        
        private void ResetRadioWindowPositions()
        {
            MessageBox.Show(this,
                "The SRS radio overlay is no longer visible likely due to a monitor reconfiguration.\n\nThe position will be reset to default to fix this issue.",
                "SRS window position reset",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        
            _logger.Warn("Radio overlay windows outside visible area of monitors, resetting positions to defaults");
        
            GlobalSettingsKeys[] radioWindowX = {
                GlobalSettingsKeys.RadioMenuSelectX,
                GlobalSettingsKeys.RadioOverlayWindowDragableX,
                GlobalSettingsKeys.RadioOneVerticalX,
                GlobalSettingsKeys.RadioTwoVerticalX,
                GlobalSettingsKeys.RadioThreeVerticalX,
                GlobalSettingsKeys.RadioFiveX,
                GlobalSettingsKeys.RadioTenVerticalX,
                GlobalSettingsKeys.RadioTenLongVerticalX,
                GlobalSettingsKeys.RadioTenTransparentX,
                GlobalSettingsKeys.RadioTenSwitchX,
                GlobalSettingsKeys.RadioEngineeringX,
                GlobalSettingsKeys.RadioOneHorizontalX,
                GlobalSettingsKeys.RadioTwoHorizontalX,
                GlobalSettingsKeys.RadioThreeHorizontalX,
                GlobalSettingsKeys.RadioFiveHorizontalX,
                GlobalSettingsKeys.RadioTenHorizontalX,
                GlobalSettingsKeys.RadioTenWideHorizontalX
            };
        
            GlobalSettingsKeys[] radioWindowY = {
                GlobalSettingsKeys.RadioMenuSelectY,
                GlobalSettingsKeys.RadioOverlayWindowDragableY,
                GlobalSettingsKeys.RadioOneVerticalY,
                GlobalSettingsKeys.RadioTwoVerticalY,
                GlobalSettingsKeys.RadioThreeVerticalY,
                GlobalSettingsKeys.RadioFiveY,
                GlobalSettingsKeys.RadioTenVerticalY,
                GlobalSettingsKeys.RadioTenLongVerticalY,
                GlobalSettingsKeys.RadioTenTransparentY,
                GlobalSettingsKeys.RadioTenSwitchY,
                GlobalSettingsKeys.RadioEngineeringY,
                GlobalSettingsKeys.RadioOneHorizontalY,
                GlobalSettingsKeys.RadioTwoHorizontalY,
                GlobalSettingsKeys.RadioThreeHorizontalY,
                GlobalSettingsKeys.RadioFiveHorizontalY,
                GlobalSettingsKeys.RadioTenHorizontalY,
                GlobalSettingsKeys.RadioTenWideHorizontalY
            };
        
            for (int i = 0; i < radioWindowX.Length; i++)
            {
                _globalSettings.SetPositionSetting(radioWindowX[i], 300);
                _globalSettings.SetPositionSetting(radioWindowY[i], 300);
            }
        
            ResetRadioWindowPositionsToDefault();
        }
        
        private void ResetRadioWindowPositionsToDefault()
        {
            ResetWindowPosition(_radioOverlayMenuSelect);
            ResetWindowPosition(_radioOverlayWindowDragable);
            ResetWindowPosition(_radioOverlayWindowOneVertical);
            ResetWindowPosition(_radioOverlayWindowTwoVertical);
            ResetWindowPosition(_radioOverlayWindowThreeVertical);
            ResetWindowPosition(_radioOverlayWindowFiveVertical);
            ResetWindowPosition(_radioOverlayWindowTenVertical);
            ResetWindowPosition(_radioOverlayWindowTenVerticalLong);
            ResetWindowPosition(_radioOverlayWindowTenTransparent);
            ResetWindowPosition(_radioOverlayWindowTenSwitch);
            ResetWindowPosition(_radioOverlayWindowEngineering);
            ResetWindowPosition(_radioOverlayWindowOneHorizontal);
            ResetWindowPosition(_radioOverlayWindowTwoHorizontal);
            ResetWindowPosition(_radioOverlayWindowThreeHorizontal);
            ResetWindowPosition(_radioOverlayWindowFiveHorizontal);
            ResetWindowPosition(_radioOverlayWindowTenHorizontal);
            ResetWindowPosition(_radioOverlayWindowTenHorizontalWide);
        }
        
        private static void ResetWindowPosition(Window window)
        {
            if (window != null)
            {
                window.Left = 300;
                window.Top = 300;
            }
        }

        private void InitFlowDocument()
        {
            //make hyperlinks work
            var hyperlinks = WPFElementHelper.GetVisuals(this.AboutFlowDocument).OfType<Hyperlink>();
            foreach (var link in hyperlinks)
                link.RequestNavigate += (sender, args) =>
                {
                    Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri));
                    args.Handled = true;
                };
        }

        private void InitDefaultAddress()
        {
            // legacy setting migration
            if (!string.IsNullOrEmpty(_globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).StringValue) &&
                FavouriteServersViewModel.Addresses.Count == 0)
            {
                var oldAddress = new ServerAddress(_globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).StringValue,
                    _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).StringValue, null, true);
                FavouriteServersViewModel.Addresses.Add(oldAddress);
            }

            ServerAddress = FavouriteServersViewModel.DefaultServerAddress;
        }

        private void InitSettingsProfiles()
        {
            ControlsProfile.IsEnabled = false;
            ControlsProfile.Items.Clear();
            foreach (var profile in _globalSettings.ProfileSettingsStore.InputProfiles.Keys)
            {
                ControlsProfile.Items.Add(profile);
            }
            ControlsProfile.IsEnabled = true;
            ControlsProfile.SelectedIndex = 0;

            CurrentProfile.Content = _globalSettings.ProfileSettingsStore.CurrentProfileName;

        }

        void ReloadProfile()
        {
            //switch profiles
            _logger.Info(ControlsProfile.SelectedValue as string + " - Profile now in use");
            _globalSettings.ProfileSettingsStore.CurrentProfileName = ControlsProfile.SelectedValue as string;

            //redraw UI
            ReloadInputBindings();
            ReloadProfileSettings();
            ReloadRadioAudioChannelSettings();

            CurrentProfile.Content = _globalSettings.ProfileSettingsStore.CurrentProfileName;
        }

        private void InitInput()
        {
            InputManager = new InputDeviceManager(this, ToggleOverlay, UpdateChannelSettings);

            InitSettingsProfiles();

            ControlsProfile.SelectionChanged += OnProfileDropDownChanged;

            RadioStartTransmitEffect.SelectionChanged += OnRadioStartTransmitEffectChanged;
            RadioEndTransmitEffect.SelectionChanged += OnRadioEndTransmitEffectChanged;

            IntercomStartTransmitEffect.SelectionChanged += OnIntercomStartTransmitEffectChanged;
            IntercomEndTransmitEffect.SelectionChanged += OnIntercomEndTransmitEffectChanged;

            // How to change the keybind names in the keybinds tab

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

            IntercomPTT.InputName = "Special Intercom Select & PTT";
            IntercomPTT.ControlInputBinding = InputBinding.IntercomPTT;
            IntercomPTT.InputDeviceManager = InputManager;

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

            TransponderIDENT.InputName = "Transponder IDENT Toggle";
            TransponderIDENT.ControlInputBinding = InputBinding.TransponderIDENT;
            TransponderIDENT.InputDeviceManager = InputManager;

            RadioVolumeUp.InputName = "Radio Volume Up";
            RadioVolumeUp.ControlInputBinding = InputBinding.RadioVolumeUp;
            RadioVolumeUp.InputDeviceManager = InputManager;

            RadioVolumeDown.InputName = "Radio Volume Down";
            RadioVolumeDown.ControlInputBinding = InputBinding.RadioVolumeDown;
            RadioVolumeDown.InputDeviceManager = InputManager;

            //Dabble Added Following Keybinds
            RadioSwap.InputName = "Swap Standby Frequency (WIP)";   //Dabble Added
            RadioSwap.ControlInputBinding = InputBinding.RadioSwap;
            RadioSwap.InputDeviceManager = InputManager;
            
            // Audio Balancing
            LeftBalance.InputName = "Left Balance (WIP)";   //Dabble Added
            LeftBalance.ControlInputBinding = InputBinding.LeftBalance;
            LeftBalance.InputDeviceManager = InputManager;

            RightBalance.InputName = "Right Balance (WIP)";   //Dabble Added
            RightBalance.ControlInputBinding = InputBinding.RightBalance;
            RightBalance.InputDeviceManager = InputManager;

            CenterBalance.InputName = "Reset Balance (WIP)";   //Dabble Added
            CenterBalance.ControlInputBinding = InputBinding.CenterBalance;
            CenterBalance.InputDeviceManager = InputManager;

            //Panel Night Mode
            PanelNightMode.InputName = "Panel Night Mode Toggle (WIP)";   //Dabble Added
            PanelNightMode.ControlInputBinding = InputBinding.PanelNightMode;
            PanelNightMode.InputDeviceManager = InputManager;

            //Specific Radio Panel Toggles
            Radio1HToggle.InputName = "1 Radio Horizontal";   //Dabble Added
            Radio1HToggle.ControlInputBinding = InputBinding.Radio1HToggle;
            Radio1HToggle.InputDeviceManager = InputManager;

            Radio1VToggle.InputName = "1 Radio Vertical";   //Dabble Added
            Radio1VToggle.ControlInputBinding = InputBinding.Radio1VToggle;
            Radio1VToggle.InputDeviceManager = InputManager;

            Radio2HToggle.InputName = "2 Radio Horizontal";   //Dabble Added
            Radio2HToggle.ControlInputBinding = InputBinding.Radio2HToggle;
            Radio2HToggle.InputDeviceManager = InputManager;

            Radio2VToggle.InputName = "2 Radio Vertical";   //Dabble Added
            Radio2VToggle.ControlInputBinding = InputBinding.Radio2VToggle;
            Radio2VToggle.InputDeviceManager = InputManager;

            Radio3HToggle.InputName = "3 Radio Horizontal";   //Dabble Added
            Radio3HToggle.ControlInputBinding = InputBinding.Radio3HToggle;
            Radio3HToggle.InputDeviceManager = InputManager;

            Radio3VToggle.InputName = "3 Radio Vertical";   //Dabble Added
            Radio3VToggle.ControlInputBinding = InputBinding.Radio3VToggle;
            Radio3VToggle.InputDeviceManager = InputManager;

            Radio5HToggle.InputName = "5 Radio Horizontal";   //Dabble Added
            Radio5HToggle.ControlInputBinding = InputBinding.Radio5HToggle;
            Radio5HToggle.InputDeviceManager = InputManager;

            Radio5VToggle.InputName = "5 Radio Vertical";   //Dabble Added
            Radio5VToggle.ControlInputBinding = InputBinding.Radio5VToggle;
            Radio5VToggle.InputDeviceManager = InputManager;

            Radio10HToggle.InputName = "10 Radio Horizontal";   //Dabble Added
            Radio10HToggle.ControlInputBinding = InputBinding.Radio10HToggle;
            Radio10HToggle.InputDeviceManager = InputManager;

            Radio10VToggle.InputName = "10 Radio Vertical";   //Dabble Added
            Radio10VToggle.ControlInputBinding = InputBinding.Radio10VToggle;
            Radio10VToggle.InputDeviceManager = InputManager;

            Radio10HWToggle.InputName = "10 Radio Horiz. Ultra-Wide";   //Dabble Added
            Radio10HWToggle.ControlInputBinding = InputBinding.Radio10HWToggle;
            Radio10HWToggle.InputDeviceManager = InputManager;

            Radio10VLToggle.InputName = "10 Radio Vertical Long";   //Dabble Added
            Radio10VLToggle.ControlInputBinding = InputBinding.Radio10VLToggle;
            Radio10VLToggle.InputDeviceManager = InputManager;

            Radio10TToggle.InputName = "Compact Panel - Original";   //Dabble Added
            Radio10TToggle.ControlInputBinding = InputBinding.Radio10TToggle;
            Radio10TToggle.InputDeviceManager = InputManager;

            Radio10SToggle.InputName = "Compact Panel - New";   //Dabble Added
            Radio10SToggle.ControlInputBinding = InputBinding.Radio10SToggle;
            Radio10SToggle.InputDeviceManager = InputManager;
        }

        #region Pages and Navigation

        private void InitPages()
        {
            _welcomePage = new WelcomePage();
            _supportPage = new SupportPage();
            _loginPage = new LoginPage();
            _guestPage = new GuestPage();
            _guestSuccessPage = new GuestSuccess();
            _homePage = new HomePage();
            _settingsPage = new SettingsPage();
            OpenPage = WelcomeIndex;
            
            HomeNavigation.IsEnabled = false;
            HomeNavigation.Visibility = Visibility.Hidden;
        }

        private void OpenPageByIndex(int index)
        {
            if (index != SupportIndex)
            {
                SupportNavigation.IsEnabled = true;
            }
            if (index != SettingsIndex)
            {
                SettingsNavigation.IsEnabled = true;
            }
            
            switch (index)
            {
                case WelcomeIndex:
                    DisplayFrame.Content = _welcomePage;
                    break;
                case SupportIndex:
                    DisplayFrame.Content = _supportPage;
                    SupportNavigation.IsEnabled = false;
                    break;
                case LoginIndex:
                    DisplayFrame.Content = _loginPage;
                    break;
                case GuestIndex:
                    DisplayFrame.Content = _guestPage;
                    break;
                case GuestSuccessIndex:
                    DisplayFrame.Content = _guestSuccessPage;
                    break;
                case HomePageIndex:
                    DisplayFrame.Content = _homePage;
                    break;
                case SettingsIndex:
                    DisplayFrame.Content = _settingsPage;
                    SettingsNavigation.IsEnabled = false;
                    break;
                default:
                    _logger.Error($"Page: {index} could not be found.");
                    break;
            }

            OpenPage = index;
        }

        private static void OpenPagePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            // lets see if we still need this - Schiba (08/21/2024)
            MainWindow mainWindow = source as MainWindow;
            int newValue = Convert.ToInt32(e.NewValue);
            if (mainWindow != null)
            {
                switch (newValue)
                {
                    case WelcomeIndex:
                        mainWindow.HomeNavigation.IsEnabled = false;
                        mainWindow.HomeNavigation.Visibility = Visibility.Hidden;
                        break;
                    case SupportIndex:
                        break;
                    case LoginIndex:
                        break;
                    case GuestIndex:
                        break;
                    case GuestSuccessIndex:
                        break;
                    case HomePageIndex:
                        break;
                    case SettingsIndex:
                        break;
                    default:
                        mainWindow._logger.Error($"Page: {newValue} could not be found.");
                        break;
                }
            }
        }

        public void On_WelcomeLoginClicked()
        {
            OpenPageByIndex(LoginIndex);
        }

        public void On_WelcomeGuestCLicked()
        {
            OpenPageByIndex(GuestIndex);
        }

        public void On_LoginLoginClicked(IPAddress ip, int port, string playerName, string coalitionPassword, LoginType loginType)
        {
            _resolvedIp = ip;
            _port = port;
            _coalitionPassword = coalitionPassword;
            _playerName = playerName;
            LoginType = loginType;
            Connect(ip, port);
        }

        public void On_LoginBackClicked()
        {
            OpenPageByIndex(WelcomeIndex);
        }

        public void On_GuestLoginClicked(IPAddress ip, int port, string playerName, string coalitionPassword)
        {
            _resolvedIp = ip;
            _port = port;
            _coalitionPassword = coalitionPassword;
            _playerName = playerName;
            LoginType = LoginType.Guest;
            Connect(ip, port);
        }

        public void On_GuestBackClicked()
        {
            OpenPageByIndex(WelcomeIndex);
        }

        public void On_GuestSuccessAcceptClicked()
        {
            OpenPageByIndex(HomePageIndex);
        }

        public void On_HomeLogOutClicked()
        {
            ConnectAwacsMode();
            Stop();
            OpenPageByIndex(WelcomeIndex);
        }

        private void SupportNavigation_Click(object sender, RoutedEventArgs e)
        {
            // Can create a Loop if back and forth between the two pages
            _oldOpenSupportPage = OpenPage == SettingsIndex ? _oldOpenSettingsPage : OpenPage;
            OpenPageByIndex(SupportIndex);
        }

        private void SettingsNavigation_Click(object sender, RoutedEventArgs e)
        {
            // Can create a Loop if back and forth between the two pages
            _oldOpenSettingsPage = OpenPage == SupportIndex ? _oldOpenSupportPage : OpenPage;
            OpenPageByIndex(SettingsIndex);
        }
        
        public void On_SettingsBackClicked()
        {
            OpenPageByIndex(_oldOpenSettingsPage);
        }
        
        public void On_SupportBackClicked()
        {
            OpenPageByIndex(_oldOpenSupportPage);
        }

        #endregion

        private void OnProfileDropDownChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ControlsProfile.IsEnabled)
                ReloadProfile();
        }

        private void OnRadioStartTransmitEffectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RadioStartTransmitEffect.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.RadioTransmissionStartSelection, ((CachedAudioEffect)RadioStartTransmitEffect.SelectedItem).FileName);
            }
        }

        private void OnRadioEndTransmitEffectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RadioEndTransmitEffect.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.RadioTransmissionEndSelection, ((CachedAudioEffect)RadioEndTransmitEffect.SelectedItem).FileName);
            }
        }

        private void OnIntercomStartTransmitEffectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IntercomStartTransmitEffect.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.IntercomTransmissionStartSelection, ((CachedAudioEffect)IntercomStartTransmitEffect.SelectedItem).FileName);
            }
        }

        private void OnIntercomEndTransmitEffectChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IntercomEndTransmitEffect.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.IntercomTransmissionEndSelection, ((CachedAudioEffect)IntercomEndTransmitEffect.SelectedItem).FileName);
            }
        }

        private void ReloadInputBindings()
        {
            this.Radio1.LoadInputSettings();
            this.Radio2.LoadInputSettings();
            this.Radio3.LoadInputSettings();
            this.PTT.LoadInputSettings();
            this.Intercom.LoadInputSettings();
            this.IntercomPTT.LoadInputSettings();
            this.Radio4.LoadInputSettings();
            this.Radio5.LoadInputSettings();
            this.Radio6.LoadInputSettings();
            this.Radio7.LoadInputSettings();
            this.Radio8.LoadInputSettings();
            this.Radio9.LoadInputSettings();
            this.Radio10.LoadInputSettings();
            this.Up100.LoadInputSettings();
            this.Up10.LoadInputSettings();
            this.Up1.LoadInputSettings();
            this.Up01.LoadInputSettings();
            this.Up001.LoadInputSettings();
            this.Up0001.LoadInputSettings();
            this.Down100.LoadInputSettings();
            this.Down10.LoadInputSettings();
            this.Down1.LoadInputSettings();
            this.Down01.LoadInputSettings();
            this.Down001.LoadInputSettings();
            this.Down0001.LoadInputSettings();
            this.ToggleGuard.LoadInputSettings();
            this.NextRadio.LoadInputSettings();
            this.PreviousRadio.LoadInputSettings();
            this.ToggleEncryption.LoadInputSettings();
            this.EncryptionKeyIncrease.LoadInputSettings();
            this.EncryptionKeyDecrease.LoadInputSettings();
            this.RadioChannelUp.LoadInputSettings();
            this.RadioChannelDown.LoadInputSettings();
            this.RadioVolumeUp.LoadInputSettings();
            this.RadioVolumeDown.LoadInputSettings();

            //added by dabble
            this.Radio1VToggle.LoadInputSettings();
            this.Radio1HToggle.LoadInputSettings();
            this.Radio2VToggle.LoadInputSettings();
            this.Radio2HToggle.LoadInputSettings();
            this.Radio3VToggle.LoadInputSettings();
            this.Radio3HToggle.LoadInputSettings();
            this.Radio5VToggle.LoadInputSettings();
            this.Radio5HToggle.LoadInputSettings();
            this.Radio10VToggle.LoadInputSettings();
            this.Radio10HToggle.LoadInputSettings();
            this.Radio10VLToggle.LoadInputSettings();
            this.Radio10HWToggle.LoadInputSettings();
            this.Radio10TToggle.LoadInputSettings();
            this.Radio10SToggle.LoadInputSettings();
        }

        private void ReloadRadioAudioChannelSettings()
        {
            Radio1Config.Reload();
            Radio2Config.Reload();
            Radio3Config.Reload();
            Radio4Config.Reload();
            Radio5Config.Reload();
            Radio6Config.Reload();
            Radio7Config.Reload();
            Radio8Config.Reload();
            Radio9Config.Reload();
            Radio10Config.Reload();
            IntercomConfig.Reload();
            
            _settingsPage.ReloadRadioAudioChannelSettings();
        }

        public void UpdateChannelSettings(ProfileSettingsKeys channel, float balance)
        {
            ReloadRadioAudioChannelSettings();
        }

        private void InitToolTips()
        {
            this.ExternalAWACSModePassword.ToolTip = ToolTips.ExternalAWACSModePassword;
            this.ExternalAWACSModeName.ToolTip = ToolTips.ExternalAWACSModeName;
            this.ServerIp.ToolTip = ToolTips.ServerIp;
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
                    ExternalAWACSModePassword.Password = string.IsNullOrWhiteSpace(value.EAMCoalitionPassword) ? "" : value.EAMCoalitionPassword;
                }
            }
        }

        private void UpdatePlayerLocationAndVuMeters(object sender, EventArgs e)
        {
            if (_audioPreview != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (AudioInput.MicrophoneAvailable)
                {
                    Mic_VU.Value = _audioPreview.MicMax;
                }
                Speaker_VU.Value = _audioPreview.SpeakerMax;
            }
            else if (_audioManager != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (AudioInput.MicrophoneAvailable)
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

            ConnectedClientsSingleton.Instance.NotifyAll();
        }

        private void InitSettingsScreen()
        {
            AutoConnectEnabledToggle.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnect);
            AutoConnectPromptToggle.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectPrompt);
            AutoConnectMismatchPromptToggle.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectMismatchPrompt);
            RadioOverlayTaskbarItem.IsChecked =
                _globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
            RefocusDCS.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.RefocusDCS);
            ExpandInputDevices.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.ExpandControls);

            MinimiseToTray.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.MinimiseToTray);
            StartMinimised.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.StartMinimised);

            MicAGC.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AGC);
            MicDenoise.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.Denoise);

            CheckForBetaUpdates.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.CheckForBetaUpdates);
            PlayConnectionSounds.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.PlayConnectionSounds);

            RequireAdminToggle.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.RequireAdmin);

            AutoSelectInputProfile.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoSelectSettingsProfile);

            VAICOMTXInhibitEnabled.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VAICOMTXInhibitEnabled);

            ShowTransmitterName.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.ShowTransmitterName);

            AllowTransmissionsRecord.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AllowRecording);
            RecordTransmissions.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.RecordAudio);
            SingleFileMixdown.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.SingleFileMixdown);
            DisallowedAudioTone.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.DisallowedAudioTone);
            RecordTransmissions_IsEnabled();

            RecordingQuality.IsEnabled = false;
            RecordingQuality.ValueChanged += RecordingQuality_ValueChanged;
            RecordingQuality.Value = double.Parse(_globalSettings.GetClientSetting(GlobalSettingsKeys.RecordingQuality).StringValue[1].ToString());
            RecordingQuality.IsEnabled = true;

            var objValue = Registry.GetValue(RegistryPath, "SRSAnalyticsOptOut", "FALSE");
            if (objValue == null || (string)objValue == "TRUE")
            {
                AllowAnonymousUsage.IsChecked = false;
            }
            else
            {
                AllowAnonymousUsage.IsChecked = true;
            }

            VOXMode.IsEnabled = false;
            VOXMode.Value =
                _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMode);
            VOXMode.ValueChanged += VOXMode_ValueChanged;
            VOXMode.IsEnabled = true;

            VOXMinimimumTXTime.IsEnabled = false;
            VOXMinimimumTXTime.Value =
                _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMinimumTime);
            VOXMinimimumTXTime.ValueChanged += VOXMinimumTime_ValueChanged;
            VOXMinimimumTXTime.IsEnabled = true;

            VOXMinimumRMS.IsEnabled = false;
            VOXMinimumRMS.Value =
                _globalSettings.GetClientSettingDouble(GlobalSettingsKeys.VOXMinimumDB);
            VOXMinimumRMS.ValueChanged += VOXMinimumRMS_ValueChanged;
            VOXMinimumRMS.IsEnabled = true;

            AllowXInputController.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AllowXInputController);
        }

        private void ReloadProfileSettings()
        {
            RadioEncryptionEffectsToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioEncryptionEffects);
            RadioSwitchIsPTT.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTT);

            RadioTxStartToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start);
            RadioTxEndToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End);

            RadioRxStartToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start);
            RadioRxEndToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End);

            RadioMIDSToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect);

            RadioSoundEffects.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioEffects);
            RadioSoundEffectsClipping.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping);
            NATORadioToneToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.NATOTone);
            HQEffectToggle.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone);
            BackgroundRadioNoiseToggle.IsChecked =
                _globalSettings.ProfileSettingsStore.GetClientSettingBool(
                    ProfileSettingsKeys.RadioBackgroundNoiseEffect);

            AutoSelectChannel.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AutoSelectPresetChannel);

            AlwaysAllowHotas.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls);
            AllowDCSPTT.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT);
            AllowRotaryIncrement.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.RotaryStyleIncrement);
            AlwaysAllowTransponderOverlay.IsChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowTransponderOverlay);

            //disable to set without triggering onchange
            PTTReleaseDelay.IsEnabled = false;
            PTTReleaseDelay.ValueChanged += PushToTalkReleaseDelay_ValueChanged;
            PTTReleaseDelay.Value =
                _globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay);
            PTTReleaseDelay.IsEnabled = true;

            PTTStartDelay.IsEnabled = false;
            PTTStartDelay.ValueChanged += PushToTalkStartDelay_ValueChanged;
            PTTStartDelay.Value =
                _globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.PTTStartDelay);
            PTTStartDelay.IsEnabled = true;

            RadioEndTransmitEffect.IsEnabled = false;
            RadioEndTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.RadioTransmissionEnd;
            RadioEndTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedRadioTransmissionEndEffect;
            RadioEndTransmitEffect.IsEnabled = true;

            RadioStartTransmitEffect.IsEnabled = false;
            RadioStartTransmitEffect.SelectedIndex = 0;
            RadioStartTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.RadioTransmissionStart;
            RadioStartTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedRadioTransmissionStartEffect;
            RadioStartTransmitEffect.IsEnabled = true;

            IntercomStartTransmitEffect.IsEnabled = false;
            IntercomStartTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.IntercomTransmissionStart;
            IntercomStartTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedIntercomTransmissionStartEffect;
            IntercomStartTransmitEffect.IsEnabled = true;

            IntercomEndTransmitEffect.IsEnabled = false;
            IntercomEndTransmitEffect.SelectedIndex = 0;
            IntercomEndTransmitEffect.ItemsSource = CachedAudioEffectProvider.Instance.IntercomTransmissionEnd;
            IntercomEndTransmitEffect.SelectedItem = CachedAudioEffectProvider.Instance.SelectedIntercomTransmissionEndEffect;
            IntercomEndTransmitEffect.IsEnabled = true;

            NATOToneVolume.IsEnabled = false;
            NATOToneVolume.ValueChanged += (sender, e) =>
            {
                if (NATOToneVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume, (float)vol);
                }

            };
            NATOToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume)
                                    / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            NATOToneVolume.IsEnabled = true;

            HQToneVolume.IsEnabled = false;
            HQToneVolume.ValueChanged += (sender, e) =>
            {
                if (HQToneVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HQToneVolume, (float)vol);
                }

            };
            HQToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HQToneVolume)
                                  / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HQToneVolume.IsEnabled = true;

            FMEffectVolume.IsEnabled = false;
            FMEffectVolume.ValueChanged += (sender, e) =>
            {
                if (FMEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume, (float)vol);
                }

            };
            FMEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            FMEffectVolume.IsEnabled = true;

            VHFEffectVolume.IsEnabled = false;
            VHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (VHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume, (float)vol);
                }

            };
            VHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            VHFEffectVolume.IsEnabled = true;

            UHFEffectVolume.IsEnabled = false;
            UHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (UHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume, (float)vol);
                }

            };
            UHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            UHFEffectVolume.IsEnabled = true;

            HFEffectVolume.IsEnabled = false;
            HFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (HFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume, (float)vol);
                }

            };
            HFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DEFAULT_SETTINGS_PROFILE_SETTINGS[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HFEffectVolume.IsEnabled = true;
        }

        private void Connect(IPAddress ip, int port)
        {
            if (ClientState.IsConnected)
            {
                Stop();
            }
            else
            {
                _connectionTransaction = SentrySdk.StartTransaction("network", "connection");
                SentrySdk.ConfigureScope(scope => scope.Transaction = _connectionTransaction);
                _connectionTransaction.SetTag("server-address", $"{ip}:{port}");
                
                SaveSelectedInputAndOutput();

                try
                {
                    _connectioNetworkSpan = _connectionTransaction.StartChild("tcp-connection");
                    _resolvedIp = ip;
                    _port = port;

                    _client = new SrsClientSyncHandler(_guid, UpdateUiCallback);

                    _loginPage.Login.IsEnabled = false;
                    _guestPage.Login.IsEnabled = false;

                    _guestPage.LoginInProgress.Opacity = 1;

                    _client.TryConnect(new IPEndPoint(_resolvedIp, _port), ConnectCallback);
                }
                catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    ClientState.IsConnected = false;
                    ToggleServerSettings.IsEnabled = false;
                }
            }
        }

        private void Stop(bool connectionError = false)
        {
            if (ClientState.IsConnected && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.PlayConnectionSounds) && !connectionError)
            {
                try
                {
                    Sounds.BeepDisconnected.Play();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to play disconnect sound");
                }
            }

            ClientState.IsConnectionErrored = connectionError;

            StartStop.Content = "Connect";
            StartStop.IsEnabled = true;
            Mic.IsEnabled = true;
            Speakers.IsEnabled = true;
            MicOutput.IsEnabled = true;
            Preview.IsEnabled = true;
            ClientState.IsConnected = false;
            ToggleServerSettings.IsEnabled = false;

            _loginPage.Login.IsEnabled = true;
            _guestPage.Login.IsEnabled = true;

            _guestPage.LoginInProgress.Opacity = 0;

            ConnectionStatus.Fill = Brushes.Red;

            if (!string.IsNullOrWhiteSpace(ClientState.LastSeenName) &&
                _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).StringValue != ClientState.LastSeenName)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, ClientState.LastSeenName);
            }

            try
            {
                _audioManager.StopEncoding();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to stop audio encoding");
            }

            if (_client != null)
            {
                _client.Disconnect();
                _client = null;
            }

            ClientState.DcsPlayerRadioInfo.Reset();
            ClientState.PlayerCoaltionLocationMetadata.Reset();
        }

        private void SaveSelectedInputAndOutput()
        {
            //save app settings
            // Only save selected microphone if one is actually available, resulting in a crash otherwise
            var defaultValue = "default";
            if (AudioInput.MicrophoneAvailable)
            {
                if (AudioInput.SelectedAudioInput.Value == null)
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, defaultValue);

                }
                else
                {
                    var input = ((MMDevice)AudioInput.SelectedAudioInput.Value).ID;
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, input);
                }
            }

            if (AudioOutput.SelectedAudioOutput.Value == null)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId, defaultValue);
            }
            else
            {
                var output = (MMDevice)AudioOutput.SelectedAudioOutput.Value;
                _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId, output.ID);
            }

            //check if we have optional output
            if (AudioOutput.SelectedMicAudioOutput.Value != null)
            {
                var micOutput = (MMDevice)AudioOutput.SelectedMicAudioOutput.Value;
                _globalSettings.SetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId, micOutput.ID);
            }
            else
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId, "");
            }

            ShowMicPassthroughWarning();
        }

        private void ShowMicPassthroughWarning()
        {
            if (_globalSettings.GetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId).RawValue
                .Equals(_globalSettings.GetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId).RawValue))
            {
                MessageBox.Show("Mic Output and Speaker Output should not be set to the same device!\n\nMic Output is just for recording and not for use as a sidetone. You will hear yourself with a small delay!\n\nHit disconnect and change Mic Output / Passthrough", "Warning", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void ConnectCallback(bool result, bool connectionError, string connection)
        {
            string currentConnection = ServerIp.Text.Trim();
            if (!currentConnection.Contains(":"))
            {
                currentConnection += ":5002";
            }

            if (result)
            {
                if (!ClientState.IsConnected)
                {
                    try
                    {
                        StartStop.Content = "disconnect";
                        StartStop.IsEnabled = true;

                        ConnectionStatus.Fill = Brushes.Orange;

                        ClientState.IsConnected = true;
                        ClientState.IsVoipConnected = false;

                        _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, ServerIp.Text);

                        _audioManager.StartEncoding(_guid, InputManager,
                            _resolvedIp, _port);

                        _logger.Debug("Starting AWACS Mode connection.");
                        _connectioNetworkSpan.Finish();
                        
                        ConnectAwacsMode();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex,
                            "Unable to get audio device - likely output device error - Pick another. Error:" +
                            ex.Message);
                        Stop();

                        var messageBoxResult = CustomMessageBox.ShowYesNo(
                            "Problem initialising Audio Output!\n\nTry a different Output device and please post your clientlog.txt to the support Discord server.\n\nJoin support Discord server now?",
                            "Audio Output Error",
                            "OPEN PRIVACY SETTINGS",
                            "JOIN DISCORD SERVER",
                            MessageBoxImage.Error);

                        if (messageBoxResult == MessageBoxResult.Yes) Process.Start(Properties.Settings.Default.DiscordURL);   //Dabble updated to reflect VNGD SRS Dev Team Discord
                    }
                }
            }
            else if (string.Equals(currentConnection, connection, StringComparison.OrdinalIgnoreCase))
            {
                // Only stop connection/reset state if connection is currently active
                // Autoconnect mismatch will quickly disconnect/reconnect, leading to double-callbacks
                Stop(connectionError);
            }
            else
            {
                if (!ClientState.IsConnected)
                {
                    Stop(connectionError);
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientY, Top);

            if (!string.IsNullOrWhiteSpace(ClientState.LastSeenName) &&
                _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).StringValue != ClientState.LastSeenName)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, ClientState.LastSeenName);
            }

            //save window position
            base.OnClosing(e);

            //stop timer
            _updateTimer?.Stop();

            ConnectAwacsMode();
            Stop();

            _audioPreview?.StopEncoding();
            _audioPreview = null;

            _radioOverlayWindowOneVertical?.Close();
            _radioOverlayWindowOneVertical = null;

            _radioOverlayWindowTwoVertical?.Close();
            _radioOverlayWindowTwoVertical = null;

            _radioOverlayWindowThreeVertical?.Close();
            _radioOverlayWindowThreeVertical = null;

            _radioOverlayWindowFiveVertical?.Close();
            _radioOverlayWindowFiveVertical = null;

            _radioOverlayWindowTenVertical?.Close();
            _radioOverlayWindowTenVertical = null;

            _radioOverlayWindowTenVerticalLong?.Close();
            _radioOverlayWindowTenVerticalLong = null;

            _radioOverlayWindowTenTransparent?.Close();
            _radioOverlayWindowTenTransparent = null;

            _radioOverlayWindowTenSwitch?.Close();
            _radioOverlayWindowTenSwitch = null;

            _radioOverlayWindowEngineering?.Close();
            _radioOverlayWindowEngineering = null;

            _radioOverlayWindowOneHorizontal?.Close();
            _radioOverlayWindowOneHorizontal = null;

            _radioOverlayWindowTwoHorizontal?.Close();
            _radioOverlayWindowTwoHorizontal = null;

            _radioOverlayWindowThreeHorizontal?.Close();
            _radioOverlayWindowThreeHorizontal = null;

            _radioOverlayWindowFiveHorizontal?.Close();
            _radioOverlayWindowFiveHorizontal = null;

            _radioOverlayWindowTenHorizontal?.Close();
            _radioOverlayWindowTenHorizontal = null;

            _radioOverlayWindowTenHorizontalWide?.Close();
            _radioOverlayWindowTenHorizontalWide = null;

            _dcsAutoConnectListener?.Stop();
            _dcsAutoConnectListener = null;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.MinimiseToTray))
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                if (!AudioInput.MicrophoneAvailable)
                {
                    _logger.Info("Unable to preview audio, no valid audio input device available or selected");
                    return;
                }

                //get device
                try
                {
                    SaveSelectedInputAndOutput();

                    _audioPreview = new AudioPreview();
                    _audioPreview.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);
                    _audioPreview.StartPreview(AudioOutput.WindowsN);

                    Preview.Content = "Stop Preview";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex,
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

        private void UpdateUiCallback()
        {
            if (ClientState.IsConnected)
            {
                ToggleServerSettings.IsEnabled = true;

                _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
            }
            else
            {
                ToggleServerSettings.IsEnabled = false;
            }
        }

        private void SpeakerBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var convertedValue = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);

            if (_audioPreview != null)
            {
                _audioPreview.SpeakerBoost = convertedValue;
            }
            if (_audioManager != null)
            {
                _audioManager.SpeakerBoost = convertedValue;
            }

            _globalSettings.SetClientSetting(GlobalSettingsKeys.SpeakerBoost,
                SpeakerBoost.Value.ToString(CultureInfo.InvariantCulture));


            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(convertedValue);
            }
        }

        private void RadioEncryptionEffects_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEncryptionEffects, RadioEncryptionEffectsToggle.IsChecked != null && (bool)RadioEncryptionEffectsToggle.IsChecked);
        }

        private void NATORadioTone_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.NATOTone, NATORadioToneToggle.IsChecked != null && (bool)NATORadioToneToggle.IsChecked);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTT, RadioSwitchIsPTT.IsChecked != null && (bool)RadioSwitchIsPTT.IsChecked);
        }

        private void ShowOverlayTwoVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TwoVerticalIndex);
        }

        private void ShowOverlayThreeVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, ThreeVerticalIndex);
        }

        private void ShowOverlayFiveVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, FiveVerticalIndex);
        }

        private void ShowOverlayTenVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TenVerticalIndex);
        }

        private void ShowOverlayHorizontalTwo_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TwoHorizontalIndex);
        }

        private void ShowOverlayThreeHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, ThreeHorizontalIndex);
        }

        private void ShowOverlayFiveHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, FiveHorizontalIndex);
        }

        private void ShowOverlayTenHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TenHorizontalIndex);
        }

        private void ShowOverlayOneVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, OneVerticalIndex);
        }

        private void ShowOverlayOneHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, OneHorizontalIndex);
        }

        private void ShowOverlayTenVerticalLong_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TenVerticalLongIndex);
        }

        private void ShowOverlayTenHorizontalWide_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TenHorizontalWideIndex);
        }

        private void ShowOverlayTenTransparent_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, TransparentIndex);
        }

        private void ShowOverlayTenSwitch_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, SwitchIndex);
        }

        private void ShowOverlayEngineering_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, EngineeringIndex);
        }

        private void ShowOverlayDragable_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, DragableIndex);
        }
        private void ShowOverlayMenuSelect_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, MenuSelectIndex);
        }

        public void ToggleOverlay(bool uiButton, int switchTo)
        {
            if (ShouldDebounce(uiButton))
            {
                _toggleShowHide = DateTime.Now.Ticks;
                if (IsInvalidSwitchTo(switchTo)) return;

                CloseAllWindows();

                if (switchTo != _windowOpen)
                {
                    OpenNewWindow(switchTo);
                }
                else
                {
                    _windowOpen = NoWindowOpen;
                }
            }
        }
        
        private bool ShouldDebounce(bool uiButton)
        {
            return (DateTime.Now.Ticks - _toggleShowHide > 6000000) || uiButton;
        }

        private bool IsInvalidSwitchTo(int switchTo)
        {
            if (switchTo < 0 || switchTo > _windows.Count() - 1)
            {
                _logger.Error($"Could not switch to RadioWindow-{switchTo}.");
                return true;
            }
            return false;
        }
        
        private void OpenNewWindow(int switchTo)
        {
            switch (switchTo)
            {
                case TwoVerticalIndex:
                    _windows[switchTo] = new RadioOverlayWindowTwoVertical(ToggleOverlay);
                    break;
                case ThreeVerticalIndex:
                    _windows[switchTo] = new RadioOverlayWindowThreeVertical(ToggleOverlay);
                    break;
                case FiveVerticalIndex:
                    _windows[switchTo] = new RadioOverlayWindowFiveVertical(ToggleOverlay);
                    break;
                case TenVerticalIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenVertical(ToggleOverlay);
                    break;
                case TwoHorizontalIndex:
                    _windows[switchTo] = new RadioOverlayWindowTwoHorizontal(ToggleOverlay);
                    break;
                case ThreeHorizontalIndex:
                    _windows[switchTo] = new RadioOverlayWindowThreeHorizontal(ToggleOverlay);
                    break;
                case FiveHorizontalIndex:
                    _windows[switchTo] = new RadioOverlayWindowFiveHorizontal(ToggleOverlay);
                    break;
                case TenHorizontalIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenHorizontal(ToggleOverlay);
                    break;
                case OneVerticalIndex:
                    _windows[switchTo] = new RadioOverlayWindowOneVertical(ToggleOverlay);
                    break;
                case OneHorizontalIndex:
                    _windows[switchTo] = new RadioOverlayWindowOneHorizontal(ToggleOverlay);
                    break;
                case TenVerticalLongIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenVerticalLong(ToggleOverlay);
                    break;
                case TenHorizontalWideIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenHorizontalWide(ToggleOverlay);
                    break;
                case TransparentIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenTransparent(ToggleOverlay);
                    break;
                case SwitchIndex:
                    _windows[switchTo] = new RadioOverlayWindowTenSwitch(ToggleOverlay);
                    break;
                case EngineeringIndex:
                    _windows[switchTo] = new RadioOverlayWindowEngineering(ToggleOverlay);
                    break;
                case DragableIndex:
                    _windows[switchTo] = new RadioOverlayWindowDragable(ToggleOverlay);
                    break;
                case MenuSelectIndex:
                    _windows[switchTo] = new RadioOverlayMenuSelect(ToggleOverlay);
                    break;
            }
            ShowWindow(switchTo);
        }
        
        private void ShowWindow(int switchTo)
        {
            try
            {
                _windows[switchTo].ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
                _windows[switchTo].Show();
                _windows[switchTo].Closed += PanelWindow_Closed;
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Could not open Window with ID: {switchTo}.");
                MessageBox.Show($"Window could not Open (Window-ID: {switchTo}).\nPlease give this Information to the SRS Development Team!", "Error Opening Panel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void CloseAllWindows()
        {
            for (int i = 0; i < _windows.Count(); i++)
            {
                if (_windows[i] != null)
                {
                    _windows[i].Close();
                    _windows[i] = null;
                    _windowOpen = i;
                }
            }
        }
        
        private void PanelWindow_Closed(object sender, EventArgs e)
        {
            // No window open -> A window was closed and Only 1 Window can be active
            _windowOpen = NoWindowOpen;

            // Erase window from windows array to clean up everything
            for (int i = 0; i < _windows.Count(); i++)
            {
                if (_windows[i] != null)
                {
                    _windows[i].Close();
                    _windows[i] = null;
                }
            }

        }

        private void AutoConnect(string address, int port)
        {
            string connection = $"{address}:{port}";
            _logger.Info($"Received AutoConnect VCS @ {connection}");
        
            if (!_globalSettings.GetClientSetting(GlobalSettingsKeys.AutoConnect).BoolValue)
            {
                _logger.Info($"Ignored Autoconnect - not Enabled");
                return;
            }
        
            if (ClientState.IsConnected)
            {
                HandleConnectedState(address, port, connection);
            }
            else
            {
                HandleDisconnectedState(address, port, connection);
            }
        }
        
        private void HandleConnectedState(string address, int port, string connection)
        {
            string[] currentConnectionParts = ServerIp.Text.Trim().Split(':');
            string currentAddress = currentConnectionParts[0];
            int currentPort = ParsePort(currentConnectionParts);
        
            if (string.Equals(address, currentAddress, StringComparison.OrdinalIgnoreCase) && port == currentPort)
            {
                _logger.Info($"Current SRS connection {currentAddress}:{currentPort} matches advertised server {connection}, ignoring autoconnect");
                return;
            }
        
            if (port != currentPort)
            {
                _ = HandleAutoConnectMismatch($"{currentAddress}:{currentPort}", connection);
                return;
            }
        
            List<string> currentIPs = ResolveHostToIPs(currentAddress);
            List<string> advertisedIPs = ResolveHostToIPs(address);
        
            if (!currentIPs.Intersect(advertisedIPs).Any())
            {
                _ = HandleAutoConnectMismatch($"{currentAddress}:{currentPort}", connection);
            }
        }
        
        private void HandleDisconnectedState(string address, int port, string connection)
        {
            bool showPrompt = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectPrompt);
            bool connectToServer = !showPrompt;
        
            if (showPrompt)
            {
                WindowHelper.BringProcessToFront(Process.GetCurrentProcess());
                var result = MessageBox.Show(this, $"Would you like to try to auto-connect to VCS @ {address}:{port}? ", "Auto Connect", MessageBoxButton.YesNo, MessageBoxImage.Question);
                connectToServer = (result == MessageBoxResult.Yes) && (StartStop.Content.ToString().ToLower() == "connect");
            }
        
            if (connectToServer)
            {
                ServerIp.Text = connection;
            }
        }
        
        private int ParsePort(string[] connectionParts)
        {
            if (connectionParts.Length >= 2 && int.TryParse(connectionParts[1], out int port))
            {
                return port;
            }
            _logger.Warn($"Failed to parse port {connectionParts[1]} of current connection, falling back to 5002 for autoconnect comparison");
            return 5002;
        }
        
        private List<string> ResolveHostToIPs(string host)
        {
            List<string> ips = new List<string>();
        
            if (IPAddress.TryParse(host, out IPAddress ip))
            {
                ips.Add(ip.ToString());
            }
            else
            {
                try
                {
                    foreach (IPAddress resolvedIp in Dns.GetHostAddresses(host))
                    {
                        if (resolvedIp.AddressFamily == AddressFamily.InterNetwork)
                        {
                            ips.Add(resolvedIp.ToString());
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.Warn(e, $"Failed to resolve host {host} to IP addresses, ignoring autoconnect advertisement");
                }
            }
        
            return ips;
        }

        private async Task HandleAutoConnectMismatch(string currentConnection, string advertisedConnection)
        {
            // Handle this after AutoConnect feature
            // Show auto connect mismatch prompt if setting has been enabled (default), otherwise automatically switch server
            bool showPrompt = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectMismatchPrompt);

            _logger.Info($"Current SRS connection {currentConnection} does not match advertised server {advertisedConnection}, {(showPrompt ? "displaying mismatch prompt" : "automatically switching server")}");

            bool switchServer = !showPrompt;
            if (showPrompt)
            {
                WindowHelper.BringProcessToFront(Process.GetCurrentProcess());

                var result = MessageBox.Show(this,
                    $"The SRS server advertised by DCS @ {advertisedConnection} does not match the SRS server @ {currentConnection} you are currently connected to.\n\n" +
                    $"Would you like to connect to the advertised SRS server?",
                    "Auto Connect Mismatch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                switchServer = result == MessageBoxResult.Yes;
            }

            if (switchServer)
            {
                Stop();

                StartStop.IsEnabled = false;
                StartStop.Content = "Connecting...";
                await Task.Delay(2000);
                StartStop.IsEnabled = true;
                ServerIp.Text = advertisedConnection;
            }
        }

        public void ResetRadioWindow_Click(object sender, RoutedEventArgs e)
        {
            //close overlay
            _radioOverlayMenuSelect?.Close();
            _radioOverlayMenuSelect = null;

            _radioOverlayWindowDragable?.Close();
            _radioOverlayWindowDragable = null;

            _radioOverlayWindowOneVertical?.Close();
            _radioOverlayWindowOneVertical = null;

            _radioOverlayWindowTwoVertical?.Close();
            _radioOverlayWindowTwoVertical = null;

            _radioOverlayWindowThreeVertical?.Close();
            _radioOverlayWindowThreeVertical = null;

            _radioOverlayWindowFiveVertical?.Close();
            _radioOverlayWindowFiveVertical = null;

            _radioOverlayWindowTenVertical?.Close();
            _radioOverlayWindowTenVertical = null;

            _radioOverlayWindowTenVerticalLong?.Close();
            _radioOverlayWindowTenVerticalLong = null;

            _radioOverlayWindowTenTransparent?.Close();
            _radioOverlayWindowTenTransparent = null;

            _radioOverlayWindowTenSwitch?.Close();
            _radioOverlayWindowTenSwitch = null;

            _radioOverlayWindowEngineering?.Close();
            _radioOverlayWindowEngineering = null;

            _radioOverlayWindowOneHorizontal?.Close();
            _radioOverlayWindowOneHorizontal = null;

            _radioOverlayWindowTwoHorizontal?.Close();
            _radioOverlayWindowTwoHorizontal = null;

            _radioOverlayWindowThreeHorizontal?.Close();
            _radioOverlayWindowThreeHorizontal = null;

            _radioOverlayWindowFiveHorizontal?.Close();
            _radioOverlayWindowFiveHorizontal = null;

            _radioOverlayWindowTenHorizontal?.Close();
            _radioOverlayWindowTenHorizontal = null;

            _radioOverlayWindowTenHorizontalWide?.Close();
            _radioOverlayWindowTenHorizontalWide = null;

            //Reset Panel Settings
            // Menu Select
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectHeight, 175);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectOpacity, 1.0);

            // Dragable Panel
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowDragableHeight, 175);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOverlayWindowOpacity, 1.0);

            // 1 Vertical
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalHeight, 175);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalOpacity, 1.0);

            // 2 Vertical
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalHeight, 265);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalOpacity, 1.0);

            // 3 Vertical
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalHeight, 355);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalOpacity, 1.0);

            // 5 Vertical
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHeight, 494);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveOpacity, 1.0);

            // 10 Vertical
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalWidth, 340);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalHeight, 500);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalOpacity, 1.0);

            // 10 Vertical Long
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalHeight, 905);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalOpacity, 1.0);

            // 10 Transparent
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentHeight, 100);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentBackgroundOpacity, 1.0);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentTextOpacity, 1.0);

            // 10 Switch
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchHeight, 100);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchBackgroundOpacity, 1.0);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchTextOpacity, 1.0);

            // Engineering
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringHeight, 100);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringBackgroundOpacity, 1.0);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringTextOpacity, 1.0);

            // 1 Horizontal
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalWidth, 340);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalHeight, 100);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalOpacity, 1.0);

            // 2 Horizontal
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalWidth, 340);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalHeight, 160);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalOpacity, 1.0);

            // 3 Horizontal
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalWidth, 510);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalHeight, 160);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalOpacity, 1.0);

            // 5 Horizontal
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalWidth, 805);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalHeight, 140);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalOpacity, 1.0);

            // 10 Horizontal
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalWidth, 805);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalHeight, 220);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalOpacity, 1.0);

            // 10 Horizontal Wide
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalWidth, 1650);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalHeight, 127);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalOpacity, 1.0);

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
                _serverSettingsWindow.Show();
            }
            else
            {
                _serverSettingsWindow.Close();
                _serverSettingsWindow = null;
            }
        }



        private void AutoConnectToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnect, AutoConnectEnabledToggle.IsChecked != null && (bool)AutoConnectEnabledToggle.IsChecked);
        }

        private void AutoConnectPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnectPrompt, AutoConnectPromptToggle.IsChecked != null && (bool)AutoConnectPromptToggle.IsChecked);
        }

        private void AutoConnectMismatchPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnectMismatchPrompt, AutoConnectMismatchPromptToggle.IsChecked != null && (bool)AutoConnectMismatchPromptToggle.IsChecked);
        }

        private void RadioOverlayTaskbarItem_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RadioOverlayTaskbarHide, RadioOverlayTaskbarItem.IsChecked != null && (bool)RadioOverlayTaskbarItem.IsChecked);

            if (_radioOverlayWindowTwoVertical != null)
                _radioOverlayWindowTwoVertical.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
            else if (_radioOverlayWindowFiveVertical != null)
                _radioOverlayWindowFiveVertical.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
            else if (_radioOverlayWindowTenHorizontal != null) _radioOverlayWindowTenHorizontal.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
        }

        private void DCSRefocus_OnClick_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RefocusDCS, RefocusDCS.IsChecked != null && (bool)RefocusDCS.IsChecked);
        }

        private void ExpandInputDevices_OnClick_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "You must restart SRS for this setting to take effect.\n\nTurning this on will allow almost any DirectX device to be used as input expect a Mouse but WILL LIKELY cause issues with other devices being detected. \n\nUse SRS Device Listing (see Discord) instead to enable the missing device\n\nDo not turn on unless you know what you're doing :) ",
                "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _globalSettings.SetClientSetting(GlobalSettingsKeys.ExpandControls, ExpandInputDevices.IsChecked != null && (bool)ExpandInputDevices.IsChecked);
        }

        private void AllowXInputController_OnClick_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "You must restart SRS for this setting to take effect.",
                "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _globalSettings.SetClientSetting(GlobalSettingsKeys.AllowXInputController, AllowXInputController.IsChecked != null && (bool)AllowXInputController.IsChecked);
        }

        private void LaunchAddressTab(object sender, RoutedEventArgs e)
        {
            TabControl.SelectedItem = FavouritesSeversTab;
        }

        private void MicAGC_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AGC, MicAGC.IsChecked != null && (bool)MicAGC.IsChecked);
        }

        private void MicDenoise_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.Denoise, MicDenoise.IsChecked != null && (bool)MicDenoise.IsChecked);
        }

        private void RadioSoundEffects_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEffects, RadioSoundEffects.IsChecked != null && (bool)RadioSoundEffects.IsChecked);
        }

        private void RadioTxStart_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start, RadioTxStartToggle.IsChecked != null && (bool)RadioTxStartToggle.IsChecked);
        }

        private void RadioTxEnd_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End, RadioTxEndToggle.IsChecked != null && (bool)RadioTxEndToggle.IsChecked);
        }

        private void RadioRxStart_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start, RadioRxStartToggle.IsChecked != null && (bool)RadioRxStartToggle.IsChecked);
        }

        private void RadioRxEnd_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End, RadioRxEndToggle.IsChecked != null && (bool)RadioRxEndToggle.IsChecked);
        }

        private void RadioMIDS_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect, RadioMIDSToggle.IsChecked != null && (bool)RadioMIDSToggle.IsChecked);
        }

        private void AudioSelectChannel_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AutoSelectPresetChannel, AutoSelectChannel.IsChecked != null && (bool)AutoSelectChannel.IsChecked);
        }

        private void RadioSoundEffectsClipping_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping, RadioSoundEffectsClipping.IsChecked != null && (bool)RadioSoundEffectsClipping.IsChecked);
        }

        private void MinimiseToTray_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.MinimiseToTray, MinimiseToTray.IsChecked != null && (bool)MinimiseToTray.IsChecked);
        }

        private void StartMinimised_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.StartMinimised, StartMinimised.IsChecked != null && (bool)StartMinimised.IsChecked);
        }

        private void AllowDCSPTT_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT, AllowDCSPTT.IsChecked != null && (bool)AllowDCSPTT.IsChecked);
        }

        private void AllowRotaryIncrement_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RotaryStyleIncrement, AllowRotaryIncrement.IsChecked != null && (bool)AllowRotaryIncrement.IsChecked);
        }

        private void AlwaysAllowHotas_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls, AlwaysAllowHotas.IsChecked != null && (bool)AlwaysAllowHotas.IsChecked);
        }

        private void CheckForBetaUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.CheckForBetaUpdates, CheckForBetaUpdates.IsChecked != null && (bool)CheckForBetaUpdates.IsChecked);
        }

        private void PlayConnectionSounds_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.PlayConnectionSounds, PlayConnectionSounds.IsChecked != null && (bool)PlayConnectionSounds.IsChecked);
        }

        private void ConnectAwacsMode()
        {
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXIC, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC));
            }


            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1))
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXR1, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1));
            }


            if (_client == null ||
                !ClientState.IsConnected ||
                (!ClientState.ExternalAWACSModelSelected &&
                 string.IsNullOrWhiteSpace(_coalitionPassword)))
            {
                return;
            }

            // Already connected, disconnect
            if (ClientState.ExternalAWACSModelSelected)
            {
                _client.DisconnectExternalAWACSMode();
            }
            else
            {
                _connectionTransaction.User = new SentryUser
                {
                    Username = _playerName
                };
                _connectionAwacsSpan = _connectionTransaction.StartChild("awacs-connection");
                _logger.Debug("Init AWACS Connection now...");
                ClientState.LastSeenName = _playerName;
                _client.ConnectExternalAWACSMode(_coalitionPassword, ExternalAwacsModeConnectionChanged);
            }
        }

        private void ExternalAwacsModeConnectionChanged(bool result, int coalition, bool error = false)
        {
            if (result)
            {
                ClientState.ExternalAWACSModelSelected = true;
                ClientState.PlayerCoaltionLocationMetadata.side = coalition;
                ClientState.PlayerCoaltionLocationMetadata.name = ClientState.LastSeenName;
                ClientState.DcsPlayerRadioInfo.name = ClientState.LastSeenName;

                StartStop.Content = "disconnect";
                
                _guestPage.LoginInProgress.Opacity = 0;
                ConnectionStatus.Fill = Brushes.Green;

                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.PlayConnectionSounds))
                {
                    try
                    {
                        Sounds.BeepConnected.Play();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn(ex, "Failed to play connect sound");
                    }
                }

                LoggedIn = true;
                ConnectedAt = DateTime.UtcNow;
                _connectionTransaction.SetTag("coalition", coalition == 0 ? "red" : "blue");
                OpenPageByIndex(OpenPage == GuestIndex ? GuestSuccessIndex : HomePageIndex);
                
                ExternalAWACSModeName.Text = ClientState.LastSeenName;

                _connectionAwacsSpan.Finish();
                
                SentrySdk.ConfigureScope(scope =>
                {
                    scope.User = new SentryUser
                    {
                        Username = ClientState.LastSeenName
                    };
                });
                
                _connectionTransaction.Finish();
            }
            else
            {
                ClientState.ExternalAWACSModelSelected = false;
                ClientState.PlayerCoaltionLocationMetadata.side = 0;
                ClientState.PlayerCoaltionLocationMetadata.name = "";
                ClientState.DcsPlayerRadioInfo.name = "";
                ClientState.DcsPlayerRadioInfo.LastUpdate = 0;
                ClientState.LastSent = 0;

                _coalitionPassword = "";
                _playerName = "";
                
                ConnectionStatus.Fill = Brushes.Orange;

                StartStop.Content = "Connect";
                LoggedIn = false;
                ConnectedAt = DateTime.Now;
                ExternalAWACSModePassword.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
                ExternalAWACSModeName.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);

                if (error)
                {
                    MessageBox.Show("Incorrect Password to connect to VCS-SRS.", "Auth Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    _logger.Warn("Stopping server connection...");
                    Stop(true);
                }
                
                SentrySdk.ConfigureScope(scope =>
                {
                    scope.User = new SentryUser();
                });
            }
        }

        private void RescanInputDevices(object sender, RoutedEventArgs e)
        {
            InputManager.InitDevices();
            MessageBox.Show(this,
                "Controller Input Devices Rescanned",
                "New controller input devices can now be used.",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void SetSRSPath_Click(object sender, RoutedEventArgs e)
        {
            Registry.SetValue(RegistryPath, "SRPathStandalone", Directory.GetCurrentDirectory());

            MessageBox.Show(this,
                "SRS Path set to: " + Directory.GetCurrentDirectory(),
                "SRS Client Path",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void RequireAdminToggle_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RequireAdmin, RequireAdminToggle.IsChecked != null && (bool)RequireAdminToggle.IsChecked);
            MessageBox.Show(this,
                "SRS Requires admin rights to be able to read keyboard input in the background. \n\nIf you do not use any keyboard binds you can disable SRS Admin Privileges. \n\nFor this setting to take effect SRS must be restarted",
                "SRS Admin Privileges", MessageBoxButton.OK, MessageBoxImage.Warning);

        }

        private void CreateProfile(object sender, RoutedEventArgs e)
        {
            var inputProfileWindow = new InputProfileWindow.InputProfileWindow(name =>
                {
                    if (name.Trim().Length > 0)
                    {
                        _globalSettings.ProfileSettingsStore.AddNewProfile(name);
                        InitSettingsProfiles();

                    }
                });
            inputProfileWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            inputProfileWindow.Owner = this;
            inputProfileWindow.ShowDialog();
        }

        private void DeleteProfile(object sender, RoutedEventArgs e)
        {
            var current = ControlsProfile.SelectedValue as string;

            if (current != null && current.Equals("default"))
            {
                MessageBox.Show(this,
                    "Cannot delete the default input!",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                var result = MessageBox.Show(this,
                    $"Are you sure you want to delete {current} ?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    ControlsProfile.SelectedIndex = 0;
                    _globalSettings.ProfileSettingsStore.RemoveProfile(current);
                    InitSettingsProfiles();
                }

            }

        }

        private void RenameProfile(object sender, RoutedEventArgs e)
        {

            var current = ControlsProfile.SelectedValue as string;
            if (current != null && current.Equals("default"))
            {
                MessageBox.Show(this,
                    "Cannot rename the default input!",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                var oldName = current;
                var inputProfileWindow = new InputProfileWindow.InputProfileWindow(name =>
                {
                    if (name.Trim().Length > 0)
                    {
                        _globalSettings.ProfileSettingsStore.RenameProfile(oldName, name);
                        InitSettingsProfiles();
                    }
                }, true, oldName);
                inputProfileWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                inputProfileWindow.Owner = this;
                inputProfileWindow.ShowDialog();
            }

        }

        private void AutoSelectInputProfile_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoSelectSettingsProfile, (AutoSelectInputProfile.IsChecked != null && ((bool)AutoSelectInputProfile.IsChecked)).ToString());
        }

        private void CopyProfile(object sender, RoutedEventArgs e)
        {
            var current = ControlsProfile.SelectedValue as string;
            var inputProfileWindow = new InputProfileWindow.InputProfileWindow(name =>
            {
                if (name.Trim().Length > 0)
                {
                    _globalSettings.ProfileSettingsStore.CopyProfile(current, name);
                    InitSettingsProfiles();
                }
            });
            inputProfileWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            inputProfileWindow.Owner = this;
            inputProfileWindow.ShowDialog();
        }

        private void VAICOMTXInhibit_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.VAICOMTXInhibitEnabled, (VAICOMTXInhibitEnabled.IsChecked != null && ((bool)VAICOMTXInhibitEnabled.IsChecked)).ToString());
        }

        private void AlwaysAllowTransponderOverlay_OnClick(object sender, RoutedEventArgs e)
        {

            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AlwaysAllowTransponderOverlay, AlwaysAllowTransponderOverlay.IsChecked != null && (bool)AlwaysAllowTransponderOverlay.IsChecked);
        }

        private void ShowClientList_OnClick(object sender, RoutedEventArgs e)
        {
            if ((_clientListWindow == null) || !_clientListWindow.IsVisible ||
                (_clientListWindow.WindowState == WindowState.Minimized))
            {
                _clientListWindow?.Close();

                _clientListWindow = new ClientListWindow();
                _clientListWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _clientListWindow.Owner = this;
                _clientListWindow.Show();
            }
            else
            {
                _clientListWindow.Close();
                _clientListWindow = null;
            }
        }

        private void ShowTransmitterName_OnClick_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.ShowTransmitterName, (ShowTransmitterName.IsChecked != null && ((bool)ShowTransmitterName.IsChecked)).ToString());
        }

        private void PushToTalkReleaseDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PTTReleaseDelay.IsEnabled)
                _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay, (float)e.NewValue);
        }

        private void PushToTalkStartDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PTTStartDelay.IsEnabled)
                _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.PTTStartDelay, (float)e.NewValue);
        }

        private void BackgroundRadioNoiseToggle_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioBackgroundNoiseEffect, BackgroundRadioNoiseToggle.IsChecked != null && (bool)BackgroundRadioNoiseToggle.IsChecked);
        }

        private void HQEffect_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone, HQEffectToggle.IsChecked != null && (bool)HQEffectToggle.IsChecked);
        }

        private void AllowAnonymousUsage_OnClick(object sender, RoutedEventArgs e)
        {
            if (AllowAnonymousUsage.IsChecked != null && !(bool)AllowAnonymousUsage.IsChecked)
            {
                MessageBox.Show(
                    "Please leave this ticked - SRS logging is extremely minimal (you can verify by looking at the source) - and limited to: Country & SRS Version on startup.\n\nBy keeping this enabled I can judge the usage of SRS, and which versions are still in use for support.",
                    "Please leave this ticked", MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Registry.SetValue(RegistryPath, "SRSAnalyticsOptOut", "TRUE");
            }
            else
            {
                MessageBox.Show(
                    "Thank you for enabling this!\n\nBy keeping this enabled I can judge the usage of SRS, and which versions are still in use for support.",
                    "Thank You!", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Registry.SetValue(RegistryPath, "SRSAnalyticsOptOut", "FALSE");
            }
        }

        private void AllowTransmissionsRecord_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AllowRecording, AllowTransmissionsRecord.IsChecked != null && (bool)AllowTransmissionsRecord.IsChecked);
        }

        private void RecordTransmissions_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RecordAudio, RecordTransmissions.IsChecked != null && (bool)RecordTransmissions.IsChecked);
            RecordTransmissions_IsEnabled();
        }

        private void RecordTransmissions_IsEnabled()
        {
            if (RecordTransmissions.IsChecked != null && (bool)RecordTransmissions.IsChecked)
            {
                this.SingleFileMixdown.IsEnabled = false;
                this.RecordingQuality.IsEnabled = false;
            }
            else
            {
                this.SingleFileMixdown.IsEnabled = true;
                this.RecordingQuality.IsEnabled = true;
            }
        }

        private void SingleFileMixdown_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.SingleFileMixdown, SingleFileMixdown.IsChecked != null && (bool)SingleFileMixdown.IsChecked);
        }

        private void RecordingQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RecordingQuality, $"V{(int)e.NewValue}");
        }

        private void DisallowedAudioTone_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.DisallowedAudioTone, DisallowedAudioTone.IsChecked != null && (bool)DisallowedAudioTone.IsChecked);
        }

        private void VOXMode_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMode.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMode, (int)e.NewValue);
        }

        private void VOXMinimumTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMinimimumTXTime.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMinimumTime, (int)e.NewValue);
        }

        private void VOXMinimumRMS_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMinimumRMS.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMinimumDB, e.NewValue);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void ChangeWindowLayout(int selectedUi)
        {
            switch (selectedUi)
            {
                case 0: // new UI
                    this.Height = 425;
                    this.BottomBar.Visibility = Visibility.Visible;
                    this.MainGrid.RowDefinitions[1].Height = new GridLength(365);
                    break;
                case 1: // Old UI
                    this.Height = 750;
                    this.BottomBar.Visibility = Visibility.Collapsed;
                    this.MainGrid.RowDefinitions[1].Height = new GridLength(730);
                    break;
            }
        }

        private void TabControl_OnSelected(object sender, RoutedEventArgs e)
        {
            switch (TabControl.SelectedIndex)
            {
                case 0:
                    ChangeWindowLayout(0);
                    break;
                case 1:
                    ChangeWindowLayout(1);
                    break;
                default:
                    _logger.Warn("Selected UI not recognised, setting layout to default.");
                    ChangeWindowLayout(0);
                    break;
            }
        }

        private void StartStop_OnClick(object sender, RoutedEventArgs e)
        {
            var address = GetAddressFromTextBox();
            var resolvedAddresses = Dns.GetHostAddresses(address);
            var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
            var port = GetPortFromTextBox();
            _resolvedIp = ip;
            _port = port;
            _coalitionPassword = ExternalAWACSModePassword.Password.Trim();
            _playerName = ExternalAWACSModeName.Text;
            LoginType = LoginType.Guest;
            Connect(ip, port);
        }

        private string GetAddressFromTextBox()
        {
            var addr = this.ServerIp.Text.Trim();

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private int GetPortFromTextBox()
        {
            var addr = this.ServerIp.Text.Trim();

            if (addr.Contains(":"))
            {
                if (int.TryParse(addr.Split(':')[1], out int port))
                {
                    return port;
                }
                throw new ArgumentException("specified port is not valid");
            }

            return 5002;
        }

        private void HomeNavigation_OnClick(object sender, RoutedEventArgs e)
        {
            OpenPageByIndex(WelcomeIndex);
        }
    }
}