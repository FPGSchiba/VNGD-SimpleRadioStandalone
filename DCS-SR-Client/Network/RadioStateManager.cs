using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Network
{
    public class RadioStateManager
    {
        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string RadioConfigFile =
            Path.Combine(AppContext.BaseDirectory, "radio-config.json");
        private const int UpdateIntervalSeconds = 60;

        private readonly SendRadioUpdate _radioUpdate;
        private readonly IEventBus _eventBus;
        private readonly ManualResetEventSlim _stopEvent = new ManualResetEventSlim(false);
        private volatile bool _stop;
        private Task _loopTask;

        public ClientRadioState CurrentState { get; private set; }

        public RadioStateManager(SendRadioUpdate radioUpdate, IEventBus eventBus = null)
        {
            _radioUpdate = radioUpdate;
            _eventBus = eventBus;
            CurrentState = LoadRadioConfig();
        }

        public void Start()
        {
            _stop = false;
            _stopEvent.Reset();
            _loopTask = Task.Factory.StartNew(
                RunLoop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        public void Stop()
        {
            _stop = true;
            _stopEvent.Set();
        }

        private void RunLoop()
        {
            Logger.Info("RadioStateManager loop started");
            InvokeUpdate();
            while (!_stop)
            {
                _stopEvent.Wait(TimeSpan.FromSeconds(UpdateIntervalSeconds));
                if (!_stop)
                    InvokeUpdate();
            }
            Logger.Info("RadioStateManager loop stopped");
        }

        private void InvokeUpdate()
        {
            try
            {
                _radioUpdate();
                _eventBus?.Publish(new LocalRadioStateChangedEvent(CurrentState));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RadioStateManager: _radioUpdate threw unexpectedly; loop continues");
            }
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
                        return new ClientRadioState(radios.AsReadOnly());
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
            return new ClientRadioState(radios.AsReadOnly());
        }
    }
}
