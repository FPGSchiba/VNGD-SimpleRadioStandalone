using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using NLog;
using SharpDX.DirectInput;
namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.Components
{
    public partial class KeybindingControl : UserControl
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private InputDeviceManager _inputDeviceManager;

        public KeybindingControl()
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
            InputNameText.Content = InputName;
            ModifierBinding = (InputBinding)((int)ControlInputBinding) + 100; //add 100 gets the enum of the modifier

            var currentInputProfile = GlobalSettingsStore.Instance.ProfileSettingsStore.GetCurrentInputProfile();
            
            if (currentInputProfile != null)
            {
                var devices = currentInputProfile;
                if (currentInputProfile.ContainsKey(ControlInputBinding))
                {
                    var button = devices[ControlInputBinding].Button;
                    PrimaryButton.Content = $"{GetDeviceText(button, devices[ControlInputBinding].DeviceName)} ({GetDeviceName(devices[ControlInputBinding].DeviceName)})";
                }
                else
                {
                    PrimaryButton.Content = "None (None)";
                }

                if (currentInputProfile.ContainsKey(ModifierBinding))
                {
                    var button = devices[ModifierBinding].Button;
                    ModifierButton.Content = $"{GetDeviceText(button, devices[ModifierBinding].DeviceName)} ({GetDeviceName(devices[ModifierBinding].DeviceName)})";
                }
                else
                {
                    ModifierButton.Content = "None (None)";
                }
            }
        }

        private void Device_Click(object sender, RoutedEventArgs e)
        {
            PrimaryButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                PrimaryButton.IsEnabled = true;
                
                if ((Key)device.Button == Key.Escape)
                {
                    DeviceClear_Click(sender, e);
                    return;
                }
                
                PrimaryButton.Content = $"{GetDeviceText(device.Button, device.DeviceName)} ({GetDeviceName(device.DeviceName)})";

                device.InputBind = ControlInputBinding;
                _logger.Debug($"Setting Input binding for device: {device.DeviceName}-{device.Button} to input bind: {device.InputBind}");

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
            else if (name.ToLowerInvariant() == "xinputcontroller")
            {
                try
                {
                    var buttonFlag = (SharpDX.XInput.GamepadButtonFlags)button;
                    return buttonFlag.ToString("G");
                }
                catch { }
            }
            return button < 128 ? (button + 1).ToString() : "POV " + (button - 127);
        }

        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ControlInputBinding);

            PrimaryButton.Content = "None (None)";
        }

        private void Modifier_Click(object sender, RoutedEventArgs e)
        {
            ModifierButton.IsEnabled = false;

            InputDeviceManager.AssignButton(device =>
            {
                ModifierButton.IsEnabled = true;
                
                if ((Key)device.Button == Key.Escape)
                {
                    ModifierClear_Click(sender, e);
                    return;
                }
                
                ModifierButton.Content = $"{GetDeviceText(device.Button, device.DeviceName)} ({GetDeviceName(device.DeviceName)})";
                device.InputBind = ModifierBinding;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetControlSetting(device);
            });
        }

        private void ModifierClear_Click(object sender, RoutedEventArgs e)
        {
            // TODO: A method to call this
            GlobalSettingsStore.Instance.ProfileSettingsStore.RemoveControlSetting(ModifierBinding);
            ModifierButton.Content = "None (None)";
        }
    }
}