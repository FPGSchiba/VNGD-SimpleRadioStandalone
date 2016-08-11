using Newtonsoft.Json;
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class DCSPlayerRadioInfo
    {
        //1 - Full Radio - No Switch or frequency
        //2 - Partial Radio - Allow Radio Switch but no frequency
        //3 - FC3 / Spectator - Allow Radio Switch + Frequency
        public enum AircraftRadioType
        {
            FULL_COCKPIT_INTEGRATION = 1,
            PARTIAL_COCKPIT_INTEGRATION = 2,
            NO_COCKPIT_INTEGRATION = 3
        }

        [JsonIgnore]
        public long LastUpdate { get; set; }

        public string name = "";

        public RadioInformation[] radios = new RadioInformation[3];
        public AircraftRadioType radioType = AircraftRadioType.NO_COCKPIT_INTEGRATION;
        public short selected = 0;
        public string unit = "";
        public UInt32 unitId;
        public volatile  bool ptt = false;
        public DcsPosition pos = new DcsPosition();

        public DCSPlayerRadioInfo()
        {
            for (var i = 0; i < 3; i++)
            {
                radios[i] = new RadioInformation();
            }
        }

        // override object.Equals
        public override bool Equals(object compare)
        {
            if (compare == null || GetType() != compare.GetType())
            {
                return false;
            }

            var compareRadio = compare as DCSPlayerRadioInfo;

            if (radioType != compareRadio.radioType)
            {
                return false;
            }
            //if (side != compareRadio.side)
            //{
            //    return false;
            //}
            if (!name.Equals(compareRadio.name))
            {
                return false;
            }
            if (!unit.Equals(compareRadio.unit))
            {
                return false;
            }
   
            if (unitId != compareRadio.unitId)
            {
                return false;
            }

            for (var i = 0; i < 3; i++)
            {
                var radio1 = radios[i];
                var radio2 = compareRadio.radios[i];

                if (radio1 != null && radio2 != null)
                {
                    if (!radio1.Equals(radio2))
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        /*
         * Was Radio updated in the last 10 Seconds
         */
        public bool IsCurrent()
        {
            return LastUpdate > Environment.TickCount - 10000;
        }

        public RadioInformation CanHearTransmission(double frequency, byte modulation, UInt32 unitId,
           out RadioReceivingState receivingState)
        {
            if (!this.IsCurrent())
            {
                receivingState = null;
                return null;
            }
            for (var i = 0; i < 3; i++)
            {
                var receivingRadio = this.radios[i];

                if (receivingRadio != null)
                {
                    //handle INTERCOM Modulation is 2
                    if (receivingRadio.modulation == 2 && modulation == 2
                        && this.unitId > 0 && unitId > 0
                        && this.unitId == unitId)
                    {
                        receivingState = new RadioReceivingState()
                        {
                            IsSecondary = false,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };

                      
                        return receivingRadio;
                    }
                    if (receivingRadio.frequency == frequency
                        && receivingRadio.modulation == modulation
                        && receivingRadio.frequency > 1)
                    {
                        receivingState = new RadioReceivingState()
                        {
                            IsSecondary = false,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };
                       
                        return receivingRadio;
                    }
                    if (receivingRadio.secondaryFrequency == frequency
                        && receivingRadio.secondaryFrequency > 100)
                    {
                        receivingState = new RadioReceivingState()
                        {
                            IsSecondary = true,
                            LastReceviedAt = Environment.TickCount,
                            ReceivedOn = i
                        };
                       
                        return receivingRadio;
                    }
                }
            }
            receivingState = null;
            return null;
        }
    }
}