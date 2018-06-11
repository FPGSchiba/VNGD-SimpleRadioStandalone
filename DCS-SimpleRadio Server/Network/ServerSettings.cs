using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NLog;
using SharpConfig;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    public class ServerSettings
    {
        public static readonly string CFG_FILE_NAME = "server.cfg";

        private static ServerSettings instance;
        private static readonly object _lock = new object();

        private readonly Configuration _configuration;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public ServerSettings()
        {
            try
            {
                _configuration = Configuration.LoadFromFile(CFG_FILE_NAME);
            }
            catch (FileNotFoundException ex)
            {
                _configuration = new Configuration();
                _configuration.Add(new Section("General Settings"));
                _configuration.Add(new Section("Server Settings"));
                _configuration.Add(new Section("External AWACS Mode Settings"));
            }
        }

        public static ServerSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new ServerSettings();
                    }
                }
                return instance;
            }
        }

        private readonly Dictionary<string, string> defaultSettings = new Dictionary<string, string>()
        {
            { ServerSettingsKeys.CLIENT_EXPORT_ENABLED.ToString(), "false" },
            { ServerSettingsKeys.COALITION_AUDIO_SECURITY.ToString(), "false" },
            { ServerSettingsKeys.DISTANCE_ENABLED.ToString(), "false" },
            { ServerSettingsKeys.EXTERNAL_AWACS_MODE.ToString(), "false" },
            { ServerSettingsKeys.EXTERNAL_AWACS_MODE_BLUE_PASSWORD.ToString(), "" },
            { ServerSettingsKeys.EXTERNAL_AWACS_MODE_RED_PASSWORD.ToString(), "" },
            { ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE.ToString(), "false" },
            { ServerSettingsKeys.IRL_RADIO_STATIC.ToString(), "false" },
            { ServerSettingsKeys.IRL_RADIO_TX.ToString(), "false" },
            { ServerSettingsKeys.LOS_ENABLED.ToString(), "false" },
            { ServerSettingsKeys.RADIO_EXPANSION.ToString(), "false" },
            { ServerSettingsKeys.SERVER_PORT.ToString(), "5002" },
            { ServerSettingsKeys.SPECTATORS_AUDIO_DISABLED.ToString(), "false" }
        };

        public Setting GetGeneralSetting(ServerSettingsKeys key)
        {
            return GetSetting("General Settings", key.ToString());
        }

        public void SetGeneralSetting(ServerSettingsKeys key, bool value)
        {
            SetSetting("General Settings", key.ToString(), value.ToString(CultureInfo.InvariantCulture));
        }

        public Setting GetServerSetting(ServerSettingsKeys key)
        {
            return GetSetting("Server Settings", key.ToString());
        }

        public void SetServerSetting(ServerSettingsKeys key, int value)
        {
            SetSetting("Server Settings", key.ToString(), value.ToString(CultureInfo.InvariantCulture));
        }

        public Setting GetExternalAWACSModeSetting(ServerSettingsKeys key)
        {
            return GetSetting("External AWACS Mode Settings", key.ToString());
        }

        public void SetExternalAWACSModeSetting(ServerSettingsKeys key, string value)
        {
            SetSetting("External AWACS Mode Settings", key.ToString(), value);
        }

        private Setting GetSetting(string section, string setting)
        {
            if (!_configuration.Contains(section))
            {
                _configuration.Add(section);
            }

            if (!_configuration[section].Contains(setting))
            {
                if (defaultSettings.ContainsKey(setting))
                {
                    _configuration[section].Add(new Setting(setting, defaultSettings[setting]));
                }
                else
                {
                    _configuration[section].Add(new Setting(setting, ""));
                }

                Save();
            }

            return _configuration[section][setting];
        }

        private void SetSetting(string section, string key, string setting)
        {
            if (setting == null)
            {
                setting = "";
            }

            if (!_configuration.Contains(section))
            {
                _configuration.Add(section);
            }

            if (!_configuration[section].Contains(key))
            {
                _configuration[section].Add(new Setting(key, setting));
            }
            else
            {
                _configuration[section][key].StringValue = setting;
            }

            Save();
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    _configuration.SaveToFile(CFG_FILE_NAME);
                } catch (Exception ex)
                {
                    _logger.Error("Unable to save settings!");
                }
            }
        }

        public Dictionary<string, string> ToDictionary()
        {
            if (!_configuration.Contains("General Settings"))
            {
                _configuration.Add("General Settings");
            }

            Dictionary<string, string> settings = new Dictionary<string, string>(_configuration["General Settings"].SettingCount);

            foreach (Setting setting in _configuration["General Settings"])
            {
                settings[setting.Name] = setting.StringValue;
            }

            return settings;
        }
    }
}