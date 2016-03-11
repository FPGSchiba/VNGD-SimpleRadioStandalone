using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class SRClient
    {
        public string ClientGuid { get; set; }

        public DCSRadios ClientRadios { get; set; }

        [JsonIgnore]
        public Socket ClientSocket { get; set; }

        [JsonIgnore]
        public long LastUpdate { get; set; }
    }
}
