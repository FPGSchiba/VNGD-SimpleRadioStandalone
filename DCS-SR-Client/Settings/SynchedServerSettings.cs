using System.Collections.Generic;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using SharpConfig;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Settings
{
    public class SyncedServerSettings
    {
        private static SyncedServerSettings instance;
        private static readonly object _lock = new object();

        private readonly Section _section;

        public SyncedServerSettings()
        {
            _section = new Section("Synced Server Settings");
        }

        public static SyncedServerSettings Instance
        {
            get
            {
                lock (_lock)
                {
                    if (instance == null)
                    {
                        instance = new SyncedServerSettings();
                    }
                    return instance;
                }
            }
        }

        public SharpConfig.Setting GetSetting(ServerSettingsKeys key)
        {
            string setting = key.ToString();

            if (!_section.Contains(setting))
            {
                return new SharpConfig.Setting(setting, "");
            }

            return _section[setting];
        }

        public bool GetSettingAsBool(ServerSettingsKeys key)
        {
            return GetSetting(key).BoolValue;
        }

        public void Decode(Dictionary<string, string> encoded)
        {
            _section.Clear();

            foreach (KeyValuePair<string, string> kvp in encoded)
            {
                _section.Add(kvp.Key, kvp.Value);
            }
        }
    }
}
