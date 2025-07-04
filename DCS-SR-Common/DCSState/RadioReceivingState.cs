using System;
using System.Collections.Concurrent;
using Vanguard.VCS.Common.Network;
using Newtonsoft.Json;
using NLog.Layouts;

namespace Vanguard.VCS.Common.DCSState
{
    public class RadioReceivingState
    {
        [JsonIgnore]
        public long LastReceviedAt { get; set; }

        public bool IsSecondary { get; set; }
        public bool IsSimultaneous { get; set; }
        public int ReceivedOn { get; set; }

        public string SentBy { get; set; }

        public bool IsReceiving
        {
            get
            {
                return (DateTime.Now.Ticks - LastReceviedAt) < 3500000;
            }
        }
    }
}