using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class SRClient:INotifyPropertyChanged
    {
      
        public string ClientGuid { get; set; }

        public string Name { get; set; }

        private int _coalition;
        public int Coalition
        {
            get { return _coalition; }
            set
            {
                _coalition = value;
                PropertyChanged?.Invoke(this,new PropertyChangedEventArgs("Coalition"));
            }
        }

        [JsonIgnore]
        public Socket ClientSocket { get; set; }

        [JsonIgnore]
        public IPEndPoint voipPort { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }

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
            return Name == "" ? "Unknown" : Name + " - " + side;
        }
    }
}