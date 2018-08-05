using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.RadioChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons
{
    public sealed class ClientStateSingleton
    {
        private static volatile ClientStateSingleton _instance;
        private static object _lock = new Object();

        public delegate bool RadioUpdatedCallback();

        private List<RadioUpdatedCallback> _radioCallbacks = new List<RadioUpdatedCallback>();

        public DCSPlayerRadioInfo DcsPlayerRadioInfo { get; }
        public DCSPlayerSideInfo DcsPlayerSideInfo { get; set; }

        // Timestamp the last UDP Game GUI broadcast was received from DCS, used for determining active game connection
        public long DcsGameGuiLastReceived { get; set; }
        // Timestamp the last UDP Export broadcast was received from DCS, used for determining active game connection
        public long DcsExportLastReceived { get; set; }

        //store radio channels here?
        public PresetChannelsViewModel[] FixedChannels { get; }

        // Indicates whether a valid microphone is available - deactivating audio input controls and transmissions otherwise
        public bool MicrophoneAvailable { get; set; }

        public long LastSent { get; set; }

        public bool IsConnected { get; set; }

        public bool IsGameGuiConnected { get { return DcsGameGuiLastReceived >= DateTime.Now.Ticks - 100000000; } }
        public bool IsGameExportConnected { get { return DcsExportLastReceived >= DateTime.Now.Ticks - 100000000; } }
        // Indicates an active game connection has been detected (1 tick = 100ns, 100000000 ticks = 10s stale timer), not updated by EAM
        public bool IsGameConnected { get { return IsGameGuiConnected && IsGameExportConnected; } }

        public bool InExternalAWACSMode { get; set; }

        public string LastSeenName { get; set; }

        private ClientStateSingleton()
        {
            DcsPlayerRadioInfo = new DCSPlayerRadioInfo();
            DcsPlayerSideInfo = new DCSPlayerSideInfo();

            DcsGameGuiLastReceived = 0;
            DcsExportLastReceived = 0;

            FixedChannels = new PresetChannelsViewModel[10];

            for (int i = 0; i < FixedChannels.Length; i++)
            {
                FixedChannels[i] = new PresetChannelsViewModel(new FilePresetChannelsStore(), i + 1);
            }

            MicrophoneAvailable = true;

            LastSent = 0;

            IsConnected = false;

            InExternalAWACSMode = false;

            LastSeenName = Settings.SettingsStore.Instance.GetClientSetting(Settings.SettingsKeys.LastSeenName).StringValue;
        }

        public static ClientStateSingleton Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new ClientStateSingleton();
                    }
                }

                return _instance;
            }
        }
    }
}