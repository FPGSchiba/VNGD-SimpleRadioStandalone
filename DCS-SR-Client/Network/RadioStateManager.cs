using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network.Models;
using Vanguard.VCS.Common.DCSState;

namespace Vanguard.VCS.Client.Network
{
    public class RadioStateManager
    {
        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly string RadioConfigFile =
            Path.Combine(AppContext.BaseDirectory, "radio-config.json");
        private static readonly string AwacsRadiosFile =
            Path.Combine(AppContext.BaseDirectory, "awacs-radios.json");
        private const int UpdateIntervalSeconds = 60;

        private volatile SendRadioUpdate _radioUpdate;
        private readonly IEventBus _eventBus;
        private readonly ManualResetEventSlim _stopEvent = new ManualResetEventSlim(false);
        private volatile bool _stop;
        private Task _loopTask;

        private volatile ClientRadioState _currentState;
        public ClientRadioState CurrentState => _currentState;

        public int SelectedRadioIndex { get; set; } = -1;

        public RadioStateManager(SendRadioUpdate radioUpdate, IEventBus eventBus = null)
        {
            _radioUpdate = radioUpdate;
            _eventBus = eventBus;
            _currentState = LoadRadioConfig();
        }

        public void SetUpdateCallback(SendRadioUpdate callback)
        {
            _radioUpdate = callback;
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

        public void SetState(ClientRadioState state)
        {
            _currentState = state;
            _eventBus?.Publish(new LocalRadioStateChangedEvent(state));
        }

        public void UpdateRadioFrequency(int radioId, double deltaHz)
        {
            if (radioId < 1 || radioId > CurrentState.Radios.Count) return;
            var updated = CurrentState.Radios.ToList();
            var r = updated[radioId - 1];
            double newFreq = Math.Max(1.0, Math.Min(9_999_999_999.0, r.FrequencyHz + deltaHz));
            updated[radioId - 1] = r with { FrequencyHz = newFreq };
            SetState(new ClientRadioState(updated.AsReadOnly()));
        }

        public void SetRadioModulation(int radioId, bool enabled, bool isIntercom)
        {
            if (radioId < 1 || radioId > CurrentState.Radios.Count) return;
            var updated = CurrentState.Radios.ToList();
            var r = updated[radioId - 1];
            updated[radioId - 1] = r with { Enabled = enabled, IsIntercom = isIntercom };
            SetState(new ClientRadioState(updated.AsReadOnly()));
        }

        public RadioInformation GetRadio(int radioId)
        {
            if (radioId < 1 || radioId > CurrentState.Radios.Count) return null;
            var r = CurrentState.Radios[radioId - 1];
            return new RadioInformation
            {
                name = r.Name,
                freq = r.FrequencyHz,
                modulation = r.Enabled
                    ? (r.IsIntercom
                        ? RadioInformation.Modulation.INTERCOM
                        : RadioInformation.Modulation.AM)
                    : RadioInformation.Modulation.DISABLED,
                freqMax = 9999999999,
                freqMin = 1,
            };
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
                _radioUpdate?.Invoke();
                _eventBus?.Publish(new LocalRadioStateChangedEvent(CurrentState));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "RadioStateManager: _radioUpdate threw unexpectedly; loop continues");
            }
        }

        private sealed class AwacsRadioJson
        {
            [JsonProperty("freq")] public double Freq { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("modulation")] public int Modulation { get; set; }
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
                Logger.Warn(ex, $"Failed to load {RadioConfigFile}, trying awacs-radios.json");
            }

            try
            {
                if (File.Exists(AwacsRadiosFile))
                {
                    var json = File.ReadAllText(AwacsRadiosFile);
                    var awacs = JsonConvert.DeserializeObject<List<AwacsRadioJson>>(json);
                    if (awacs != null && awacs.Count > 0)
                    {
                        var radios = awacs
                            .Select(r => new ClientRadio(r.Name, r.Freq, r.Modulation != 3, r.Modulation == 2))
                            .ToList();
                        Logger.Info($"Loaded {radios.Count} radios from {AwacsRadiosFile}");
                        return new ClientRadioState(radios.AsReadOnly());
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load {AwacsRadiosFile}, using defaults");
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
