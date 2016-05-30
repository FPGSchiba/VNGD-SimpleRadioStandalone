using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Ciribob.DCS.SimpleRadio.Standalone.Client.InputDevice;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputConfiguration
    {
        const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

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
            InputDevice device = new InputDevice();
            try
            {

                string key = bind.ToString();


            string deviceName = (string)Registry.GetValue(REG_PATH,
                key+"_name",
                "");

            int button = (int)Registry.GetValue(REG_PATH,
               key + "_button",
               "");

            string guid = (string)Registry.GetValue(REG_PATH,
               key + "_guid",
               "");

           
                device.DeviceName = deviceName;
                device.Button = button;
                device.InstanceGUID = Guid.Parse(guid);
                device.InputBind = bind;

                return device;
            }
            catch (Exception ex) {

            }


            return null;
        }

        public void WriteInputRegistry(InputBinding bind, InputDevice device)
        {
            try
            {
                string key = bind.ToString();
                Registry.SetValue(REG_PATH,
            key + "_name",
            device.DeviceName);

                Registry.SetValue(REG_PATH,
                  key + "_button",
                  device.Button);

                Registry.SetValue(REG_PATH,
                  key + "_guid",
                  device.InstanceGUID.ToString());
            }
            catch(Exception ex)
            {

            }
        
        }


    }
}
