﻿using System;
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
using System.Reflection.Metadata;
using Xamarin.Forms.Shapes;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayWindowEngineering : Window
    {
        private double _aspectRatio;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Client.UI.AwacsRadioOverlayWindow.RadioControlGroupSwitch[] radioControlGroupSwitch = new Client.UI.AwacsRadioOverlayWindow.RadioControlGroupSwitch[3];

        private readonly DispatcherTimer _updateTimer;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private readonly double _originalMinHeight;

        private double radioHeight = 20;
        private double currentHeight;

        private long _lastUnitId;

        private readonly ImageBrush _expandIcon = new ImageBrush(Images.IconExpand);
        private readonly ImageBrush _contractIcon = new ImageBrush(Images.IconContract);

        // Damage Control Component State Colors
        private readonly SolidColorBrush DCComponentGood = new SolidColorBrush(Colors.Green);
        private readonly SolidColorBrush DCComponentFair = new SolidColorBrush(Colors.Yellow);
        private readonly SolidColorBrush DCComponentCritical = new SolidColorBrush(Colors.Red);
        private readonly SolidColorBrush DCComponentDestroyed = new SolidColorBrush(Colors.Black);
        private readonly SolidColorBrush DCComponentNotInstalled = new SolidColorBrush(Colors.Gray);

        private readonly Action<bool, int> _toggleOverlay;

        private readonly Action<bool, int> _ExpandPanelImageConverter;
        
        // BitmapImage expandimage = new BitmapImage(new Uri("/ ExpandIcon.png", UriKind.Relative));
    
        public RadioOverlayWindowEngineering(Action<bool, int> ToggleOverlay)
        {
            //load opacity before the intialising as the slider changed
            //method fires after initialisation
            InitializeComponent();

            //Used to determine if starting panel is expanded or contracted
            //Show = Contracted
            //Hide = Expanded
            buttonShowText.Text = "Show";

            //Used to determine if starting panel is expanded or contracted
            //expand = Contracted
            //contract = Expanded
            buttonExpandText.Text = "expand";
            


            this.WindowStartupLocation = WindowStartupLocation.Manual;

            _aspectRatio = MinWidth / MinHeight;

            _originalMinHeight = MinHeight;
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(MainWindow.GetWindow(this));
            Screen screen = System.Windows.Forms.Screen.FromHandle(windowInteropHelper.Handle);
            MaxHeight = screen.Bounds.Height;

            AllowsTransparency = true;
            BackgroundOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringBackgroundOpacity).DoubleValue;
            TextOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringTextOpacity).DoubleValue;

            radioControlGroupSwitch[0] = Radio1;
            radioControlGroupSwitch[1] = Radio2;
            radioControlGroupSwitch[2] = Radio3;
            
            //allows click and drag anywhere on the window
            ContainerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringY).DoubleValue;

            Width = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringWidth).DoubleValue;
            Height = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioEngineeringHeight).DoubleValue;

            currentHeight = Height;

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
            int numVisibleRadios = 0;

            foreach (var radio in radioControlGroupSwitch)
            {
                radioHeight = !double.IsNaN(radio.Height) ? radio.Height : 0;
                radio.RepaintRadioStatus();
                radio.RepaintRadioReceive();

                if ((buttonShowText.Text != null) && (buttonShowText.Text == "Hide"))
                {
                    radio.Visibility = Visibility.Visible;
                    numVisibleRadios++;
                    // Console.WriteLine("radio " + radio.RadioLabel.ToString() + " set Visible");
                }
                else if (((buttonShowText.Text != null) && (buttonShowText.Text == "Show")) && (radio.RadioEnabled.Content == "On"))
                {
                    radio.Visibility = Visibility.Visible;
                    numVisibleRadios++;
                    // Console.WriteLine("radio " + radio.RadioLabel.ToString() + " set visible");
                }
                else
                {
                    radio.Visibility = Visibility.Collapsed;
                    // Console.WriteLine("radio " + radio.RadioLabel.ToString() + " set collapsed");
                }
            }

            CalculateHeight(numVisibleRadios);

            Intercom.RepaintRadioStatus();

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
                        ControlText.Text = "Compact Radio Panel - Engineering";
                    }
                    else
                    {
                        ControlText.Text = "Compact Radio Panel - Engineering";
                    }
                }
                else
                {
                    ControlText.Text = "Compact Radio Panel - Engineering (Disconnected)";
                    
                }
            }
            else
            {
                ControlText.Text = "Compact Radio Panel - Engineering (Disconnected)";
            }

            FocusDCS();
        }

        private void CalculateHeight(int numVisibleRadios)
        {

            double neededRadioHeight = !double.IsNaN(radioHeight) ? radioHeight * numVisibleRadios : 0;
            double neededHeaderHeight = !double.IsNaN(Header.ActualHeight) && Header.ActualHeight != 0 ? 14 : 0; // Using the expand button to determine the window state
            double neededFooterHeight = !double.IsNaN(Footer.ActualHeight) && Footer.ActualHeight != 0 ? 90 : 0;
            double newNeededHeight = neededRadioHeight + neededFooterHeight + neededHeaderHeight + 31;
            if (newNeededHeight != currentHeight && !double.IsNaN(newNeededHeight))
            {
                MinHeight = newNeededHeight;
                _aspectRatio = MinWidth / newNeededHeight;
                containerPanel_SizeChanged(null, null);
                Height = Height + 1;
                currentHeight = newNeededHeight;
            }
            /* May be use this code if it gets unstable with self adjusting.
             * This is not recommended, due to performance
             * It requires quite a bit of calculation and should not be done without reason.
            else
            {
                containerPanel_SizeChanged(null, null);
            }
            */
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
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringWidth, Width);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringHeight, Height);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringBackgroundOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringTextOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioEngineeringY, Top);
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
            if ((buttonShowText.Text == null) || (buttonShowText.Text == "Hide"))
            {
                buttonShowText.Text = "Show";
                 
            }
            else
            {
                buttonShowText.Text = "Hide";
                
            }
        }

        private void Button_Expand(object sender, RoutedEventArgs e)
        {
            if ((buttonExpandText.Text == null) || (buttonExpandText.Text == "expand"))
            {
                buttonExpandText.Text = "contract";
                buttonExpandText.Background = _expandIcon;
                Header.Visibility = Visibility.Collapsed;
                Footer.Visibility = Visibility.Collapsed;
                
                Logger.Debug("button expanded pressed - window now in contract mode");
            }
            else
            {
                buttonExpandText.Text = "expand";
                buttonExpandText.Background = _contractIcon;
                Header.Visibility = Visibility.Visible;
                Footer.Visibility = Visibility.Visible;

                Logger.Debug("button contract pressed - window now in expand mode");
            }
        }


        private void Button_Close(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void textOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            //Header
            ControlText.Opacity = e.NewValue;
            Orientation.Opacity = e.NewValue;
            buttonMinimize.Opacity = e.NewValue;
            buttonClose.Opacity = e.NewValue;
            buttonExpand.Opacity = e.NewValue;

            //Radio 1
            Radio1.RadioEnabled.Opacity = e.NewValue;
            Radio1.PresetChannelsView.Opacity = e.NewValue;
            Radio1.RadioTextGroup.Opacity = e.NewValue;
            Radio1.RadioLabel.Opacity = e.NewValue;
            Radio1.RadioFrequency.Opacity = e.NewValue;
            Radio1.RadioMetaData.Opacity = e.NewValue;
            Radio1.TransmitterName.Opacity = e.NewValue;
            Radio1.RadioActive.Opacity = e.NewValue;
            Radio1.RadioVolume.Opacity = e.NewValue;
            //Radio 2
            Radio2.RadioEnabled.Opacity = e.NewValue;
            Radio2.PresetChannelsView.Opacity = e.NewValue;
            Radio2.RadioTextGroup.Opacity = e.NewValue;
            Radio2.RadioLabel.Opacity = e.NewValue;
            Radio2.RadioFrequency.Opacity = e.NewValue;
            Radio2.RadioMetaData.Opacity = e.NewValue;
            Radio2.TransmitterName.Opacity = e.NewValue;
            Radio2.RadioActive.Opacity = e.NewValue;
            Radio2.RadioVolume.Opacity = e.NewValue;
            //Radio 3
            Radio3.RadioEnabled.Opacity = e.NewValue;
            Radio3.PresetChannelsView.Opacity = e.NewValue;
            Radio3.RadioTextGroup.Opacity = e.NewValue;
            Radio3.RadioLabel.Opacity = e.NewValue;
            Radio3.RadioFrequency.Opacity = e.NewValue;
            Radio3.RadioMetaData.Opacity = e.NewValue;
            Radio3.TransmitterName.Opacity = e.NewValue;
            Radio3.RadioActive.Opacity = e.NewValue;
            Radio3.RadioVolume.Opacity = e.NewValue;
            
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
            Logger.Debug("Size Changed Transparent Panel");
        }


        private void CalculateScale()
        {
            var yScale = ActualHeight / RadioOverlayWin.MinHeight;
            var xScale = ActualWidth / RadioOverlayWin.MinHeight;
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
            typeof(double), typeof(RadioOverlayWindowEngineering),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindowEngineering;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindowEngineering;
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

        private void ShowOverlayMenuSelect_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 15);
        }

        private void ShipConditionChangeResponse_Click(object sender, RoutedEventArgs e)
        {
            // if Ship Condition changed, then button visibility.show happen

            // when button is pressed, button visibility.hidden & send response to state manager
        }


        //Damage Control Color Changes
        private void DamageControlStatusUpdates(object sender, RoutedEventArgs e)
        {
            // DCQuantumDrive.Background = DCComponentGood;
            // DCCooler.Background = DCComponentGood;
            // DCEngine.Background = DCComponentGood;
            // DCGuns.Background = DCComponentGood;
            // DCMissile.Background = DCComponentGood;
            // DCTorpedo.Background = DCComponentGood;
            // DCShield.Background = DCComponentGood;
            // DCCrewStatus.Background = DCComponentGood;
        }
    }
}