using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons
{
    public class ConnectedClientsSingleton
    {
        private readonly ConcurrentDictionary<string, SRClient> _clients = new ConcurrentDictionary<string, SRClient>();
        private static volatile ConnectedClientsSingleton _instance;
        private static object _lock = new Object();
        private readonly string guid = GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.CliendIdShort).StringValue;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;

        public ConcurrentDictionary<string, SRClient> Clients {
            get
            {
                return _clients;
            }
        }

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

        private ConnectedClientsSingleton()
        {

        }

        public int ClientsOnFreq(double freq, RadioInformation.Modulation modulation)
        {
            if (!_serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TUNED_COUNT))
            {
                return 0;
            }
            var currentClientPos = ClientStateSingleton.Instance.PlayerCoaltionLocationMetadata;
            var currentUnitId = ClientStateSingleton.Instance.DcsPlayerRadioInfo.unitId;
            var coalitionSecurity = SyncedServerSettings.Instance.GetSettingAsBool(ServerSettingsKeys.COALITION_AUDIO_SECURITY);
            var globalFrequencies = _serverSettings.GlobalFrequencies;
            var global = globalFrequencies.Contains(freq);
            int count = 0;

            foreach (var client in _clients)
            {
                if (!client.Key.Equals(guid))
                {
                    // check that either coalition radio security is disabled OR the coalitions match
                    if (global|| (!coalitionSecurity || (client.Value.Coalition == currentClientPos.side)))
                    {

                        var radioInfo = client.Value.RadioInfo;

                        if (radioInfo != null)
                        {
                            RadioReceivingState radioReceivingState = null;
                            bool decryptable;
                            var receivingRadio = radioInfo.CanHearTransmission(freq,
                                modulation,
                                0,
                                currentUnitId,
                                new List<int>(),
                                out radioReceivingState,
                                out decryptable);

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
    }
}
