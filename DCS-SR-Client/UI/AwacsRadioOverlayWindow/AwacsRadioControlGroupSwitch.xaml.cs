using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    public partial class RadioControlGroupSwitch : UserControl
    {
        private const double MHz = 1000000;
        private bool _dragging;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly ConnectedClientsSingleton _connectClientsSingleton = ConnectedClientsSingleton.Instance;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Brush RadioOn = (Brush)new BrushConverter().ConvertFromString("#666");
        private static readonly Brush RadioOff = Brushes.IndianRed;
        
        public bool IsEnabled
        {
            get => RadioEnabled.Background == RadioOn;
        }

        public PresetChannelsViewModel ChannelViewModel { get; set; }
        public PresetStandbyChannelsViewModel StandbyChannelViewModel { get; set; }
        
        public RadioControlGroupSwitch()
        {
            this.DataContext = this; // set data context

            InitializeComponent();

            RadioFrequency.MaxLines = 1;
            RadioFrequency.MaxLength = 7;
            
            StandbyRadioFrequency.MaxLines = 1;
            StandbyRadioFrequency.MaxLength = 7;

            RadioFrequency.LostFocus += RadioFrequencyOnLostFocus;
            StandbyRadioFrequency.LostFocus += StandbyFrequencyOnLostFocus;

            RadioFrequency.KeyDown += RadioFrequencyOnKeyDown;
            StandbyRadioFrequency.KeyDown += StandbyFrequencyOnKeyDown;

            RadioFrequency.GotFocus += RadioFrequencyOnGotFocus;
            StandbyRadioFrequency.GotFocus += StandbyFrequencyOnGotFocus;
        }

        private int _radioId;

        public int RadioId
        {
            get { return _radioId; }
            set
            {
                _radioId = value;
                UpdateBinding();
            }
        }

        //updates the binding so the changes are picked up for the linked FixedChannelsModel
        private void UpdateBinding()
        {
            ChannelViewModel = _clientStateSingleton.FixedChannels[_radioId - 1];
            StandbyChannelViewModel = _clientStateSingleton.StandbyChannels[_radioId - 1];

            var bindingExpression = PresetChannelsView.GetBindingExpression(DataContextProperty);
            bindingExpression?.UpdateTarget();
            var standbyBindingExpression = StandbyPresetChannelsView.GetBindingExpression(DataContextProperty);
            standbyBindingExpression?.UpdateTarget();
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
            // Some locales/cultures (e.g. German) do not parse "." as decimal points since they use decimal commas ("123,45"), leading to "123.45" being parsed as "12345" and frequencies being set too high
            // Using an invariant culture makes sure the decimal point is parsed properly for all locales - replacing any commas makes sure people entering numbers in a weird format still get correct results
            if (double.TryParse(RadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double freq))
            {
                RadioHelper.UpdateRadioFrequency(freq, RadioId, false);
            }
            else
            {
                RadioFrequency.Text = "";
            }
        }
        
        private void StandbyFrequencyOnGotFocus(object sender, RoutedEventArgs routedEventArgs)
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

        private void StandbyFrequencyOnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs.Key == Key.Enter)
            {
                //remove focus to somewhere else
                RadioVolume.Focus();
                Keyboard.ClearFocus(); //then clear altogher
            }
        }

        private void StandbyFrequencyOnLostFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            // Some locales/cultures (e.g. German) do not parse "." as decimal points since they use decimal commas ("123,45"), leading to "123.45" being parsed as "12345" and frequencies being set too high
            // Using an invariant culture makes sure the decimal point is parsed properly for all locales - replacing any commas makes sure people entering numbers in a weird format still get correct results
            if (double.TryParse(StandbyRadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double freq))
            {
                RadioHelper.UpdateStandbyRadioFrequency(freq, RadioId, false);
            }
            else
            {
                StandbyRadioFrequency.Text = "";
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


        // Dabble Added Standby Frequency Left and Right Click Option
        private void StandbyRadioFrequencyText_Click(object sender, MouseButtonEventArgs e)
        {
            RadioHelper.SelectRadio(RadioId);
        }

        private void StandbyRadioFrequencyText_RightClick(object sender, MouseButtonEventArgs e)
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
        
        private void ToggleButtons()
        {

            if (_clientStateSingleton.IsConnected && _clientStateSingleton.ExternalAWACSModeConnected)
            {
                var radio = RadioHelper.GetRadio(RadioId);

                if (radio != null)
                {
                    RadioEnabled.Background = radio.modulation != RadioInformation.Modulation.DISABLED ? RadioOn : RadioOff;
                    RadioEnabled.Content = new TextBlock
                    {
                        FontSize = 5,
                        Text = radio.modulation != RadioInformation.Modulation.DISABLED ? "On" : "Off",
                    };
                }
                else
                {
                    Logger.Warn($"Radio with ID: {RadioId} was not found. And could not Toggle.");
                }
                
            }
            else
            {
                RadioEnabled.Background = RadioOff;
                RadioEnabled.Content = new TextBlock
                {
                    FontSize = 5,
                    Text = "Off",
                };
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
                StandbyRadioFrequency.Text = "Unknown";

                RadioMetaData.Text = "";
                StandbyRadioMetaData.Text = "";

                RadioVolume.IsEnabled = false;

                ToggleButtons();
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
                    RadioActive.Fill = RadioOff;
                    RadioLabel.Text = "OFF";
                    RadioFrequency.Text = "";
                    RadioMetaData.Text = "";

                    StandbyRadioFrequency.Text = "";
                    StandbyRadioMetaData.Text = "";

                    SwapRadio.Visibility = Visibility.Hidden;

                    RadioVolume.IsEnabled = true; // volume slider works even when radio is turned off.

                    ToggleButtons();
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
                    if (currentRadio.freqMode != RadioInformation.FreqMode.COCKPIT && currentRadio.modulation != RadioInformation.Modulation.DISABLED)
                    {
                        SwapRadio.Visibility = Visibility.Visible;  //makes swap radio button visible when radio is turned on - Dabble
                        if (!RadioFrequency.IsFocused)
                        {
                            RadioFrequency.Text =
                                (currentRadio.freq / MHz).ToString("0.000",
                                    CultureInfo.InvariantCulture); //make number UK / US style with decimals not commas!
                        }

                        if (!StandbyRadioFrequency.IsFocused)
                        {
                            StandbyRadioFrequency.Text =
                                (currentRadio.standbyfreq / MHz).ToString("0.000",
                                    CultureInfo.InvariantCulture); //make number UK / US style with decimals not commas!
                        }
                    }
                }
                
                RadioLabel.Text = dcsPlayerRadioInfo.radios[RadioId].name;

                int freqCount = _connectClientsSingleton.ClientsOnFreq(currentRadio.freq, currentRadio.modulation);
                int standbyCount =
                    _connectClientsSingleton.ClientsOnFreq(currentRadio.standbyfreq, currentRadio.modulation);

                RadioMetaData.Text = "👤" + freqCount;
                StandbyRadioMetaData.Text = "👤" + standbyCount;

                RadioVolume.IsEnabled = true;

                ToggleButtons();
                RadioEnabled.IsEnabled = currentRadio.freqMode == RadioInformation.FreqMode.OVERLAY;

                if (!_dragging)
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
                RadioEnabled.Background = RadioOn;
                RadioEnabled.Content = new TextBlock
                {
                    FontSize = 5,
                    Text = "On" ,
                };
            }
            else if (currentRadio != null && currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.DISABLED);
                RadioEnabled.Background = RadioOff;
                RadioEnabled.Content = new TextBlock
                {
                    FontSize = 5,
                    Text = "Off",
                };
            }
        }
        private void SwapStandbyFrequency_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(RadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double freq))
            {
                RadioHelper.UpdateRadioFrequency(freq, RadioId, false);
            }
            else
            {
                RadioFrequency.Text = "";
            }
            if (double.TryParse(StandbyRadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double standbyFreq))
            {
                RadioHelper.UpdateRadioFrequency(freq, RadioId, false);
            }
            else
            {
                StandbyRadioFrequency.Text = "";
            }
            
            RadioHelper.UpdateStandbyRadioFrequency(freq, RadioId, false);
            RadioHelper.UpdateRadioFrequency(standbyFreq, RadioId, false);
        }
    }
}