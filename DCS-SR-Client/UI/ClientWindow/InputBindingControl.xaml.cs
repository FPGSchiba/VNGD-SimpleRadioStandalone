using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for InputBindingControl.xaml
    /// </summary>
    public partial class InputBindingControl : UserControl
    {
        private InputDeviceManager _inputDeviceManager;

        public InputBindingControl()
        {
            InitializeComponent();
        }

        public InputDeviceManager InputDeviceManager
        {
            get { return _inputDeviceManager; }
            set
            {
                _inputDeviceManager = value;
                LoadInputSettings();
            }
        }

        public InputBinding ControlInputBinding { get; set; }
        public InputBinding ModifierBinding { get; set; }
        public string InputName { get; set; }

        public void LoadInputSettings()
        {
            DeviceLabel.Content = InputName;
            ModifierLabel.Content = InputName + " Modifier";
            ModifierBinding = (InputBinding)((int)ControlInputBinding) + 100; //add 100 gets the enum of the modifier

            var currentInputProfile = GlobalSettingsStore.Instance.ProfileSettingsStore.GetCurrentInputProfile();

            if (currentInputProfile != null)
            {
                var devices = currentInputProfile;
                if (currentInputProfile.ContainsKey(ControlInputBinding))
                {
                    if (devices[ControlInputBinding].IsAxis)
                    {
                        DeviceText.Text = devices[ControlInputBinding].Axis + " Axis";
                    }
                    else
                    {
                        var button = devices[ControlInputBinding].Button;
                        DeviceText.Text = button < 128 ? (button + 1).ToString() : "POV " + (button - 127); //output POV info}
                    }
                    Device.Text = devices[ControlInputBinding].DeviceName;
                }
                else
                {
                    DeviceText.Text = "None";
                    Device.Text = "None";
                }

                if (currentInputProfile.ContainsKey(ModifierBinding))
                {
                    if (devices[ControlInputBinding].IsAxis)
                    {
                        ModifierText.Text = devices[ControlInputBinding].Axis + " Axis";
                    }
                    else
                    {
                        var button = devices[ModifierBinding].Button;
                        ModifierText.Text = button < 128 ? (button + 1).ToString() : "POV " + (button - 127); //output POV info
                    }

                    ModifierDevice.Text = devices[ModifierBinding].DeviceName;
                }
                else
                {
                    ModifierText.Text = "None";
                    ModifierDevice.Text = "None";
                }
            }
        }

        private void DeviceButton_Click(object sender, RoutedEventArgs e)
        {
            DeviceClear.IsEnabled = false;
            DeviceButton.IsEnabled = false;
            DeviceAxis.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                DeviceClear.IsEnabled = true;
                DeviceButton.IsEnabled = true;
                DeviceAxis.IsEnabled=true;

                Device.Text = device.DeviceName;
                DeviceText.Text = device.Button < 128 ? (device.Button + 1).ToString() : "POV " + (device.Button - 127);
                //output POV info;

                device.InputBind = ControlInputBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private void DeviceAxis_Click(object sender, RoutedEventArgs e)
        {
            DeviceClear.IsEnabled = false;
            DeviceButton.IsEnabled = false;
            DeviceAxis.IsEnabled = false;

            InputDeviceManager.AssignAxis(device =>
            {
                DeviceClear.IsEnabled = true;
                DeviceButton.IsEnabled= true;
                DeviceAxis.IsEnabled = true;

                Device.Text = device.DeviceName;
                DeviceText.Text = device.Axis + " Axis";

                device.InputBind = ControlInputBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ControlInputBinding);

            Device.Text = "None";
            DeviceText.Text = "None";
        }
        private void ModifierButton_Click(object sender, RoutedEventArgs e)
        {
            ModifierButtonClear.IsEnabled = false;
            ModifierButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                ModifierButtonClear.IsEnabled = true;
                ModifierButton.IsEnabled = true;

                ModifierDevice.Text = device.DeviceName;
                ModifierText.Text = device.Button < 128 ? (device.Button + 1).ToString() : "POV " + (device.Button - 127);
                //output POV info;

                device.InputBind = ModifierBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }


        private void ModifierAxis_Click(object sender, RoutedEventArgs e)
        {
            ModifierButtonClear.IsEnabled = false;
            ModifierButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                ModifierButtonClear.IsEnabled = true;
                ModifierButton.IsEnabled = true;

                ModifierDevice.Text = device.DeviceName;
                ModifierText.Text = device.Axis + " Axis";

                device.InputBind = ModifierBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private void ModifierClear_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ModifierBinding);
            ModifierDevice.Text = "None";
            ModifierText.Text = "None";
        }
    }
}