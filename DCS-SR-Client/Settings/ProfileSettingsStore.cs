using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using NLog;
using SharpConfig;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Settings
{
    public enum ProfileSettingsKeys
    {
        Radio1Channel,
        Radio2Channel,
        Radio3Channel,
        Radio4Channel,
        Radio5Channel,
        Radio6Channel,
        Radio7Channel,
        Radio8Channel,
        Radio9Channel,
        Radio10Channel,
        IntercomChannel,

        RadioEffects,
        RadioEncryptionEffects, //Radio Encryption effects
        RadioEffectsClipping,
        NATOTone,

        RadioRxEffects_Start, // Recieving Radio Effects
        RadioRxEffects_End,
        RadioTxEffects_Start, // Recieving Radio Effects
        RadioTxEffects_End,

        AutoSelectPresetChannel, //auto select preset channel

        AlwaysAllowHotasControls,
        AllowDCSPTT,
        RadioSwitchIsPTT,


        AlwaysAllowTransponderOverlay,
        RadioSwitchIsPTTOnlyWhenValid,

        MIDSRadioEffect, //if on and Radio TX effects are on the MIDS tone is used
        
        PTTReleaseDelay,

        RadioTransmissionStartSelection,
        RadioTransmissionEndSelection,
        HAVEQUICKTone,
        RadioBackgroundNoiseEffect,
        NATOToneVolume,
        HQToneVolume,
        FMNoiseVolume,
        VHFNoiseVolume,
        UHFNoiseVolume,
        HFNoiseVolume,

        PTTStartDelay,

        RotaryStyleIncrement,
        IntercomTransmissionStartSelection,
        IntercomTransmissionEndSelection,
        AMCollisionVolume,
    }

    public class ProfileSettingsStore
    {
        private static readonly object Lock = new object();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        //cache all the settings in their correct types for speed
        //fixes issue where we access settings a lot and have issues
        private readonly ConcurrentDictionary<string, object> _settingsCache = new ConcurrentDictionary<string, object>();

        public string CurrentProfileName
        {
            get => _currentProfileName;
            set
            {
                _settingsCache.Clear();
                _currentProfileName = value;

            }
        }

        private string Path { get; }

        private const string FalseDefault = "false";
        private const string TrueDefault = "true";
        private const string DefaultProfile = "default";
        private const string ClientSettings = "Client Settings";
        
        public static readonly Dictionary<string, string> DEFAULT_SETTINGS_PROFILE_SETTINGS = new Dictionary<string, string>()
        {
            {ProfileSettingsKeys.RadioEffects.ToString(), FalseDefault},
            {ProfileSettingsKeys.RadioEffectsClipping.ToString(), FalseDefault},
            
            {ProfileSettingsKeys.Radio1Channel.ToString(), "-0.2"},
            {ProfileSettingsKeys.Radio2Channel.ToString(), "0.2"},

            {ProfileSettingsKeys.RadioEncryptionEffects.ToString(), TrueDefault},
            {ProfileSettingsKeys.NATOTone.ToString(), FalseDefault},
            {ProfileSettingsKeys.HAVEQUICKTone.ToString(), FalseDefault},

            {ProfileSettingsKeys.RadioRxEffects_Start.ToString(), TrueDefault},
            {ProfileSettingsKeys.RadioRxEffects_End.ToString(), TrueDefault},

            {ProfileSettingsKeys.RadioTransmissionStartSelection.ToString(), CachedAudioEffect.AudioEffectTypes.RADIO_TRANS_START+".wav"},
            {ProfileSettingsKeys.RadioTransmissionEndSelection.ToString(), CachedAudioEffect.AudioEffectTypes.RADIO_TRANS_END+".wav"},


            {ProfileSettingsKeys.RadioTxEffects_Start.ToString(), TrueDefault},
            {ProfileSettingsKeys.RadioTxEffects_End.ToString(), TrueDefault},
            {ProfileSettingsKeys.MIDSRadioEffect.ToString(), TrueDefault},

            {ProfileSettingsKeys.AutoSelectPresetChannel.ToString(), FalseDefault},

            {ProfileSettingsKeys.AlwaysAllowHotasControls.ToString(),FalseDefault },
            {ProfileSettingsKeys.AllowDCSPTT.ToString(),TrueDefault },
            {ProfileSettingsKeys.RadioSwitchIsPTT.ToString(), TrueDefault},
            {ProfileSettingsKeys.RadioSwitchIsPTTOnlyWhenValid.ToString(), FalseDefault},
            {ProfileSettingsKeys.AlwaysAllowTransponderOverlay.ToString(), FalseDefault},

            {ProfileSettingsKeys.PTTReleaseDelay.ToString(), "100"},
            {ProfileSettingsKeys.PTTStartDelay.ToString(), "0"},

            {ProfileSettingsKeys.RadioBackgroundNoiseEffect.ToString(), FalseDefault},

            {ProfileSettingsKeys.NATOToneVolume.ToString(), "1.2"},
            {ProfileSettingsKeys.HQToneVolume.ToString(), "0.3"},

            {ProfileSettingsKeys.VHFNoiseVolume.ToString(), "0.15"},
            {ProfileSettingsKeys.HFNoiseVolume.ToString(), "0.15"},
            {ProfileSettingsKeys.UHFNoiseVolume.ToString(), "0.15"},
            {ProfileSettingsKeys.FMNoiseVolume.ToString(), "0.4"},

            {ProfileSettingsKeys.AMCollisionVolume.ToString(), "1.0"},

            {ProfileSettingsKeys.RotaryStyleIncrement.ToString(), FalseDefault},
        };


        public List<string> ProfileNames
        {
            get
            {
                return new List<string>(InputProfiles.Keys);
            }
            
        }

        public Dictionary<InputBinding, InputDevice> GetCurrentInputProfile()
        {
            return InputProfiles[GetProfileName(CurrentProfileName)];
        }

        public Configuration GetCurrentProfile()
        {
            return _inputConfigs[GetProfileCfgFileName(CurrentProfileName)];
        }
        public Dictionary<string, Dictionary<InputBinding, InputDevice>> InputProfiles { get; set; } = new Dictionary<string, Dictionary<InputBinding, InputDevice>>();

        private readonly Dictionary<string, Configuration> _inputConfigs = new Dictionary<string, Configuration>();

        private readonly GlobalSettingsStore _globalSettings;
        private string _currentProfileName = DefaultProfile;

        public ProfileSettingsStore(GlobalSettingsStore globalSettingsStore)
        {
            this._globalSettings = globalSettingsStore;
            this.Path = _globalSettings.Path;
        
            MigrateOldSettings();
        
            var profiles = GetProfiles();
            foreach (var profile in profiles)
            {
                LoadProfile(profile);
            }
        
            AddDefaultProfile();
        }
        
        private void LoadProfile(string profile)
        {
            Configuration configuration = null;
            try
            {
                configuration = LoadConfiguration(profile);
                _inputConfigs[GetProfileCfgFileName(profile)] = configuration;
        
                var inputProfile = new Dictionary<InputBinding, InputDevice>();
                InputProfiles[GetProfileName(profile)] = inputProfile;
        
                foreach (InputBinding bind in Enum.GetValues(typeof(InputBinding)))
                {
                    var device = GetControlSetting(bind, configuration);
                    if (device != null)
                    {
                        inputProfile[bind] = device;
                    }
                }
        
                configuration.SaveToFile(Path + GetProfileCfgFileName(profile), Encoding.UTF8);
            }
            catch (FileNotFoundException e)
            {
                _logger.Info(e, $"Did not find input config file at path {profile}, initialising with default config");
            }
            catch (ParserException e)
            {
                _logger.Info(e, "Error with input config - creating a new default ");
            }
        
            if (configuration == null)
            {
                InitializeDefaultConfiguration(profile);
            }
        }
        
        private Configuration LoadConfiguration(string profile)
        {
            int count = 0;
            while (GlobalSettingsStore.IsFileLocked(new FileInfo(Path + GetProfileCfgFileName(profile))) && count < 10)
            {
                Thread.Sleep(200);
                count++;
            }
            return Configuration.LoadFromFile(Path + GetProfileCfgFileName(profile));
        }
        
        private void InitializeDefaultConfiguration(string profile)
        {
            var configuration = new Configuration();
            var inputProfile = new Dictionary<InputBinding, InputDevice>();
            InputProfiles[GetProfileName(profile)] = inputProfile;
            _inputConfigs[GetProfileCfgFileName(profile)] = configuration;
            configuration.SaveToFile(Path + GetProfileCfgFileName(profile), Encoding.UTF8);
        }
        
        private void AddDefaultProfile()
        {
            if (!InputProfiles.ContainsKey(GetProfileName(DefaultProfile)))
            {
                _inputConfigs[GetProfileCfgFileName(DefaultProfile)] = new Configuration();
        
                var inputProfile = new Dictionary<InputBinding, InputDevice>();
                InputProfiles[GetProfileName(DefaultProfile)] = inputProfile;
        
                _inputConfigs[GetProfileCfgFileName(DefaultProfile)].SaveToFile(GetProfileCfgFileName(DefaultProfile));
            }
        }

        private void MigrateOldSettings()
        {
            try
            {
                //combine global.cfg and input-default.cfg
                if (File.Exists(Path+"input-default.cfg") && File.Exists(Path + "global.cfg") && !File.Exists("default.cfg"))
                {
                    //Copy the current GLOBAL settings - not all relevant but will be ignored
                    File.Copy(Path + "global.cfg", Path + "default.cfg");

                    var inputText = File.ReadAllText(Path + "input-default.cfg", Encoding.UTF8);

                    File.AppendAllText(Path + "default.cfg", inputText, Encoding.UTF8);

                    _logger.Info("Migrated the previous input-default.cfg and global settings to the new profile");
                }
                else
                {
                    _logger.Info("No need to migrate - migration complete");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex,"Error migrating input profiles");
            }
          
        }

        public List<string> GetProfiles()
        {
            var profiles = _globalSettings.GetClientSetting(GlobalSettingsKeys.SettingsProfiles).StringValueArray;

            if (profiles == null || profiles.Length == 0 || !profiles.Contains(DefaultProfile))
            {
                profiles = new[] { DefaultProfile };
                _globalSettings.SetClientSetting(GlobalSettingsKeys.SettingsProfiles, profiles);
            }

            return new List<string>(profiles);
        }

        public void AddNewProfile(string profileName)
        {
            var profiles = InputProfiles.Keys.ToList();
            profiles.Add(profileName);

            _globalSettings.SetClientSetting(GlobalSettingsKeys.SettingsProfiles, profiles.ToArray());

            _inputConfigs[GetProfileCfgFileName(profileName)] = new Configuration();

            var inputProfile = new Dictionary<InputBinding, InputDevice>();
            InputProfiles[GetProfileName(profileName)] = inputProfile;
        }

        private static string GetProfileCfgFileName(string prof)
        {
            if (prof.Contains(".cfg"))
            {
                return prof;
            }

            return  prof + ".cfg";
        }

        private static string GetProfileName(string cfg)
        {
            if (cfg.Contains(".cfg"))
            {
                return cfg.Replace(".cfg","");
            }

            return cfg;
        }

        public InputDevice GetControlSetting(InputBinding key, Configuration configuration)
        {
            if (!configuration.Contains(key.ToString()))
            {
                return null;
            }

            try
            {
                var device = new InputDevice();
                device.DeviceName = configuration[key.ToString()]["name"].StringValue;

                device.Button = configuration[key.ToString()]["button"].IntValue;
                device.InstanceGuid =
                    Guid.Parse(configuration[key.ToString()]["guid"].RawValue);
                device.InputBind = key;

                device.ButtonValue = configuration[key.ToString()]["value"].IntValue;

                return device;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error reading input device saved settings ");
            }


            return null;
        }
        public void SetControlSetting(InputDevice device)
        {
            RemoveControlSetting(device.InputBind);

            var configuration = GetCurrentProfile();

            configuration.Add(new Section(device.InputBind.ToString()));

            //create the sections
            var section = configuration[device.InputBind.ToString()];

            section.Add(new Setting("name", device.DeviceName.Replace("\0", "")));
            section.Add(new Setting("button", device.Button));
            section.Add(new Setting("value", device.ButtonValue));
            section.Add(new Setting("guid", device.InstanceGuid.ToString()));

            var inputDevices = GetCurrentInputProfile();

            inputDevices[device.InputBind] = device;

            Save();
        }

        public void RemoveControlSetting(InputBinding binding)
        {
            var configuration = GetCurrentProfile();

            if (configuration.Contains(binding.ToString()))
            {
                configuration.Remove(binding.ToString());
            }

            var inputDevices = GetCurrentInputProfile();
            inputDevices.Remove(binding);

            Save();
        }

        private Setting GetSetting(string section, string setting)
        {
            var configuration = GetCurrentProfile();

            if (!configuration.Contains(section))
            {
                configuration.Add(section);
            }

            if (!configuration[section].Contains(setting))
            {
                if (DEFAULT_SETTINGS_PROFILE_SETTINGS.ContainsKey(setting))
                {
                    //save
                    configuration[section]
                        .Add(new Setting(setting, DEFAULT_SETTINGS_PROFILE_SETTINGS[setting]));

                    Save();
                }
                else
                {
                    configuration[section]
                        .Add(new Setting(setting, ""));
                    Save();
                }
            }

            return configuration[section][setting];
        }

        public bool GetClientSettingBool(ProfileSettingsKeys key)
        {

            if (_settingsCache.TryGetValue(key.ToString(), out var val))
            {
                return (bool)val;
            }

            var setting = GetSetting(ClientSettings, key.ToString());
            if (setting.RawValue.Length == 0)
            {
                _settingsCache[key.ToString()] = false;
                return false;
            }

            _settingsCache[key.ToString()] = setting.BoolValue;

            return setting.BoolValue;
        }

        public float GetClientSettingFloat(ProfileSettingsKeys key)
        {
            if (_settingsCache.TryGetValue(key.ToString(),out var val))
            {
                if (val == null)
                {
                    return 0f;
                }
                return (float) val;
            }

            var setting =  GetSetting(ClientSettings, key.ToString()).FloatValue;

            _settingsCache[key.ToString()] = setting;

            return setting;
        }

        public string GetClientSettingString(ProfileSettingsKeys key)
        {
            if (_settingsCache.TryGetValue(key.ToString(), out var val))
            {
                return (string)val;
            }

            var setting = GetSetting(ClientSettings, key.ToString()).RawValue;

            _settingsCache[key.ToString()] = setting;

            return setting;
        }


        public void SetClientSettingBool(ProfileSettingsKeys key, bool value)
        {
            SetSetting(ClientSettings, key.ToString(), value);

            _settingsCache.TryRemove(key.ToString(), out var _);
        }

        public void SetClientSettingFloat(ProfileSettingsKeys key, float value)
        {
            SetSetting(ClientSettings, key.ToString(), value);

            _settingsCache.TryRemove(key.ToString(), out var _);
        }
        public void SetClientSettingString(ProfileSettingsKeys key, string value)
        {
            SetSetting(ClientSettings, key.ToString(), value);
            _settingsCache.TryRemove(key.ToString(), out var _);
        }

        private void SetSetting(string section, string key, object setting)
        {
            var configuration = GetCurrentProfile();

            if (setting == null)
            {
                setting = "";
            }
            if (!configuration.Contains(section))
            {
                configuration.Add(section);
            }

            if (!configuration[section].Contains(key))
            {
                configuration[section].Add(new Setting(key, setting));
            }
            else
            {
                if (setting is bool bSetting)
                {
                    configuration[section][key].BoolValue = bSetting;
                }
                else if (setting is float fSetting)
                {
                    configuration[section][key].FloatValue = fSetting;
                }
                else if (setting is double dSetting)
                {
                    configuration[section][key].DoubleValue = dSetting;
                }
                else if (setting is int iSetting)
                {
                    configuration[section][key].DoubleValue = iSetting;
                }
                else if (setting is string sSetting)
                {
                    configuration[section][key].StringValue = sSetting;
                }
                else if (setting is string[] ssSetting)
                {
                    configuration[section][key].StringValueArray = ssSetting;
                }
                else
                {
                    _logger.Error("Unknown Setting Type - Not Saved ");
                }
            }

            Save();
        }

        public void Save()
        {
            lock (Lock)
            {
                try
                {
                    var configuration = GetCurrentProfile();
                    configuration.SaveToFile(Path+GetProfileCfgFileName(CurrentProfileName));
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to save settings!");
                }
            }
        }

        public void RemoveProfile(string profile)
        {
            _inputConfigs.Remove(GetProfileCfgFileName(profile));
            InputProfiles.Remove(GetProfileName(profile));

            var profiles = InputProfiles.Keys.ToList();
            _globalSettings.SetClientSetting(GlobalSettingsKeys.SettingsProfiles, profiles.ToArray());

            try
            {
                File.Delete(Path + GetProfileCfgFileName(profile));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete profile!");
            }

            CurrentProfileName = DefaultProfile;
        }

        public void RenameProfile(string oldName,string newName)
        {
            _inputConfigs[GetProfileCfgFileName(newName)] = _inputConfigs[GetProfileCfgFileName(oldName)];
            InputProfiles[GetProfileName(newName)]= InputProfiles[GetProfileName(oldName)];

            _inputConfigs.Remove(GetProfileCfgFileName(oldName));
            InputProfiles.Remove(GetProfileName(oldName));

            var profiles = InputProfiles.Keys.ToList();
            _globalSettings.SetClientSetting(GlobalSettingsKeys.SettingsProfiles, profiles.ToArray());

            CurrentProfileName = DefaultProfile;

            _inputConfigs[GetProfileCfgFileName(newName)].SaveToFile(GetProfileCfgFileName(newName));

            try
            {
                File.Delete(Path + GetProfileCfgFileName(oldName));
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to rename profile!");
            }
        }

        public void CopyProfile(string profileToCopy, string profileName)
        {
            var config = Configuration.LoadFromFile(Path+GetProfileCfgFileName(profileToCopy));
            _inputConfigs[GetProfileCfgFileName(profileName)] = config;

            var inputProfile = new Dictionary<InputBinding, InputDevice>();
            InputProfiles[GetProfileName(profileName)] = inputProfile;

            foreach (InputBinding bind in Enum.GetValues(typeof(InputBinding)))
            {
                var device = GetControlSetting(bind, config);

                if (device != null)
                {
                    inputProfile[bind] = device;
                }
            }

            var profiles = InputProfiles.Keys.ToList();
            _globalSettings.SetClientSetting(GlobalSettingsKeys.SettingsProfiles, profiles.ToArray());

            CurrentProfileName = DefaultProfile;

            _inputConfigs[GetProfileCfgFileName(profileName)].SaveToFile(Path+GetProfileCfgFileName(profileName));

        }
    }
}
