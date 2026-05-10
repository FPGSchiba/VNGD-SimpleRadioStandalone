using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;

namespace Vanguard.VCS.Client.UI.RadioOverlayWindow.Utils
{
    /// <summary>
    ///     Interaction logic for IntercomControlGroup3Horizontal.xaml
    /// </summary>
    public partial class IntercomControlGroup3Horizontal : UserControl
    {
        private bool _dragging;

        private bool _init = true;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        // Color for Vox Button
        public static Brush voxEnabled = Brushes.MediumSeaGreen;
        public static Brush voxDisabled = Brushes.IndianRed;
        public static Brush voxicDisabled = Brushes.Gray;

        public IntercomControlGroup3Horizontal()
        {
            InitializeComponent();

            Radio1Enabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1) ? voxEnabled : voxDisabled;
            IntercomEnabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC) ? voxEnabled : voxicDisabled;
            IntercomNumberSpinner.Maximum = 100;
            IntercomNumberSpinner.Minimum = 1;
            IntercomEnabled.IsEnabled = IntercomNumberSpinner.Value != 1;
        }

        public int RadioId { private get; set; }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
        }

        private void RadioVolume_DragStarted(object sender, RoutedEventArgs e)
        {
            _dragging = true;
        }


        private void RadioVolume_DragCompleted(object sender, RoutedEventArgs e)
        {
            _dragging = false;
        }

        internal void RepaintRadioStatus()
        {
            var transmitting = _clientStateSingleton.RadioSendingState;
            var receiveState = _clientStateSingleton.RadioReceivingState[RadioId];

            if ((receiveState != null) && receiveState.IsReceiving)
            {
                RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
            }
            else if (transmitting.IsSending && (transmitting.SendingOn == RadioId))
            {
                RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
            }
            else
            {
                RadioActive.Fill = new SolidColorBrush(Colors.Orange);
            }

            RadioLabel.Text = "VOX / INTERCOM";
            Radio1Enabled.IsEnabled = true;
            IntercomNumberSpinner.IsEnabled = true;
        }

        private void VoxR1Enabled_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXR1, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1));

            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1))
            {
                Radio1Enabled.Background = voxEnabled;
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXIC, false);
                    IntercomEnabled.Background = voxDisabled;
                }
            }
            else
            {
                Radio1Enabled.Background = voxDisabled;
            }
        }

        private void VoxICEnabled_OnClick(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXIC, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC));

            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
            {
                IntercomEnabled.Background = voxEnabled;
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1))
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXR1, false);
                    Radio1Enabled.Background = voxDisabled;
                }
            }
            else
            {
                IntercomEnabled.Background = voxDisabled;
            }
        }

        private void IntercomNumber_SpinnerChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_init)
            {
                //ignore
                _init = false;
                return;
            }

            int spinnervalue;
            if (!int.TryParse(IntercomNumberSpinner.Value.ToString(), out spinnervalue))
            {
                return;
            }
            
            if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXIC, !_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC));
                IntercomEnabled.Background = voxDisabled;
            }


            if (spinnervalue == 1)
            {
                IntercomEnabled.IsEnabled = false;
                IntercomEnabled.Background = voxicDisabled;
            }
            else
            {
                IntercomEnabled.IsEnabled = true;
                if (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC))
                {
                    IntercomEnabled.Background = voxEnabled;
                }
                else
                {
                    IntercomEnabled.Background = voxDisabled;
                }
            }

        }
    }
}