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
        private bool dragging;

        private RadioTransmit lastActive;

        private DateTime lastActiveTime = new DateTime(0L);
        private DCSPlayerRadioInfo _lastUpdate;
        public int radioId;

        public RadioControlGroup()
        {
            InitializeComponent();
        }

        private void up01_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz/10);
            FocusDCS();
        }

        private void up1_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz);
            FocusDCS();
        }

        private void up10_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz*10);
            FocusDCS();
        }

        private void down10_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz*-10);
            FocusDCS();
        }

        private void down1_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz*-1);
            FocusDCS();
        }

        private void down01_Click(object sender, RoutedEventArgs e)
        {
            sendFrequencyChange(MHz/10*-1);
            FocusDCS();
        }

        private void sendUDPUpdate(RadioCommand update)
        {
            //only send update if the aircraft doesnt have its own radio system, i.e FC3
            if (_lastUpdate != null &&
                _lastUpdate.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
            {
                var bytes = Encoding.ASCII.GetBytes(JsonConvert.SerializeObject(update) + "\n");
                //multicast
                send("239.255.50.10", 5070, bytes);
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

        private void send(string ipStr, int port, byte[] bytes)
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

        private void sendFrequencyChange(double frequency)
        {
            var update = new RadioCommand();
            update.freq = frequency;
            update.radio = radioId;
            update.cmdType = RadioCommand.CmdType.FREQUENCY;

            sendUDPUpdate(update);
        }

        private void radioSelectSwitch(object sender, RoutedEventArgs e)
        {
            var update = new RadioCommand();
            update.radio = radioId;
            update.cmdType = RadioCommand.CmdType.SELECT;

            sendUDPUpdate(update);

            FocusDCS();
        }

        private void radioFrequencyText_Click(object sender, MouseButtonEventArgs e)
        {
            var update = new RadioCommand();
            update.radio = radioId;
            update.cmdType = RadioCommand.CmdType.SELECT;

            sendUDPUpdate(update);
            FocusDCS();
        }

        private void radioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            dragging = true;
        }


        private void radioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {
            var update = new RadioCommand();
            update.radio = radioId;
            update.volume = (float) radioVolume.Value/100.0f;
            update.cmdType = RadioCommand.CmdType.VOLUME;

            sendUDPUpdate(update);
            dragging = false;

            FocusDCS();
        }


        internal void update(DCSPlayerRadioInfo lastUpdate, TimeSpan elapsedSpan)
        {
            this._lastUpdate = lastUpdate;
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
                dragging = false;
            }
            else
            {
                if (radioId == lastUpdate.selected)
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Orange);
                }

                var currentRadio = lastUpdate.radios[radioId];

                if (currentRadio.modulation == 3) // disabled
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
                        radioFrequency.Text += " E"+currentRadio.enc; // ENCRYPTED
                    }
                }
                radioLabel.Content = lastUpdate.radios[radioId].name;

                if (lastUpdate.radioType == DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
                {
                    radioVolume.IsEnabled = false;
                    up10.IsEnabled = false;
                    up1.IsEnabled = false;
                    up01.IsEnabled = false;

                    down10.IsEnabled = false;
                    down1.IsEnabled = false;
                    down01.IsEnabled = false;


                    //reset dragging just incase
                    dragging = false;
                }
                else if (lastUpdate.radioType == DCSPlayerRadioInfo.AircraftRadioType.PARTIAL_COCKPIT_INTEGRATION)
                {
                    radioVolume.IsEnabled = true;

                    up10.IsEnabled = false;
                    up1.IsEnabled = false;
                    up01.IsEnabled = false;

                    down10.IsEnabled = false;
                    down1.IsEnabled = false;
                    down01.IsEnabled = false;

                    //reset dragging just incase
                    dragging = false;
                }
                else
                {
                    radioVolume.IsEnabled = true;
                    up10.IsEnabled = true;
                    up1.IsEnabled = true;
                    up01.IsEnabled = true;

                    down10.IsEnabled = true;
                    down1.IsEnabled = true;
                    down01.IsEnabled = true;
                }

                if (dragging == false)
                {
                    radioVolume.Value = currentRadio.volume*100.0;
                }
            }
        }

        public void setLastRadioTransmit(RadioTransmit radio)
        {
            lastActive = radio;
            lastActiveTime = DateTime.Now;
        }

        internal void repaintRadioTransmit()
        {
            if (lastActive == null)
            {
                radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
            }
            else
            {
                //check if current
                var elapsedTicks = DateTime.Now.Ticks - lastActiveTime.Ticks;
                var elapsedSpan = new TimeSpan(elapsedTicks);

                if (elapsedSpan.TotalSeconds > 0.5)
                {
                    radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                }
                else
                {
                    if (lastActive.radio == radioId)
                    {
                        if (lastActive.secondary)
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