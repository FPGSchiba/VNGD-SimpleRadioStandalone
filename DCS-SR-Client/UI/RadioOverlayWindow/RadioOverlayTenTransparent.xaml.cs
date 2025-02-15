using System;
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
    public partial class RadioOverlayWindowTenTransparent
    {
        private double _aspectRatio;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Client.UI.AwacsRadioOverlayWindow.RadioControlGroupTransparent[] radioControlGroupTransparent =
            new Client.UI.AwacsRadioOverlayWindow.RadioControlGroupTransparent[10];

        private readonly DispatcherTimer _updateTimer;

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        
        private static readonly double RadioHeight = 10;
        private double _currentHeight;

        private readonly ImageBrush _expandIcon = new ImageBrush(Images.IconExpand);
        private readonly ImageBrush _contractIcon = new ImageBrush(Images.IconContract);

        private readonly Action<bool, int> _toggleOverlay;
    
        public RadioOverlayWindowTenTransparent(Action<bool, int> toggleOverlay, RadioCapabilities radioCapabilitiesWindow)
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
            
            WindowInteropHelper windowInteropHelper = new WindowInteropHelper(MainWindow.GetWindow(this) ?? throw new InvalidOperationException());
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

            _currentHeight = Height;
            
            CalculateScale();
            containerPanel_SizeChanged(this, null);

            RadioRefresh(null, null);

            //init radio refresh
            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromMilliseconds(80)};
            _updateTimer.Tick += RadioRefresh;
            _updateTimer.Start();

            this._toggleOverlay = toggleOverlay;
        }
        
        private int getNumVisibleRadios()
        {
            int numVisibleRadios = 0;

            foreach (var radio in radioControlGroupTransparent)
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
                HandleCurrentDcsPlayerRadioInfo(dcsPlayerRadioInfo);
            }
            else
            {
                ControlText.Text = "Compact Radio Panel - Original (Disconnected)";
            }
        }
        
        private void HandleCurrentDcsPlayerRadioInfo(DCSPlayerRadioInfo dcsPlayerRadioInfo)
        {
            var availableRadios = GetAvailableRadiosCount(dcsPlayerRadioInfo);
        
            ControlText.Text = availableRadios > 1 ? "Compact Radio Panel - Original" : "Compact Radio Panel - Original (Disconnected)";
        }
        
        private static int GetAvailableRadiosCount(DCSPlayerRadioInfo dcsPlayerRadioInfo)
        {
            return dcsPlayerRadioInfo.radios.Select(t => t.modulation).Count(mod => mod != RadioInformation.Modulation.DISABLED);
        }

        private void CalculateHeight(int numVisibleRadios)
        {

            double neededRadioHeight = !double.IsNaN(RadioHeight) ? RadioHeight * numVisibleRadios : 0;
            double neededHeaderHeight = !double.IsNaN(Header.ActualHeight) && Header.ActualHeight != 0 ? 14 : 0; // Using the expand button to determine the window state
            double neededFooterHeight = !double.IsNaN(Footer.ActualHeight) && Footer.ActualHeight != 0 ? 10 : 0;
            double newNeededHeight = neededRadioHeight + neededFooterHeight + neededHeaderHeight + 30;
            if (!double.IsNaN(newNeededHeight) && Math.Abs(newNeededHeight - _currentHeight) > 0.001f)
            {
                MinHeight = newNeededHeight;
                _aspectRatio = MinWidth / newNeededHeight;
                _currentHeight = newNeededHeight;
                containerPanel_SizeChanged(null, null);
                Height += 1;
            }
        }
        
        private void WrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            int numVisibleRadios = 0;

            foreach (var radio in radioControlGroupTransparent)
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
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentWidth, Width * 0.71);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioTenTransparentHeight, Height * 0.71);
            
            // Save the position of the window
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

        private void TextOpacitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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
            //Vox and Intercom
            Intercom.Opacity = e.NewValue;
            //Footer
            buttonShow.Opacity = e.NewValue;
            textBackground.Opacity = e.NewValue;
            textText.Opacity = e.NewValue;
        }

        private void BackgroundOpacitySliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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
            typeof(double), typeof(RadioOverlayWindowTenTransparent),
            new UIPropertyMetadata(1.0, OnScaleValueChanged, OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindowTenTransparent;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double)value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindowTenTransparent;
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