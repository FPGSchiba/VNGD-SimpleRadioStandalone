using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;

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
            ModifierBinding = (InputBinding)((int)ControlInputBinding)+100; //add 100 gets the enum of the modifier
          

            if (InputDeviceManager.InputConfig.InputDevices != null)
            {
                if (InputDeviceManager.InputConfig.InputDevices[ControlInputBinding] != null)
                {
                    var button = InputDeviceManager.InputConfig.InputDevices[ ControlInputBinding].Button;
                    DeviceText.Text = button < 128 ? button.ToString() : "POV " + (button - 127); //output POV info
                    Device.Text = InputDeviceManager.InputConfig.InputDevices[ControlInputBinding].DeviceName;
                }

                if (InputDeviceManager.InputConfig.InputDevices[ModifierBinding] != null)
                {
                    var button = InputDeviceManager.InputConfig.InputDevices[ ModifierBinding].Button;
                    ModifierText.Text = button < 128 ? button.ToString() : "POV " + (button - 127); //output POV info
                    ModifierDevice.Text = InputDeviceManager.InputConfig.InputDevices[ModifierBinding].DeviceName;
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

                Device.Text = device.DeviceName;
                DeviceText.Text = device.Button < 128 ? device.Button.ToString() : "POV " + (device.Button - 127);
                //output POV info;

                device.InputBind = ControlInputBinding;

                InputDeviceManager.InputConfig.InputDevices[ControlInputBinding] = device;
                InputDeviceManager.InputConfig.WriteInputRegistry(ControlInputBinding, device);
            });
        }


        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            InputDeviceManager.InputConfig.ClearInputRegistry(ControlInputBinding);
            InputDeviceManager.InputConfig.InputDevices[ControlInputBinding] = null;

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
                ModifierText.Text = device.Button < 128 ? device.Button.ToString() : "POV " + (device.Button - 127);
                //output POV info;

                device.InputBind = ModifierBinding;

                InputDeviceManager.InputConfig.InputDevices[ModifierBinding] = device;
                InputDeviceManager.InputConfig.WriteInputRegistry(ModifierBinding, device);
            });
        }


        private void ModifierClear_Click(object sender, RoutedEventArgs e)
        {
            InputDeviceManager.InputConfig.ClearInputRegistry(ModifierBinding);
            InputDeviceManager.InputConfig.InputDevices[ModifierBinding] = null;

            ModifierDevice.Text = "None";
            ModifierText.Text = "None";
        }
    }
}