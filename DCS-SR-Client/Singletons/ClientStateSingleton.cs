using System;
using System.Collections.Generic;
using System.ComponentModel;
using Vanguard.VCS.Client.Network.Models;
using Vanguard.VCS.Client.Network.VAICOM.Models;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Settings.RadioChannels;
using Vanguard.VCS.Client.UI.RadioOverlayWindow.PresetChannels;
using Vanguard.VCS.Common;
using Vanguard.VCS.Common.DCSState;
using Vanguard.VCS.Common.Network;

namespace Vanguard.VCS.Client.Singletons
{
    public sealed class ClientStateSingleton : INotifyPropertyChanged
    {
        private static volatile ClientStateSingleton _instance;
        private static object _lock = new Object();

        public delegate bool RadioUpdatedCallback();

        private List<RadioUpdatedCallback> _radioCallbacks = new List<RadioUpdatedCallback>();

        public event PropertyChangedEventHandler PropertyChanged;

        // Timestamp the last UDP Game GUI broadcast was received from DCS, used for determining active game connection
        public long DcsGameGuiLastReceived { get; set; }

        // Timestamp for the last time
        public long LotATCLastReceived { get; set; }

        //store radio channels here?
        public PresetChannelsViewModel[] FixedChannels { get; }
        public PresetStandbyChannelsViewModel[] StandbyChannels { get; }

        public long LastPositionCoalitionSent { get; set; }

        public RadioSendingState RadioSendingState { get; set; }
        public  RadioReceivingState[] RadioReceivingState { get; }

        private bool isConnected;
        public bool IsConnected
        {
            get
            {
                return isConnected;
            }
            set
            {
                isConnected = value;
                NotifyPropertyChanged("IsConnected");
            }
        }

        private bool isVoipConnected;
        public bool IsVoipConnected
        {
            get
            {
                return isVoipConnected;
            }
            set
            {
                isVoipConnected = value;
                NotifyPropertyChanged("IsVoipConnected");
            }
        }

        private volatile bool _isServerMuted;
        public bool IsServerMuted
        {
            get => _isServerMuted;
            set
            {
                _isServerMuted = value;
                NotifyPropertyChanged(nameof(IsServerMuted));
            }
        }

        private bool isConnectionErrored;
        public Guid ClientId { get; private set; }

        public void RegisterClientGuid(Guid guid)
        {
            ClientId = guid;
        }
        
        public bool IsConnectionErrored
        {
            get
            {
                return isConnectionErrored;
            }
            set
            {
                isConnectionErrored = value;
                NotifyPropertyChanged("isConnectionErrored");
            }
        }

        public bool IsLotATCConnected { get { return LotATCLastReceived >= DateTime.Now.Ticks - 50000000; } }

        public bool IsGameGuiConnected { get { return DcsGameGuiLastReceived >= DateTime.Now.Ticks - 100000000; } }

        public string LastSeenName { get; set; }

        public VAICOMMessageWrapper InhibitTX { get; set; } = new VAICOMMessageWrapper(); //used to temporarily stop PTT for VAICOM

        private ClientStateSingleton()
        {
            RadioSendingState = new RadioSendingState();
            RadioReceivingState = new RadioReceivingState[11];

            ClientId = ShortGuid.NewGuid();

            DcsGameGuiLastReceived = 0;

            FixedChannels = new PresetChannelsViewModel[10];
            StandbyChannels = new PresetStandbyChannelsViewModel[10];
            
            for (int i = 0; i < FixedChannels.Length; i++)
            {
                FixedChannels[i] = new PresetChannelsViewModel(new FilePresetChannelsStore(), i + 1);
                StandbyChannels[i] = new PresetStandbyChannelsViewModel(new FilePresetChannelsStore(), i + 1);
            }

            IsConnected = false;

            LastSeenName = GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.LastSeenName).RawValue;
        }
        
        public void SetGuid(Guid guid)
        {
            if (guid == Guid.Empty)
                throw new ArgumentException("GUID cannot be null or empty.", nameof(guid));

            ClientId = guid;
            NotifyPropertyChanged(nameof(ClientId));
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

        private void NotifyPropertyChanged(string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}