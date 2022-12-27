using System.Runtime.InteropServices;
using System;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    static unsafe class Native
    {
        [DllImport("XINPUT1_4.DLL")]
        public static extern uint XInputGetState(uint dwUserIndex, SharpDX.XInput.State* pState);
    }

    /// <summary>
    /// Controller class that masquerades as a SharpDX.DirectInput.Device.
    /// </summary>
    public class XInputController : IDisposable
    {
        public struct InformationData
        {
            // Randomly generated Guid.
            static Guid _productGuid = new Guid("bb78c26f-9bfd-41c1-81f0-2f1258d25075");

            public string ProductName
            {
                get { return "XInputController"; }
            }

            public Guid InstanceGuid
            {
                get { return ProductGuid; }
            }

            public static Guid ProductGuid
            {
                get { return _productGuid; }
            }
        }

        public static Guid DeviceGuid
        {
            get { return InformationData.ProductGuid; }
        }

        private InformationData _information;
        private bool _disposed = false;
        private SharpDX.XInput.State _state = new SharpDX.XInput.State();

        // Dispose interface, not much to do.
        public bool IsDisposed
        {
            get
            {
                return _disposed;
            }
        }

        public void Dispose()
        {
            // noop.
            _disposed = true;
        }

        // SharpDX.DirectInput.Device masquerading.
        public bool Poll()
        {
            unsafe
            {
                fixed (SharpDX.XInput.State* pstate = &_state)
                {
                    return Native.XInputGetState(0, pstate) == 0;
                }
            }
        }

        public InformationData Information
        {
            get
            {
                return _information;
            }
        }

        public SharpDX.XInput.GamepadButtonFlags GetCurrentState()
        {
            return _state.Gamepad.Buttons;
        }

        public void Unacquire()
        {
            // noop.
        }
    }
}