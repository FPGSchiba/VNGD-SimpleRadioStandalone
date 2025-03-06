﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow;
using NLog;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using System.Windows.Forms;
using System.Windows.Media;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayWindowTenSwitch : Window
    {
        private double _aspectRatio;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Client.UI.AwacsRadioOverlayWindow.RadioControlGroupSwitch[] radioControlGroupSwitch =
            new Client.UI.AwacsRadioOverlayWindow.RadioControlGroupSwitch[10];

        private readonly DispatcherTimer _updateTimer;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private static readonly double RadioHeight = 20;
        private double _currentHeight;

        private long _lastUnitId;

        private readonly ImageBrush _expandIcon = new ImageBrush(Images.IconExpand);
        private readonly ImageBrush _contractIcon = new ImageBrush(Images.IconContract);

        private readonly Action<bool, int> _toggleOverlay;

        private readonly Action<bool, int> _ExpandPanelImageConverter;
        
        // BitmapImage expandimage = new BitmapImage(new Uri("/ ExpandIcon.png", UriKind.Relative));
    
        public RadioOverlayWindowTenSwitch(Action<bool, int> ToggleOverlay)
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
            
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(MainWindow.GetWindow(this));
            Screen screen = System.Windows.Forms.Screen.FromHandle(windowInteropHelper.Handle);
            MaxHeight = screen.Bounds.Height;

            AllowsTransparency = true;
            BackgroundOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchBackgroundOpacity).DoubleValue;
            TextOpacitySlider.Value = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchTextOpacity).DoubleValue;
            
            radioControlGroupSwitch[0] = Radio1;
            radioControlGroupSwitch[1] = Radio2;
            radioControlGroupSwitch[2] = Radio3;
            radioControlGroupSwitch[3] = Radio4;
            radioControlGroupSwitch[4] = Radio5;
            radioControlGroupSwitch[5] = Radio6;
            radioControlGroupSwitch[6] = Radio7;
            radioControlGroupSwitch[7] = Radio8;
            radioControlGroupSwitch[8] = Radio9;
            radioControlGroupSwitch[9] = Radio10;
            
            //allows click and drag anywhere on the window
            ContainerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchY).DoubleValue;

            Width = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchWidth).DoubleValue;
            Height = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioTenSwitchHeight).DoubleValue;

            _currentHeight = Height;

            // Calculate initial scale and layout
            CalculateScale();
            containerPanel_SizeChanged(this, null);

            RadioRefresh(null, null);

            //init radio refresh
            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(80)};
            _updateTimer.Tick += RadioRefresh;
            _updateTimer.Start();

            this._toggleOverlay = ToggleOverlay;
        }
        
        private int getNumVisibleRadios()
        {
            int numVisibleRadios = 0;

            foreach (var radio in radioControlGroupSwitch)
            {
                radio.RepaintRadioStatus();
                radio.RepaintRadioReceive();

                if (buttonShowText.Text == "Hide")
                {
                    radio.Visibility = Visibility.Visible;
                    numVisibleRadios++;
                }
                else
                {
                    if (radio.IsRadioEnabled)
                    {
                        radio.Visibility = Visibility.Visible;
                        numVisibleRadios++;
                    }
                    else
                    {
                        radio.Visibility = Visibility.Collapsed;
                    }
                }
            }

            return numVisibleRadios;
        }

        private void RadioRefresh(object sender, EventArgs eventArgs)
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;
            
            int numVisibleRadios = getNumVisibleRadios();
            CalculateHeight(numVisibleRadios);
            Intercom.RepaintRadioStatus();

            if (dcsPlayerRadioInfo != null && dcsPlayerRadioInfo.IsCurrent())
            {
                if (_lastUnitId != dcsPlayerRadioInfo.unitId)
                {
                    _lastUnitId = dcsPlayerRadioInfo.unitId;
                }

                var availableRadios = dcsPlayerRadioInfo.radios.Count(r => r.modulation != RadioInformation.Modulation.DISABLED);

                ControlText.Text = availableRadios > 1
                    ? "Compact Radio Panel - New"
                    : "Compact Radio Panel - New (Disconnected)";
            }
            else
            {
                ControlText.Text = "Compact Radio Panel - New (Disconnected)";
            }
        }

        private void CalculateHeight(int numVisibleRadios)
        {
            double neededRadioHeight = RadioHeight * numVisibleRadios;
            double neededHeaderHeight = !double.IsNaN(Header.ActualHeight) && Header.ActualHeight != 0 ? 15 : 0; // Using the expand button to determine the window state
            double neededFooterHeight = !double.IsNaN(Footer.ActualHeight) && Footer.ActualHeight != 0 ? 15 : 0;
            double newNeededHeight = neededRadioHeight + neededFooterHeight + neededHeaderHeight + 30;
            if (!double.IsNaN(newNeededHeight) && newNeededHeight != _currentHeight)
            {
                MinHeight = newNeededHeight;
                _aspectRatio = MinWidth / newNeededHeight;
                _currentHeight = newNeededHeight;
                containerPanel_SizeChanged(null, null);
                Height += 1;
            }
        }
        
        private RadioCapabilities _radioCapabilitiesWindow;

        private void WrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            int numVisibleRadios = 0;

            foreach (var radio in radioControlGroupSwitch)
            {
                radio.RepaintRadioStatus();
                radio.RepaintRadioReceive();

                if (radio.IsRadioEnabled)
                {
                    numVisibleRadios++;
                }
            }
            
            Logger.Debug("Closing Radio Overlay Window, with {0} visible radios", numVisibleRadios);
            
            CalculateHeight(numVisibleRadios);
            containerPanel_SizeChanged(this, null);
            
            // This is a bit of a hack to ensure the window is closed properly and saved so it opens the same size next time
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchWidth, Width * 0.747);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchHeight, Height * 0.747);
            
            // Save the position of the window
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchBackgroundOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchTextOpacity, Opacity);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenSwitchY, Top);
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
                
        private void Button_ShowAllRadios(object sender, RoutedEventArgs e)
        {
            if (buttonShowText.Text == "Hide")
            {
                buttonShowText.Text = "Show";
            }
            else
            {
                buttonShowText.Text = "Hide";
            }
            
            // Refresh the radio visibility
            RadioRefresh(sender, e);
            
            // Scale and height recalculation
            CalculateScale();
            CalculateHeight(getNumVisibleRadios());
            containerPanel_SizeChanged(sender, null);
            
            // Force the UI to update
            InvalidateVisual();
            UpdateLayout();
            Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
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

            CalculateScale();
            CalculateHeight(getNumVisibleRadios());
            containerPanel_SizeChanged(sender, null);
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
            Radio1.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio1.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio1.SwapRadio.Opacity = e.NewValue;
            Radio1.RadioTextGroupStandby.Opacity = e.NewValue;
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
            Radio2.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio2.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio2.SwapRadio.Opacity = e.NewValue;
            Radio2.RadioTextGroupStandby.Opacity = e.NewValue;
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
            Radio3.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio3.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio3.SwapRadio.Opacity = e.NewValue;
            Radio3.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 4
            Radio4.RadioEnabled.Opacity = e.NewValue;
            Radio4.PresetChannelsView.Opacity = e.NewValue;
            Radio4.RadioTextGroup.Opacity = e.NewValue;
            Radio4.RadioLabel.Opacity = e.NewValue;
            Radio4.RadioFrequency.Opacity = e.NewValue;
            Radio4.RadioMetaData.Opacity = e.NewValue;
            Radio4.TransmitterName.Opacity = e.NewValue;
            Radio4.RadioActive.Opacity = e.NewValue;
            Radio4.RadioVolume.Opacity = e.NewValue;
            Radio4.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio4.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio4.SwapRadio.Opacity = e.NewValue;
            Radio4.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 5
            Radio5.RadioEnabled.Opacity = e.NewValue;
            Radio5.PresetChannelsView.Opacity = e.NewValue;
            Radio5.RadioTextGroup.Opacity = e.NewValue;
            Radio5.RadioLabel.Opacity = e.NewValue;
            Radio5.RadioFrequency.Opacity = e.NewValue;
            Radio5.RadioMetaData.Opacity = e.NewValue;
            Radio5.TransmitterName.Opacity = e.NewValue;
            Radio5.RadioActive.Opacity = e.NewValue;
            Radio5.RadioVolume.Opacity = e.NewValue;
            Radio5.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio5.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio5.SwapRadio.Opacity = e.NewValue;
            Radio5.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 6
            Radio6.RadioEnabled.Opacity = e.NewValue;
            Radio6.PresetChannelsView.Opacity = e.NewValue;
            Radio6.RadioTextGroup.Opacity = e.NewValue;
            Radio6.RadioLabel.Opacity = e.NewValue;
            Radio6.RadioFrequency.Opacity = e.NewValue;
            Radio6.RadioMetaData.Opacity = e.NewValue;
            Radio6.TransmitterName.Opacity = e.NewValue;
            Radio6.RadioActive.Opacity = e.NewValue;
            Radio6.RadioVolume.Opacity = e.NewValue;
            Radio6.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio6.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio6.SwapRadio.Opacity = e.NewValue;
            Radio6.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 7
            Radio7.RadioEnabled.Opacity = e.NewValue;
            Radio7.PresetChannelsView.Opacity = e.NewValue;
            Radio7.RadioTextGroup.Opacity = e.NewValue;
            Radio7.RadioLabel.Opacity = e.NewValue;
            Radio7.RadioFrequency.Opacity = e.NewValue;
            Radio7.RadioMetaData.Opacity = e.NewValue;
            Radio7.TransmitterName.Opacity = e.NewValue;
            Radio7.RadioActive.Opacity = e.NewValue;
            Radio7.RadioVolume.Opacity = e.NewValue;
            Radio7.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio7.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio7.SwapRadio.Opacity = e.NewValue;
            Radio7.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 8
            Radio8.RadioEnabled.Opacity = e.NewValue;
            Radio8.PresetChannelsView.Opacity = e.NewValue;
            Radio8.RadioTextGroup.Opacity = e.NewValue;
            Radio8.RadioLabel.Opacity = e.NewValue;
            Radio8.RadioFrequency.Opacity = e.NewValue;
            Radio8.RadioMetaData.Opacity = e.NewValue;
            Radio8.TransmitterName.Opacity = e.NewValue;
            Radio8.RadioActive.Opacity = e.NewValue;
            Radio8.RadioVolume.Opacity = e.NewValue;
            Radio8.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio8.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio8.SwapRadio.Opacity = e.NewValue;
            Radio8.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 9
            Radio9.RadioEnabled.Opacity = e.NewValue;
            Radio9.PresetChannelsView.Opacity = e.NewValue;
            Radio9.RadioTextGroup.Opacity = e.NewValue;
            Radio9.RadioLabel.Opacity = e.NewValue;
            Radio9.RadioFrequency.Opacity = e.NewValue;
            Radio9.RadioMetaData.Opacity = e.NewValue;
            Radio9.TransmitterName.Opacity = e.NewValue;
            Radio9.RadioActive.Opacity = e.NewValue;
            Radio9.RadioVolume.Opacity = e.NewValue;
            Radio9.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio9.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio9.SwapRadio.Opacity = e.NewValue;
            Radio9.RadioTextGroupStandby.Opacity = e.NewValue;
            //Radio 10
            Radio10.RadioEnabled.Opacity = e.NewValue;
            Radio10.PresetChannelsView.Opacity = e.NewValue;
            Radio10.RadioTextGroup.Opacity = e.NewValue;
            Radio10.RadioLabel.Opacity = e.NewValue;
            Radio10.RadioFrequency.Opacity = e.NewValue;
            Radio10.RadioMetaData.Opacity = e.NewValue;
            Radio10.TransmitterName.Opacity = e.NewValue;
            Radio10.RadioActive.Opacity = e.NewValue;
            Radio10.RadioVolume.Opacity = e.NewValue;
            Radio10.StandbyRadioFrequency.Opacity = e.NewValue;
            Radio10.StandbyRadioMetaData.Opacity = e.NewValue;
            Radio10.SwapRadio.Opacity = e.NewValue;
            Radio10.RadioTextGroupStandby.Opacity = e.NewValue;
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
            ScaleValue = (double)OnCoerceScaleValue(RadioOverlayWin, value);

            // Recalculate the layout after changing the scale
            WindowState = WindowState.Normal;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged)
                Width = sizeInfo.NewSize.Height * _aspectRatio;
            else
                Height = sizeInfo.NewSize.Width / _aspectRatio;
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty ScaleValueProperty = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(RadioOverlayWindowTenSwitch),
            new UIPropertyMetadata(1.0, OnScaleValueChanged, OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindowTenSwitch;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double)value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindowTenSwitch;
            if (mainWindow != null)
                mainWindow.OnScaleValueChanged((double)e.OldValue, (double)e.NewValue);
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
            ApplicationScaleTransform.ScaleX = newValue;
            ApplicationScaleTransform.ScaleY = newValue;
        }

        public double ScaleValue
        {
            get { return (double)GetValue(ScaleValueProperty); }
            set { SetValue(ScaleValueProperty, value); }
        }

        #endregion

        private void ShowOverlayMenuSelect_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 15);
        }
    }
}