using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayMenuSelect
    {
        private readonly double _aspectRatio;

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        private readonly Action<bool, int> _toggleOverlay;

        public RadioOverlayMenuSelect(Action<bool, int> toggleOverlay)
        {
            InitializeComponent();

            this.WindowStartupLocation = WindowStartupLocation.Manual;

            _aspectRatio = MinWidth / MinHeight;

            AllowsTransparency = true;
            Opacity = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectOpacity).DoubleValue;
            windowOpacitySlider.Value = Opacity;

            Left = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectX).DoubleValue;
            Top = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectY).DoubleValue;

            Width = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectWidth).DoubleValue;
            Height = _globalSettings.GetPositionSetting(GlobalSettingsKeys.RadioMenuSelectHeight).DoubleValue;
            

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            CalculateScale();

            LocationChanged += Location_Changed;

            this._toggleOverlay = toggleOverlay;
        }

        private void Location_Changed(object sender, EventArgs e)
        {
            //force aspect ratio
            CalculateScale();
        }

        private void WrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectX, Left);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectY, Top);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectWidth, Width);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectHeight, Height);
            _globalSettings.SetPositionSetting(GlobalSettingsKeys.RadioMenuSelectOpacity, 1);
            
            base.OnClosing(e);
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
            var value = Math.Max(xScale, yScale);
            ScaleValue = (double) OnCoerceScaleValue(RadioOverlayWin, value);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged)
                Width = sizeInfo.NewSize.Height * _aspectRatio;
            else
                Height = sizeInfo.NewSize.Width / _aspectRatio;
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty SCALE_VALUE_PROPERTY = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(RadioOverlayMenuSelect),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));


        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayMenuSelect;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayMenuSelect;
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
            get { return (double) GetValue(SCALE_VALUE_PROPERTY); }
            set { SetValue(SCALE_VALUE_PROPERTY, value); }
        }

        #endregion

        #region Radio Selection Buttons

        private void ShowOverlayTwoVertical_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 0);
        }

        private void ShowOverlayThreeVertical_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 1);
        }

        private void ShowOverlayFiveVertical_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 2);
        }

        private void ShowOverlayTenVertical_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 3);
        }

        private void ShowOverlayTwoHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 4);
        }

        private void ShowOverlayThreeHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 5);
        }

        private void ShowOverlayFiveHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 6);
        }

        private void ShowOverlayTenHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 7);
        }

        private void ShowOverlayOneVertical_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 8);
        }

        private void ShowOverlayOneHorizontal_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 9);
        }

        private void ShowOverlayTenVerticalLong_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 10);
        }

        private void ShowOverlayTenHorizontalWide_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 11);
        }

        private void ShowOverlayTenTransparent_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 12);
        }

        private void ShowOverlayTenSwitch_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 13);
        }
        
        private void ShowOverlayDragable_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 14);
        }
        
        private void ShowOverlayMenuSelect_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
            _toggleOverlay(true, 15);
        }
        
        #endregion

    }
}