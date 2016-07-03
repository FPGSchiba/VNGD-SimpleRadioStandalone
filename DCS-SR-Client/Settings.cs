using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public enum SettingType
    {
        RADIO_EFFECTS = 0,
        RADIO1_CHANNEL = 1,
        RADIO2_CHANNEL = 2,
        RADIO3_CHANNEL = 3
    }


    public class Settings
    {
        private static Settings instance;

        public String[] UserSettings
        {
            get;
        }

        public static Settings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new Settings();
                }
                return instance;
            }
        }

        public Settings()
        {
            UserSettings = new string[4];

            foreach(SettingType set in Enum.GetValues(typeof(SettingType)))
            {
                UserSettings[(int)set] = ReadSetting(set);
            }

        }

      

        public String ReadSetting(SettingType settingType)
        {  
            try
            {

                string setting = (string)Registry.GetValue(InputConfiguration.REG_PATH,
                    settingType + "_setting",
                    "");
                return setting;

            }
            catch (Exception ex)
            {

            }
            return null;

        }

        public void WriteSetting(SettingType settingType, String setting)
        {
            try
            {
               
               Registry.SetValue(InputConfiguration.REG_PATH,
                    settingType + "_setting",
                    setting);

                UserSettings[(int)settingType] = setting;


            }
            catch (Exception ex)
            {

            }

        }
    }
}
