using Ciribob.DCS.SimpleRadio.Standalone.Common.DCSState;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Windows.Media;
using Newtonsoft.Json;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common.Network
{
    public partial class SRClient : INotifyPropertyChanged
    {
        private int _coalition;

        [JsonIgnore]
        private float _lineOfSightLoss; // 0.0 is NO Loss therefore Full line of sight

        public string ClientGuid { get; set; }
        private string _name = "";

        public string Name
        {
            get { return _name; }
            set
            {
                if (value == null || value == "")
                {
                    value = "---";
                }

                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Name"));
                }
            }
        }

        public int Seat { get; set; }

        public int Coalition
        {
            get { return _coalition; }
            set
            {
                _coalition = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Coalition"));
            }
        }

        public bool AllowRecord { get; set; }

        [JsonIgnore]
        public string AllowRecordingStatus
        {
            get { return AllowRecord ? "R" : "-"; }
        }

        [JsonIgnore]
        public SolidColorBrush ClientCoalitionColour
        {
            get
            {
                switch (Coalition)
                {
                    case 0:
                        return new SolidColorBrush(Colors.White);
                    case 1:
                        return new SolidColorBrush(Colors.Red);
                    case 2:
                        return new SolidColorBrush(Colors.Blue);
                    default:
                        return new SolidColorBrush(Colors.White);
                }
            }
        }

        [JsonIgnore]
        public bool Muted { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        [JsonIgnore]
        public IPEndPoint VoipPort { get; set; }

        [JsonIgnore]
        public long LastRadioUpdateSent { get; set; }

        public DCSPlayerRadioInfo RadioInfo { get; set; }

        [JsonDCSIgnoreSerialization]
        public DCSLatLngPosition LatLngPosition { get; set; }

        [JsonIgnore]
        public float LineOfSightLoss
        {
            get
            {
                if (_lineOfSightLoss == 0) return 0;
                if ((LatLngPosition.lat == 0) && (LatLngPosition.lng == 0)) return 0;
                return _lineOfSightLoss;
            }
            set { _lineOfSightLoss = value; }
        }

        private string _transmittingFrequency;
        [JsonIgnore]
        public string TransmittingFrequency
        {
            get { return _transmittingFrequency; }
            set
            {
                if (_transmittingFrequency != value)
                {
                    _transmittingFrequency = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("TransmittingFrequency"));
                }
            }
        }

        [JsonIgnore]
        public DateTime LastTransmissionReceived { get; set; }

        [JsonIgnore]
        public object ClientSession { get; set; }

        // Ship state properties
        private ShipCondition _shipCondition;
        private Dictionary<ShipComponent, string> _shipComponentStates;

        public ShipCondition ShipCondition
        {
            get { return _shipCondition; }
            set
            {
                if (_shipCondition != value)
                {
                    _shipCondition = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShipCondition)));
                }
            }
        }

        public Dictionary<ShipComponent, string> ShipComponentStates
        {
            get { return _shipComponentStates ?? (_shipComponentStates = new Dictionary<ShipComponent, string>()); }
            set
            {
                _shipComponentStates = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShipComponentStates)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString()
        {
            string side;
            switch (Coalition)
            {
                case 1:
                    side = "Red";
                    break;
                case 2:
                    side = "Blue";
                    break;
                default:
                    side = "Spectator";
                    break;
            }
            return Name == "" ? "Unknown" : $"{Name} - {side} LOS Loss {_lineOfSightLoss} Pos{LatLngPosition}";
        }
    }
}