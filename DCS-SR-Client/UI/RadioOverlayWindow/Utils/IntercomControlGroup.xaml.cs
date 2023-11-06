using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json.Linq;

namespace Ciribob.DCS.SimpleRadio.Standalone.Overlay
{
    /// <summary>
    ///     Interaction logic for IntercomControlGroup.xaml
    /// </summary>
    public partial class IntercomControlGroup : UserControl
    {
        private bool _dragging;

        private bool _init = true;
        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        // Color for Vox Button
        public static Brush voxEnabled = Brushes.MediumSeaGreen;
        public static Brush voxDisabled = Brushes.IndianRed;
        public static Brush voxicDisabled = Brushes.Gray;

        public IntercomControlGroup()
        {
            InitializeComponent();

            Radio1Enabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXR1) ? voxEnabled : voxDisabled;
            IntercomEnabled.Background = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.VOXIC) ? voxEnabled : voxicDisabled;
            IntercomEnabled.IsEnabled = IntercomNumberSpinner.Value != 1;
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
                    _clientStateSingleton.DcsPlayerRadioInfo.selected = (short)RadioId;
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

                    clientRadio.volume = (float)RadioVolume.Value / 100.0f;
                }
            }

            _dragging = false;
        }

        internal void RepaintRadioStatus()
        {
            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo == null) || !dcsPlayerRadioInfo.IsCurrent())
            {
                RadioActive.Fill = new SolidColorBrush(Colors.Red);

                RadioVolume.IsEnabled = false;

                //reset dragging just incase
                _dragging = false;

                IntercomNumberSpinner.IsEnabled = false;
            }
            else
            {
                var currentRadio = dcsPlayerRadioInfo.radios[RadioId];
                var transmitting = _clientStateSingleton.RadioSendingState;
                var receiveState = _clientStateSingleton.RadioReceivingState[RadioId];
                if ((receiveState != null) && receiveState.IsReceiving)
                {
                    RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
                }
                else if (RadioId == dcsPlayerRadioInfo.selected || transmitting.IsSending && (transmitting.SendingOn == RadioId))
                {

                    if (transmitting.IsSending && (transmitting.SendingOn == RadioId))
                    {
                        RadioActive.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#96FF6D"));
                    }
                    else
                    {
                        RadioActive.Fill = new SolidColorBrush(Colors.Green);
                    }
                }
                else
                {
                    if (currentRadio.simul && dcsPlayerRadioInfo.simultaneousTransmission)
                    {
                        // if (transmitting.IsSending)
                        // {
                        //     RadioActive.Fill = new SolidColorBrush(Colors.LightBlue);
                        // }
                        // else
                        // {
                        RadioActive.Fill = new SolidColorBrush(Colors.DarkBlue);
                        // }

                    }
                    else
                    {
                        RadioActive.Fill = new SolidColorBrush(Colors.Orange);
                    }

                }

                if (currentRadio.modulation == RadioInformation.Modulation.INTERCOM) //intercom
                {
                    RadioLabel.Text = "VOX / INTERCOM";

                    Radio1Enabled.IsEnabled = true;
                    if (IntercomNumberSpinner.Value != 1)
                    {
                        IntercomEnabled.IsEnabled = true;
                    }
                    IntercomNumberSpinner.IsEnabled = true;

                    if (dcsPlayerRadioInfo.unitId >= DCSPlayerRadioInfo.UnitIdOffset)
                    {
                        IntercomNumberSpinner.IsEnabled = true;
                        IntercomNumberSpinner.Value = _clientStateSingleton.IntercomOffset;
                    }
                    else
                    {
                        IntercomNumberSpinner.IsEnabled = false;
                        IntercomNumberSpinner.Value = 1;
                        _clientStateSingleton.IntercomOffset = 1;
                    }
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
                    _clientStateSingleton.IntercomOffset = 1;

                    Radio1Enabled.Background = voxicDisabled;
                    IntercomEnabled.Background = voxicDisabled;
                }

                if (_dragging == false)
                {
                    RadioVolume.Value = currentRadio.volume * 100.0;
                }
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

            var dcsPlayerRadioInfo = _clientStateSingleton.DcsPlayerRadioInfo;

            if ((dcsPlayerRadioInfo != null) && dcsPlayerRadioInfo.IsCurrent() &&
                (dcsPlayerRadioInfo.unitId >= DCSPlayerRadioInfo.UnitIdOffset))
            {
                _clientStateSingleton.IntercomOffset = (int)IntercomNumberSpinner.Value;
                dcsPlayerRadioInfo.unitId =
                    (uint)(DCSPlayerRadioInfo.UnitIdOffset + _clientStateSingleton.IntercomOffset);
                _clientStateSingleton.LastSent = 0; //force refresh
            }
        }
    }
}