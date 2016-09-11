using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayWindow : Window
    {
        private readonly double _aspectRatio;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly RadioControlGroup[] radioControlGroup = new RadioControlGroup[3];

        private volatile bool _end;


        public RadioOverlayWindow()
        {
            //load opacity before the intialising as the slider changed
            //method fires after initialisation
            var opacity = AppConfiguration.Instance.RadioOpacity;

            InitializeComponent();

            _aspectRatio = MinWidth/MinHeight;

            AllowsTransparency = true;
            Opacity = opacity;
            windowOpacitySlider.Value = Opacity;

            radioControlGroup[0] = radio1;
            radioControlGroup[1] = radio2;
            radioControlGroup[2] = radio3;

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            Top = AppConfiguration.Instance.RadioX;
            Left = AppConfiguration.Instance.RadioY;

            Width = AppConfiguration.Instance.RadioWidth;
            Height = AppConfiguration.Instance.RadioHeight;

            //  Window_Loaded(null, null);
            CalculateScale();

            SetupRadioRefresh();

            LocationChanged += Location_Changed;

            //init radio
            foreach (var radio in radioControlGroup)
            {
                radio.RepaintRadioReceive();
                radio.RepaintRadioStatus();
            }
        }

        private void Location_Changed(object sender, EventArgs e)
        {
            AppConfiguration.Instance.RadioX = Top;
            AppConfiguration.Instance.RadioY = Left;
        }

        private void SetupRadioRefresh()
        {
            Task.Run(() =>
            {
                while (!_end)
                {
                    //roughly 20 FPS
                    Thread.Sleep(50);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var radio in radioControlGroup)
                        {
                            radio.RepaintRadioReceive();
                            radio.RepaintRadioStatus();
                        }

                        intercom.RepaintRadioStatus();
                    });
                }
            });
        }

        private void WrapPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            _end = true;
        }

        ~RadioOverlayWindow()
        {
            //Shut down threads
            _end = true;
        }

        private void Button_Minimise(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }


        private void Button_Close(object sender, RoutedEventArgs e)
        {
            _end = true;
            Close();
        }

        private void windowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Opacity = e.NewValue;
            AppConfiguration.Instance.RadioOpacity = Opacity;
        }

        private void containerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //force aspect ratio
            CalculateScale();

            WindowState = WindowState.Normal;
        }


        private void CalculateScale()
        {
            var yScale = ActualHeight/myMainWindow.MinWidth;
            var xScale = ActualWidth/myMainWindow.MinWidth;
            var value = Math.Min(xScale, yScale);
            ScaleValue = (double) OnCoerceScaleValue(myMainWindow, value);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            if (sizeInfo.WidthChanged)
                Width = sizeInfo.NewSize.Height*_aspectRatio;
            else
                Height = sizeInfo.NewSize.Width/_aspectRatio;

            AppConfiguration.Instance.RadioWidth = Width;
            AppConfiguration.Instance.RadioHeight = Height;
            // Console.WriteLine(this.Height +" width:"+ this.Width);
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty ScaleValueProperty = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(RadioOverlayWindow),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as RadioOverlayWindow;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as RadioOverlayWindow;
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
    }
}