using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for IntercomControlGroup.xaml
    /// </summary>
    public partial class IntercomControlGroup : UserControl
    {
        private const double MHz = 1000000;
        private bool _dragging;

        public int RadioId { private get; set; }

        public IntercomControlGroup()
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

        private void FocusDCS()
        {
            var localByName = Process.GetProcessesByName("dcs");

            if (localByName != null && localByName.Length > 0)
            {
                //    WindowHelper.BringProcessToFront(localByName[0]);
            }
        }

        private void SendFrequencyChange(double frequency)
        {

            if (RadioSyncServer.DcsPlayerRadioInfo.radioType == DCSPlayerRadioInfo.AircraftRadioType.NO_COCKPIT_INTEGRATION
               && RadioId >= 0
               && RadioId < RadioSyncServer.DcsPlayerRadioInfo.radios.Length)
            {
                //sort out the frequencies
                var clientRadio = RadioSyncServer.DcsPlayerRadioInfo.radios[RadioId];
                clientRadio.frequency += frequency;

                //make sure we're not over or under a limit
                if (clientRadio.frequency > clientRadio.freqMax)
                {
                    clientRadio.frequency = clientRadio.freqMax;
                }
                else if (clientRadio.frequency < clientRadio.freqMin)
                {
                    clientRadio.frequency = clientRadio.freqMin;
                }

                //make radio data stale to force resysnc
                RadioSyncServer.LastSent = 0;

            }
        }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            if (RadioSyncServer.DcsPlayerRadioInfo.radioType != DCSPlayerRadioInfo.AircraftRadioType.FULL_COCKPIT_INTEGRATION)
            {
                RadioSyncServer.DcsPlayerRadioInfo.selected = (short)RadioId;
            }

            FocusDCS();
        }

   
        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            _dragging = true;
        }


        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {

            if (RadioSyncServer.DcsPlayerRadioInfo.radioType == DCSPlayerRadioInfo.AircraftRadioType.NO_COCKPIT_INTEGRATION)
            {
                var clientRadio = RadioSyncServer.DcsPlayerRadioInfo.radios[RadioId];

                clientRadio.volume = (float)radioVolume.Value / 100.0f;
            }

            _dragging = false;

            FocusDCS();
        }

       
        internal void RepaintRadioStatus()
        {
            var dcsPlayerRadioInfo = RadioSyncServer.DcsPlayerRadioInfo;

            if (dcsPlayerRadioInfo  == null || !dcsPlayerRadioInfo.IsCurrent())
            {
                radioActive.Fill = new SolidColorBrush(Colors.Red);

                radioVolume.IsEnabled = false;
               
                //reset dragging just incase
                _dragging = false;
            }
            else
            {
                if (RadioId == dcsPlayerRadioInfo.selected)
                {
                    var transmitting = UdpVoiceHandler.RadioSendingState;
                    var receiveState = UdpVoiceHandler.RadioReceivingState;

                    if ((transmitting.IsSending && transmitting.SendingOn == RadioId )
                        ||
                        (receiveState.IsReceiving() && receiveState.ReceivedOn == RadioId))
                    {
                        radioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
                    }
                    else
                    {
                        radioActive.Fill = new SolidColorBrush(Colors.Green);
                    }
                }
                else
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Orange);
                }

                var currentRadio = dcsPlayerRadioInfo.radios[RadioId];

                if (currentRadio.modulation == 2) //intercom
                {
                    radioLabel.Content = "INTERCOM";
                    radioVolume.IsEnabled = false;
                }
                else
                {
                    radioLabel.Content = "No INTERCOM";
                    radioActive.Fill = new SolidColorBrush(Colors.Red);
                }
             
                if (_dragging == false)
                {
                    radioVolume.Value = currentRadio.volume*100.0;
                }
            }
        }

    }
}