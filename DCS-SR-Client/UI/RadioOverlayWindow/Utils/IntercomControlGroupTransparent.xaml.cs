using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;

namespace Vanguard.VCS.Client.UI.RadioOverlayWindow.Utils
{
    /// <summary>
    ///     Interaction logic for IntercomControlGroupTransparency.xaml
    /// </summary>
    public partial class IntercomControlGroupTransparent : UserControl
    {
        private bool _dragging;

        private bool _init = true;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        // Color for Vox Button
        public static Brush voxEnabled = Brushes.MediumSeaGreen;
        public static Brush voxDisabled = Brushes.IndianRed;
        public static Brush voxicDisabled = Brushes.Gray;

        public IntercomControlGroupTransparent()
        {
            InitializeComponent();

            Radio1Enabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1) ? voxEnabled : voxDisabled;
            IntercomEnabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC) ? voxEnabled : voxicDisabled;
            IntercomNumberSpinner.Maximum = 10;
            IntercomNumberSpinner.Minimum = 1;
            IntercomEnabled.IsEnabled = IntercomNumberSpinner.Value != 1;
        }

        public int RadioId { private get; set; }

        private void RadioSelectSwitch(object sender, RoutedEventArgs e)
        {
            // No DCS radio selection in new model
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
            var radios = _clientStateSingleton.CurrentRadioState?.Radios;

            if (radios == null || RadioId >= radios.Count)
            {
                RadioActive.Fill = new SolidColorBrush(Colors.Red);
                RadioVolume.IsEnabled = false;
                _dragging = false;
                IntercomNumberSpinner.IsEnabled = false;
                return;
            }

            var currentRadio = radios[RadioId];
            var transmitting = _clientStateSingleton.RadioSendingState;
            var receiveState = _clientStateSingleton.RadioReceivingState[RadioId];

            if ((receiveState != null) && receiveState.IsReceiving)
            {
                RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
            }
            else if (RadioId == transmitting.SendingOn && transmitting.IsSending)
            {
                RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
            }
            else
            {
                RadioActive.Fill = new SolidColorBrush(Colors.Orange);
            }

            if (currentRadio.IsIntercom)
            {
                //Dabble removing this functionality
                //RadioLabel.Text = "VOX / INTERCOM";

                Radio1Enabled.IsEnabled = true;
                if (IntercomNumberSpinner.Value != 1)
                {
                    IntercomEnabled.IsEnabled = true;
                }
                IntercomNumberSpinner.IsEnabled = true;
                IntercomNumberSpinner.Value = 1;
            }
            else
            {
                RadioLabel.Text = "NO VOX / INTERCOM";
                RadioActive.Fill = new SolidColorBrush(Colors.Red);
                Radio1Enabled.IsEnabled = false;
                IntercomEnabled.IsEnabled = false;
                RadioVolume.IsEnabled = false;
                IntercomNumberSpinner.Value = 1;
                IntercomNumberSpinner.IsEnabled = false;

                Radio1Enabled.Background = voxicDisabled;
                IntercomEnabled.Background = voxicDisabled;
            }
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
