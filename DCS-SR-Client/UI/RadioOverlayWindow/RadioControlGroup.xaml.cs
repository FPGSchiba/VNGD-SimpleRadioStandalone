using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.RadioChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for RadioControlGroup.xaml
    /// </summary>
    public partial class RadioControlGroup : UserControl
    {
        private const double MHz = 1000000;
        private bool _dragging;

        public PresetChannelsViewModel ChannelViewModel { get; }

        public RadioControlGroup()
        {

            ChannelViewModel = new PresetChannelsViewModel(new MockPresetChannelsStore());
            this.DataContext = this; // set data context
            InitializeComponent();

        }

        public int RadioId { private get; set; }

        private void Up0001_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(0.001, RadioId);
        }

        private void Up001_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(0.01, RadioId);
        }

        private void Up01_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(0.1, RadioId);
        }

        private void Up1_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(1, RadioId);
        }

        private void Up10_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(10, RadioId);
        }

        private void Down10_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(-10, RadioId);
        }

        private void Down1_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(-1, RadioId);
        }

        private void Down01_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(-0.1, RadioId);
        }

        private void Down001_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(-0.01, RadioId);
        }

        private void Down0001_Click(object sender, RoutedEventArgs e)
        {
            RadioHelper.UpdateRadioFrequency(-0.001, RadioId);
        }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            RadioHelper.SelectRadio(RadioId);
        }

        private void RadioFrequencyText_Click(object sender, MouseButtonEventArgs e)
        {
            RadioHelper.SelectRadio(RadioId);
        }

        private void RadioFrequencyText_RightClick(object sender, MouseButtonEventArgs e)
        {
            RadioHelper.ToggleGuard(RadioId);
        }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            _dragging = true;
        }


        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioDCSSyncServer.DcsPlayerRadioInfo.radios[RadioId];

            if (currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY)
            {
                var clientRadio = RadioDCSSyncServer.DcsPlayerRadioInfo.radios[RadioId];

                clientRadio.volume = (float) radioVolume.Value/100.0f;
            }

            _dragging = false;
        }

        private void ToggleButtons(bool enable)
        {
            if (enable)
            {
                up10.Visibility = Visibility.Visible;
                up1.Visibility = Visibility.Visible;
                up01.Visibility = Visibility.Visible;
                up001.Visibility = Visibility.Visible;
                up0001.Visibility = Visibility.Visible;

                down10.Visibility = Visibility.Visible;
                down1.Visibility = Visibility.Visible;
                down01.Visibility = Visibility.Visible;
                down001.Visibility = Visibility.Visible;
                down0001.Visibility = Visibility.Visible;

                up10.IsEnabled = true;
                up1.IsEnabled = true;
                up01.IsEnabled = true;
                up001.IsEnabled = true;
                up0001.IsEnabled = true;

                down10.IsEnabled = true;
                down1.IsEnabled = true;
                down01.IsEnabled = true;
                down001.IsEnabled = true;
                down0001.IsEnabled = true;

              //  ReloadButton.IsEnabled = true;
                //LoadFromFileButton.IsEnabled = true;
            }
            else
            {
                up10.Visibility = Visibility.Hidden;
                up1.Visibility = Visibility.Hidden;
                up01.Visibility = Visibility.Hidden;
                up001.Visibility = Visibility.Hidden;
                up0001.Visibility = Visibility.Hidden;

                down10.Visibility = Visibility.Hidden;
                down1.Visibility = Visibility.Hidden;
                down01.Visibility = Visibility.Hidden;
                down001.Visibility = Visibility.Hidden;
                down0001.Visibility = Visibility.Hidden;

               // ReloadButton.IsEnabled = false;
              //  LoadFromFileButton.IsEnabled = false;
            }
        }

        internal void RepaintRadioStatus()
        {
            SetupEncryption();

            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo == null) || !dcsPlayerRadioInfo.IsCurrent())
            {
                radioActive.Fill = new SolidColorBrush(Colors.Red);
                radioLabel.Text = "No Radio";
                radioFrequency.Text = "Unknown";

                radioVolume.IsEnabled = false;

                ToggleButtons(false);

                //reset dragging just incase
                _dragging = false;
            }
            else
            {
                if (RadioId == dcsPlayerRadioInfo.selected)
                {
                    var transmitting = UdpVoiceHandler.RadioSendingState;

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

                if (currentRadio.modulation == RadioInformation.Modulation.DISABLED) // disabled
                {
                    radioActive.Fill = new SolidColorBrush(Colors.Red);
                    radioLabel.Text = "No Radio";
                    radioFrequency.Text = "Unknown";

                    radioVolume.IsEnabled = false;

                    ToggleButtons(false);
                    return;
                }
                if (currentRadio.modulation == RadioInformation.Modulation.INTERCOM) //intercom
                {
                    radioFrequency.Text = "INTERCOM";
                }
                else
                {
                    radioFrequency.Text = (currentRadio.freq/MHz).ToString("0.000", CultureInfo.InvariantCulture) + //make nuber UK / US style with decimals not commas!
                    (currentRadio.modulation == 0 ? "AM" : "FM");
                    if (currentRadio.secFreq > 100)
                    {
                        radioFrequency.Text += " G";
                    }
                    if (currentRadio.enc && (currentRadio.encKey > 0))
                    {
                        radioFrequency.Text += " E" + currentRadio.encKey; // ENCRYPTED
                    }
                }
                radioLabel.Text = dcsPlayerRadioInfo.radios[RadioId].name;

                if (currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY)
                {
                    radioVolume.IsEnabled = true;

                    //reset dragging just incase
                    //    _dragging = false;
                }
                else
                {
                    radioVolume.IsEnabled = false;

                    //reset dragging just incase
                    //  _dragging = false;
                }

                ToggleButtons(currentRadio.freqMode == RadioInformation.FreqMode.OVERLAY);

                if (_dragging == false)
                {
                    radioVolume.Value = currentRadio.volume*100.0;
                }
            }
        }

        private void SetupEncryption()
        {
            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo != null) || dcsPlayerRadioInfo.IsCurrent())
            {
                var currentRadio = dcsPlayerRadioInfo.radios[RadioId];

                EncryptionKeySpinner.Value = currentRadio.encKey;

                //update stuff
                if ((currentRadio.encMode == RadioInformation.EncryptionMode.NO_ENCRYPTION)
                    || (currentRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_FULL)
                    || (currentRadio.modulation == RadioInformation.Modulation.INTERCOM))
                {
                    //Disable everything
                    EncryptionKeySpinner.IsEnabled = false;
                    EncryptionButton.IsEnabled = false;
                    EncryptionButton.Content = "Enable";
                }
                else if (currentRadio.encMode ==
                         RadioInformation.EncryptionMode.ENCRYPTION_COCKPIT_TOGGLE_OVERLAY_CODE)
                {
                    //allow spinner
                    EncryptionKeySpinner.IsEnabled = true;

                    //disallow encryption toggle
                    EncryptionButton.IsEnabled = false;
                    EncryptionButton.Content = "Enable";
                }
                else if (currentRadio.encMode ==
                         RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY)
                {
                    EncryptionKeySpinner.IsEnabled = true;
                    EncryptionButton.IsEnabled = true;

                    if (currentRadio.enc)
                    {
                        EncryptionButton.Content = "Disable";
                    }
                    else
                    {
                        EncryptionButton.Content = "Enable";
                    }
                }
            }
            else
            {
                //Disable everything
                EncryptionKeySpinner.IsEnabled = false;
                EncryptionButton.IsEnabled = false;
                EncryptionButton.Content = "Enable";
            }
        }


        internal void RepaintRadioReceive()
        {
            var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;
            if (dcsPlayerRadioInfo == null)
            {
                radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
            }
            else
            {
                var receiveState = UdpVoiceHandler.RadioReceivingState[RadioId];
                //check if current

                if ((receiveState == null) || !receiveState.IsReceiving)
                {
                    radioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                }
                else if ((receiveState != null) && receiveState.IsReceiving)
                {
                    if (receiveState.IsSecondary)
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


        private void Encryption_ButtonClick(object sender, RoutedEventArgs e)
        {

            var currentRadio = RadioHelper.GetRadio(RadioId);

            if (currentRadio != null && 
                currentRadio.modulation != RadioInformation.Modulation.DISABLED) // disabled
            {
                //update stuff
                if (currentRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY)
                {
                    RadioHelper.ToggleEncryption(RadioId);

                    if (currentRadio.enc)
                    { 
                        EncryptionButton.Content = "Enable";
                    }
                    else
                    {
                        EncryptionButton.Content = "Disable";
                    }
                }
            }
            
        }

        private void EncryptionKeySpinner_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if(EncryptionKeySpinner?.Value != null)
                RadioHelper.SetEncryptionKey(RadioId,(byte)EncryptionKeySpinner.Value);
        }
        
    }
}