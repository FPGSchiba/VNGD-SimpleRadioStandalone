using System;
using System.Net;
using System.Net.Sockets;
using Newtonsoft.Json;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class SRClient
    {
        public string ClientGuid { get; set; }

        public string Name { get; set; }

        public int Coalition { get; set; }

        [JsonIgnore]
        public Socket ClientSocket { get; set; }

        [JsonIgnore]
        public IPEndPoint voipPort { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        public bool isCurrent()
        {
            return LastUpdate > Environment.TickCount - 10000; //last in game 10 seconds ago
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