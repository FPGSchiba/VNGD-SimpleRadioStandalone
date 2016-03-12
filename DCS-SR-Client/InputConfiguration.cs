using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputConfiguration
    {
        const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SimpleRadioStandalone";

        public InputDevice PTTCommon { get; set; }

        public InputDevice SelectRadio1 { get; set; }

        public InputDevice SelectRadio2 { get; set; }

        public InputDevice SelectRadio3 { get; set; }

        public InputConfiguration()
        {
            //load from registry
            //    PTTCommon = ReadInputRegistry("common");
 
            PTTCommon = ReadInputRegistry("common");

        }

        public InputDevice ReadInputRegistry(String key)
        {
            InputDevice device = new InputDevice();
            try
            {
            



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

                return device;
            }
            catch (Exception ex) {

            }


            return null;
        }

        public void WriteInputRegistry(String key,InputDevice device)
        {
            try
            {
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
