using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class SRClient : INotifyPropertyChanged
    {
        //  public DcsPosition Position { get; set; }

        private int _coalition;

        [JsonIgnore] private float _lineOfSightLoss; // 0.0 is NO Loss therefore Full line of sight

        public string ClientGuid { get; set; }

        public string Name { get; set; }


        public int Coalition
        {
            get { return _coalition; }
            set
            {
                _coalition = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Coalition"));
            }
        }

        [JsonIgnore]
        public Socket ClientSocket { get; set; }

        [JsonIgnore]
        public IPEndPoint VoipPort { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        public DCSPlayerRadioInfo RadioInfo { get; set; }
        public DcsPosition Position { get; set; }

        [JsonIgnore]
        public float LineOfSightLoss
        {
            get
            {
                if (_lineOfSightLoss == 0)
                {
                    return 0;
                }
                if ((Position.x == 0) && (Position.z == 0))
                {
                    return 0;
                }
                return _lineOfSightLoss;
            }
            set { _lineOfSightLoss = value; }
        }

        public event PropertyChangedEventHandler PropertyChanged;


        public bool isCurrent()
        {
            return true;
//             if(LastUpdate > Environment.TickCount - 10000)//last in game 10 seconds ago
//             {
//                Console.WriteLine("NOT CURRENT!");
//                 return true;
//             }
//            else
//            {
//                return true;
//            }
        }

        public override string ToString()
        {
            string side;

            if (Coalition == 1)
            {
                side = "Red";
            }
            else if (Coalition == 2)
            {
                side = "Blue";
            }
            else
            {
                side = "Spectator";
            }
            return Name == "" ? "Unknown" : Name + " - " + side + " LOS Loss " + _lineOfSightLoss + " Pos" + Position;
        }
    }
}