using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputAxisDevice : InputDeviceBase
    {
        public string Axis { get; set; }
        public double Curvature { get; set; }
        public bool Invert { get; set; }
        public bool UseAsButton { get; set; }
        public int AxisCenterValue { get; internal set; }
        //public int ActivationThreshold { get; set; }

        public override bool IsSameBind(InputDeviceBase compare)
        {
            InputAxisDevice converted = compare as InputAxisDevice;
            return Axis == converted.Axis &&
                  converted.InstanceGuid == InstanceGuid;
        }
    }
}
