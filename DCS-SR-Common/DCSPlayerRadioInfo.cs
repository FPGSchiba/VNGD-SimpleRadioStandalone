using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class RadioInformation
    {
        public string name = "";
        public double frequency = 1;
        public sbyte modulation = 0;
        public float volume = 1.0f;
        public double secondaryFrequency = 1;
        public double freqMin = 1;
        public double freqMax = 1;

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
                return false;

            RadioInformation compare = (RadioInformation)obj;

            if (!name.Equals(compare.name))
            {
                return false;
            }
            if (frequency != compare.frequency)
            {
                return false;
            }

            if (modulation != compare.modulation)
            {
                return false;
            }
            if (secondaryFrequency != compare.secondaryFrequency)
            {
                return false;
            }
            if (volume != compare.volume)
            {
                return false;
            }
            if (freqMin != compare.freqMin)
            {
                return false;
            }
            if (freqMax != compare.freqMax)
            {
                return false;
            }


            return true;
        }
    };

    public class DCSPlayerRadioInfo
    {
        public enum AircraftRadioType
        {
            FULL_COCKPIT_INTEGRATION = 1,
            PARTIAL_COCKPIT_INTEGRATION = 2,
            NO_COCKPIT_INTEGRATION = 3,
        }


        public long lastUpdate = 0;
        public string name = "";
        public string unit = "";
        public short selected = 0;
        public int unitId;
        // public int side = 0; // 1 = red, 2 = blue, 0 = none


        public RadioInformation[] radios = new RadioInformation[3];
        public AircraftRadioType radioType = AircraftRadioType.NO_COCKPIT_INTEGRATION;
        //1 - Full Radio - No Switch or frequency
        //2 - Partial Radio - Allow Radio Switch but no frequency
        //3 - FC3 / Spectator - Allow Radio Switch + Frequency

        public DCSPlayerRadioInfo()
        {
            for (int i = 0; i < 3; i++)
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

            DCSPlayerRadioInfo compareRadio = compare as DCSPlayerRadioInfo;

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
            if (selected != compareRadio.selected)
            {
                return false;
            }
            if (unitId != compareRadio.unitId)
            {
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                RadioInformation radio1 = this.radios[i];
                RadioInformation radio2 = compareRadio.radios[i];

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


        public bool isCurrent()
        {
            return this.lastUpdate > (System.Environment.TickCount - 10000);
        }
    };
}