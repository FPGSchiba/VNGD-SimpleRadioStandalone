using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Network
{
    public class RadioStateManager
    {
        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string RadioConfigFile = "radio-config.json";
        private const int UpdateIntervalSeconds = 60;

        private readonly SendRadioUpdate _radioUpdate;
        private volatile bool _stop;

        public ClientRadioState CurrentState { get; private set; }

        public RadioStateManager(SendRadioUpdate radioUpdate)
        {
            _radioUpdate = radioUpdate;
            CurrentState = LoadRadioConfig();
        }

        public void Start()
        {
            _stop = false;
            Task.Factory.StartNew(RunLoop, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            _stop = true;
        }

        private void RunLoop()
        {
            Logger.Info("RadioStateManager loop started");
            _radioUpdate();
            while (!_stop)
            {
                Thread.Sleep(UpdateIntervalSeconds * 1000);
                if (!_stop)
                    _radioUpdate();
            }
            Logger.Info("RadioStateManager loop stopped");
        }

        internal static ClientRadioState LoadRadioConfig()
        {
            try
            {
                if (File.Exists(RadioConfigFile))
                {
                    var json = File.ReadAllText(RadioConfigFile);
                    var radios = JsonConvert.DeserializeObject<List<ClientRadio>>(json);
                    if (radios != null && radios.Count > 0)
                    {
                        Logger.Info($"Loaded {radios.Count} radios from {RadioConfigFile}");
                        return new ClientRadioState(radios);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load {RadioConfigFile}, using defaults");
            }
            return CreateDefaultState();
        }

        internal static ClientRadioState CreateDefaultState()
        {
            var radios = new List<ClientRadio>(11);
            for (int i = 0; i < 11; i++)
                radios.Add(new ClientRadio($"Radio {i + 1}", 1.0, false, false));
            return new ClientRadioState(radios);
        }
    }
}
