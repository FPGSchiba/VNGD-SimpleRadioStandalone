using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Vanguard.VCS.Client.Audio.Managers;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Input;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Client.Stores;
using Vanguard.VCS.Client.UI.ClientWindow.HomePages;
using Vanguard.VCS.Client.UI.ClientWindow.Favourites;
using Vanguard.VCS.Client.Utils;
using Vanguard.VCS.Common.Helpers;
using Vanguard.VCS.Common.Network;
using NAudio.CoreAudioApi;
using NLog;
using Sentry;
using Vanguard.VCS.Client.Settings.Favourites;
using Vanguard.VCS.Client.UI.ClientWindow.LoginPages;
using Vanguard.VCS.Client.UI.ClientWindow.SettingPages;
using Vanguard.VCS.Client.UI.ClientWindow.WelcomePages;
using Vanguard.VCS.Client.UI.RadioOverlayWindow;
using Vanguard.VCS.Client.Events;

namespace Vanguard.VCS.Client.UI.ClientWindow
{
    public enum ConnectionStep
    {
        ServerSelect,
        Auth,
        GuestLogin,
        MemberLogin,
        UnitSelection,
        Ready
    }

    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton, int switchTo);
        public delegate void UpdateChannelCallback(ProfileSettingsKeys channel, float balance);

        public readonly AudioManager AudioManager;

        private Guid _guid;
        private string _connectedServerAddress = "";
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private AudioPreview _audioPreview;
        private VcsClientSyncHandler _vcsClient;
        private IDisposable _connectionStateSubscription;
        private IDisposable _serverActionSubscription;
        private IDisposable _serverMuteSubscription;
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
        public VcsRole ClientRole { get; private set; }
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

        private UnitSelectionPage _unitSelectionPage;
        private const int UnitSelectionIndex = 7;
        
        private ServerSelectPage _serverSelectPage;
        private const int ServerSelectPageIndex = 18;

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
        public ClientStateStore ClientState => App.ClientStateStore;

        /// <remarks>Used in the XAML for DataBinding the connected client count</remarks>
        public ConnectedClientsStore Clients => App.ConnectedClientsStore;

        /// <remarks>Used in the XAML for DataBinding input audio related UI elements</remarks>
        public AudioInputSingleton AudioInput { get; } = AudioInputSingleton.Instance;

        /// <remarks>Used in the XAML for DataBinding output audio related UI elements</remarks>
        public AudioOutputSingleton AudioOutput { get; } = AudioOutputSingleton.Instance;

        public MainWindow()
        {
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

            InitializeComponent();

            // Initialize Pages
            InitPages();

            // Initialize ToolTip controls
            ToolTips.Init();

            // Initialize images/icons
            Images.Init();

            // Initialise sounds
            Sounds.Init();

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
            
            Analytics.Log("Client", "Startup", _globalSettings.GetClientSetting(GlobalSettingsKeys.ClientIdLong).RawValue);
            
            CheckWindowVisibility();

            InputManager = new InputDeviceManager(this, ToggleOverlay, UpdateChannelSettings);

            FavouriteServersViewModel = new FavouriteServersViewModel(new CsvFavouriteServerStore());

            InitDefaultAddress();

            AudioManager = new AudioManager();

            // Use Update Checker for automatic Updates, needs rewrite of the UpdateCheker

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdatePlayerLocationAndVuMeters;
            _updateTimer.Start();

            _connectionStateSubscription = App.EventBus.Subscribe<ConnectionStateChangedEvent>(e =>
                Dispatcher.Invoke(() => HandleConnectionStateChanged(e.State)));

            _serverActionSubscription = App.EventBus.Subscribe<ServerActionEvent>(e =>
            {
                if (e.Type == ServerAction.Types.ActionType.Kick || e.Type == ServerAction.Types.ActionType.Ban)
                    Dispatcher.Invoke(() => HandleForcedDisconnect(e.Reason));
            });

            _serverMuteSubscription = App.EventBus.Subscribe<ServerMuteChangedEvent>(e =>
                Dispatcher.Invoke(() => HandleServerMuteChanged(e.IsMuted)));
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
            var monitors = MonitorHelper.GetAllMonitors();
            
            foreach (MonitorInfo screen in monitors)
            {
                var primary = "primary ";
                _logger.Trace($"Checking {(screen.IsPrimary ? primary : "")}screen {screen.DeviceName} with bounds {screen.Bounds} for window visibility");
    
                if (screen.Bounds.Contains(mainWindowX, mainWindowY))
                {
                    _logger.Trace($"Main client window {{X={mainWindowX},Y={mainWindowY}}} is visible on {(screen.IsPrimary ? primary : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    mainWindowVisible = true;
                }
    
                radioWindowVisible = CheckRadioWindowVisibility(screen, radioWindowVisible);
            }
            
            return mainWindowVisible;
        }
            
        private bool CheckRadioWindowVisibility(MonitorInfo screen, bool radioWindowVisible)
        {
            int[] radioWindowX = {
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectX).DoubleValue,
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioDraggableX).DoubleValue,
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
                (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioDraggableY).DoubleValue,
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
                    _logger.Trace($"Radio overlay {{X={radioWindowX[i]},Y={radioWindowY[i]}}} is visible on {(screen.IsPrimary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
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
                GlobalSettingsKeys.RadioDraggableX,
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
                GlobalSettingsKeys.RadioDraggableY,
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

        #region Pages and Navigation

        private void InitPages()
        {
            _serverSelectPage = new ServerSelectPage();
            _welcomePage = new WelcomePage();
            _supportPage = new SupportPage();
            _loginPage = new LoginPage();
            _guestPage = new GuestPage();
            _guestSuccessPage = new GuestSuccess();
            _homePage = new HomePage();
            _settingsPage = new SettingsPage();
            _unitSelectionPage = new UnitSelectionPage();
            DisplayFrame.Content = _serverSelectPage;
            OpenPage = ServerSelectPageIndex;

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
                case UnitSelectionIndex:
                    DisplayFrame.Content = _unitSelectionPage;
                    break;
                case ServerSelectPageIndex:
                    DisplayFrame.Content = _serverSelectPage;
                    break;
                default:
                    _logger.Error($"Page: {index} could not be found.");
                    break;
            }

            OpenPage = index;
        }

        private static void OpenPagePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
        {
            if (source is MainWindow mainWindow)
            {
                var index = Convert.ToInt32(e.NewValue);
                if (index == WelcomeIndex || index == ServerSelectPageIndex)
                {
                    mainWindow.HomeNavigation.IsEnabled = false;
                    mainWindow.HomeNavigation.Visibility = Visibility.Hidden;
                }
            }
        }

        private void NavigateToStep(ConnectionStep step)
        {
            UpdateStepperHeader(step);
            switch (step)
            {
                case ConnectionStep.ServerSelect:
                    OpenPageByIndex(ServerSelectPageIndex);
                    _serverSelectPage.ShowIdle();
                    break;
                case ConnectionStep.Auth:
                    OpenPageByIndex(WelcomeIndex);
                    break;
                case ConnectionStep.GuestLogin:
                    OpenPageByIndex(GuestIndex);
                    break;
                case ConnectionStep.MemberLogin:
                    OpenPageByIndex(LoginIndex);
                    break;
                case ConnectionStep.UnitSelection:
                    OpenPageByIndex(UnitSelectionIndex);
                    break;
                case ConnectionStep.Ready:
                    OpenPageByIndex(HomePageIndex);
                    break;
            }
        }

        private void UpdateStepperHeader(ConnectionStep step)
        {
            if (step == ConnectionStep.Ready)
            {
                StepperHeader.Visibility = Visibility.Collapsed;
                return;
            }

            StepperHeader.Visibility = Visibility.Visible;
            StepperPanel.Children.Clear();

            var useGuestPath = step == ConnectionStep.ServerSelect
                || step == ConnectionStep.Auth
                || step == ConnectionStep.GuestLogin;

            var steps = useGuestPath
                ? new[] { ("Server", ConnectionStep.ServerSelect), ("Auth", ConnectionStep.Auth), ("Ready", ConnectionStep.Ready) }
                : new[] { ("Server", ConnectionStep.ServerSelect), ("Login", ConnectionStep.MemberLogin), ("Unit", ConnectionStep.UnitSelection), ("Ready", ConnectionStep.Ready) };

            for (int i = 0; i < steps.Length; i++)
            {
                var (label, s) = steps[i];
                bool isDone = s < step;
                bool isCurrent = s == step;

                var dot = new Ellipse
                {
                    Width = 18, Height = 18,
                    Fill = isDone ? new SolidColorBrush(Color.FromRgb(46, 160, 67))
                         : isCurrent ? Brushes.DodgerBlue
                         : new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                };
                var text = new TextBlock
                {
                    Text = isDone ? "✓" : (i + 1).ToString(),
                    Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var circle = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0) };
                circle.Children.Add(dot);
                circle.Children.Add(text);
                StepperPanel.Children.Add(circle);
                StepperPanel.Children.Add(new TextBlock
                {
                    Text = label, FontSize = 11,
                    Foreground = isCurrent ? Brushes.Black
                               : isDone ? new SolidColorBrush(Color.FromRgb(46, 160, 67))
                               : new SolidColorBrush(Color.FromRgb(160, 160, 160)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                });
                if (i < steps.Length - 1)
                    StepperPanel.Children.Add(new TextBlock
                    {
                        Text = "—",
                        Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                    });
            }
        }

        public void On_WelcomeLoginClicked() => NavigateToStep(ConnectionStep.MemberLogin);

        public void On_WelcomeGuestClicked() => NavigateToStep(ConnectionStep.GuestLogin);

        public void On_ServerConnectClicked(IPEndPoint endpoint, bool isCustom)
        {
            _connectedServerAddress = isCustom ? $"{endpoint.Address}:{endpoint.Port}" : "Vanguard VCS Server";
            _resolvedIp = endpoint.Address;
            _port = endpoint.Port;
            Connect(endpoint.Address, endpoint.Port);
        }

        public void On_ServerSelectCancelled()
        {
            Stop();
        }

        public void On_LoginLoginClicked(string email, string password)
        {
            Login(new UserLogin() { Username = email, Password = password, LoginType = LoginRequestType.Internal});
        }

        public void On_LoginBackClicked()
        {
            NavigateToStep(ConnectionStep.Auth);
        }

        public void On_GuestLoginClicked(string playerName, string fleetCode, string coalitionPassword)
        {
            _coalitionPassword = coalitionPassword;
            _playerName = playerName;
            ClientRole = VcsRole.Guest;
            Login(new UserLogin() { 
                Username = playerName, 
                Password = coalitionPassword,
                LoginType = LoginRequestType.Guest,
                UnitId = fleetCode
            });
        }

        public void On_GuestBackClicked()
        {
            NavigateToStep(ConnectionStep.Auth);
        }

        public void On_GuestSuccessAcceptClicked()
        {
            OpenPageByIndex(HomePageIndex);
        }

        public void On_UnitSelectionBackClicked()
        {
            Stop();
            NavigateToStep(ConnectionStep.ServerSelect);
        }
        
        public void On_UnitSelectionContinueClicked(string unitId, string coalition, uint roleId)
        {
            SelectUnit(unitId, coalition, roleId);
        }

        public void On_HomeLogOutClicked()
        {
            Stop();
            NavigateToStep(ConnectionStep.ServerSelect);
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

        private void ReloadRadioAudioChannelSettings()
        {
            _settingsPage.ReloadRadioAudioChannelSettings();
        }

        public void UpdateChannelSettings(ProfileSettingsKeys channel, float balance)
        {
            ReloadRadioAudioChannelSettings();
        }

        public InputDeviceManager InputManager { get; set; }

        public FavouriteServersViewModel FavouriteServersViewModel { get; }

        public ServerAddress ServerAddress
        {
            get { return _serverAddress; }
            set
            {
                _serverAddress = value;
            }
        }

        private void UpdatePlayerLocationAndVuMeters(object sender, EventArgs e)
        {
            ConnectedClientsSingleton.Instance.NotifyAll();
        }

        private void HandleConnectionStateChanged(ConnectionState state)
        {
            ConnectionStatus.Fill = state switch
            {
                ConnectionState.Connected => Brushes.Green,
                ConnectionState.Connecting => Brushes.Orange,
                _ => Brushes.Red
            };
        }

        private void HandleForcedDisconnect(string reason)
        {
            Stop(connectionError: true);
            NavigateToStep(ConnectionStep.ServerSelect);
            _serverSelectPage.ShowError(reason);
        }

        private void HandleServerMuteChanged(bool isMuted)
        {
            ClientStateSingleton.Instance.IsServerMuted = isMuted;
            ServerMuteBanner.Visibility = isMuted && LoggedIn
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void VcsUiUpdate(VcsUiUpdateType type, object message)
        {
            switch (type)
            {
                case VcsUiUpdateType.ConnectionError:
                    Dispatcher.Invoke(() => HandleConnectionError(message, isGuest: true));
                    break;
                case VcsUiUpdateType.InitializationError:
                    Dispatcher.Invoke(() => HandleInitializationError(message));
                    break;
                case VcsUiUpdateType.InitializationSuccess:
                    Dispatcher.Invoke(() => HandleInitializationSuccessEvent(message));
                    break;
                case VcsUiUpdateType.GuestLoginSuccess:
                    Dispatcher.Invoke(HandleGuestLoginSuccess);
                    break;
                case VcsUiUpdateType.GuestLoginError:
                    Dispatcher.Invoke(() => HandleGuestLoginError(message));
                    break;
                case VcsUiUpdateType.InternalLoginError:
                    Dispatcher.Invoke(() => HandleConnectionError(message, isGuest: false));
                    break;
                case VcsUiUpdateType.InternalLoginSuccess:
                    Dispatcher.Invoke(() => HandleInternalLoginSuccessEvent(message));
                    break;
                case VcsUiUpdateType.InternalUnitSelectionError:
                    Dispatcher.Invoke(() => HandleUnitSelectError(message));
                    break;
                case VcsUiUpdateType.InternalUnitSelectionSuccess:
                    Dispatcher.Invoke(() => HandleUnitSelectSuccess(message));
                    break;
                default:
                    _logger.Info($"{type} - {message}");
                    break;
            }
        }

        private void HandleUnitSelectSuccess(object message)
        {
            if (message is UnitSelectionResult unitSelectionResult)
            {
                _logger.Info($"Unit selection successful. Selected Role: {unitSelectionResult.SelectedRole}, Unit ID: {unitSelectionResult.SelectedUnitId}, Coalition: {unitSelectionResult.SelectedCoalition}");
                ClientRole = unitSelectionResult.SelectedRole;
                // ClientState.Coalition = unitSelectionResult.SelectedCoalition; TODO: Implement coalition handling with new VCS
                HandleConnectionSuccess();
            }
            else
            {
                _logger.Error("Unit selection error with no message provided.");
                _unitSelectionPage.ShowError("An unknown unit selection error occurred.");
            }
        }
        
        private void HandleConnectionSuccess()
        {
            if (ClientState.IsConnected) return;
            try
            {
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXIC, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC));
                }


                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1))
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXR1, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1));
                }
                    
                _connectionTransaction.User = new SentryUser
                {
                    Username = _playerName
                };
                _connectionAwacsSpan = _connectionTransaction.StartChild("awacs-connection");
                
                ClientStateSingleton.Instance.LastSeenName = _playerName; // TODO: implement LastSeenName on ClientStateStore

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

                AudioManager.StartEncoding(InputManager, _resolvedIp, _port);

                StepperHeader.Visibility = Visibility.Collapsed;
                NavigateToStep(ConnectionStep.Ready);

                _connectionAwacsSpan.Finish();

                SentrySdk.ConfigureScope(scope =>
                {
                    scope.User = new SentryUser
                    {
                        Username = ClientStateSingleton.Instance.LastSeenName // TODO: implement LastSeenName on ClientStateStore
                    };
                });
                ClientStateSingleton.Instance.IsConnected = true; // TODO: drive IsConnected via event bus on ClientStateStore
                _connectionTransaction.Finish();
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "Unable to get audio device - likely output device error - Pick another. Error:" +
                    ex.Message);
                Stop();
                        
                var messageBoxResult = MessageBox.Show(
                    "Problem initialising Audio Output!\n\nTry a different Output device and please post your clientlog.txt to the support Discord server.\n\nJoin support Discord server now?",
                    "Audio Output Error",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (messageBoxResult == MessageBoxResult.Yes) Process.Start("https://discord.gg/PMKtQsSk");
            }
        }
        
        private void HandleUnitSelectError(object message)
        {
            var errorMsg = message as string;
            if (!string.IsNullOrEmpty(errorMsg))
            {
                _logger.Error($"Unit selection error: {errorMsg}");
                _unitSelectionPage.ShowError(errorMsg);
            }
            else
            {
                _logger.Error("Unit selection error with no message provided.");
                _unitSelectionPage.ShowError("An unknown unit selection error occurred.");
            }
        }

        private void HandleConnectionError(object message, bool isGuest)
        {
            var errorMsg = message as string ?? "An unknown connection error occurred.";
            _logger.Error($"Connection error: {errorMsg}");
            Stop(true);
            NavigateToStep(ConnectionStep.ServerSelect);
            _serverSelectPage.ShowError(errorMsg);
        }

        private void HandleInitializationError(object message)
        {
            var errorMsg = message as string ?? "An unknown initialization error occurred.";
            _logger.Error($"Initialization error: {errorMsg}");
            Stop(true);
            NavigateToStep(ConnectionStep.ServerSelect);
            _serverSelectPage.ShowError(errorMsg);
        }
        
        private void HandleInitializationSuccessEvent(object message)
        {
            if (message is InitializationResult initializationResult)
            {
                _logger.Info($"Initialization successful. Client GUID: {initializationResult.ClientGuid}");
                _guid = initializationResult.ClientGuid;
                ClientStateSingleton.Instance.SetGuid(_guid);
                HandleInitializationSuccess(initializationResult.IsVanguardLoginAvailable, initializationResult.IsGuestLoginAvailable);
            }
            else
            {
                _logger.Error("Initialization error with no message provided.");
                Stop(true);
                NavigateToStep(ConnectionStep.ServerSelect);
                _serverSelectPage.ShowError("An unknown initialization error occurred.");
            }
        }
        
        private void HandleInternalLoginSuccessEvent(object message)
        {
            if (message is InternalLoginResult internalLoginResult)
            {
                _unitSelectionPage.SetSelectionData(internalLoginResult);
                _playerName = internalLoginResult.PlayerName;
                ClientStateSingleton.Instance.LastSeenName = internalLoginResult.PlayerName; // TODO: implement LastSeenName on ClientStateStore
                NavigateToStep(ConnectionStep.UnitSelection);
            }
            else
            {
                _logger.Error("Connection error with no message provided.");
                Stop(true);
                NavigateToStep(ConnectionStep.ServerSelect);
                _serverSelectPage.ShowError("An unknown connection error occurred.");
            }
        }
        
        private void HandleGuestLoginError(object message)
        {
            var errorMsg = message as string ?? "An unknown guest login error occurred.";
            _logger.Error($"Guest login error: {errorMsg}");
            _guestPage.ShowError(errorMsg);
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

                    _vcsClient = new VcsClientSyncHandler(VcsUiUpdate, App.EventBus, App.RadioStateManager);
                    
                    Task.Run(() => _vcsClient.ConnectVcs(new IPEndPoint(_resolvedIp, _port)));
                }
                catch (Exception ex) when (ex is SocketException or ArgumentException)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    
                    _logger.Warn(ex, "Failed to connect to server");

                    ClientStateSingleton.Instance.IsConnected = false; // TODO: drive IsConnected via event bus on ClientStateStore
                }
            }
        }

        private void Login(UserLogin loginInformation)
        {
            // Not on the main thread blocking the UI
            Task.Run(() => _vcsClient.VcsLogin(loginInformation));
        }

        private void SelectUnit(string unitId, string coalition, uint roleId)
        {
            // Not on the main thread blocking the UI
            Task.Run(() => _vcsClient.SelectUnit(unitId, coalition, roleId));
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

            ClientStateSingleton.Instance.IsConnectionErrored = connectionError; // TODO: implement IsConnectionErrored on ClientStateStore

            ClientStateSingleton.Instance.IsConnected = false; // TODO: drive IsConnected via event bus on ClientStateStore

            _loginPage.Login.IsEnabled = true;
            _guestPage.Login.IsEnabled = true;

            _guestPage.LoginInProgress.Opacity = 0;
            
            ConnectionStatus.Fill = Brushes.Red;

            // TODO: implement LastSeenName on ClientStateStore
            if (!string.IsNullOrWhiteSpace(ClientStateSingleton.Instance.LastSeenName) &&
                _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).StringValue != ClientStateSingleton.Instance.LastSeenName)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, ClientStateSingleton.Instance.LastSeenName);
            }

            try
            {
                AudioManager.StopEncoding();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to stop audio encoding");
            }

            if (_vcsClient != null)
            {
                _vcsClient.Disconnect();
                _vcsClient = null;
            }
            
            _loginPage.LoginFailed();
            _guestPage.LoginFailed();

            ClientStateSingleton.Instance.IsServerMuted = false;
            ServerMuteBanner.Visibility = Visibility.Collapsed;
            LoggedIn = false;
            StepperHeader.Visibility = Visibility.Visible;
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

        private void HandleInitializationSuccess(bool isVanguardLoginEnabled, bool isGuestLoginEnabled)
        {
            _logger.Info("Initialization successful, setting up UI");
            ConnectionStatus.Fill = Brushes.Orange;
            _welcomePage.SetLoginEnabled(isVanguardLoginEnabled);
            _welcomePage.SetGuestEnabled(isGuestLoginEnabled);
            _welcomePage.ShowServerConnected(_connectedServerAddress);
            StepperHeader.Visibility = Visibility.Visible;
            NavigateToStep(ConnectionStep.Auth);
        }
        
        private void HandleGuestLoginSuccess()
        {
            ClientRole = VcsRole.Guest;
            HandleConnectionSuccess();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _connectionStateSubscription?.Dispose();
            _connectionStateSubscription = null;
            _serverActionSubscription?.Dispose();
            _serverActionSubscription = null;
            _serverMuteSubscription?.Dispose();
            _serverMuteSubscription = null;

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientY, Top);

            // TODO: implement LastSeenName on ClientStateStore
            if (!string.IsNullOrWhiteSpace(ClientStateSingleton.Instance.LastSeenName) &&
                _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).StringValue != ClientStateSingleton.Instance.LastSeenName)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, ClientStateSingleton.Instance.LastSeenName);
            }

            //save window position
            base.OnClosing(e);

            //stop timer
            _updateTimer?.Stop();

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

        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.MinimiseToTray))
            {
                Hide();
            }

            base.OnStateChanged(e);
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
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioDraggableX, 300);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioDraggableY, 300);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioDraggableWidth, 170);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioDraggableHeight, 175);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioDraggableOpacity, 1.0);

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

        private void HomeNavigation_OnClick(object sender, RoutedEventArgs e)
        {
            NavigateToStep(ConnectionStep.Auth);
        }

        #region Externally Called Methods

        private void Grid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
        
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}