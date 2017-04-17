using System;
using Microsoft.Win32;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Settings
{
    public class SettingsStore
    {
        private static SettingsStore _instance;

        public SettingsStore()
        {
            UserSettings = new string[Enum.GetValues(typeof(SettingType)).Length];

            foreach (SettingType set in Enum.GetValues(typeof(SettingType)))
            {
                UserSettings[(int) set] = ReadSetting(set);
            }
        }

        public string[] UserSettings { get; }

        public static SettingsStore Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SettingsStore();
                }
                return _instance;
            }
        }


        public string ReadSetting(SettingType settingType)
        {
            try
            {
                var setting = (string) Registry.GetValue(InputConfiguration.RegPath,
                    settingType + "_setting",
                    "");
                return setting;
            }
            catch (Exception ex)
            {
            }
            return null;
        }

        public void WriteSetting(SettingType settingType, string setting)
        {
            try
            {
                Registry.SetValue(InputConfiguration.RegPath,
                    settingType + "_setting",
                    setting);

                UserSettings[(int) settingType] = setting;
            }
            catch (Exception ex)
            {
            }
        }
    }
}