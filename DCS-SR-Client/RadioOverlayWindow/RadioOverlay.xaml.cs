using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using MahApps.Metro;
using MahApps.Metro.Controls;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioOverlayWindow.xaml
    /// </summary>
    public partial class RadioOverlayWindow : Window
    {
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private double _aspectRatio;

        private volatile bool _end;

        public RadioOverlayWindow()
        {
        
            InitializeComponent();
            this.AllowsTransparency = true;
            this.Opacity = 1.0;

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            radio1.RadioId = 0;
            radio2.RadioId = 1;
            radio3.RadioId = 2;

            SetupActiveRadio();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _aspectRatio = ActualWidth/ActualHeight;
        }


        private void SetupActiveRadio()
        {
        
            Task.Run(() =>
            {
                while (!_end)
                {
                    Thread.Sleep(50);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        radio1.RepaintRadioReceive();
                        radio2.RepaintRadioReceive();
                        radio3.RepaintRadioReceive();

                        radio1.RepaintRadioStatus();
                        radio2.RepaintRadioStatus();
                        radio3.RepaintRadioStatus();
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
        }

        private void containerPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //force aspect ratio
            CalculateScale();
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