using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for InputBindingControl.xaml
    /// </summary>
    public partial class InputBindingControl : UserControl
    {
        private InputDeviceManager _inputDeviceManager;
        public InputDeviceManager InputDeviceManager { get {

                return _inputDeviceManager;
            }
            set
            {
                _inputDeviceManager = value;
                LoadInputSettings();
            }
        }
        public InputBinding ControlInputBinding { get; set; }
        public InputBinding ModifierBinding { get; set; }
        public string InputName { get; set; }

        public InputBindingControl()
        {
           
            InitializeComponent();

          

         //   LoadInputSettings();

        }

        public void LoadInputSettings()
        {

            DeviceLabel.Content = InputName;
            ModifierLabel.Content = InputName + " Modifier";

            if (ControlInputBinding == InputBinding.Ptt)
            {
                ModifierBinding = InputBinding.ModifierPtt;
            }
            else if (ControlInputBinding == InputBinding.Switch1)
            {
                ModifierBinding = InputBinding.ModifierSwitch1;
            }
            else if (ControlInputBinding == InputBinding.Switch2)
            {
                ModifierBinding = InputBinding.ModifierSwitch2;
            }
            else if (ControlInputBinding == InputBinding.Switch3)
            {
                ModifierBinding = InputBinding.ModifierSwitch3;
            }

            if (InputDeviceManager.InputConfig.InputDevices != null)
            {
                if (InputDeviceManager.InputConfig.InputDevices[(int)ControlInputBinding] != null)
                {
                    DeviceText.Text = InputDeviceManager.InputConfig.InputDevices[(int)ControlInputBinding].Button.ToString();
                    Device.Text = InputDeviceManager.InputConfig.InputDevices[(int)ControlInputBinding].DeviceName;
                }

                if (InputDeviceManager.InputConfig.InputDevices[(int)ModifierBinding] != null)
                {
                    ModifierText.Text = InputDeviceManager.InputConfig.InputDevices[(int)ModifierBinding].Button.ToString();
                    ModifierDevice.Text = InputDeviceManager.InputConfig.InputDevices[(int)ModifierBinding].DeviceName;
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
                DeviceText.Text = device.Button.ToString();

                device.InputBind = ControlInputBinding;

                InputDeviceManager.InputConfig.InputDevices[(int)ControlInputBinding] = device;
                InputDeviceManager.InputConfig.WriteInputRegistry(ControlInputBinding, device);
            });
        }

   

        private void DeviceClear_Click(object sender, RoutedEventArgs e)
        {
            InputDeviceManager.InputConfig.ClearInputRegistry(ControlInputBinding);
            InputDeviceManager.InputConfig.InputDevices[(int)ControlInputBinding] = null;

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
                ModifierText.Text = device.Button.ToString();

                device.InputBind = ModifierBinding;

                InputDeviceManager.InputConfig.InputDevices[(int)ModifierBinding] = device;
                InputDeviceManager.InputConfig.WriteInputRegistry(ModifierBinding, device);
            });
        }



        private void ModifierClear_Click(object sender, RoutedEventArgs e)
        {
            InputDeviceManager.InputConfig.ClearInputRegistry(ModifierBinding);
            InputDeviceManager.InputConfig.InputDevices[(int)ModifierBinding] = null;

            ModifierDevice.Text = "None";
            ModifierText.Text = "None";
        }


    }
}
