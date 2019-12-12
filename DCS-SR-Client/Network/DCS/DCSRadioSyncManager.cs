using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Newtonsoft.Json;
using NLog;

/**
Keeps radio information in Sync Between DCS and

**/

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class DCSRadioSyncManager
    {
        public static readonly string AWACS_RADIOS_FILE = "awacs-radios.json";
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly ClientStateSingleton _clientStateSingleton = ClientStateSingleton.Instance;
        private readonly DCSGameGuiHandler _dcsGameGuiHandler;
        private readonly DCSLineOfSightHandler _lineOfSightHandler;
        private readonly UDPCommandHandler _udpCommandHandler; 
        private readonly DCSRadioSyncHandler _dcsRadioSyncHandler;

        public delegate void ClientSideUpdate();
        public delegate void SendRadioUpdate();

        private volatile bool _stopExternalAWACSMode;

        private readonly ConcurrentDictionary<string, SRClient> _clients;

        public bool IsListening { get; private set; }

        public DCSRadioSyncManager(SendRadioUpdate clientRadioUpdate, ClientSideUpdate clientSideUpdate,
            ConcurrentDictionary<string, SRClient> clients, string guid, DCSRadioSyncHandler.NewAircraft _newAircraftCallback)
        {
            this._clients = clients;
            IsListening = false;
            _lineOfSightHandler = new DCSLineOfSightHandler(clients,guid);
            _udpCommandHandler = new UDPCommandHandler();
            _dcsGameGuiHandler = new DCSGameGuiHandler(clientSideUpdate);
            _dcsRadioSyncHandler = new DCSRadioSyncHandler(clientRadioUpdate, _clients, _newAircraftCallback);
        }

        public void Start()
        {
            DcsListener();
            IsListening = true;
        }

        public void StartExternalAWACSModeLoop()
        {
            _stopExternalAWACSMode = false;

            RadioInformation[] awacsRadios;
            try
            {
                string radioJson = File.ReadAllText(AWACS_RADIOS_FILE);
                awacsRadios = JsonConvert.DeserializeObject<RadioInformation[]>(radioJson);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load AWACS radio file");

                awacsRadios = new RadioInformation[11];
                for (int i = 0; i < 11; i++)
                {
                    awacsRadios[i] = new RadioInformation
                    {
                        freq = 1,
                        freqMin = 1,
                        freqMax = 1,
                        secFreq = 0,
                        modulation = RadioInformation.Modulation.DISABLED,
                        name = "No Radio",
                        freqMode = RadioInformation.FreqMode.COCKPIT,
                        encMode = RadioInformation.EncryptionMode.NO_ENCRYPTION,
                        volMode = RadioInformation.VolumeMode.COCKPIT
                    };
                }
            }

            // Force an immediate update of radio information
            _clientStateSingleton.LastSent = 0;

            Task.Factory.StartNew(() =>
            {
                Logger.Debug("Starting external AWACS mode loop");

                while (!_stopExternalAWACSMode)
                {
                    _dcsRadioSyncHandler.ProcessRadioInfo(new DCSPlayerRadioInfo
                    {
                        LastUpdate = 0,
                        control = DCSPlayerRadioInfo.RadioSwitchControls.HOTAS,
                        name = _clientStateSingleton.LastSeenName,
                        pos = new DcsPosition { x = 0, y = 0, z = 0 },
                        ptt = false,
                        radios = awacsRadios,
                        selected = 1,
                        latLng = new DCSLatLngPosition(),
                        simultaneousTransmission = false,
                        unit = "External AWACS",
                        unitId = 100000001,
                        inAircraft = false
                    });

                    Thread.Sleep(200);
                }

                Logger.Debug("Stopping external AWACS mode loop");
            });
        }

        public void StopExternalAWACSModeLoop()
        {
            _stopExternalAWACSMode = true;
        }

        private void DcsListener()
        {
            _dcsRadioSyncHandler.Start();
            _dcsGameGuiHandler.Start();
            _lineOfSightHandler.Start();
            _udpCommandHandler.Start();
        }

        public void Stop()
        {
            _stopExternalAWACSMode = true;
            IsListening = false;

            _dcsRadioSyncHandler.Stop();
            _dcsGameGuiHandler.Stop();
            _lineOfSightHandler.Stop();
            _udpCommandHandler.Stop();
        }
    }
}