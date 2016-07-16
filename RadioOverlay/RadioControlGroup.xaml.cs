using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioControlGroup.xaml
    /// </summary>
    public partial class RadioControlGroup : UserControl
    {
        private const double MHz = 1000000;
        private bool _dragging;

        private RadioTransmit _lastActive;

        private DateTime _lastActiveTime = new DateTime(0L);
        private DCSPlayerRadioInfo _lastUpdate;
        public int RadioId { private get; set; }

        public RadioControlGroup()
        {
            InitializeComponent();
        }

        private void Up001_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz/100);
            FocusDCS();
        }
        private void Up01_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz / 10);
            FocusDCS();
        }

        private void Up1_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz);
            FocusDCS();
        }

        private void Up10_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz*10);
            FocusDCS();
        }

        private void Down10_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz*-10);
            FocusDCS();
        }

        private void Down1_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz*-1);
            FocusDCS();
        }

        private void Down01_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz/10*-1);
            FocusDCS();
        }
        private void Down001_Click(object sender, RoutedEventArgs e)
        {
            SendFrequencyChange(MHz / 100 * -1);
            FocusDCS();
        }

        private void SendUdpUpdate(RadioCommand update)
        {
            //only send update if the aircraft doesnt have its own radio system, i.e FC3
            if (_lastUpdate != null &&
                _lastUpdate.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
            {
                var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(update) + "\n");
                //multicast
                Send("239.255.50.10", 5070, bytes);
                //unicast
                //  send("127.0.0.1", 5061, bytes);
            }
        }

        private void FocusDCS()
        {
            var localByName = Process.GetProcessesByName("dcs");

            if (localByName != null && localByName.Length > 0)
            {
                //    WindowHelper.BringProcessToFront(localByName[0]);
            }
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

        private void SendFrequencyChange(double frequency)
        {
            var update = new RadioCommand
            {
                freq = frequency,
                radio = RadioId,
                cmdType = RadioCommand.CmdType.FREQUENCY
            };

            SendUdpUpdate(update);
        }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            var update = new RadioCommand
            {
                radio = RadioId,
                cmdType = RadioCommand.CmdType.SELECT
            };

            SendUdpUpdate(update);

            FocusDCS();
        }

        private void RadioFrequencyText_Click(object sender, MouseButtonEventArgs e)
        {
            var update = new RadioCommand
            {
                radio = RadioId,
                cmdType = RadioCommand.CmdType.SELECT
            };

            SendUdpUpdate(update);
            FocusDCS();
        }

        private void RadioFrequencyText_RightClick(object sender, MouseButtonEventArgs e)
        {
            // Console.WriteLine("Click!");
            var update = new RadioCommand
            {
                radio = RadioId,
                cmdType = RadioCommand.CmdType.GUARD_TOGGLE
            };

            SendUdpUpdate(update);
            FocusDCS();
        }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            _dragging = true;
        }


        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {
            var update = new RadioCommand
            {
                radio = RadioId,
                volume = (float) radioVolume.Value/100.0f,
                cmdType = RadioCommand.CmdType.VOLUME
            };

            SendUdpUpdate(update);
            _dragging = false;

            FocusDCS();
        }


        internal void Update(DCSPlayerRadioInfo lastUpdate, TimeSpan elapsedSpan)
        {
            _lastUpdate = lastUpdate;
            if (elapsedSpan.TotalSeconds > 10)
            {
                radioActive.Fill = new SolidColorBrush(Colors.Red);
                radioLabel.Content = "No Radio";
                radioFrequency.Text = "Unknown";

                radioVolume.IsEnabled = false;

                up10.IsEnabled = false;
                up1.IsEnabled = false;
                up01.IsEnabled = false;

                down10.IsEnabled = false;
                down1.IsEnabled = false;
                down01.IsEnabled = false;


                //reset dragging just incase
                _dragging = false;
            }
            else
            {
                if (RadioId == lastUpdate.selected)
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Orange);
                }

                var currentRadio = lastUpdate.radios[RadioId];

                if (currentRadio.modulation == 3) // disabled
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Red);
                    radioLabel.Content = "No Radio";
                    radioFrequency.Text = "Unknown";

                    radioVolume.IsEnabled = false;

                    up10.IsEnabled = false;
                    up1.IsEnabled = false;
                    up01.IsEnabled = false;
                    up001.IsEnabled = false;

                    down10.IsEnabled = false;
                    down1.IsEnabled = false;
                    down01.IsEnabled = false;
                    down001.IsEnabled = false;
                    return;
                }
                if (currentRadio.modulation == 2) //intercom
                {
                    radioFrequency.Text = "INTERCOM";
                }
                else
                {
                    radioFrequency.Text = (currentRadio.frequency/MHz).ToString("0.000") +
                                          (currentRadio.modulation == 0 ? "AM" : "FM");
                    if (currentRadio.secondaryFrequency > 100)
                    {
                        radioFrequency.Text += " G";
                    }
                    if (currentRadio.enc > 0)
                    {
                        radioFrequency.Text += " E" + currentRadio.enc; // ENCRYPTED
                    }
                }
                radioLabel.Content = lastUpdate.radios[RadioId].name;

                if (lastUpdate.radioType == DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    radioVolume.IsEnabled = false;
                    up10.IsEnabled = false;
                    up1.IsEnabled = false;
                    up01.IsEnabled = false;
                    up001.IsEnabled = false;

                    down10.IsEnabled = false;
                    down1.IsEnabled = false;
                    down01.IsEnabled = false;
                    down001.IsEnabled = false;

                    //reset dragging just incase
                    _dragging = false;
                }
                else if (lastUpdate.radioType == DCSPlayerRadioInfo.AircraftRadioType.PARTIAL_COCKPIT_INTEGRATION)
                {
                    radioVolume.IsEnabled = true;

                    up10.IsEnabled = false;
                    up1.IsEnabled = false;
                    up01.IsEnabled = false;
                    up001.IsEnabled = false;

                    down10.IsEnabled = false;
                    down1.IsEnabled = false;
                    down01.IsEnabled = false;
                    down001.IsEnabled = false;

                    //reset dragging just incase
                    _dragging = false;
                }
                else
                {
                    radioVolume.IsEnabled = true;
                    up10.IsEnabled = true;
                    up1.IsEnabled = true;
                    up01.IsEnabled = true;
                    up001.IsEnabled = true;

                    down10.IsEnabled = true;
                    down1.IsEnabled = true;
                    down01.IsEnabled = true;
                    down001.IsEnabled = true;
                }

                if (_dragging == false)
                {
                    radioVolume.Value = currentRadio.volume*100.0;
                }
            }
        }

        public void SetLastRadioTransmit(RadioTransmit radio)
        {
            _lastActive = radio;
            _lastActiveTime = DateTime.Now;
        }

        internal void RepaintRadioTransmit()
        {
            if (_lastActive == null)
            {
                radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
            }
            else
            {
                //check if current
                var elapsedTicks = DateTime.Now.Ticks - _lastActiveTime.Ticks;
                var elapsedSpan = new TimeSpan(elapsedTicks);

                if (elapsedSpan.TotalSeconds > 0.5)
                {
                    radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                }
                else
                {
                    if (_lastActive.radio == RadioId)
                    {
                        if (_lastActive.secondary)
                        {
                            radioFrequency.Foreground = new SolidColorBrush(Colors.Red);
                        }
                        else
                        {
                            radioFrequency.Foreground = new SolidColorBrush(Colors.White);
                        }
                    }
                    else
                    {
                        radioFrequency.Foreground =
                            new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                    }
                }
            }
        }
    }
}