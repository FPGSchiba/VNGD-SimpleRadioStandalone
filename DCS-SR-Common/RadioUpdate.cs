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

            return true;
        }

    };
    public class DCSRadios
    {
        public long lastUpdate = 0;
        public string name = "";
        public string unit = "";
        public short selected = 0;
        public int unitId;


        public RadioInformation[] radios = new RadioInformation[3];
        public bool hasRadio = false;
        public bool allowNonPlayers = true;
        public bool groundCommander = false;

        // override object.Equals
        public override bool Equals(object compare)
        {

            if (compare == null || GetType() != compare.GetType())
            {
                return false;
            }

            DCSRadios compareRadio = compare as DCSRadios;

            if (groundCommander != compareRadio.groundCommander)
            {
                return false;
            }
            if (hasRadio != compareRadio.hasRadio)
            {
                return false;
            }
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

            for(int i =0;i<3;i++)
            {
                RadioInformation radio1 = this.radios[i];
                RadioInformation radio2 = compareRadio.radios[i];

                if(radio1!=null && radio2 !=null)
                {
                    if(!radio1.Equals(radio2))
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
