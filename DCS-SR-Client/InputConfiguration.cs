using System;
using Microsoft.Win32;
using static Ciribob.DCS.SimpleRadio.Standalone.Client.InputDevice;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputConfiguration
    {
        public static readonly string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

        public InputDevice[] inputDevices = new InputDevice[4];


        public InputConfiguration()
        {
            //load from registry
            //    PTTCommon = ReadInputRegistry("common");

            inputDevices[0] = ReadInputRegistry(InputBinding.PTT);
            inputDevices[1] = ReadInputRegistry(InputBinding.SWITCH_1);
            inputDevices[2] = ReadInputRegistry(InputBinding.SWITCH_2);
            inputDevices[3] = ReadInputRegistry(InputBinding.SWITCH_3);
        }

        public InputDevice ReadInputRegistry(InputBinding bind)
        {
            var device = new InputDevice();
            try
            {
                var key = bind.ToString();


                var deviceName = (string) Registry.GetValue(REG_PATH,
                    key + "_name",
                    "");

                var button = (int) Registry.GetValue(REG_PATH,
                    key + "_button",
                    "");

                var guid = (string) Registry.GetValue(REG_PATH,
                    key + "_guid",
                    "");


                device.DeviceName = deviceName;
                device.Button = button;
                device.InstanceGUID = Guid.Parse(guid);
                device.InputBind = bind;

                return device;
            }
            catch (Exception ex)
            {
            }


            return null;
        }

        public void WriteInputRegistry(InputBinding bind, InputDevice device)
        {
            try
            {
                var key = bind.ToString();
                Registry.SetValue(REG_PATH,
                    key + "_name",
                    device.DeviceName.Replace("\0", ""));

                Registry.SetValue(REG_PATH,
                    key + "_button",
                    device.Button);

                Registry.SetValue(REG_PATH,
                    key + "_guid",
                    device.InstanceGUID.ToString());
            }
            catch (Exception ex)
            {
            }
        }

        public void ClearInputRegistry(InputBinding bind)
        {
            try
            {
                var key = bind.ToString();
                Registry.SetValue(REG_PATH,
                    key + "_name",
                    "");

                Registry.SetValue(REG_PATH,
                    key + "_button",
                    "");

                Registry.SetValue(REG_PATH,
                    key + "_guid",
                    "");
            }
            catch (Exception ex)
            {
            }
        }
    }
}