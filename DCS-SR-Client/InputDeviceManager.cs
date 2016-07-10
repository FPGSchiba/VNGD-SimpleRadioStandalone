using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using NLog;
using SharpDX.DirectInput;
using SharpDX.Multimedia;
using static Ciribob.DCS.SimpleRadio.Standalone.Client.InputDevice;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(bool state, InputBinding inputType);

        private volatile bool _detectPtt;
        private readonly DirectInput _directInput;
        private readonly List<Device> _inputDevices = new List<Device>();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public InputDeviceManager(Window window)
        {
            InputConfig = new InputConfiguration();
            _directInput = new DirectInput();
            var deviceInstances = _directInput.GetDevices();

            foreach (var deviceInstance in deviceInstances)
            {
                Device device = null;

                //Corsair K65 Gaming Keyboard breaks... It reports as a Joystick when its a keyboard...
                if (deviceInstance.ProductGuid != new Guid("1b171b1c-0000-0000-0000-504944564944"))
                {
                    //Workaround for RGB keyboard
                    device = new Joystick(_directInput, deviceInstance.ProductGuid);
                    Logger.Info("Found " + deviceInstance.ProductGuid + " "+deviceInstance.ProductName);

                    if (deviceInstance.Type == DeviceType.Keyboard)
                    {
                        device = new Keyboard(_directInput);
                    }
                    else if (deviceInstance.Usage == UsageId.GenericJoystick)
                    {
                        device = new Joystick(_directInput, deviceInstance.ProductGuid);
                    }
                    else if (deviceInstance.Usage == UsageId.GenericGamepad)
                    {
                        device = new Joystick(_directInput, deviceInstance.ProductGuid);
                    }

                }

                if (device != null)
                {
                    var helper =
                        new WindowInteropHelper(window);

                    device.SetCooperativeLevel(helper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(device);
                }
            }
        }

        public InputConfiguration InputConfig { get; set; }


        public void Dispose()
        {
            StopPtt();
            foreach (var device in _inputDevices)
            {
                if (device != null)
                {
                    device.Unacquire();
                    device.Dispose();
                }
            }
        }

        public void AssignButton(DetectButton callback)
        {
            //detect the state of all current buttons
            Task.Run(() =>
            {
                var initial = new bool[_inputDevices.Count, 128];

                for (var i = 0; i < _inputDevices.Count; i++)
                {
                    _inputDevices[i].Poll();

                    if (_inputDevices[i] is Joystick)
                    {
                        var state = (_inputDevices[i] as Joystick).GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.Buttons[j];
                        }
                    }
                    else if (_inputDevices[i] is Keyboard)
                    {
                        var state = (_inputDevices[i] as Keyboard).GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.IsPressed(state.AllKeys[j]);
                        }
                    }
                }

                var device = string.Empty;
                var button = 0;
                var deviceGuid = Guid.Empty;
                var found = false;

                while (!found)
                {
                    Thread.Sleep(100);

                    for (var i = 0; i < _inputDevices.Count; i++)
                    {
                        _inputDevices[i].Poll();

                        if (_inputDevices[i] is Joystick)
                        {
                            var state = (_inputDevices[i] as Joystick).GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                if (state.Buttons[j] && !initial[i, j])
                                {
                                    found = true;
                                    button = j;
                                    device = _inputDevices[i].Information.ProductName.Trim().Replace("\0", "");
                                    deviceGuid = _inputDevices[i].Information.InstanceGuid;
                                    break;
                                }
                            }
                        }
                        else if (_inputDevices[i] is Keyboard)
                        {
                            var state = (_inputDevices[i] as Keyboard).GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                if (state.IsPressed(state.AllKeys[j]) && !initial[i, j])
                                {
                                    found = true;
                                    button = j;
                                    device = _inputDevices[i].Information.ProductName.Trim().Replace("\0", "");
                                    deviceGuid = _inputDevices[i].Information.InstanceGuid;
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            break;
                        }
                    }
                }


                Application.Current.Dispatcher.Invoke(
                    () =>
                    {
                        callback(new InputDevice {DeviceName = device, Button = button, InstanceGuid = deviceGuid});
                    });
            });
        }

        public void StartDetectPtt(DetectPttCallback callback)
        {
            _detectPtt = true;
            //detect the state of all current buttons
            //TODO replace task.run with thread

            var pttInputThread = new Thread(() =>
            {
                while (_detectPtt)
                {
                    foreach (var device in InputConfig.InputDevices)
                    {
                        if (device != null)
                        {
                            for (var i = 0; i < _inputDevices.Count; i++)
                            {
                                if (_inputDevices[i].Information.InstanceGuid.Equals(device.InstanceGuid))
                                {
                                    if (_inputDevices[i] is Joystick)
                                    {
                                        _inputDevices[i].Poll();
                                        var state = (_inputDevices[i] as Joystick).GetCurrentState();

                                        callback(state.Buttons[device.Button], device.InputBind);
                                        break;
                                    }
                                    if (_inputDevices[i] is Keyboard)
                                    {
                                        _inputDevices[i].Poll();
                                        var state = (_inputDevices[i] as Keyboard).GetCurrentState();

                                        callback(state.IsPressed(state.AllKeys[device.Button]), device.InputBind);
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    Thread.Sleep(1);
                }
            });
            pttInputThread.Start();
        }

        public void StopPtt()
        {
            _detectPtt = false;
        }
    }
}