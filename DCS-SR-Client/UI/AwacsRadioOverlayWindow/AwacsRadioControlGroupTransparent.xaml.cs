using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NLog;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Client.UI.RadioOverlayWindow.PresetChannels;
using Vanguard.VCS.Client.Utils;
using Vanguard.VCS.Common.DCSState;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Vanguard.VCS.Client.UI.AwacsRadioOverlayWindow
{
    /// <summary>
    ///     Interaction logic for RadioControlGroup.xaml
    /// </summary>
    public partial class RadioControlGroupTransparent
    {
        private const double MHz = 1000000;
        private bool _dragging;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly ConnectedClientsSingleton _connectClientsSingleton = ConnectedClientsSingleton.Instance;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly Brush RadioOn = (Brush)new BrushConverter().ConvertFromString("#666");
        private static readonly Brush RadioOff = Brushes.IndianRed;
        private static readonly Brush GreenForeground = (Brush)new BrushConverter().ConvertFromString("#0F0");
        
        public bool IsRadioEnabled
        {
            get => this.RadioEnabled.Background == RadioOn;
        }
        
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

            var bindingExpression = PresetChannelsView.GetBindingExpression(DataContextProperty);
            bindingExpression?.UpdateTarget();
        }


        private void RadioFrequencyOnGotFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            this.RadioVolume.Focus();
            Keyboard.ClearFocus();
        }

        private void RadioFrequencyOnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs.Key == Key.Enter)
            {
                //remove focus to somewhere else
                this.RadioVolume.Focus();
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

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            RadioHelper.SelectRadio(RadioId);
        }

        private void RadioFrequencyText_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // TODO functionality to use scroll wheel to change frequency using scroll wheel
            if (e.Delta > 0)
            {
                Logger.Info("MouseWheel radio frequency Up");
            }
            else if (e.Delta < 0)
            {
                Logger.Info("MouseWheel radio frequency Down");
            }
            e.Handled = true;
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
            _dragging = false;
        }

        private void ToggleButtons(bool enable)
        {
            if (_clientStateSingleton.IsConnected)
            {
                RadioEnabled.Background = enable ? RadioOn : RadioOff;
                RadioEnabled.Content = new TextBlock
                {
                    FontSize = 5,
                    Text = enable ? "On" : "Off",
                };
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
            var radios = _clientStateSingleton.CurrentRadioState?.Radios;

            if (!_clientStateSingleton.IsConnected || radios == null || RadioId < 1 || RadioId > radios.Count)
            {
                RadioActive.Fill = new SolidColorBrush(Colors.Red);
                RadioLabel.Text = "No Radio";
                RadioFrequency.Text = "No Conn";
                RadioMetaData.Text = "";
                RadioVolume.IsEnabled = false;
                ToggleButtons(false);
                RadioEnabled.IsEnabled = false;
                _dragging = false;
                return;
            }

            var radio = radios[RadioId - 1];
            var transmitting = _clientStateSingleton.RadioSendingState;
            RadioActive.Fill = transmitting.IsSending && transmitting.SendingOn == RadioId
                ? (Brush)new BrushConverter().ConvertFromString("#96FF6D")
                : new SolidColorBrush(Colors.Orange);

            RadioLabel.Text = radio.Name;
            RadioMetaData.Text = "";

            if (radio.IsIntercom)
            {
                RadioFrequency.Text = "INTERCOM";
            }
            else if (!RadioFrequency.IsFocused)
            {
                RadioFrequency.Text = (radio.FrequencyHz / MHz).ToString("0.000", CultureInfo.InvariantCulture);
            }

            RadioVolume.IsEnabled = radio.Enabled;
            ToggleButtons(radio.Enabled);
            RadioEnabled.IsEnabled = true;
        }

        internal void RepaintRadioReceive()
        {
            TransmitterName.Visibility = Visibility.Collapsed;
            RadioFrequency.Visibility = Visibility.Visible;
            RadioMetaData.Visibility = Visibility.Visible;

            var receiveState = _clientStateSingleton.RadioReceivingState[RadioId];

            if (receiveState == null || !receiveState.IsReceiving)
            {
                RadioFrequency.Foreground = GreenForeground;
                RadioMetaData.Foreground = GreenForeground;
            }
            else
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
        }

        private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioHelper.GetRadio(RadioId);
            if (currentRadio == null) return;
            if (currentRadio.modulation == RadioInformation.Modulation.DISABLED)
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.AM);
            else
                RadioHelper.SetRadioModulation(RadioId, RadioInformation.Modulation.DISABLED);
        }
    }
}