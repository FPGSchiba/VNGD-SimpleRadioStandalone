using System;
using Newtonsoft.Json;
using NLog.Layouts;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioReceivingState
    {
        [JsonIgnore]
        public double LastReceviedAt { get; set; }

        public bool IsSecondary { get; set; }
        public int ReceivedOn { get; set; }

        public bool PlayedEndOfTransmission { get; set; }

        public bool IsReceiving => (Environment.TickCount - LastReceviedAt) < 500;
    }
}