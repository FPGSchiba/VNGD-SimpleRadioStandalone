using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using SharpDX.DirectInput;

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
                    var button = devices[ControlInputBinding].Button;
                    DeviceText.Text =
                        GetDeviceText(button, devices[ControlInputBinding].DeviceName);
                    Device.Text = GetDeviceName(devices[ControlInputBinding].DeviceName);
                }
                else
                {
                    DeviceText.Text = "None";
                    Device.Text = "None";
                }

                if (currentInputProfile.ContainsKey(ModifierBinding))
                {
                    var button = devices[ModifierBinding].Button;
                    ModifierText.Text =
                        GetDeviceText(button, devices[ModifierBinding].DeviceName);
                    ModifierDevice.Text = GetDeviceName(devices[ModifierBinding].DeviceName);
                }
                else
                {
                    ModifierText.Text = "None";
                    ModifierDevice.Text = "None";
                }
            }
        }

        private void Device_Click(object sender, RoutedEventArgs e)
        {
            DeviceClear.IsEnabled = false;
            DeviceButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                DeviceClear.IsEnabled = true;
                DeviceButton.IsEnabled = true;

                Device.Text = GetDeviceName(device.DeviceName);
                DeviceText.Text = GetDeviceText(device.Button, device.DeviceName);

                device.InputBind = ControlInputBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private string GetDeviceName(string name)
        {
            //fix crazy long WINWING names
            if (name.Length > 30)
            {
                return name.Trim().Substring(0, 30);
            }

            return name;
        }

        private string GetDeviceText(int button, string name)
        {
            if (name.ToLowerInvariant() == "keyboard")
            {
                try
                {
                    var key = (Key)button;
                    return key.ToString();
                }
                catch { }

            }
            return button < 128 ? (button + 1).ToString() : "POV " + (button - 127);
        }

        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ControlInputBinding);

            Device.Text = "None";
            DeviceText.Text = "None";
        }

        private void Modifier_Click(object sender, RoutedEventArgs e)
        {
            ModifierButtonClear.IsEnabled = false;
            ModifierButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                ModifierButtonClear.IsEnabled = true;
                ModifierButton.IsEnabled = true;

                ModifierDevice.Text = device.DeviceName;
                ModifierText.Text = GetDeviceText(device.Button, device.DeviceName);
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