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
//             if(LastUpdate > Environment.TickCount - 10000)//last in game 10 seconds ago
//             {
//                Console.WriteLine("NOT CURRENT!");
//                 return true;
//             }
//            else
//            {
                return true;
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