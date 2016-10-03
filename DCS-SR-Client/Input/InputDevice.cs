using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public enum InputBinding
    {
        Switch1 = 0,
        Switch2 = 1,
        Switch3 = 2,
        Ptt = 3,

        ModifierSwitch1 = 4,
        ModifierSwitch2 = 5,
        ModifierSwitch3 = 6,
        ModifierPtt = 7,

        OverlayToggle = 8, //all buttons that are "main" buttons will be in hundreds
        ModifierOverlayToggle = 9,

        Intercom = 10,
        ModifierIntercom = 11,

        Switch4 = 12,
        ModifierSwitch4 = 13,

        Switch5 = 14,
        ModifierSwitch5 = 15,

        Switch6 = 16,
        ModifierSwitch6 = 17,

        Switch7 = 18,
        ModifierSwitch7 = 19,

        Switch8 = 20,
        ModifierSwitch8 = 21,

        Switch9 = 22,
        ModifierSwitch9 = 23,

        Switch10 = 24,
        ModifierSwitch10 = 25,
    }

    public class InputDevice
    {
        public InputBinding InputBind { get; set; }

        public string DeviceName { get; set; }

        public int Button { get; set; }
        public Guid InstanceGuid { get; internal set; }
        public int ButtonValue { get; internal set; }
    }
}