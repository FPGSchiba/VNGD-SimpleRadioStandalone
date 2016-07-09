using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public class DCSPlayerRadioInfo
    {
        public enum AircraftRadioType
        {
            FULL_COCKPIT_INTEGRATION = 1,
            PARTIAL_COCKPIT_INTEGRATION = 2,
            NO_COCKPIT_INTEGRATION = 3
        }


        public long lastUpdate = 0;
        public string name = "";
        // public int side = 0; // 1 = red, 2 = blue, 0 = none


        public RadioInformation[] radios = new RadioInformation[3];
        public AircraftRadioType radioType = AircraftRadioType.NO_COCKPIT_INTEGRATION;
        public short selected = 0;
        public string unit = "";
        public int unitId;
        //1 - Full Radio - No Switch or frequency
        //2 - Partial Radio - Allow Radio Switch but no frequency
        //3 - FC3 / Spectator - Allow Radio Switch + Frequency

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
            if (selected != compareRadio.selected)
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


        public bool isCurrent()
        {
            return lastUpdate > Environment.TickCount - 10000;
        }
    }
}