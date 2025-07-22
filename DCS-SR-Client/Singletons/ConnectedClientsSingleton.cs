using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Google.Protobuf.Collections;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Network;
using Vanguard.VCS.Common.Setting;

namespace Vanguard.VCS.Client.Singletons
{
    public sealed class ConnectedClientsSingleton : INotifyPropertyChanged
    {
        private readonly ConcurrentDictionary<Guid, SRClient> _clients = new ConcurrentDictionary<Guid, SRClient>();
        private static volatile ConnectedClientsSingleton _instance;
        private static object _lock = new Object();
        private readonly Guid _guid = ClientStateSingleton.Instance.ClientId;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        public event PropertyChangedEventHandler PropertyChanged;

        private ConnectedClientsSingleton() { }

        public static ConnectedClientsSingleton Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ConnectedClientsSingleton();
                    }
                }

                return _instance;
            }
        }

        private void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void NotifyAll()
        {
            NotifyPropertyChanged("Total");
        }

        public SRClient this[Guid key]
        {
            get
            {
                return _clients[key];
            }
            set
            {
                _clients[key] = value;
               NotifyAll();
            }
        }

        public ICollection<SRClient> Values
        {
            get
            {
                return _clients.Values;
            }
        }

        public int Total
        {
            get
            {
                return _clients.Count();
            }
        }

        public bool TryRemove(Guid key, out SRClient value)
        {
            bool result = _clients.TryRemove(key, out value);
            if (result)
            {
                NotifyPropertyChanged("Total");
            }
            return result;
        }

        public void Clear()
        {
            _clients.Clear();
            NotifyPropertyChanged("Total");
        }

        public bool TryGetValue(Guid key, out SRClient value)
        {
            return _clients.TryGetValue(key, out value);
        }

        public bool ContainsKey(Guid key)
        {
            return _clients.ContainsKey(key);
        }

        public int ClientsOnFreq(double freq, RadioInformation.Modulation modulation)
        {
            if (!_serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TUNED_COUNT))
            {
                return 0;
            }
            var currentClientPos = ClientStateSingleton.Instance.PlayerCoaltionLocationMetadata;
            var coalitionSecurity = SyncedServerSettings.Instance.GetSettingAsBool(ServerSettingsKeys.COALITION_AUDIO_SECURITY);
            var globalFrequencies = _serverSettings.GlobalFrequencies;
            var global = globalFrequencies.Contains(freq);
            int count = 0;

            foreach (var client in _clients)
            {
                if (!client.Key.Equals(_guid))
                {
                    // check that either coalition radio security is disabled OR the coalitions match
                    if (global|| (!coalitionSecurity || (client.Value.Coalition == currentClientPos.side)))
                    {

                        var radioInfo = client.Value.RadioInfo;

                        if (radioInfo != null)
                        {
                            RadioReceivingState radioReceivingState = null;
                            var receivingRadio = radioInfo.CanHearTransmission(freq,
                                modulation,
                                out radioReceivingState);

                            //only send if we can hear!
                            if (receivingRadio != null)
                            {
                                count++;
                            }
                        }
                    }
                }
            }

            return count;
        }

        public void DecodeVcs(MapField<string, ClientInfo> clients, MapField<string, RadioInfo> radios)
        {
            foreach (var clientId in clients.Keys)
            {
                var clientGuid = Guid.Parse(clientId);
                var clientInfo = clients[clientId];
                var srClient = new SRClient()
                {
                    ClientGuid = clientGuid,
                    Name = clientInfo.Name,
                    Coalition = 0, // Default to 0, will be set later as we refactor coalition handling
                    AllowRecord = true,
                    Muted = radios[clientId].Muted,
                    LastUpdate = clientInfo.LastUpdate,
                    Seat = 0,
                    RadioInfo = new DCSPlayerRadioInfo()
                    {
                        radios = radios[clientId].Radios.Select(r => new RadioInformation
                        {
                            freq = r.Frequency,
                            modulation = r.Enabled ? RadioInformation.Modulation.DISABLED : r.IsIntercom ? RadioInformation.Modulation.INTERCOM : RadioInformation.Modulation.AM,
                            name = r.Name,
                            enc = false,
                            freqMax = 9999999999,
                            freqMin = 1,
                        }).ToArray()
                    }
                };
                if (_clients.TryGetValue(clientGuid, out var existingClient))
                {
                    // Update existing client
                    existingClient.Name = srClient.Name;
                    existingClient.RadioInfo = srClient.RadioInfo;
                    existingClient.Muted = srClient.Muted;
                    existingClient.LastUpdate = srClient.LastUpdate;
                    existingClient.RadioInfo = srClient.RadioInfo;
                }
                else
                {
                    // Add new client
                    _clients.TryAdd(clientGuid, srClient);
                }
            }
        }
    }
}
