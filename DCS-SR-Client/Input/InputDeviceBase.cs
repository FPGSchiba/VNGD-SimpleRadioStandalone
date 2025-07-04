using System;
using Vanguard.VCS.Client.Settings;

namespace Vanguard.VCS.Client.Input
{
    abstract public class InputDeviceBase
    {
        public InputBinding InputBind { get; set; }
        public string DeviceName { get; set; }
        public Guid InstanceGuid { get; internal set; }
        public abstract bool IsSameBind(InputDeviceBase compare);
    }
}