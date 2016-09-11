using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioReceivingState
    {
        public double LastReceviedAt { get; set; }

        public bool IsSecondary { get; set; }
        public int ReceivedOn { get; set; }

        public bool PlayedEndOfTransmission { get; set; }
        //   public bool Encrypted { get; set; }

        public bool IsReceiving()
        {
            return Environment.TickCount - LastReceviedAt < 200;
        }
    }
}