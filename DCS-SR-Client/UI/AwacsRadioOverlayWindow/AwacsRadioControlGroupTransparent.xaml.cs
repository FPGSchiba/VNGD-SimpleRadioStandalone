using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NLog;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network.Models;
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
        private int _actualRadioIndex = -1;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private IDisposable _radioStateSub;
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
            _radioStateSub = App.EventBus.Subscribe<LocalRadioStateChangedEvent>(_ =>
                Dispatcher.Invoke(RepaintRadioStatus));
            Unloaded += (_, _) => _radioStateSub?.Dispose();

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
                this.RadioVolume.Focus();
                Keyboard.ClearFocus();
            }
        }

        private void RadioFrequencyOnLostFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            // Some locales/cultures (e.g. German) do not parse "." as decimal points since they use decimal commas ("123,45"), leading to "123.45" being parsed as "12345" and frequencies being set too high
            // Using an invariant culture makes sure the decimal point is parsed properly for all locales - replacing any commas makes sure people entering numbers in a weird format still get correct results
            if (double.TryParse(RadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double freq))
            {
                RadioHelper.UpdateRadioFrequency(freq, _actualRadioIndex + 1, false);
            }
            else
            {
                RadioFrequency.Text = "";
            }
        }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e) { RadioHelper.SelectRadio(_actualRadioIndex); }

        private void RadioFrequencyText_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
                Logger.Info("MouseWheel radio frequency Up");
            else if (e.Delta < 0)
                Logger.Info("MouseWheel radio frequency Down");
            e.Handled = true;
        }

        private void RadioFrequencyText_Click(object sender, MouseButtonEventArgs e) { RadioHelper.SelectRadio(_actualRadioIndex); }

        private void RadioFrequencyText_RightClick(object sender, MouseButtonEventArgs e) { RadioHelper.ToggleGuard(_actualRadioIndex + 1); }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e) { _dragging = true; }

        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e) { _dragging = false; }

        private void ToggleButtons(bool enable)
        {
            if (_clientStateSingleton.IsConnected)
            {
                RadioEnabled.Background = enable ? RadioOn : RadioOff;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = enable ? "On" : "Off" };
            }
            else
            {
                RadioEnabled.Background = RadioOff;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = "Off" };
            }
        }

        internal void RepaintRadioStatus()
        {
            var radios = App.RadioStateManager?.CurrentState?.Radios;

            if (!_clientStateSingleton.IsConnected || radios == null || RadioId < 1)
            {
                _actualRadioIndex = -1;
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

            var (actualIndex, radio) = FindNthNonIntercomRadio(radios, RadioId);
            if (actualIndex < 0)
            {
                _actualRadioIndex = -1;
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
            _actualRadioIndex = actualIndex;

            var transmitting = _clientStateSingleton.RadioSendingState;
            RadioActive.Fill = transmitting.IsSending && transmitting.SendingOn == actualIndex + 1
                ? (Brush)new BrushConverter().ConvertFromString("#96FF6D")
                : new SolidColorBrush(Colors.Orange);

            RadioLabel.Text = radio.Name;
            RadioMetaData.Text = "";

            if (!RadioFrequency.IsFocused)
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

            var receiveState = _actualRadioIndex >= 0
                ? _clientStateSingleton.RadioReceivingState[_actualRadioIndex]
                : null;

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
            var currentRadio = RadioHelper.GetRadio(_actualRadioIndex + 1);
            if (currentRadio == null) return;
            if (currentRadio.modulation == RadioInformation.Modulation.DISABLED)
                RadioHelper.SetRadioModulation(_actualRadioIndex + 1, RadioInformation.Modulation.AM);
            else
                RadioHelper.SetRadioModulation(_actualRadioIndex + 1, RadioInformation.Modulation.DISABLED);
        }

        private static (int Index, ClientRadio Radio) FindNthNonIntercomRadio(IReadOnlyList<ClientRadio> radios, int n)
        {
            int count = 0;
            for (int i = 0; i < radios.Count; i++)
            {
                if (!radios[i].IsIntercom)
                {
                    count++;
                    if (count == n)
                        return (i, radios[i]);
                }
            }
            return (-1, null);
        }
    }
}
