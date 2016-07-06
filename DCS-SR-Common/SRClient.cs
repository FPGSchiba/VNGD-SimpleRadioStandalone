using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class SRClient
    {
        public string ClientGuid { get; set; }

        public String Name { get; set; }

        public int Coalition { get; set; }

        [JsonIgnore]
        public Socket ClientSocket { get; set; }

        [JsonIgnore]
        public IPEndPoint voipPort { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        public bool isCurrent()
        {
            return LastUpdate > (System.Environment.TickCount - 10000); //last in game 10 seconds ago
        }

        public override string ToString()
        {
            return Name;
        }
    }
}