using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow;
using NLog;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using System.Windows.Forms;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow;
using MessageBox = System.Windows.Forms.MessageBox;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayWindowTenTransparent : Window
    {
        private  double _aspectRatio;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Client.UI.AwacsRadioOverlayWindow.RadioControlGroupTransparent[] radioControlGroupTransparent =
            new Client.UI.AwacsRadioOverlayWindow.RadioControlGroupTransparent[10];

        private readonly DispatcherTimer _updateTimer;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private readonly double _originalMinHeight;

        private readonly double _radioHeight;

        private long _lastUnitId;

        private readonly Action<bool, int> _toggleOverlay;

        public RadioOverlayWindowTenTransparent(Action<bool, int> ToggleOverlay)
        {
            //load opacity before the intialising as the slider changed
            //method fires after initialisation
            InitializeComponent();

            this.WindowStartupLocation = WindowStartupLocation.Manual;

            _aspectRatio = MinWidth / MinHeight;

            _originalMinHeight = MinHeight;
            _radioHeight = Radio1.Height;
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(MainWindow.GetWindow(this));
            Screen screen = System.Windows.Forms.Screen.FromHandle(windowInteropHelper.Handle);
            MaxHeight = screen.Bounds.Height;

            AllowsTransparency = true;
            BackgroundOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentBackgroundOpacity).DoubleValue;
            TextOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentTextOpacity).DoubleValue;
            
            radioControlGroupTransparent[0] = Radio1;
            radioControlGroupTransparent[1] = Radio2;
            radioControlGroupTransparent[2] = Radio3;
            radioControlGroupTransparent[3] = Radio4;
            radioControlGroupTransparent[4] = Radio5;
            radioControlGroupTransparent[5] = Radio6;
            radioControlGroupTransparent[6] = Radio7;
            radioControlGroupTransparent[7] = Radio8;
            radioControlGroupTransparent[8] = Radio9;
            radioControlGroupTransparent[9] = Radio10;
            
            //allows click and drag anywhere on the window
            ContainerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY).DoubleValue;

            Width = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentWidth).DoubleValue;
            Height = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenTransparentHeight).DoubleValue;

            //  Window_Loaded(null, null);
            CalculateScale();

            LocationChanged += Location_Changed;

            RadioRefresh(null, null);

            //init radio refresh
            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(80)};
            _updateTimer.Tick += RadioRefresh;
            _updateTimer.Start();

            this._toggleOverlay = ToggleOverlay;
        }

        private void Location_Changed(object sender, EventArgs e)
        {
        }

        private void RadioRefresh(object sender, EventArgs eventArgs)
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            foreach (var radio in radioControlGroupTransparent)
            {
                radio.RepaintRadioStatus();
                radio.RepaintRadioReceive();
            }

            Intercom.RepaintRadioStatus();

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent())
            {
                //reset when we switch planes
                if (_lastUnitId != dcsPlayerRadioInfo.unitId)
                {
                    _lastUnitId = dcsPlayerRadioInfo.unitId;
                    ResetHeight();
                }

                var availableRadios = 0;

                for (var i = 0; i < dcsPlayerRadioInfo.radios.Length; i++)
                {
                    if (dcsPlayerRadioInfo.radios[i].modulation != RadioInformation.Modulation.DISABLED)
                    {
                        availableRadios++;

                    }
                }

                if (availableRadios > 1)
                {
                    if (dcsPlayerRadioInfo.control == DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
                    {
                        ControlText.Text = "10 Radio Panel Transparent";
                    }
                    else
                    {
                        ControlText.Text = "10 Radio Panel Transparent";
                    }
                }
                else
                {
                    ControlText.Text = "10 Radio Panel Transparent (Disconnected)";
                    
                }
            }
            else
            {
                ResetHeight();
                ControlText.Text = "10 Radio Panel Transparent (Disconnected)";
            }

            FocusDCS();
        }

        private void ResetHeight()
        {

            if (MinHeight != _originalMinHeight)
            {
                MinHeight = _originalMinHeight;
                Recalculate();
            }
        }

        private void Recalculate()
        {
            _aspectRatio = MinWidth / MinHeight;
            containerPanel_SizeChanged(null, null);
            Height = Height+1;
        }

        private long _lastFocus;
        private RadioCapabilities _radioCapabilitiesWindow;

        private void FocusDCS()
        {
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RefocusDCS))
            {
                var overlayWindow = new WindowInteropHelper(this).Handle;

                //focus DCS if needed
                var foreGround = WindowHelper.GetForegroundWindow();

                Process[] localByName = Process.GetProcessesByName("dcs");

                if (localByName != null && localByName.Length > 0)
                {
                    //either DCS is in focus OR Overlay window is not in focus
                    if (foreGround == localByName[0].MainWindowHandle || overlayWindow != foreGround ||
                        this.IsMouseOver)
                    {
                        _lastFocus = DateTime.Now.Ticks;
                    }
                    else if (DateTime.Now.Ticks > _lastFocus + 20000000 && overlayWindow == foreGround)
                    {
                        WindowHelper.BringProcessToFront(localByName[0]);
                    }
                }
            }
        }

        private void WrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentWidth, Width);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentHeight, Height);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentBackgroundOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentTextOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentY, Top);
            base.OnClosing(e);

            _updateTimer.Stop();
        }

        private void Button_Minimise(object sender, RoutedEventArgs e)
        {
            // Minimising a window without a taskbar icon leads to the window's menu bar still showing up in the bottom of screen
            // Since controls are unusable, but a very small portion of the always-on-top window still showing, we're closing it instead, similar to toggling the overlay
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.RadioOverlayTaskbarHide))
            {
                Close();
            }
            else
            {
                WindowState = WindowState.Minimized;
            }
        }

        private void Button_About(object sender, RoutedEventArgs e)
        {
            //Show Radio Capabilities
            if ((_radioCapabilitiesWindow == null) || !_radioCapabilitiesWindow.IsVisible ||
                (_radioCapabilitiesWindow.WindowState == WindowState.Minimized))
            {
                _radioCapabilitiesWindow?.Close();

                _radioCapabilitiesWindow = new RadioCapabilities();
                _radioCapabilitiesWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _radioCapabilitiesWindow.Owner = this;
                _radioCapabilitiesWindow.ShowDialog();
            }
            else
            {
                _radioCapabilitiesWindow?.Close();
                _radioCapabilitiesWindow = null;
            }

        }
        private void Button_ShowAllRadios(object sender, RoutedEventArgs e)
        {
            foreach (var Visibility in radioControlGroupTransparent)
            {
                //TODO: Add set all radio groups to visible
            }
        }

        private void Button_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }

        //TODO - likely remove this function for the transparency panels (this comment written by dabble)
        private void Button_Swap_Orientation(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 5); // index 5 is the horizontal orientation
        }

        private void textOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //Header
            ControlText.Opacity = e.NewValue;
            Orientation.Opacity = e.NewValue;
            buttonOrientation.Opacity = e.NewValue;
            buttonAbout.Opacity = e.NewValue;
            buttonMinimize.Opacity = e.NewValue;
            buttonClose.Opacity = e.NewValue;

            //Radio 1
            Radio1.HideRadio.Opacity = e.NewValue;
            Radio1.RadioEnabled.Opacity = e.NewValue;
            Radio1.RadioLabel.Opacity = e.NewValue;
            Radio1.RadioFrequency.Opacity = e.NewValue;
            Radio1.RadioMetaData.Opacity = e.NewValue;
            Radio1.TransmitterName.Opacity = e.NewValue;
            Radio1.RadioActive.Opacity = e.NewValue;
            Radio1.RadioVolume.Opacity = e.NewValue;
            //Radio 2
            Radio2.HideRadio.Opacity = e.NewValue;
            Radio2.RadioEnabled.Opacity = e.NewValue;
            Radio2.RadioLabel.Opacity = e.NewValue;
            Radio2.RadioFrequency.Opacity = e.NewValue;
            Radio2.RadioMetaData.Opacity = e.NewValue;
            Radio2.TransmitterName.Opacity = e.NewValue;
            Radio2.RadioActive.Opacity = e.NewValue;
            Radio2.RadioVolume.Opacity = e.NewValue;
            //Radio 3
            Radio3.HideRadio.Opacity = e.NewValue;
            Radio3.RadioEnabled.Opacity = e.NewValue;
            Radio3.RadioLabel.Opacity = e.NewValue;
            Radio3.RadioFrequency.Opacity = e.NewValue;
            Radio3.RadioMetaData.Opacity = e.NewValue;
            Radio3.TransmitterName.Opacity = e.NewValue;
            Radio3.RadioActive.Opacity = e.NewValue;
            Radio3.RadioVolume.Opacity = e.NewValue;
            //Radio 4
            Radio4.HideRadio.Opacity = e.NewValue;
            Radio4.RadioEnabled.Opacity = e.NewValue;
            Radio4.RadioLabel.Opacity = e.NewValue;
            Radio4.RadioFrequency.Opacity = e.NewValue;
            Radio4.RadioMetaData.Opacity = e.NewValue;
            Radio4.TransmitterName.Opacity = e.NewValue;
            Radio4.RadioActive.Opacity = e.NewValue;
            Radio4.RadioVolume.Opacity = e.NewValue;
            //Radio 5
            Radio5.HideRadio.Opacity = e.NewValue;
            Radio5.RadioEnabled.Opacity = e.NewValue;
            Radio5.RadioLabel.Opacity = e.NewValue;
            Radio5.RadioFrequency.Opacity = e.NewValue;
            Radio5.RadioMetaData.Opacity = e.NewValue;
            Radio5.TransmitterName.Opacity = e.NewValue;
            Radio5.RadioActive.Opacity = e.NewValue;
            Radio5.RadioVolume.Opacity = e.NewValue;
            //Radio 6
            Radio6.HideRadio.Opacity = e.NewValue;
            Radio6.RadioEnabled.Opacity = e.NewValue;
            Radio6.RadioLabel.Opacity = e.NewValue;
            Radio6.RadioFrequency.Opacity = e.NewValue;
            Radio6.RadioMetaData.Opacity = e.NewValue;
            Radio6.TransmitterName.Opacity = e.NewValue;
            Radio6.RadioActive.Opacity = e.NewValue;
            Radio6.RadioVolume.Opacity = e.NewValue;
            //Radio 7
            Radio7.HideRadio.Opacity = e.NewValue;
            Radio7.RadioEnabled.Opacity = e.NewValue;
            Radio7.RadioLabel.Opacity = e.NewValue;
            Radio7.RadioFrequency.Opacity = e.NewValue;
            Radio7.RadioMetaData.Opacity = e.NewValue;
            Radio7.TransmitterName.Opacity = e.NewValue;
            Radio7.RadioActive.Opacity = e.NewValue;
            Radio7.RadioVolume.Opacity = e.NewValue;
            //Radio 8
            Radio7.HideRadio.Opacity = e.NewValue;
            Radio7.RadioEnabled.Opacity = e.NewValue;
            Radio7.RadioLabel.Opacity = e.NewValue;
            Radio7.RadioFrequency.Opacity = e.NewValue;
            Radio7.RadioMetaData.Opacity = e.NewValue;
            Radio7.TransmitterName.Opacity = e.NewValue;
            Radio7.RadioActive.Opacity = e.NewValue;
            Radio7.RadioVolume.Opacity = e.NewValue;
            //Radio 8
            Radio8.HideRadio.Opacity = e.NewValue;
            Radio8.RadioEnabled.Opacity = e.NewValue;
            Radio8.RadioLabel.Opacity = e.NewValue;
            Radio8.RadioFrequency.Opacity = e.NewValue;
            Radio8.RadioMetaData.Opacity = e.NewValue;
            Radio8.TransmitterName.Opacity = e.NewValue;
            Radio8.RadioActive.Opacity = e.NewValue;
            Radio8.RadioVolume.Opacity = e.NewValue;
            //Radio 9
            Radio9.HideRadio.Opacity = e.NewValue;
            Radio9.RadioEnabled.Opacity = e.NewValue;
            Radio9.RadioLabel.Opacity = e.NewValue;
            Radio9.RadioFrequency.Opacity = e.NewValue;
            Radio9.RadioMetaData.Opacity = e.NewValue;
            Radio9.TransmitterName.Opacity = e.NewValue;
            Radio9.RadioActive.Opacity = e.NewValue;
            Radio9.RadioVolume.Opacity = e.NewValue;
            //Radio 10
            Radio10.HideRadio.Opacity = e.NewValue;
            Radio10.RadioEnabled.Opacity = e.NewValue;
            Radio10.RadioLabel.Opacity = e.NewValue;
            Radio10.RadioFrequency.Opacity = e.NewValue;
            Radio10.RadioMetaData.Opacity = e.NewValue;
            Radio10.TransmitterName.Opacity = e.NewValue;
            Radio10.RadioActive.Opacity = e.NewValue;
            Radio10.RadioVolume.Opacity = e.NewValue;
            //Vox and Intercom
            Intercom.Opacity = e.NewValue;
            //Footer
            buttonShow.Opacity = e.NewValue;
            textBackground.Opacity = e.NewValue;
            textText.Opacity = e.NewValue;



        }

        private void backgroundOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Background.Opacity = e.NewValue;
            
            //Opacity = e.NewValue;
        }

        private void containerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //force aspect ratio
            CalculateScale();

            WindowState = WindowState.Normal;
        }


        private void CalculateScale()
        {
            var yScale = ActualHeight / RadioOverlayWin.MinWidth;
            var xScale = ActualWidth / RadioOverlayWin.MinWidth;
            var value = Math.Min(xScale, yScale);
            ScaleValue = (double) OnCoerceScaleValue(RadioOverlayWin, value);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged)
                Width = sizeInfo.NewSize.Height * _aspectRatio;
            else
                Height = sizeInfo.NewSize.Width / _aspectRatio;


            // Console.WriteLine(this.Height +" width:"+ this.Width);
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty ScaleValueProperty = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(RadioOverlayWindowTenTransparent),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindowTenTransparent;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindowTenTransparent;
            if (mainWindow != null)
                mainWindow.OnScaleValueChanged((double) e.OldValue, (double) e.NewValue);
        }

        protected virtual double OnCoerceScaleValue(double value)
        {
            if (double.IsNaN(value))
                return 1.0f;

            value = Math.Max(0.1, value);
            return value;
        }

        protected virtual void OnScaleValueChanged(double oldValue, double newValue)
        {
        }

        public double ScaleValue
        {
            get { return (double) GetValue(ScaleValueProperty); }
            set { SetValue(ScaleValueProperty, value); }
        }

        #endregion

        private void RadioOverlayWindow_OnLocationChanged(object sender, EventArgs e)
        {
            //reset last focus so we dont switch back to dcs while dragging
            _lastFocus = DateTime.Now.Ticks;
        }
    }
}