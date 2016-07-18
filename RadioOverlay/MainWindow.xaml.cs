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
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int UdpClientBroadcastPort = 35034;
        private const int ActiveRadioClientPort = 35035;

        private const double MHZ = 1000000;

        private UdpClient activeRadioUdpClient;

        private double aspectRatio;

        private volatile bool end;

        private DCSPlayerRadioInfo lastUpdate;

        private DateTime lastUpdateTime = new DateTime(0L);
        private UdpClient udpClient;

        public MainWindow()
        {
            InitializeComponent();

            // this.SourceInitialized += MainWindow_SourceInitialized;

            if (Is_SimpleRadio_running())
            {
                Close();
            }

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += WrapPanel_MouseLeftButtonDown;

            radio1.RadioId = 0;
            radio2.RadioId = 1;
            radio3.RadioId = 2;

            SetupActiveRadio();
            SetupRadioStatus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            aspectRatio = ActualWidth/ActualHeight;
        }


        /// <summary>
        ///     Only allow one instance of SimpleRadio
        /// </summary>
        /// <returns></returns>
        private bool Is_SimpleRadio_running()
        {
            var i = 0;
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Equals("sr-overlay"))
                {
                    i++;
                }
            }

            return i > 1;
        }

        private void SetupRadioStatus()
        {
            //setup UDP
            udpClient = new UdpClient();
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            udpClient.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, UdpClientBroadcastPort);
            udpClient.Client.Bind(localEp);

            Task.Run(() =>
            {
                using (udpClient)
                {
                    while (!end)
                    {
                        try
                        {
                            //IPEndPoint object will allow us to read datagrams sent from any source.
                            var remoteEndPoint = new IPEndPoint(IPAddress.Any, UdpClientBroadcastPort);
                            udpClient.Client.ReceiveTimeout = 10000;
                            var receivedResults = udpClient.Receive(ref remoteEndPoint);

                            lastUpdate =
                                JsonConvert.DeserializeObject<DCSPlayerRadioInfo>(
                                    Encoding.UTF8.GetString(receivedResults));

                            lastUpdateTime = DateTime.Now;
                        }
                        catch (Exception e)
                        {
                            Console.Out.WriteLine(e.ToString());
                        }
                    }

                    udpClient.Close();
                }
            });

            Task.Run(() =>
            {
                while (!end)
                {
                    Thread.Sleep(100);

                    //check
                    if (lastUpdate != null && lastUpdate.name != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            //check if current
                            var elapsedTicks = DateTime.Now.Ticks - lastUpdateTime.Ticks;
                            var elapsedSpan = new TimeSpan(elapsedTicks);

                            radio1.Update(lastUpdate, elapsedSpan);
                            radio2.Update(lastUpdate, elapsedSpan);
                            radio3.Update(lastUpdate, elapsedSpan);
                        });
                    }
                }
            });
        }

        private void SetupActiveRadio()
        {
            //setup UDP
            activeRadioUdpClient = new UdpClient();
            activeRadioUdpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            activeRadioUdpClient.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var multicastaddress = IPAddress.Parse("239.255.50.10");
            activeRadioUdpClient.JoinMulticastGroup(multicastaddress);

            var localEp = new IPEndPoint(IPAddress.Any, ActiveRadioClientPort);
            activeRadioUdpClient.Client.Bind(localEp);

            Task.Run(() =>
            {
                using (activeRadioUdpClient)
                {
                    while (!end)
                    {
                        try
                        {
                            //IPEndPoint object will allow us to read datagrams sent from any source.
                            var remoteEndPoint = new IPEndPoint(IPAddress.Any, ActiveRadioClientPort);
                            activeRadioUdpClient.Client.ReceiveTimeout = 10000;
                            var receivedResults = activeRadioUdpClient.Receive(ref remoteEndPoint);

                            var lastRadioTransmit =
                                JsonConvert.DeserializeObject<RadioTransmit>(Encoding.UTF8.GetString(receivedResults));
                            switch (lastRadioTransmit.radio)
                            {
                                case 0:
                                    radio1.SetLastRadioTransmit(lastRadioTransmit);
                                    break;
                                case 1:
                                    radio2.SetLastRadioTransmit(lastRadioTransmit);
                                    break;
                                case 2:
                                    radio3.SetLastRadioTransmit(lastRadioTransmit);
                                    break;
                                default:
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            // Console.Out.WriteLine(e.ToString());
                        }
                    }

                    activeRadioUdpClient.Close();
                }
            });

            Task.Run(() =>
            {
                while (!end)
                {
                    Thread.Sleep(50);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        radio1.RepaintRadioTransmit();
                        radio2.RepaintRadioTransmit();
                        radio3.RepaintRadioTransmit();
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

            end = true;
        }

        ~MainWindow()
        {
            //Shut down threads
            end = true;
        }

        private void Button_Minimise(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Button_Close(object sender, RoutedEventArgs e)
        {
            end = true;
            Close();
        }

        private void windowOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            Opacity = e.NewValue;
        }

        private void SendUDPCommand(RadioCommand.CmdType type)
        {
            var update = new RadioCommand();
            update.freq = 1;
            update.volume = 1;
            update.radio = 0;
            update.cmdType = type;

            var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(update) + "\n");
            //multicast
            Send("239.255.50.10", 5070, bytes);
            //unicast
            // Send("127.0.0.1", 5061, bytes);
        }

        private void Send(string ipStr, int port, byte[] bytes)
        {
            try
            {
                var client = new UdpClient();
                var ip = new IPEndPoint(IPAddress.Parse(ipStr), port);

                client.Send(bytes, bytes.Length, ip);
                client.Close();
            }
            catch (Exception e)
            {
            }
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
                Width = sizeInfo.NewSize.Height*aspectRatio;
            else
                Height = sizeInfo.NewSize.Width/aspectRatio;

            // Console.WriteLine(this.Height +" width:"+ this.Width);
        }

        #region ScaleValue Depdency Property //StackOverflow: http://stackoverflow.com/questions/3193339/tips-on-developing-resolution-independent-application/5000120#5000120

        public static readonly DependencyProperty ScaleValueProperty = DependencyProperty.Register("ScaleValue",
            typeof(double), typeof(MainWindow),
            new UIPropertyMetadata(1.0, OnScaleValueChanged,
                OnCoerceScaleValue));

        private static object OnCoerceScaleValue(DependencyObject o, object value)
        {
            var mainWindow = o as MainWindow;
            if (mainWindow != null)
                return mainWindow.OnCoerceScaleValue((double) value);
            return value;
        }

        private static void OnScaleValueChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            var mainWindow = o as MainWindow;
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