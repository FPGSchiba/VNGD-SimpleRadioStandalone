using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public enum InputBinding
    {
        Switch1,
        Switch2,
        Switch3,
        Ptt,
        ModifierSwitch1,
        ModifierSwitch2,
        ModifierSwitch3,
        ModifierPtt,

    }

    public class InputDevice
    {
     
        public InputBinding InputBind { get; set; }

        public string DeviceName { get; set; }

        public int Button { get; set; }
        public Guid InstanceGuid { get; internal set; }
    }
}