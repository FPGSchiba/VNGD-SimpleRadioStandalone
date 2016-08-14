using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    public struct DcsPosition
    {
        public double x;
        public double y;
        public double z;

        public override string ToString()
        {

            return $"Pos:[{x},{y},{z}]"; 
        }
    }

}
