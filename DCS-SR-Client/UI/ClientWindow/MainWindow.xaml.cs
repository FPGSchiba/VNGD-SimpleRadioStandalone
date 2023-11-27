using System;
using System.Collections.Concurrent;
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
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Recording;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.ClientList;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.Favourites;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.InputProfileWindow;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Overlay;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Dmo;
using NAudio.Wave;
using NLog;
using WPFCustomMessageBox;
using InputBinding = Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.InputBinding;
using System.Windows.Navigation;
using System.Security.Cryptography;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        public delegate void ReceivedAutoConnect(string address, int port);

        public delegate void ToggleOverlayCallback(bool uiButton, int switchTo);

        private readonly AudioManager _audioManager;

        private readonly string _guid;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private AudioPreview _audioPreview;
        private SRSClientSyncHandler _client;
        private DCSAutoConnectHandler _dcsAutoConnectListener;
        private int _port = 5002;

        private int _windowOpen = 12;

        // Vertical Radio-Overlays 
        private RadioOverlayWindowOneVertical _radioOverlayWindowOneVertical;
        private RadioOverlayWindowTwoVertical _radioOverlayWindowTwoVertical;
        private RadioOverlayWindowFiveVertical _radioOverlayWindowFiveVertical;
        private RadioOverlayWindowThreeVertical _radioOverlayWindowThreeVertical;
        private RadioOverlayWindowTenVertical _radioOverlayWindowTenVertical;
        private RadioOverlayWindowTenVerticalLong _radioOverlayWindowTenVerticalLong;
        private RadioOverlayWindowTenTransparent _radioOverlayWindowTenTransparent;

        // Horizontal Radio-Overlays
        private RadioOverlayWindowOneHorizontal _radioOverlayWindowOneHorizontal;
        private RadioOverlayWindowTwoHorizontal _radioOverlayWindowTwoHorizontal;
        private RadioOverlayWindowThreeHorizontal _radioOverlayWindowThreeHorizontal;
        private RadioOverlayWindowFiveHorizontal _radioOverlayWindowFiveHorizontal;
        private RadioOverlayWindowTenHorizontal _radioOverlayWindowTenHorizontal;
        private RadioOverlayWindowTenHorizontalWide _radioOverlayWindowTenHorizontalWide;

        // Windows array
        private Window[] windows = new Window[12];

        private IPAddress _resolvedIp;
        private ServerSettingsWindow _serverSettingsWindow;

        private ClientListWindow _clientListWindow;

        //used to debounce toggle
        private long _toggleShowHide;

        private readonly DispatcherTimer _updateTimer;
        private ServerAddress _serverAddress;
        private readonly DelegateCommand _connectCommand;

        private string version = "loading";

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

            // Initialize ToolTip controls
            ToolTips.Init();

            // Initialize images/icons
            Images.Init();

            // Initialise sounds
            Sounds.Init();

            // Set up tooltips that are always defined
            InitToolTips();

            DataContext = this;

            windows[0] = _radioOverlayWindowTwoVertical;
            windows[1] = _radioOverlayWindowThreeVertical;
            windows[2] = _radioOverlayWindowFiveVertical;
            windows[3] = _radioOverlayWindowTenVertical;
            windows[4] = _radioOverlayWindowTwoHorizontal;
            windows[5] = _radioOverlayWindowThreeHorizontal;
            windows[6] = _radioOverlayWindowFiveHorizontal;
            windows[7] = _radioOverlayWindowTenHorizontal;
            windows[8] = _radioOverlayWindowOneVertical;
            windows[9] = _radioOverlayWindowOneHorizontal;
            windows[10] = _radioOverlayWindowTenVerticalLong;
            windows[11] = _radioOverlayWindowTenHorizontalWide;
            windows[12] = _radioOverlayWindowTenTransparent;

            var client = ClientStateSingleton.Instance;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientY).DoubleValue;
            Assembly assembly = Assembly.GetExecutingAssembly();

            version = Regex.Replace(AssemblyName.GetAssemblyName(assembly.Location).Version.ToString(), @"(?<=\d\.\d\.\d)(.*)(?=)", "");

            Title = Title + " - " + version; //UpdaterChecker.VERSION

            CheckWindowVisibility();

            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.StartMinimised))
            {
                Hide();
                WindowState = WindowState.Minimized;

                Logger.Info("Started DCS-SimpleRadio Client " + version + " minimized"); //UpdaterChecker.VERSION
            }
            else
            {
                Logger.Info("Started DCS-SimpleRadio Client " + version); //UpdaterChecker.VERSION
            }

            _guid = ClientStateSingleton.Instance.ShortGUID;
            Analytics.Log("Client", "Startup", _globalSettings.GetClientSetting(GlobalSettingsKeys.ClientIdLong).RawValue);

            InitSettingsScreen();

            InitSettingsProfiles();
            ReloadProfile();

            InitInput();

            _connectCommand = new DelegateCommand(Connect, () => ServerAddress != null);
            FavouriteServersViewModel = new FavouriteServersViewModel(new CsvFavouriteServerStore());

            InitDefaultAddress();

            SpeakerBoost.Value = _globalSettings.GetClientSetting(GlobalSettingsKeys.SpeakerBoost).DoubleValue;

            Speaker_VU.Value = -100;
            Mic_VU.Value = -100;

            ExternalAWACSModeName.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).RawValue;

            _audioManager = new AudioManager(AudioOutput.WindowsN);
            _audioManager.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(_audioManager.SpeakerBoost);
            }

            // TODO: Use this for Vanguard
            // UpdaterChecker.CheckForUpdate(_globalSettings.GetClientSettingBool(GlobalSettingsKeys.CheckForBetaUpdates));

            InitFlowDocument();

            _dcsAutoConnectListener = new DCSAutoConnectHandler(AutoConnect);

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdatePlayerLocationAndVUMeters;
            _updateTimer.Start();

        }

        private void CheckWindowVisibility()
        {
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.DisableWindowVisibilityCheck))
            {
                Logger.Info("Window visibility check is disabled, skipping");
                return;
            }

            bool mainWindowVisible = false;
            bool radioWindowVisible = false;
            bool awacsWindowVisible = true; //changed to bypass check

            int mainWindowX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientX).DoubleValue;
            int mainWindowY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.ClientY).DoubleValue;
            
            Logger.Info($"Checking window visibility for main client window {{X={mainWindowX},Y={mainWindowY}}}");


            // -------- Vertical Panels -------
            // 1 Radio
            int radioOneVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneVerticalX).DoubleValue;
            int radioOneVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneVerticalY).DoubleValue;
            Logger.Info($"Checking window visibility for one vertical radio overlay {{X={radioOneVerticalX},Y={radioOneVerticalY}}}");
            // 2 Radio
            int radioTwoVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalX).DoubleValue;
            int radioTwoVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalY).DoubleValue;
            Logger.Info($"Checking window visibility for two vertical radio overlay {{X={radioTwoVerticalX},Y={radioTwoVerticalY}}}");
            // 3 Radio
            int radioThreeVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalX).DoubleValue;
            int radioThreeVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalY).DoubleValue;
            Logger.Info($"Checking window visibility for three vertical radio overlay {{X={radioThreeVerticalX},Y={radioThreeVerticalY}}}");
            // 5 Radio
            int radioFiveVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveX).DoubleValue;
            int radioFiveVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveY).DoubleValue;
            Logger.Info($"Checking window visibility for five vertical radio horizontal overlay {{X={radioFiveVerticalX},Y={radioFiveVerticalY}}}");
            // 10 Radio
            int radioTenVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenVerticalX).DoubleValue;
            int radioTenVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenVerticalY).DoubleValue;
            Logger.Info($"Checking window visibility for ten vertical radio overlay {{X={radioTenVerticalX},Y={radioTenVerticalY}}}");
            // 10 Radio Long
            int radioTenLongVerticalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalX).DoubleValue;
            int radioTenLongVerticalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalY).DoubleValue;
            Logger.Info($"Checking window visibility for ten vertical long radio overlay {{X={radioTenLongVerticalX},Y={radioTenLongVerticalY}}}");
            // 10 Radio Transparent
            int radioTenTransparentX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX).DoubleValue;
            int radioTenTransparentY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY).DoubleValue;
            Logger.Info($"Checking window visibility for ten transparent radio overlay {{X={radioTenTransparentX},Y={radioTenTransparentY}}}");

            // -------- Horizontal Panels -------
            // 1 Radio
            int radioOneHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX).DoubleValue;
            int radioOneHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for one horizontal radio overlay {{X={radioOneHorizontalX},Y={radioOneHorizontalY}}}");
            // 2 Radio
            int radioTwoHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalX).DoubleValue;
            int radioTwoHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for two horizontal radio overlay {{X={radioTwoHorizontalX},Y={radioTwoHorizontalY}}}");
            // 3 Radio
            int radioThreeHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalX).DoubleValue;
            int radioThreeHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for three horizontal radio overlay {{X={radioThreeHorizontalX},Y={radioThreeHorizontalY}}}");
            // 5 Radio
            int radioFiveHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalX).DoubleValue;
            int radioFiveHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for five horizontal radio overlay {{X={radioFiveHorizontalX},Y={radioFiveHorizontalY}}}");

            //10 Radio
            int radioTenHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalX).DoubleValue;
            int radioTenHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for ten radio horizontal overlay {{X={radioTenHorizontalX},Y={radioTenHorizontalY}}}");
            //10 Radio Wide
            int radioTenWideHorizontalX = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalX).DoubleValue;
            int radioTenWideHorizontalY = (int)_globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalY).DoubleValue;
            Logger.Info($"Checking window visibility for ten wide radio horizontal overlay {{X={radioTenWideHorizontalX},Y={radioTenWideHorizontalY}}}");



            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                Logger.Info($"Checking {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds} for window visibility");

                if (screen.Bounds.Contains(mainWindowX, mainWindowY))
                {
                    Logger.Info($"Main client window {{X={mainWindowX},Y={mainWindowY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    mainWindowVisible = true;
                }
                // ------- Vertical Panels ------
                if (screen.Bounds.Contains(radioOneVerticalX, radioOneVerticalY))
                {
                    Logger.Info($"Radio One Vertical overlay {{X={radioOneVerticalX},Y={radioOneVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTwoVerticalX, radioTwoVerticalY))
                {
                    Logger.Info($"Radio Two Vertical overlay {{X={radioTwoVerticalX},Y={radioTwoVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioThreeVerticalX, radioThreeVerticalY))
                {
                    Logger.Info($"Radio Three Vertical overlay {{X={radioThreeVerticalX},Y={radioThreeVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioFiveVerticalX, radioFiveVerticalY))
                {
                    Logger.Info($"Radio Five Vertical overlay {{X={radioFiveVerticalX},Y={radioFiveVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTenVerticalX, radioTenVerticalY))
                {
                    Logger.Info($"Radio Ten Vertical overlay {{X={radioTenVerticalX},Y={radioTenVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTenLongVerticalX, radioTenLongVerticalY))
                {
                    Logger.Info($"Radio Ten Long Vertical overlay {{X={radioTenLongVerticalX},Y={radioTenLongVerticalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTenTransparentX, radioTenTransparentY))
                {
                    Logger.Info($"Radio Ten Transparent overlay {{X={radioTenTransparentX},Y={radioTenTransparentY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }

                // -------- Horizontal Panels -----------
                if (screen.Bounds.Contains(radioOneHorizontalX, radioOneHorizontalY))
                {
                    Logger.Info($"Radio One Horizontal overlay {{X={radioOneHorizontalX},Y={radioOneHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTwoHorizontalX, radioTwoHorizontalY))
                {
                    Logger.Info($"Radio Two Horizontal overlay {{X={radioTwoHorizontalX},Y={radioTwoHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioThreeHorizontalX, radioThreeHorizontalY))
                {
                    Logger.Info($"Radio Three Horizontal overlay {{X={radioThreeHorizontalX},Y={radioThreeHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioFiveHorizontalX, radioFiveHorizontalY))
                {
                    Logger.Info($"Radio Five Horizontal overlay {{X={radioFiveHorizontalX},Y={radioFiveHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTenHorizontalX, radioTenHorizontalY))
                {
                    Logger.Info($"Radio Ten Horizontal overlay {{X={radioTenHorizontalX},Y={radioTenHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
                if (screen.Bounds.Contains(radioTenWideHorizontalX, radioTenWideHorizontalY))
                {
                    Logger.Info($"Radio Ten Wide Horizontal overlay {{X={radioTenWideHorizontalX},Y={radioTenWideHorizontalY}}} is visible on {(screen.Primary ? "primary " : "")}screen {screen.DeviceName} with bounds {screen.Bounds}");
                    radioWindowVisible = true;
                }
            }

            if (!mainWindowVisible)
            {
                MessageBox.Show(this,
                    "The SRS client window is no longer visible likely due to a monitor reconfiguration.\n\nThe position will be reset to default to fix this issue.",
                    "SRS window position reset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Logger.Warn($"Main client window outside visible area of monitors, resetting position ({mainWindowX},{mainWindowY}) to defaults");

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientX, 200);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.ClientY, 200);

                Left = 200;
                Top = 200;
            }

            if (!radioWindowVisible)
            {
                MessageBox.Show(this,
                    "The SRS radio overlay is no longer visible likely due to a monitor reconfiguration.\n\nThe position will be reset to default to fix this issue.",
                    "SRS window position reset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                // ------- Vertical Panels ---------
                Logger.Warn($"Radio One Vertical overlay window outside visible area of monitors, resetting position ({radioOneVerticalX},{radioOneVerticalY}) to defaults");
                Logger.Warn($"Radio Two Vertical overlay window outside visible area of monitors, resetting position ({radioTwoVerticalX},{radioTwoVerticalY}) to defaults");
                Logger.Warn($"Radio Three Vertical overlay window outside visible area of monitors, resetting position ({radioThreeVerticalX},{radioThreeVerticalY}) to defaults");
                Logger.Warn($"Radio Five Vertical overlay window outside visible area of monitors, resetting position ({radioFiveVerticalX},{radioFiveVerticalY}) to defaults");
                Logger.Warn($"Radio Ten Vertical overlay window outside visible area of monitors, resetting position ({radioTenVerticalX},{radioTenVerticalY}) to defaults");
                Logger.Warn($"Radio Ten Long Vertical overlay window outside visible area of monitors, resetting position ({radioTenLongVerticalX},{radioTenLongVerticalY}) to defaults");
                Logger.Warn($"Radio Ten Transparent overlay window outside visible area of monitors, resetting position ({radioTenTransparentX},{radioTenTransparentY}) to defaults");

                // ------- Horizontal Panels ----------
                Logger.Warn($"Radio One Horizontal overlay window outside visible area of monitors, resetting position ({radioOneHorizontalX},{radioOneHorizontalY}) to defaults");
                Logger.Warn($"Radio Two Horizontal overlay window outside visible area of monitors, resetting position ({radioTwoHorizontalX},{radioTwoHorizontalY}) to defaults");
                Logger.Warn($"Radio Three Horizontal overlay window outside visible area of monitors, resetting position ({radioThreeHorizontalX},{radioThreeHorizontalY}) to defaults");
                Logger.Warn($"Radio Five Horizontal overlay window outside visible area of monitors, resetting position ({radioFiveHorizontalX},{radioFiveHorizontalY}) to defaults");
                Logger.Warn($"Radio Ten Horizontal overlay window outside visible area of monitors, resetting position ({radioTenHorizontalX},{radioTenHorizontalY}) to defaults");
                Logger.Warn($"Radio Ten Wide Horizontal overlay window outside visible area of monitors, resetting position ({radioTenWideHorizontalX},{radioTenWideHorizontalY}) to defaults");

                // Reset Radio Panel Positions
                // ----- Vertical -------
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneVerticalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoVerticalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeVerticalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenVerticalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenLongVerticalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY, 300);

                // ------ Horizontal ------
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTwoHorizontalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioThreeHorizontalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioFiveHorizontalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalY, 300);

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenWideHorizontalY, 300);

                // Null Value Protection
                // ---- Vertical Panels -----
                if (_radioOverlayWindowOneVertical != null)
                {
                    _radioOverlayWindowOneVertical.Left = 300;
                    _radioOverlayWindowOneVertical.Top = 300;
                }

                if (_radioOverlayWindowTwoVertical != null)
                {
                    _radioOverlayWindowTwoVertical.Left = 300;
                    _radioOverlayWindowTwoVertical.Top = 300;
                }

                if (_radioOverlayWindowThreeVertical != null)
                {
                    _radioOverlayWindowThreeVertical.Left = 300;
                    _radioOverlayWindowThreeVertical.Top = 300;
                }

                if (_radioOverlayWindowFiveVertical != null)
                {
                    _radioOverlayWindowFiveVertical.Left = 300;
                    _radioOverlayWindowFiveVertical.Top = 300;
                }

                if (_radioOverlayWindowTenVertical != null)
                {
                    _radioOverlayWindowTenVertical.Left = 300;
                    _radioOverlayWindowTenVertical.Top = 300;
                }

                if (_radioOverlayWindowTenVerticalLong != null)
                {
                    _radioOverlayWindowTenVerticalLong.Left = 300;
                    _radioOverlayWindowTenVerticalLong.Top = 300;
                }

                if (_radioOverlayWindowTenTransparent != null)
                {
                    _radioOverlayWindowTenTransparent.Left = 300;
                    _radioOverlayWindowTenTransparent.Top = 300;
                }

                // ------- Horizontal Panels ---------
                if (_radioOverlayWindowOneHorizontal != null)
                {
                    _radioOverlayWindowOneHorizontal.Left = 300;
                    _radioOverlayWindowOneHorizontal.Top = 300;
                }

                if (_radioOverlayWindowTwoHorizontal != null)
                {
                    _radioOverlayWindowTwoHorizontal.Left = 300;
                    _radioOverlayWindowTwoHorizontal.Top = 300;
                }

                if (_radioOverlayWindowThreeHorizontal != null)
                {
                    _radioOverlayWindowThreeHorizontal.Left = 300;
                    _radioOverlayWindowThreeHorizontal.Top = 300;
                }

                if (_radioOverlayWindowFiveHorizontal != null)
                {
                    _radioOverlayWindowFiveHorizontal.Left = 300;
                    _radioOverlayWindowFiveHorizontal.Top = 300;
                }

                if (_radioOverlayWindowTenHorizontal != null)
                {
                    _radioOverlayWindowTenHorizontal.Left = 300;
                    _radioOverlayWindowTenHorizontal.Top = 300;
                }

                if (_radioOverlayWindowTenHorizontalWide != null)
                {
                    _radioOverlayWindowTenHorizontalWide.Left = 300;
                    _radioOverlayWindowTenHorizontalWide.Top = 300;
                }
            }

           if (!awacsWindowVisible)   // --- removed by dabble
            {
                MessageBox.Show(this,
                    "The SRS AWACS overlay is no longer visible likely due to a monitor reconfiguration.\n\nThe position will be reset to default to fix this issue",
                    "SRS window position reset",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Logger.Warn($"AWACS overlay window outside visible area of monitors, resetting position ({radioTenHorizontalX},{radioTenHorizontalY}) to defaults");

                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalX, 300);
                _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenHorizontalY, 300);

                if (_radioOverlayWindowTenHorizontal != null)
                {
                    _radioOverlayWindowTenHorizontal.Left = 300;
                    _radioOverlayWindowTenHorizontal.Top = 300;
                }
            }
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
            Logger.Info(ControlsProfile.SelectedValue as string + " - Profile now in use");
            _globalSettings.ProfileSettingsStore.CurrentProfileName = ControlsProfile.SelectedValue as string;

            //redraw UI
            ReloadInputBindings();
            ReloadProfileSettings();
            ReloadRadioAudioChannelSettings();

            CurrentProfile.Content = _globalSettings.ProfileSettingsStore.CurrentProfileName;
        }

        private void InitInput()
        {
            InputManager = new InputDeviceManager(this, ToggleOverlay);

            InitSettingsProfiles();

            ControlsProfile.SelectionChanged += OnProfileDropDownChanged;

            RadioStartTransmitEffect.SelectionChanged += OnRadioStartTransmitEffectChanged;
            RadioEndTransmitEffect.SelectionChanged += OnRadioEndTransmitEffectChanged;

            IntercomStartTransmitEffect.SelectionChanged += OnIntercomStartTransmitEffectChanged;
            IntercomEndTransmitEffect.SelectionChanged += OnIntercomEndTransmitEffectChanged;

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

            RadioOverlay.InputName = "Overlay Toggle";
            RadioOverlay.ControlInputBinding = InputBinding.OverlayToggle;
            RadioOverlay.InputDeviceManager = InputManager;

            AwacsOverlayToggle.InputName = "Awacs Toggle";
            AwacsOverlayToggle.ControlInputBinding = InputBinding.AwacsOverlayToggle;
            AwacsOverlayToggle.InputDeviceManager = InputManager;

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
        }

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
            Radio1.LoadInputSettings();
            Radio2.LoadInputSettings();
            Radio3.LoadInputSettings();
            PTT.LoadInputSettings();
            Intercom.LoadInputSettings();
            IntercomPTT.LoadInputSettings();
            RadioOverlay.LoadInputSettings();
            Radio4.LoadInputSettings();
            Radio5.LoadInputSettings();
            Radio6.LoadInputSettings();
            Radio7.LoadInputSettings();
            Radio8.LoadInputSettings();
            Radio9.LoadInputSettings();
            Radio10.LoadInputSettings();
            Up100.LoadInputSettings();
            Up10.LoadInputSettings();
            Up1.LoadInputSettings();
            Up01.LoadInputSettings();
            Up001.LoadInputSettings();
            Up0001.LoadInputSettings();
            Down100.LoadInputSettings();
            Down10.LoadInputSettings();
            Down1.LoadInputSettings();
            Down01.LoadInputSettings();
            Down001.LoadInputSettings();
            Down0001.LoadInputSettings();
            ToggleGuard.LoadInputSettings();
            NextRadio.LoadInputSettings();
            PreviousRadio.LoadInputSettings();
            ToggleEncryption.LoadInputSettings();
            EncryptionKeyIncrease.LoadInputSettings();
            EncryptionKeyDecrease.LoadInputSettings();
            RadioChannelUp.LoadInputSettings();
            RadioChannelDown.LoadInputSettings();
            RadioVolumeUp.LoadInputSettings();
            RadioVolumeDown.LoadInputSettings();
            AwacsOverlayToggle.LoadInputSettings();
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
                    ExternalAWACSModePassword.Password = string.IsNullOrWhiteSpace(value.EAMCoalitionPassword) ? "" : value.EAMCoalitionPassword;
                }

                _connectCommand.RaiseCanExecuteChanged();
            }
        }

        public ICommand ConnectCommand => _connectCommand;

        private void UpdatePlayerLocationAndVUMeters(object sender, EventArgs e)
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
            RecordingQuality.Value = double.Parse(
                _globalSettings.GetClientSetting(GlobalSettingsKeys.RecordingQuality).StringValue[1].ToString());
            RecordingQuality.IsEnabled = true;

            var objValue = Registry.GetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRSAnalyticsOptOut", "FALSE");
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
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume, (float)vol);
                }

            };
            NATOToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            NATOToneVolume.IsEnabled = true;

            HQToneVolume.IsEnabled = false;
            HQToneVolume.ValueChanged += (sender, e) =>
            {
                if (HQToneVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HQToneVolume, (float)vol);
                }

            };
            HQToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HQToneVolume)
                                  / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HQToneVolume.IsEnabled = true;

            FMEffectVolume.IsEnabled = false;
            FMEffectVolume.ValueChanged += (sender, e) =>
            {
                if (FMEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume, (float)vol);
                }

            };
            FMEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            FMEffectVolume.IsEnabled = true;

            VHFEffectVolume.IsEnabled = false;
            VHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (VHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume, (float)vol);
                }

            };
            VHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            VHFEffectVolume.IsEnabled = true;

            UHFEffectVolume.IsEnabled = false;
            UHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (UHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume, (float)vol);
                }

            };
            UHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            UHFEffectVolume.IsEnabled = true;

            HFEffectVolume.IsEnabled = false;
            HFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (HFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume, (float)vol);
                }

            };
            HFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HFEffectVolume.IsEnabled = true;
        }

        private void Connect()
        {
            if (ClientState.IsConnected)
            {
                Stop();
            }
            else
            {
                SaveSelectedInputAndOutput();

                try
                {
                    //process hostname
                    var resolvedAddresses = Dns.GetHostAddresses(GetAddressFromTextBox());
                    var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4

                    if (ip != null)
                    {
                        _resolvedIp = ip;
                        _port = GetPortFromTextBox();

                        _client = new SRSClientSyncHandler(_guid, UpdateUICallback, delegate (string name, int seat)
                        {
                            try
                            {
                                //on MAIN thread
                                Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                                    new ThreadStart(() =>
                                    {
                                        //Handle Aircraft Name - find matching profile and select if you can
                                        name = Regex.Replace(name.Trim().ToLower(), "[^a-zA-Z0-9]", "");
                                        //add one to seat so seat_2 is copilot
                                        var nameSeat = $"_{seat + 1}";

                                        foreach (var profileName in _globalSettings.ProfileSettingsStore.ProfileNames)
                                        {
                                            //find matching seat
                                            var splitName = profileName.Trim().ToLowerInvariant().Split('_').First();
                                            if (name.StartsWith(Regex.Replace(splitName, "[^a-zA-Z0-9]", "")) && profileName.Trim().EndsWith(nameSeat))
                                            {
                                                ControlsProfile.SelectedItem = profileName;
                                                return;
                                            }
                                        }

                                        foreach (var profileName in _globalSettings.ProfileSettingsStore.ProfileNames)
                                        {
                                            //find matching seat
                                            if (name.StartsWith(Regex.Replace(profileName.Trim().ToLower(), "[^a-zA-Z0-9_]", "")))
                                            {
                                                ControlsProfile.SelectedItem = profileName;
                                                return;
                                            }
                                        }

                                        ControlsProfile.SelectedIndex = 0;

                                    }));
                            }
                            catch (Exception)
                            {
                            }

                        });
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

                        ClientState.IsConnected = false;
                        ToggleServerSettings.IsEnabled = false;
                    }
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

        private void Stop(bool connectionError = false)
        {
            if (ClientState.IsConnected && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.PlayConnectionSounds))
            {
                try
                {
                    Sounds.BeepDisconnected.Play();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "Failed to play disconnect sound");
                }
            }

            ClientState.IsConnectionErrored = connectionError;

            StartStop.Content = "Connect: Server";
            StartStop.IsEnabled = true;
            Mic.IsEnabled = true;
            Speakers.IsEnabled = true;
            MicOutput.IsEnabled = true;
            Preview.IsEnabled = true;
            ClientState.IsConnected = false;
            ToggleServerSettings.IsEnabled = false;

            ConnectExternalAWACSMode.IsEnabled = false;
            ConnectExternalAWACSMode.Content = "Connect: Radios";

            if (!string.IsNullOrWhiteSpace(ClientState.LastSeenName) &&
                _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).StringValue != ClientState.LastSeenName)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, ClientState.LastSeenName);
            }

            try
            {
                _audioManager.StopEncoding();
            }
            catch (Exception)
            {
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
            if (AudioInput.MicrophoneAvailable)
            {
                if (AudioInput.SelectedAudioInput.Value == null)
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, "default");

                }
                else
                {
                    var input = ((MMDevice)AudioInput.SelectedAudioInput.Value).ID;
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, input);
                }
            }

            if (AudioOutput.SelectedAudioOutput.Value == null)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId, "default");
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
                        StartStop.Content = "Disconnect: Server";
                        StartStop.IsEnabled = true;

                        ClientState.IsConnected = true;
                        ClientState.IsVoipConnected = false;

                        if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.PlayConnectionSounds))
                        {
                            try
                            {
                                Sounds.BeepConnected.Play();
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn(ex, "Failed to play connect sound");
                            }
                        }

                        _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, ServerIp.Text);

                        _audioManager.StartEncoding(_guid, InputManager,
                            _resolvedIp, _port);
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
                    Logger.Info("Unable to preview audio, no valid audio input device available or selected");
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
            if (ClientState.IsConnected)
            {
                ToggleServerSettings.IsEnabled = true;

                bool eamEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);

                ConnectExternalAWACSMode.IsEnabled = eamEnabled;
                ConnectExternalAWACSMode.Content = ClientState.ExternalAWACSModelSelected ? "Disconnect: Radios" : "Connect: Radios";
            }
            else
            {
                ToggleServerSettings.IsEnabled = false;
                ConnectExternalAWACSMode.IsEnabled = false;
                ConnectExternalAWACSMode.Content = "Connect: Radios";
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
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEncryptionEffects,
                (bool)RadioEncryptionEffectsToggle.IsChecked);
        }

        private void NATORadioTone_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.NATOTone,
                (bool)NATORadioToneToggle.IsChecked);
        }

        private void RadioSwitchPTT_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioSwitchIsPTT, (bool)RadioSwitchIsPTT.IsChecked);
        }

        private void ShowOverlayTwoVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 0);
        }

        private void ShowOverlayThreeVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 1);
        }

        private void ShowOverlayFiveVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 2);
        }

        private void ShowOverlayTenVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 3);
        }

        private void ShowOverlayHorizontalTwo_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 4);
        }

        private void ShowOverlayThreeHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 5);
        }

        private void ShowOverlayFiveHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 6);
        }

        private void ShowOverlayTenHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 7);
        }

        private void ShowOverlayOneVertical_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 8);
        }

        private void ShowOverlayOneHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 9);
        }

        private void ShowOverlayTenVerticalLong_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 10);
        }

        private void ShowOverlayTenHorizontalWide_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 11);
        }

        private void ShowOverlayTenTransparent_OnClick(object sender, RoutedEventArgs e)
        {
            ToggleOverlay(true, 12);
        }

        private void ToggleOverlay(bool uiButton, int switchTo)
        {
            //debounce show hide (1 tick = 100ns, 6000000 ticks = 600ms debounce)
            if ((DateTime.Now.Ticks - _toggleShowHide > 6000000) || uiButton)
            {
                _toggleShowHide = DateTime.Now.Ticks;
                // Catching out of bounds switch to.
                // TODO: Maybe take switchTo = 13 to close all windows.
                if (switchTo < 0 || switchTo > windows.Count() - 1)
                {
                    Logger.Error($"Could not switch to RadioWindow-{switchTo}.");
                    return;
                }

                // This can be expanded or smallered quite easily, not like the last bit of code
                int openWindow = _windowOpen;
                for (int i = 0; i < windows.Count(); i++)
                {
                    if (windows[i] != null)
                    {
                        windows[i].Close();
                        windows[i] = null;
                        openWindow = i;
                    }
                }

                // Check if we want to toggle or open another window
                if (switchTo != openWindow)
                {
                    // Open new Window
                    // Only needed to select right instance of the Radio panel Overlay
                    switch (switchTo)
                    {
                        case 0:
                            windows[switchTo] = new RadioOverlayWindowTwoVertical(ToggleOverlay);
                            break;
                        case 1:
                            windows[switchTo] = new RadioOverlayWindowThreeVertical(ToggleOverlay);
                            break;
                        case 2:
                            windows[switchTo] = new RadioOverlayWindowFiveVertical(ToggleOverlay);
                            break;
                        case 3:
                            windows[switchTo] = new RadioOverlayWindowTenVertical(ToggleOverlay);
                            break;
                        case 4:
                            windows[switchTo] = new RadioOverlayWindowTwoHorizontal(ToggleOverlay);
                            break;
                        case 5:
                            windows[switchTo] = new RadioOverlayWindowThreeHorizontal(ToggleOverlay);
                            break;
                        case 6:
                            windows[switchTo] = new RadioOverlayWindowFiveHorizontal(ToggleOverlay);
                            break;
                        case 7:
                            windows[switchTo] = new RadioOverlayWindowTenHorizontal(ToggleOverlay);
                            break;
                        case 8:
                            // TODO: Add Toggle Overlay for Horizontal Swap
                            windows[switchTo] = new RadioOverlayWindowOneVertical();
                            break;
                        case 9:
                            // TODO: Add Toggle Overlay for Horizontal Swap
                            windows[switchTo] = new RadioOverlayWindowOneHorizontal();
                            break;
                        case 10:
                            // TODO: Add Toggle Overlay for Horizontal Swap
                            windows[switchTo] = new RadioOverlayWindowTenVerticalLong();
                            break;
                        case 11:
                            // TODO: Add Toggle Overlay for Horizontal Swap
                            windows[switchTo] = new RadioOverlayWindowTenHorizontalWide();
                            break;
                        case 12:
                            windows[switchTo] = new RadioOverlayWindowTenTransparent(ToggleOverlay);
                            break;
                    }
                    try
                    {
                        windows[switchTo].ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
                        windows[switchTo].Show();
                        windows[switchTo].Closed += PanelWindow_Closed;
                    }
                    catch
                    {
                        Logger.Error($"Could not open Window with ID: {switchTo}.");
                        MessageBox.Show($"Window could not Open (Window-ID: {switchTo}).\nPlease give this Information to the SRS Development Team!", "Error Opening Panel", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    
                }
                else
                {
                    // No Panel window is open
                    _windowOpen = 13;
                }
            }
        }

        // This needs to listen on every window[switchTo].Closed Event.
        private void PanelWindow_Closed(object sender, EventArgs e)
        {
            // No window open -> A window was closed and Only 1 Window can be active
            _windowOpen = 13;

            // Erase window from windows array to clean up everything
            for (int i = 0; i < windows.Count(); i++)
            {
                if (windows[i] != null)
                {
                    windows[i].Close();
                    windows[i] = null;
                }
            }

        }

        private void AutoConnect(string address, int port)
        {
            string connection = $"{address}:{port}";

            Logger.Info($"Received AutoConnect DCS-SRS @ {connection}");

            var enabled = _globalSettings.GetClientSetting(GlobalSettingsKeys.AutoConnect).BoolValue;

            if (!enabled)
            {
                Logger.Info($"Ignored Autoconnect - not Enabled");
            }

            if (ClientState.IsConnected)
            {
                // Always show prompt about active/advertised SRS connection mismatch if client is already connected
                string[] currentConnectionParts = ServerIp.Text.Trim().Split(':');
                string currentAddress = currentConnectionParts[0];
                int currentPort = 5002;
                if (currentConnectionParts.Length >= 2)
                {
                    if (!int.TryParse(currentConnectionParts[1], out currentPort))
                    {
                        Logger.Warn($"Failed to parse port {currentConnectionParts[1]} of current connection, falling back to 5002 for autoconnect comparison");
                        currentPort = 5002;
                    }
                }
                string currentConnection = $"{currentAddress}:{currentPort}";

                if (string.Equals(address, currentAddress, StringComparison.OrdinalIgnoreCase) && port == currentPort)
                {
                    // Current connection matches SRS server advertised by DCS, all good
                    Logger.Info($"Current SRS connection {currentConnection} matches advertised server {connection}, ignoring autoconnect");
                    return;
                }
                else if (port != currentPort)
                {
                    // Port mismatch, will always be a different server, no need to perform hostname lookups
                    HandleAutoConnectMismatch(currentConnection, connection);
                    return;
                }

                // Perform DNS lookup of advertised and current hostnames to find hostname/resolved IP matches
                List<string> currentIPs = new List<string>();

                if (IPAddress.TryParse(currentAddress, out IPAddress currentIP))
                {
                    currentIPs.Add(currentIP.ToString());
                }
                else
                {
                    try
                    {
                        foreach (IPAddress ip in Dns.GetHostAddresses(currentConnectionParts[0]))
                        {
                            // SRS currently only supports IPv4 (due to address/port parsing)
                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                currentIPs.Add(ip.ToString());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e, $"Failed to resolve current SRS host {currentConnectionParts[0]} to IP addresses, ignoring autoconnect advertisement");
                        return;
                    }
                }

                List<string> advertisedIPs = new List<string>();

                if (IPAddress.TryParse(address, out IPAddress advertisedIP))
                {
                    advertisedIPs.Add(advertisedIP.ToString());
                }
                else
                {
                    try
                    {
                        foreach (IPAddress ip in Dns.GetHostAddresses(connection))
                        {
                            // SRS currently only supports IPv4 (due to address/port parsing)
                            if (ip.AddressFamily == AddressFamily.InterNetwork)
                            {
                                advertisedIPs.Add(ip.ToString());
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Warn(e, $"Failed to resolve advertised SRS host {address} to IP addresses, ignoring autoconnect advertisement");
                        return;
                    }
                }

                if (!currentIPs.Intersect(advertisedIPs).Any())
                {
                    // No resolved IPs match, display mismatch warning
                    HandleAutoConnectMismatch(currentConnection, connection);
                }
            }
            else
            {
                // Show auto connect prompt if client is not connected yet and setting has been enabled, otherwise automatically connect
                bool showPrompt = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectPrompt);

                bool connectToServer = !showPrompt;
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectPrompt))
                {
                    WindowHelper.BringProcessToFront(Process.GetCurrentProcess());

                    var result = MessageBox.Show(this,
                        $"Would you like to try to auto-connect to DCS-SRS @ {address}:{port}? ", "Auto Connect",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    connectToServer = (result == MessageBoxResult.Yes) && (StartStop.Content.ToString().ToLower() == "connect: server");
                }

                if (connectToServer)
                {
                    ServerIp.Text = connection;
                    Connect();
                }
            }
        }

        private async void HandleAutoConnectMismatch(string currentConnection, string advertisedConnection)
        {
            // Show auto connect mismatch prompt if setting has been enabled (default), otherwise automatically switch server
            bool showPrompt = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AutoConnectMismatchPrompt);

            Logger.Info($"Current SRS connection {currentConnection} does not match advertised server {advertisedConnection}, {(showPrompt ? "displaying mismatch prompt" : "automatically switching server")}");

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
                Connect();
            }
        }

        private void ResetRadioWindow_Click(object sender, RoutedEventArgs e)
        {
            //close overlay
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
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentHeight, 905);

            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentOpacity, 0.0);

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
                _serverSettingsWindow?.Close();
                _serverSettingsWindow = null;
            }
        }



        private void AutoConnectToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnect, (bool)AutoConnectEnabledToggle.IsChecked);
        }

        private void AutoConnectPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnectPrompt, (bool)AutoConnectPromptToggle.IsChecked);
        }

        private void AutoConnectMismatchPromptToggle_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoConnectMismatchPrompt, (bool)AutoConnectMismatchPromptToggle.IsChecked);
        }

        private void RadioOverlayTaskbarItem_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RadioOverlayTaskbarHide, (bool)RadioOverlayTaskbarItem.IsChecked);

            if (_radioOverlayWindowTwoVertical != null)
                _radioOverlayWindowTwoVertical.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
            else if (_radioOverlayWindowFiveVertical != null)
                _radioOverlayWindowFiveVertical.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
            else if (_radioOverlayWindowTenHorizontal != null) _radioOverlayWindowTenHorizontal.ShowInTaskbar = !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide);
        }

        private void DCSRefocus_OnClick_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RefocusDCS, (bool)RefocusDCS.IsChecked);
        }

        private void ExpandInputDevices_OnClick_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "You must restart SRS for this setting to take effect.\n\nTurning this on will allow almost any DirectX device to be used as input expect a Mouse but WILL LIKELY cause issues with other devices being detected. \n\nUse SRS Device Listing (see Discord) instead to enable the missing device\n\nDo not turn on unless you know what you're doing :) ",
                "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _globalSettings.SetClientSetting(GlobalSettingsKeys.ExpandControls, (bool)ExpandInputDevices.IsChecked);
        }

        private void AllowXInputController_OnClick_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "You must restart SRS for this setting to take effect.",
                "Restart SimpleRadio Standalone", MessageBoxButton.OK,
                MessageBoxImage.Warning);

            _globalSettings.SetClientSetting(GlobalSettingsKeys.AllowXInputController, (bool)AllowXInputController.IsChecked);
        }

        private void LaunchAddressTab(object sender, RoutedEventArgs e)
        {
            TabControl.SelectedItem = FavouritesSeversTab;
        }

        private void MicAGC_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AGC, (bool)MicAGC.IsChecked);
        }

        private void MicDenoise_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.Denoise, (bool)MicDenoise.IsChecked);
        }

        private void RadioSoundEffects_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEffects,
                (bool)RadioSoundEffects.IsChecked);
        }

        private void RadioTxStart_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_Start, (bool)RadioTxStartToggle.IsChecked);
        }

        private void RadioTxEnd_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioTxEffects_End, (bool)RadioTxEndToggle.IsChecked);
        }

        private void RadioRxStart_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_Start, (bool)RadioRxStartToggle.IsChecked);
        }

        private void RadioRxEnd_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioRxEffects_End, (bool)RadioRxEndToggle.IsChecked);
        }

        private void RadioMIDS_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.MIDSRadioEffect, (bool)RadioMIDSToggle.IsChecked);
        }

        private void AudioSelectChannel_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AutoSelectPresetChannel, (bool)AutoSelectChannel.IsChecked);
        }

        private void RadioSoundEffectsClipping_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioEffectsClipping,
                (bool)RadioSoundEffectsClipping.IsChecked);

        }

        private void MinimiseToTray_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.MinimiseToTray, (bool)MinimiseToTray.IsChecked);
        }

        private void StartMinimised_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.StartMinimised, (bool)StartMinimised.IsChecked);
        }

        private void AllowDCSPTT_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT, (bool)AllowDCSPTT.IsChecked);
        }

        private void AllowRotaryIncrement_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RotaryStyleIncrement, (bool)AllowRotaryIncrement.IsChecked);
        }

        private void AlwaysAllowHotas_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls, (bool)AlwaysAllowHotas.IsChecked);
        }

        private void CheckForBetaUpdates_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.CheckForBetaUpdates, (bool)CheckForBetaUpdates.IsChecked);
        }

        private void PlayConnectionSounds_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.PlayConnectionSounds, (bool)PlayConnectionSounds.IsChecked);
        }

        private void ConnectExternalAWACSMode_OnClick(object sender, RoutedEventArgs e)
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
                !_serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE) ||
                (!ClientState.ExternalAWACSModelSelected &&
                string.IsNullOrWhiteSpace(ExternalAWACSModePassword.Password)))
            {
                return;
            }

            // Already connected, disconnect
            if (ClientState.ExternalAWACSModelSelected)
            {
                _client.DisconnectExternalAWACSMode();
            }
            else if (!ClientState.IsGameExportConnected) //only if we're not in game
            {
                ClientState.LastSeenName = ExternalAWACSModeName.Text;
                _client.ConnectExternalAWACSMode(ExternalAWACSModePassword.Password.Trim(), ExternalAWACSModeConnectionChanged);
            }
        }

        private void ExternalAWACSModeConnectionChanged(bool result, int coalition)
        {
            if (result)
            {
                ClientState.ExternalAWACSModelSelected = true;
                ClientState.PlayerCoaltionLocationMetadata.side = coalition;
                ClientState.PlayerCoaltionLocationMetadata.name = ClientState.LastSeenName;
                ClientState.DcsPlayerRadioInfo.name = ClientState.LastSeenName;

                ConnectExternalAWACSMode.Content = "Disconnect: Radios";
            }
            else
            {
                ClientState.ExternalAWACSModelSelected = false;
                ClientState.PlayerCoaltionLocationMetadata.side = 0;
                ClientState.PlayerCoaltionLocationMetadata.name = "";
                ClientState.DcsPlayerRadioInfo.name = "";
                ClientState.DcsPlayerRadioInfo.LastUpdate = 0;
                ClientState.LastSent = 0;

                ConnectExternalAWACSMode.Content = "Connect: Radios";
                ExternalAWACSModePassword.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
                ExternalAWACSModeName.IsEnabled = _serverSettings.GetSettingAsBool(Common.Setting.ServerSettingsKeys.EXTERNAL_AWACS_MODE);
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
            Registry.SetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRPathStandalone", Directory.GetCurrentDirectory());

            MessageBox.Show(this,
                "SRS Path set to: " + Directory.GetCurrentDirectory(),
                "SRS Client Path",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void RequireAdminToggle_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RequireAdmin, (bool)RequireAdminToggle.IsChecked);
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

            if (current.Equals("default"))
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
            if (current.Equals("default"))
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
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AutoSelectSettingsProfile, ((bool)AutoSelectInputProfile.IsChecked).ToString());
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
            _globalSettings.SetClientSetting(GlobalSettingsKeys.VAICOMTXInhibitEnabled, ((bool)VAICOMTXInhibitEnabled.IsChecked).ToString());
        }

        private void AlwaysAllowTransponderOverlay_OnClick(object sender, RoutedEventArgs e)
        {

            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.AlwaysAllowTransponderOverlay, (bool)AlwaysAllowTransponderOverlay.IsChecked);
        }

        private void CurrentPosition_OnClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var pos = ClientState.PlayerCoaltionLocationMetadata.LngLngPosition;

                Process.Start($"https://maps.google.com/maps?q=loc:{pos.lat},{pos.lng}");
            }
            catch { }

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
                _clientListWindow?.Close();
                _clientListWindow = null;
            }
        }

        private void ShowTransmitterName_OnClick_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.ShowTransmitterName, ((bool)ShowTransmitterName.IsChecked).ToString());
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
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.RadioBackgroundNoiseEffect, (bool)BackgroundRadioNoiseToggle.IsChecked);
        }

        private void HQEffect_Click(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone, (bool)HQEffectToggle.IsChecked);
        }

        private void AllowAnonymousUsage_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(bool)AllowAnonymousUsage.IsChecked)
            {
                MessageBox.Show(
                    "Please leave this ticked - SRS logging is extremely minimal (you can verify by looking at the source) - and limited to: Country & SRS Version on startup.\n\nBy keeping this enabled I can judge the usage of SRS, and which versions are still in use for support.",
                    "Please leave this ticked", MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Registry.SetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRSAnalyticsOptOut", "TRUE");
            }
            else
            {
                MessageBox.Show(
                    "Thank you for enabling this!\n\nBy keeping this enabled I can judge the usage of SRS, and which versions are still in use for support.",
                    "Thank You!", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Registry.SetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRSAnalyticsOptOut", "FALSE");
            }
        }

        private void AllowTransmissionsRecord_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.AllowRecording, (bool)AllowTransmissionsRecord.IsChecked);
        }

        private void RecordTransmissions_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RecordAudio, (bool)RecordTransmissions.IsChecked);
            RecordTransmissions_IsEnabled();
        }

        private void RecordTransmissions_IsEnabled()
        {
            if ((bool)RecordTransmissions.IsChecked)
            {
                SingleFileMixdown.IsEnabled = false;
                RecordingQuality.IsEnabled = false;
            }
            else
            {
                SingleFileMixdown.IsEnabled = true;
                RecordingQuality.IsEnabled = true;
            }
        }

        private void SingleFileMixdown_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.SingleFileMixdown, (bool)SingleFileMixdown.IsChecked);
        }

        private void RecordingQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RecordingQuality, $"V{(int)e.NewValue}");
        }

        private void DisallowedAudioTone_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.DisallowedAudioTone, (bool)DisallowedAudioTone.IsChecked);
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
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMinimumDB, (double)e.NewValue);
        }

        
    }
}