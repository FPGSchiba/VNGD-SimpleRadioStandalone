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

}
