using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow
{
    /// <summary>
    ///     Interaction logic for RadioControlGroup.xaml
    /// </summary>
    public partial class RadioControlGroupTransparent : UserControl
    {
        private const double MHz = 1000000;
        private const int MaxSimultaneousTransmissions = 1;
        private bool _dragging;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly ConnectedClientsSingleton _connectClientsSingleton = ConnectedClientsSingleton.Instance;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Brush radioOn = (Brush)new BrushConverter().ConvertFromString("#666");
        private static Brush radioOff = Brushes.IndianRed;

        public PresetChannelsViewModel ChannelViewModel { get; set; }


        public RadioControlGroupTransparent()
        {
            this.DataContext = this; // set data context

            InitializeComponent();

            RadioFrequency.MaxLines = 1;
            RadioFrequency.MaxLength = 7;

            RadioFrequency.LostFocus += RadioFrequencyOnLostFocus;

            RadioFrequency.KeyDown += RadioFrequencyOnKeyDown;

            RadioFrequency.GotFocus += RadioFrequencyOnGotFocus;
        }

        private int _radioId;

        public int RadioId
        {
            private get { return _radioId; }
            set
            {
                _radioId = value;
            }
        }

       

        private void RadioFrequencyOnGotFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo == null) || !dcsPlayerRadioInfo.IsCurrent() ||
                RadioId > dcsPlayerRadioInfo.radios.Length - 1 || RadioId < 0)
            {
                //remove focus to somewhere else
                RadioVolume.Focus();
                Keyboard.ClearFocus(); //then clear altogether
            }
        }

        private void RadioFrequencyOnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs.Key == Key.Enter)
            {
                //remove focus to somewhere else
                RadioVolume.Focus();
                Keyboard.ClearFocus(); //then clear altogher
            }
        }

        private void RadioFrequencyOnLostFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            double freq = 0;
            // Some locales/cultures (e.g. German) do not parse "." as decimal points since they use decimal commas ("123,45"), leading to "123.45" being parsed as "12345" and frequencies being set too high
            // Using an invariant culture makes sure the decimal point is parsed properly for all locales - replacing any commas makes sure people entering numbers in a weird format still get correct results
            if (double.TryParse(RadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out freq))
            {
                RadioHelper.UpdateRadioFrequency(freq, RadioId, false);
            }
            else
            {
                RadioFrequency.Text = "";
            }
        }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            RadioHelper.SelectRadio(RadioId);
        }

        private void RadioFrequencyText_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // TODO functionality to use scroll wheel to change frequency using scroll wheel
            

            {if (e.Delta > 0)
                {
                    //TODO when mouse wheel goes up
                    Logger.Info("MouseWheel radio frequency Up");
                }

            if (e.Delta < 0)
                {
                    //TODO when mouse wheel goes down
                    Logger.Info("MouseWheel radio frequency Down");
                }
                e.Handled = true;
            }

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
            var currentRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[RadioId];

            if (currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY)
            {
                var clientRadio = _clientStateSingleton.DcsPlayerRadioInfo.radios[RadioId];

                clientRadio.volume = (float) RadioVolume.Value / 100.0f;
            }

            _dragging = false;
        }

        private void ToggleButtons(bool enable)
        {

            if (_clientStateSingleton.IsConnected && _clientStateSingleton.ExternalAWACSModeConnected)
            {
                RadioEnabled.Background =
                    RadioHelper.GetRadio(RadioId).modulation != RadioInformation.Modulation.DISABLED
                        ? radioOn
                        : radioOff;
                RadioEnabled.Content = RadioHelper.GetRadio(RadioId).modulation != RadioInformation.Modulation.DISABLED
                    ? "On"
                    : "Off";
            }
            else
            {
                RadioEnabled.Background = radioOff;
                RadioEnabled.Content = "Off";
            }
        }

        internal void RepaintRadioStatus()
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            if (!_clientStateSingleton.IsConnected || (dcsPlayerRadioInfo == null) || !dcsPlayerRadioInfo.IsCurrent() ||
                RadioId > dcsPlayerRadioInfo.radios.Length - 1)
            {
                //Color and settings for disconnected radio
                RadioActive.Fill = new SolidColorBrush(Colors.Red);
                RadioLabel.Text = "No Radio";
                RadioFrequency.Text = "Unknown";

                RadioMetaData.Text = "";

                RadioVolume.IsEnabled = false;

                ToggleButtons(false);
                RadioEnabled.IsEnabled = false;

                //reset dragging just incase
                _dragging = false;
            }
            else
            {
                var currentRadio = dcsPlayerRadioInfo.radios[RadioId];
                var transmitting = _clientStateSingleton.RadioSendingState;

                if (transmitting.IsSending)
                {
                    if (transmitting.SendingOn == RadioId)
                    {
                        //Color for user transmitting - dabble
                        RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
                    }
                    else if (currentRadio != null && currentRadio.simul)
                    {
                        //Color for simultaneous transmissions - dabble
                        RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4F86FF"));
                    }
                    else
                    {
                        //unknown purposes yet - dabble
                        RadioActive.Fill = RadioId == dcsPlayerRadioInfo.selected ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Orange);
                    }
                }
                else
                {
                    if (RadioId == dcsPlayerRadioInfo.selected)
                    {
                        //Color for selected radio, not transmitting - dabble
                        RadioActive.Fill = new SolidColorBrush(Colors.Green);
                    }
                    else if (currentRadio != null && currentRadio.simul)
                    {
                        //Color for deselected radio that is setup for simultaneous transmissions - dabble
                        RadioActive.Fill = new SolidColorBrush(Colors.DarkBlue);
                    }
                    else
                    {
                        //Color for unselected radio that is powered on - dabble
                        RadioActive.Fill = new SolidColorBrush(Colors.Orange);
                    }
                }

                if (currentRadio == null || currentRadio.modulation == RadioInformation.Modulation.DISABLED) // disabled
                {
                    RadioActive.Fill = radioOff;
                    RadioLabel.Text = "No Radio";
                    RadioFrequency.Text = "Unknown";
                    RadioMetaData.Text = "";


                    RadioVolume.IsEnabled = false;

                    ToggleButtons(false);
                    RadioEnabled.IsEnabled = true;

                    return;
                }
                if (currentRadio.modulation == RadioInformation.Modulation.INTERCOM) //intercom
                {
                    RadioFrequency.Text = "INTERCOM";
                    RadioMetaData.Text = "";
                }
                else if (currentRadio.modulation == RadioInformation.Modulation.MIDS) //MIDS
                {
                    RadioFrequency.Text = "MIDS";
                    if (currentRadio.channel >= 0)
                    {
                        RadioMetaData.Text = " CHN " + currentRadio.channel;
                    }
                    else
                    {
                        RadioMetaData.Text = " OFF";
                    }
  
                }
                else
                {
                    if (!RadioFrequency.IsFocused
                        || currentRadio.freqMode == RadioInformation.FreqMode.COCKPIT
                        || currentRadio.modulation == RadioInformation.Modulation.DISABLED)
                    {
                        RadioFrequency.Text =
                            (currentRadio.freq / MHz).ToString("0.000",
                                CultureInfo.InvariantCulture); //make number UK / US style with decimals not commas!
                    }

                    if (currentRadio.modulation == RadioInformation.Modulation.AM)
                    {
                        //Dabble updated this
                        //Changed Text to remove AM from here as AM is the default Modulation.
                        //We are keeping the other modulations for better troubleshooting should anyone
                        //change the modulation in the future
                        RadioMetaData.Text = "";
                    }
                    else if (currentRadio.modulation == RadioInformation.Modulation.FM)
                    {
                        RadioMetaData.Text = "FM";
                    }
                    else if (currentRadio.modulation == RadioInformation.Modulation.HAVEQUICK)
                    {
                        RadioMetaData.Text = "HQ";
                    }
                    else
                    {
                        RadioMetaData.Text += "";
                    }

                    if (currentRadio.secFreq > 100)
                    {
                        //Dabble updated this
                        //Because are not using the secondary radios, we don't need to identify "Guard".
                        //Original text here " G"
                        RadioMetaData.Text += "";
                    }

                    if (currentRadio.channel > -1)
                    {
                        RadioMetaData.Text += (" C" + currentRadio.channel);
                    }
                    if (currentRadio.enc && (currentRadio.encKey > 0))
                    {
                        RadioMetaData.Text += " E" + currentRadio.encKey; // ENCRYPTED
                    }

                 
                }
                RadioLabel.Text = dcsPlayerRadioInfo.radios[RadioId].name;

                int count = _connectClientsSingleton.ClientsOnFreq(currentRadio.freq, currentRadio.modulation);

                if (count > 0)
                {
                    RadioMetaData.Text += " 👤" + count;
                }

                if (currentRadio.volMode == RadioInformation.VolumeMode.OVERLAY)
                {
                    RadioVolume.IsEnabled = true;

                    //reset dragging just incase
                    //    _dragging = false;
                }
                else
                {
                    RadioVolume.IsEnabled = false;

                    //reset dragging just incase
                    //  _dragging = false;
                }

                ToggleButtons(currentRadio.freqMode == RadioInformation.FreqMode.OVERLAY);
                RadioEnabled.IsEnabled = currentRadio.freqMode == RadioInformation.FreqMode.OVERLAY;

                if (_dragging == false)
                {
                    RadioVolume.Value = currentRadio.volume * 100.0;
                }
            }

            
        }

        


        internal void RepaintRadioReceive()
        {
            TransmitterName.Visibility = Visibility.Collapsed;
            RadioFrequency.Visibility = Visibility.Visible;
            RadioMetaData.Visibility = Visibility.Visible;

            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;
            if (dcsPlayerRadioInfo == null)
            {
                RadioFrequency.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                RadioMetaData.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                
            }
            else
            {
                var receiveState = _clientStateSingleton.RadioReceivingState[RadioId];
                //check if current

                if ((receiveState == null) || !receiveState.IsReceiving)
                {
                    RadioFrequency.Foreground =
                        new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                    RadioMetaData.Foreground = new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                }
                else if ((receiveState != null) && receiveState.IsReceiving)
                {
                    if (receiveState.SentBy.Length > 0)
                    {
                        TransmitterName.Text = receiveState.SentBy;

                        TransmitterName.Visibility = Visibility.Visible;
                        RadioFrequency.Visibility = Visibility.Collapsed;
                        RadioMetaData.Visibility = Visibility.Collapsed;

                    }
                    if (receiveState.IsSecondary)
                    {
                        TransmitterName.Foreground = new SolidColorBrush(Colors.Red);
                        RadioFrequency.Foreground = new SolidColorBrush(Colors.Red);
                        RadioMetaData.Foreground = new SolidColorBrush(Colors.Red);
                    }
                    else
                    {
                        TransmitterName.Foreground = new SolidColorBrush(Colors.White);
                        RadioFrequency.Foreground = new SolidColorBrush(Colors.White);
                        RadioMetaData.Foreground = new SolidColorBrush(Colors.White);
                    }
                }
                else
                {
                    RadioFrequency.Foreground =
                        new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                    RadioMetaData.Foreground =
                        new SolidColorBrush((Color) ColorConverter.ConvertFromString("#00FF00"));
                }
            }
        }


        private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioHelper.GetRadio(RadioId);
            // Radio is disabled and exists
            if (currentRadio != null && currentRadio.modulation == RadioInformation.Modulation.DISABLED)
            {
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.AM);
                RadioEnabled.Background = radioOn;
                RadioEnabled.Content = "On";

            }
            else if (currentRadio != null && currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.DISABLED);
                RadioEnabled.Background = radioOff;
                RadioEnabled.Content = "Off";
            }
        }

        private void HideRadio_Click(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioHelper.GetRadio(RadioId);
            // Radio is disabled and exists
            if (currentRadio != null && currentRadio.modulation == RadioInformation.Modulation.DISABLED)
            {
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.AM);
                RadioMetaData.Visibility = Visibility.Collapsed;
                RadioEnabled.Visibility = Visibility.Collapsed;
                RadioFrequency.Visibility = Visibility.Collapsed;
                RadioActive.Visibility = Visibility.Collapsed;
                RadioVolume.Visibility = Visibility.Collapsed;
                RadioEnabled.Background = radioOn;
                RadioEnabled.Content = "On";

            }
            else if (currentRadio != null && currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                return;
            }
        }
    }
}