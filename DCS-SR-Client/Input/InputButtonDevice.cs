using System;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Settings
{
    public class InputButtonDevice : InputDeviceBase
    {
        public int Button { get; set; }
        public int ButtonValue { get; internal set; }

        public override bool IsSameBind(InputDeviceBase compare)
        {
            InputButtonDevice converted = compare as InputButtonDevice;
            return Button == converted.Button &&
                       converted.InstanceGuid == InstanceGuid &&
                       ButtonValue == converted.ButtonValue;
        }
    }
}