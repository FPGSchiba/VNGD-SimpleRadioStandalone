using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using SharpDX.DirectInput;
using SharpDX.Multimedia;
using static Ciribob.DCS.SimpleRadio.Standalone.Client.InputDevice;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPTTCallback(bool state, InputBinding inputType);

        private volatile bool detectPTT;
        private readonly DirectInput directInput;
        private readonly List<Device> inputDevices = new List<Device>();

        public InputDeviceManager(Window window)
        {
            InputConfig = new InputConfiguration();
            directInput = new DirectInput();
            var deviceInstances = directInput.GetDevices();

            foreach (var deviceInstance in deviceInstances)
            {
                Device device = null;

                if (deviceInstance.Type == DeviceType.Keyboard)
                {
                    device = new Keyboard(directInput);
                }
                else if (deviceInstance.Usage == UsageId.GenericJoystick)
                {
                    device = new Joystick(directInput, deviceInstance.ProductGuid);
                }
                else if (deviceInstance.Usage == UsageId.GenericGamepad)
                {
                    device = new Joystick(directInput, deviceInstance.ProductGuid);
                }


                if (device != null)
                {
                    var helper =
                        new WindowInteropHelper(window);

                    device.SetCooperativeLevel(helper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    inputDevices.Add(device);
                }
            }
        }

        public InputConfiguration InputConfig { get; set; }


        public void Dispose()
        {
            StopPTT();
            foreach (var device in inputDevices)
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
                var initial = new bool[inputDevices.Count, 128];

                for (var i = 0; i < inputDevices.Count; i++)
                {
                    inputDevices[i].Poll();

                    if (inputDevices[i] is Joystick)
                    {
                        var state = (inputDevices[i] as Joystick).GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.Buttons[j];
                        }
                    }
                    else if (inputDevices[i] is Keyboard)
                    {
                        var state = (inputDevices[i] as Keyboard).GetCurrentState();

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

                    for (var i = 0; i < inputDevices.Count; i++)
                    {
                        inputDevices[i].Poll();

                        if (inputDevices[i] is Joystick)
                        {
                            var state = (inputDevices[i] as Joystick).GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                if (state.Buttons[j] && !initial[i, j])
                                {
                                    found = true;
                                    button = j;
                                    device = inputDevices[i].Information.ProductName.Trim().Replace("\0", "");
                                    deviceGuid = inputDevices[i].Information.InstanceGuid;
                                    break;
                                }
                            }
                        }
                        else if (inputDevices[i] is Keyboard)
                        {
                            var state = (inputDevices[i] as Keyboard).GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                if (state.IsPressed(state.AllKeys[j]) && !initial[i, j])
                                {
                                    found = true;
                                    button = j;
                                    device = inputDevices[i].Information.ProductName.Trim().Replace("\0", "");
                                    deviceGuid = inputDevices[i].Information.InstanceGuid;
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
                        callback(new InputDevice {DeviceName = device, Button = button, InstanceGUID = deviceGuid});
                    });
            });
        }

        public void StartDetectPTT(DetectPTTCallback callback)
        {
            detectPTT = true;
            //detect the state of all current buttons
            //TODO replace task.run with thread

            var pttInputThread = new Thread(() =>
            {
                while (detectPTT)
                {
                    foreach (var device in InputConfig.inputDevices)
                    {
                        if (device != null)
                        {
                            for (var i = 0; i < inputDevices.Count; i++)
                            {
                                if (inputDevices[i].Information.InstanceGuid.Equals(device.InstanceGUID))
                                {
                                    if (inputDevices[i] is Joystick)
                                    {
                                        inputDevices[i].Poll();
                                        var state = (inputDevices[i] as Joystick).GetCurrentState();

                                        callback(state.Buttons[device.Button], device.InputBind);
                                        break;
                                    }
                                    if (inputDevices[i] is Keyboard)
                                    {
                                        inputDevices[i].Poll();
                                        var state = (inputDevices[i] as Keyboard).GetCurrentState();

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

        public void StopPTT()
        {
            detectPTT = false;
        }
    }
}