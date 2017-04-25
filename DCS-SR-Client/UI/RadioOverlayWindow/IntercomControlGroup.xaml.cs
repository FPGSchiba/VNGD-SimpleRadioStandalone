using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for IntercomControlGroup.xaml
    /// </summary>
    public partial class IntercomControlGroup : UserControl
    {
        private bool _dragging;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        public IntercomControlGroup()
        {
            InitializeComponent();
        }

        public int RadioId { private get; set; }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            var currentRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[RadioId];

            if (currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                if (_clientStateSingleton.DcsPlayerRadioInfo.control ==
                    DCSPlayerRadioInfo.RadioSwitchControls.HOTAS)
                {
                    _clientStateSingleton.DcsPlayerRadioInfo.selected = (short) RadioId;
                }
            }
        }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            _dragging = true;
        }


        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {
            var currentRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[RadioId];

            if (currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                if (currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY)
                {
                    var clientRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[RadioId];

                    clientRadio.volume = (float) radioVolume.Value/100.0f;
                }
            }

            _dragging = false;
        }

        internal void RepaintRadioStatus()
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo == null) || !dcsPlayerRadioInfo.IsCurrent())
            {
                radioActive.Fill = new SolidColorBrush(Colors.Red);

                radioVolume.IsEnabled = false;

                //reset dragging just incase
                _dragging = false;
            }
            else
            {
                var transmitting = UdpVoiceHandler.RadioSendingState;
                var receiveState = UdpVoiceHandler.RadioReceivingState[RadioId];

                if ((receiveState != null) && receiveState.IsReceiving)
                {
                    radioActive.Fill = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#96FF6D"));
                }
                else if (RadioId == dcsPlayerRadioInfo.selected)
                {
                    if (transmitting.IsSending && (transmitting.SendingOn == RadioId))
                    {
                        radioActive.Fill = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#96FF6D"));
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

                if (currentRadio.modulation == RadioInformation.Modulation.INTERCOM) //intercom
                {
                    radioLabel.Text = "INTERCOM";

                    radioVolume.IsEnabled = currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY;
                }
                else
                {
                    radioLabel.Text = "NO INTERCOM";
                    radioActive.Fill = new SolidColorBrush(Colors.Red);
                    radioVolume.IsEnabled = false;
                }

                if (_dragging == false)
                {
                    radioVolume.Value = currentRadio.volume*100.0;
                }
            }
        }
    }
}