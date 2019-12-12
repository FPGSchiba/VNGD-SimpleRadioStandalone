using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Microsoft.Win32;
using NLog;
using SharpConfig;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputSettingsStore
    {
        private static readonly object _lock = new object();
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public string CurrentProfileName { get; set; } = "default";


        public List<string> ProfileNames
        {
            get
            {
                return new List<string>(InputProfiles.Keys);
            }
            
        }

        public Dictionary<InputBinding, InputDevice> GetCurrentInputProfile()
        {
            return InputProfiles[GetControlProfileName(CurrentProfileName)];
        }

        public Configuration GetCurrentInputConfig()
        {
            return InputConfigs[GetControlCfgFileName(CurrentProfileName)];
        }
        public Dictionary<string, Dictionary<InputBinding, InputDevice>> InputProfiles { get; set; } = new Dictionary<string, Dictionary<InputBinding, InputDevice>>();

        private Dictionary<string, Configuration> InputConfigs = new Dictionary<string, Configuration>();

        private readonly SettingsStore _settings;

        public InputSettingsStore(SettingsStore settingsStore)
        {
            this._settings = settingsStore;
            var profiles = GetProfiles();
            foreach (var profile in profiles)
            {
                Configuration _configuration = null;
                try
                {
                     _configuration = Configuration.LoadFromFile(GetControlCfgFileName(profile));
                    InputConfigs[GetControlCfgFileName(profile)] = _configuration;

                    var inputProfile = new Dictionary<InputBinding, InputDevice>();
                    InputProfiles[GetControlProfileName(profile)] = inputProfile;

                    foreach (InputBinding bind in Enum.GetValues(typeof(InputBinding)))
                    {
                        var device = GetControlSetting(bind, _configuration);

                        if (device != null)
                        {
                            inputProfile[bind] = device;
                        }
                    }

                    _configuration.SaveToFile(GetControlCfgFileName(profile), Encoding.UTF8);
                
                }
                catch (FileNotFoundException ex)
                {
                    Logger.Info(
                        $"Did not find input config file at path {profile}, initialising with default config");
                }
                catch (ParserException ex)
                {
                    Logger.Info(
                        $"Error with input config - creating a new default ");
                }

                if (_configuration == null)
                {
                    _configuration = new Configuration();
                    var inputProfile = new Dictionary<InputBinding, InputDevice>();
                    InputProfiles[GetControlProfileName(profile)] = inputProfile;
                    InputConfigs[GetControlCfgFileName(profile)] = new Configuration();
                    _configuration.SaveToFile(GetControlCfgFileName(profile), Encoding.UTF8);

                    if (profile.Equals("default"))
                    {
                        //import 
                        Logger.Info(
                            $"Import CFG from {settingsStore.ConfigFileName}");
                        try
                        {
                            _configuration = Configuration.LoadFromFile(settingsStore.ConfigFileName);

                            InputConfigs[GetControlCfgFileName(profile)] = _configuration;

                            inputProfile = new Dictionary<InputBinding, InputDevice>();
                            InputProfiles[GetControlProfileName(profile)] = inputProfile;

                            foreach (InputBinding bind in Enum.GetValues(typeof(InputBinding)))
                            {
                                var device = GetControlSetting(bind, _configuration);

                                if (device != null)
                                {
                                    inputProfile[bind] = device;
                                }
                            }
                            _configuration.SaveToFile(GetControlCfgFileName(profile), Encoding.UTF8);

                            Logger.Info(
                                $"Imported CFG from {settingsStore.ConfigFileName}");
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                   
                }
            }

            //add default
            if (!InputProfiles.ContainsKey(GetControlProfileName("default")))
            {
                InputConfigs[GetControlCfgFileName("default")] = new Configuration();

                var inputProfile = new Dictionary<InputBinding, InputDevice>();
                InputProfiles[GetControlProfileName("default")] = inputProfile;
            }
        }


        public List<string> GetProfiles()
        {
            var profiles = _settings.GetClientSetting(SettingsKeys.InputProfiles).StringValueArray;

            if (profiles == null || profiles.Length == 0 || !profiles.Contains("default"))
            {
                profiles = new[] { "default" };
                _settings.SetClientSetting(SettingsKeys.InputProfiles, profiles);
            }

            return new List<string>(profiles);
        }

        public void AddNewProfile(string profileName)
        {
            var profiles = InputProfiles.Keys.ToList();
            profiles.Add(profileName);

            _settings.SetClientSetting(SettingsKeys.InputProfiles, profiles.ToArray());

            InputConfigs[GetControlCfgFileName(profileName)] = new Configuration();

            var inputProfile = new Dictionary<InputBinding, InputDevice>();
            InputProfiles[GetControlProfileName(profileName)] = inputProfile;

        }

        private string GetControlCfgFileName(string prof)
        {
            if (prof.Contains(".cfg") && prof.Contains("input-"))
            {
                return prof;
            }

            return "input-" + prof + ".cfg";
        }

        private string GetControlProfileName(string cfg)
        {
            if (cfg.Contains(".cfg") && cfg.Contains("input-"))
            {
                return cfg.Replace("input-", "").Replace(".cfg","");
            }

            return cfg;
        }

        public InputDevice GetControlSetting(InputBinding key)
        {
            return GetControlSetting(key, GetCurrentInputConfig());
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
                Logger.Error(e, "Error reading input device saved settings ");
            }


            return null;
        }
        public void SetControlSetting(InputDevice device)
        {
            RemoveControlSetting(device.InputBind);

            var configuration = GetCurrentInputConfig();

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
            var configuration = GetCurrentInputConfig();

            if (configuration.Contains(binding.ToString()))
            {
                configuration.Remove(binding.ToString());
            }

            var inputDevices = GetCurrentInputProfile();
            inputDevices.Remove(binding);

            Save();
        }

        public void Save()
        {
            lock (_lock)
            {
                try
                {
                    var configuration = GetCurrentInputConfig();
                    configuration.SaveToFile(GetControlCfgFileName(CurrentProfileName));
                }
                catch (Exception ex)
                {
                    Logger.Error("Unable to save settings!");
                }
            }
        }

        public void RemoveProfile(string profile)
        {
            InputConfigs.Remove(GetControlCfgFileName(profile));
            InputProfiles.Remove(GetControlProfileName(profile));

            var profiles = InputProfiles.Keys.ToList();
            _settings.SetClientSetting(SettingsKeys.InputProfiles, profiles.ToArray());

            try
            {
                File.Delete(GetControlCfgFileName(profile));
            }
            catch
            { }

            CurrentProfileName = "default";
        }

        public void RenameProfile(string oldName,string newName)
        {
            InputConfigs[GetControlCfgFileName(newName)] = InputConfigs[GetControlCfgFileName(oldName)];
            InputProfiles[GetControlProfileName(newName)]= InputProfiles[GetControlProfileName(oldName)];

            InputConfigs.Remove(GetControlCfgFileName(oldName));
            InputProfiles.Remove(GetControlProfileName(oldName));

            var profiles = InputProfiles.Keys.ToList();
            _settings.SetClientSetting(SettingsKeys.InputProfiles, profiles.ToArray());

            CurrentProfileName = "default";

            InputConfigs[GetControlCfgFileName(newName)].SaveToFile(GetControlCfgFileName(newName));

            try
            {
                File.Delete(GetControlCfgFileName(oldName));
            }
            catch
            { }
        }
    }
}
