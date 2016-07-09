using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputDevice
    {
        public enum InputBinding
        {
            Switch1,
            Switch2,
            Switch3,
            Ptt
        }

        public InputBinding InputBind { get; set; }

        public string DeviceName { get; set; }

        public int Button { get; set; }
        public Guid InstanceGuid { get; internal set; }
    }
}