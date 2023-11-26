using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    public partial class RadioOverlayWindowOneHorizontal : Window
    {
        private  double _aspectRatio;  //originally readonly - dabble removed to match 1V panel
        
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly RadioControlGroup[] radioControlGroup = new RadioControlGroup[1];

        private readonly DispatcherTimer _updateTimer;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private readonly double _originalMinHeight;

        private readonly double _radioHeight;

        private long _lastUnitId;

        
       
        public RadioOverlayWindowOneHorizontal()
        {
            //load opacity before the intialising as the slider changed
            //method fires after initialisation
            InitializeComponent();

            this.WindowStartupLocation = WindowStartupLocation.Manual;

            _aspectRatio = MinWidth / MinHeight;

            _originalMinHeight = MinHeight;
            _radioHeight = radio1.Height;
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(MainWindow.GetWindow(this));
            Screen screen = System.Windows.Forms.Screen.FromHandle(windowInteropHelper.Handle);
            MaxHeight = screen.Bounds.Height;

            AllowsTransparency = true;
            Opacity = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalOpacity).DoubleValue;
            windowOpacitySlider.Value = Opacity;
            
            radioControlGroup[0] = radio1;


            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY).DoubleValue;

            Width = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalWidth).DoubleValue;
            Height = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalHeight).DoubleValue;


            CalculateScale();

            LocationChanged += Location_Changed;

            RadioRefresh(null, null);

            //init radio refresh
            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(80)};
            _updateTimer.Tick += RadioRefresh;
            _updateTimer.Start();
        }

        private void Location_Changed(object sender, EventArgs e)
        {
            //   AppConfiguration.Instance.RadioX = Top;
            //  AppConfiguration.Instance.RadioY = Left;
        }

        private void RadioRefresh(object sender, EventArgs eventArgs)
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            foreach (var radio in radioControlGroup)
            {
                radio.RepaintRadioStatus();
                radio.RepaintRadioReceive();
            }

            intercom.RepaintRadioStatus();

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent())
            {
                //reset when we switch planes
                if (_lastUnitId != dcsPlayerRadioInfo.unitId)
                {
                    _lastUnitId = dcsPlayerRadioInfo.unitId;
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
                        ControlText.Text = "1 Horizontal";
                    }
                    else
                    {
                        ControlText.Text = "1 Horizontal";
                    }
                }
                else
                {
                    ControlText.Text = "1 Horizontal (Disconnected)";

                }
            }
            else
            {
                ResetHeight();
                ControlText.Text = "1 Horizontal (Disconnected)";
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
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalWidth, Width);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalHeight, Height);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioOneHorizontalY, Top);
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


        private void Button_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void windowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Opacity = e.NewValue;
            //AppConfiguration.Instance.RadioOpacity = Opacity;
        }

        private void containerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //force aspect ratio
            CalculateScale();

            WindowState = WindowState.Normal;
        }


        private void CalculateScale()
        {
            var yScale = ActualHeight / RadioOverlayWin.MinHeight;
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

            //  AppConfiguration.Instance.RadioWidth = Width;
            // AppConfiguration.Instance.RadioHeight = Height;
            // Console.WriteLine(this.Height +" width:"+ this.Width);
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty ScaleValueProperty = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(RadioOverlayWindowOneHorizontal),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));


        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindowOneHorizontal;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindowOneHorizontal;
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

        private void ToggleGlobalSimultaneousTransmissionButton_Click(object sender, RoutedEventArgs e)
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;
            if (dcsPlayerRadioInfo != null)
            {
                dcsPlayerRadioInfo.simultaneousTransmission = !dcsPlayerRadioInfo.simultaneousTransmission;

                if (!dcsPlayerRadioInfo.simultaneousTransmission)
                {
                    foreach (var radio in dcsPlayerRadioInfo.radios)
                    {
                        radio.simul = false;
                    }
                }

                foreach (var radio in radioControlGroup)
                {
                    radio.RepaintRadioStatus();
                }
            }
        }
    }
}