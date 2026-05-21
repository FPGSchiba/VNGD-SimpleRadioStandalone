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
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using Vanguard.VCS.Client.UI.RadioOverlayWindow.PresetChannels;
using Vanguard.VCS.Client.Utils;
using Vanguard.VCS.Common.DCSState;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace Vanguard.VCS.Client.UI.AwacsRadioOverlayWindow
{
    /// <summary>
    ///     Interaction logic for RadioControlGroup.xaml
    /// </summary>
    public partial class RadioControlGroup : UserControl
    {
        private const double MHz = 1000000;
        private const int MaxSimultaneousTransmissions = 1;
        private bool _dragging;
        private int _actualRadioIndex = -1;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private IDisposable _radioStateSub;
        private readonly ConnectedClientsSingleton _connectClientsSingleton = ConnectedClientsSingleton.Instance;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static Brush radioOn = (Brush)new BrushConverter().ConvertFromString("#666");
        private static Brush radioOff = Brushes.IndianRed;
        private GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        public PresetChannelsViewModel ChannelViewModel { get; set; }


        public RadioControlGroup()
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
            private get { return _radioId; }
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
            RadioVolume.Focus();
            Keyboard.ClearFocus();
        }

        private void RadioFrequencyOnKeyDown(object sender, KeyEventArgs keyEventArgs)
        {
            if (keyEventArgs.Key == Key.Enter)
            {
                RadioVolume.Focus();
                Keyboard.ClearFocus();
            }
        }

        private void RadioFrequencyOnLostFocus(object sender, RoutedEventArgs routedEventArgs)
        {
            double freq = 0;
            // Some locales/cultures (e.g. German) do not parse "." as decimal points since they use decimal commas ("123,45"), leading to "123.45" being parsed as "12345" and frequencies being set too high
            // Using an invariant culture makes sure the decimal point is parsed properly for all locales - replacing any commas makes sure people entering numbers in a weird format still get correct results
            if (double.TryParse(RadioFrequency.Text.Replace(',', '.').Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out freq))
            {
                RadioHelper.UpdateRadioFrequency(freq, _actualRadioIndex + 1, false);
            }
            else
            {
                RadioFrequency.Text = "";
            }
        }

        private void Up0001_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(0.001, _actualRadioIndex + 1); }
        private void Up001_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(0.01, _actualRadioIndex + 1); }
        private void Up01_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(0.1, _actualRadioIndex + 1); }
        private void Up1_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(1, _actualRadioIndex + 1); }
        private void Up10_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(10, _actualRadioIndex + 1); }
        private void Down10_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(-10, _actualRadioIndex + 1); }
        private void Down1_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(-1, _actualRadioIndex + 1); }
        private void Down01_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(-0.1, _actualRadioIndex + 1); }
        private void Down001_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(-0.01, _actualRadioIndex + 1); }
        private void Down0001_Click(object sender, RoutedEventArgs e) { RadioHelper.UpdateRadioFrequency(-0.001, _actualRadioIndex + 1); }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e) { RadioHelper.SelectRadio(_actualRadioIndex); }

        private void RadioFrequencyText_Click(object sender, MouseButtonEventArgs e) { RadioHelper.SelectRadio(_actualRadioIndex); }

        private void RadioFrequencyText_RightClick(object sender, MouseButtonEventArgs e) { RadioHelper.ToggleGuard(_actualRadioIndex + 1); }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e) { _dragging = true; }

        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e) { _dragging = false; }

        private void ToggleButtons(bool enable)
        {
            if (_clientStateSingleton.IsConnected)
            {
                RadioEnabled.Background = enable ? radioOn : radioOff;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = enable ? "On" : "Off" };
            }
            else
            {
                RadioEnabled.Background = radioOff;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = "Off" };
            }

            if (enable)
            {
                Up10.Visibility = Visibility.Visible;
                Up1.Visibility = Visibility.Visible;
                Up01.Visibility = Visibility.Visible;
                Up001.Visibility = Visibility.Visible;
                Up0001.Visibility = Visibility.Visible;
                Down10.Visibility = Visibility.Visible;
                Down1.Visibility = Visibility.Visible;
                Down01.Visibility = Visibility.Visible;
                Down001.Visibility = Visibility.Visible;
                Down0001.Visibility = Visibility.Visible;

                Up10.IsEnabled = true;
                Up1.IsEnabled = true;
                Up01.IsEnabled = true;
                Up001.IsEnabled = true;
                Up0001.IsEnabled = true;
                Down10.IsEnabled = true;
                Down1.IsEnabled = true;
                Down01.IsEnabled = true;
                Down001.IsEnabled = true;
                Down0001.IsEnabled = true;

                PresetChannelsView.IsEnabled = true;
                ChannelTab.Visibility = Visibility.Visible;
            }
            else
            {
                Up10.Visibility = Visibility.Hidden;
                Up1.Visibility = Visibility.Hidden;
                Up01.Visibility = Visibility.Hidden;
                Up001.Visibility = Visibility.Hidden;
                Up0001.Visibility = Visibility.Hidden;
                Down10.Visibility = Visibility.Hidden;
                Down1.Visibility = Visibility.Hidden;
                Down01.Visibility = Visibility.Hidden;
                Down001.Visibility = Visibility.Hidden;
                Down0001.Visibility = Visibility.Hidden;

                ChannelTab.Visibility = Visibility.Collapsed;
            }
        }

        internal void RepaintRadioStatus()
        {
            SetupEncryption();

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
                TabItem item = TabControl.SelectedItem as TabItem;
                if (item?.Visibility != Visibility.Visible)
                    TabControl.SelectedIndex = 0;
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
                TabItem noRadioItem = TabControl.SelectedItem as TabItem;
                if (noRadioItem?.Visibility != Visibility.Visible)
                    TabControl.SelectedIndex = 0;
                return;
            }
            _actualRadioIndex = actualIndex;

            var transmitting = _clientStateSingleton.RadioSendingState;
            RadioActive.Fill = transmitting.IsSending && transmitting.SendingOn == actualIndex + 1
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"))
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

            TabItem tabItem = TabControl.SelectedItem as TabItem;
            if (tabItem?.Visibility != Visibility.Visible)
                TabControl.SelectedIndex = 0;
        }

        private void SetupEncryption()
        {
            EncryptionKeySpinner.IsEnabled = false;
            EncryptionButton.IsEnabled = false;
            EncryptionButton.Visibility = Visibility.Hidden;
            EncryptionButton.Content = "Enable";
            EncryptionTab.Visibility = Visibility.Collapsed;
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
                RadioFrequency.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF00"));
                RadioMetaData.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF00"));
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

        private void Encryption_ButtonClick(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioHelper.GetRadio(_actualRadioIndex + 1);
            if (currentRadio != null && currentRadio.modulation != RadioInformation.Modulation.DISABLED)
            {
                if (currentRadio.encMode == RadioInformation.EncryptionMode.ENCRYPTION_JUST_OVERLAY)
                {
                    RadioHelper.ToggleEncryption(_actualRadioIndex + 1);
                    EncryptionButton.Content = currentRadio.enc ? "Enable" : "Disable";
                }
            }
        }

        private void EncryptionKeySpinner_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (EncryptionKeySpinner?.Value != null)
                RadioHelper.SetEncryptionKey(_actualRadioIndex + 1, (byte)EncryptionKeySpinner.Value);
        }

        private void SetRadioEnabled(bool enabled)
        {
            var currentRadio = RadioHelper.GetRadio(_actualRadioIndex + 1);

            if (enabled && currentRadio != null)
            {
                RadioHelper.SetRadioModulation(_actualRadioIndex + 1, RadioInformation.Modulation.AM);
                RadioEnabled.Background = radioOn;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = "On" };
            }
            else if (currentRadio != null)
            {
                RadioHelper.SetRadioModulation(_actualRadioIndex + 1, RadioInformation.Modulation.DISABLED);
                RadioEnabled.Background = radioOff;
                RadioEnabled.Content = new TextBlock { FontSize = 5, Text = "Off" };
            }
            else
            {
                Logger.Warn($"Radio with ID: {RadioId} was not found and could not be toggled.");
                return;
            }

            switch (RadioId)
            {
                case 1: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio1Enabled, enabled); break;
                case 2: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio2Enabled, enabled); break;
                case 3: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio3Enabled, enabled); break;
                case 4: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio4Enabled, enabled); break;
                case 5: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio5Enabled, enabled); break;
                case 6: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio6Enabled, enabled); break;
                case 7: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio7Enabled, enabled); break;
                case 8: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio8Enabled, enabled); break;
                case 9: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio9Enabled, enabled); break;
                case 10: _globalSettings.SetClientSetting(GlobalSettingsKeys.Radio10Enabled, enabled); break;
            }
            RepaintRadioStatus();
        }

        private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
        {
            var currentRadio = RadioHelper.GetRadio(_actualRadioIndex + 1);
            if (currentRadio == null) return;
            if (currentRadio.modulation == RadioInformation.Modulation.DISABLED)
                SetRadioEnabled(true);
            else
                SetRadioEnabled(false);
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
