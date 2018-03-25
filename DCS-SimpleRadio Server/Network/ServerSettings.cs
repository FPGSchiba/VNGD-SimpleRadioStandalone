using System;
using System.IO;
using NLog;
using SharpConfig;

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
            ServerSetting = new bool[Enum.GetValues(typeof(ServerSettingType)).Length];

            try
            {
                _configuration = Configuration.LoadFromFile(CFG_FILE_NAME);
            }
            catch (FileNotFoundException ex)
            {
                _configuration = new Configuration();
                _configuration.Add(new Section("General Settings"));
                _configuration.Add(new Section("Server Settings"));
            }

            foreach (var section in _configuration)
            {
                if (section.Name.Equals("General Settings"))
                {
                    foreach (var setting in section)
                    {
                        try
                        {
                            ServerSettingType settingEnum;
                            if (Enum.TryParse(setting.Name, true, out settingEnum))
                            {
                                ServerSetting[(int) settingEnum] = setting.BoolValue;
                            }
                            else
                            {
                                _logger.Warn("Invalid setting: " + setting.Name);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn("Invalid setting: " + setting.Name);
                        }
                    }
                }
            }

            SaveAllGeneral(true);
        }

        public bool[] ServerSetting { get; }

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

        public void WriteSetting(ServerSettingType settingType, bool setting)
        {
            ServerSetting[(int) settingType] = setting;
            try
            {
                var section = _configuration["General Settings"];
                section[settingType.ToString()].BoolValue = setting;

                SaveAllGeneral(true);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to save Settings: " + ex.Message);
            }
        }

        private void SaveAllGeneral(bool savePort)
        {
            var section = _configuration["General Settings"];
            for (var i = 0; i < ServerSetting.Length; i++)
            {
                var serverSettingType = (ServerSettingType) i;
                section[serverSettingType.ToString()].BoolValue = ServerSetting[i];
            }

            if (savePort)
            {
                //load port in too
                ServerListeningPort();
            }

            try
            {
                _configuration.SaveToFile(CFG_FILE_NAME);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to save Settings: " + ex.Message);
            }
        }


        public int ServerListeningPort()
        {
            try
            {
                SaveAllGeneral(false);

                var section = _configuration["Server Settings"];

                return section["port"].IntValue;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to read server port: " + ex.Message);
            }

            _configuration["Server Settings"]["port"].IntValue = 5002;
            return 5002; //UDP Port is always 1 More
        }
    }
}