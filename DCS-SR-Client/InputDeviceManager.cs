using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using NLog;
using SharpDX.DirectInput;
using SharpDX.Multimedia;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(bool[] buttonStates);

        private volatile bool _detectPtt;
        private readonly DirectInput _directInput;
        private readonly List<Device> _inputDevices = new List<Device>();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static readonly HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
        {
            new Guid("1b171b1c-0000-0000-0000-504944564944"),
            //Corsair K65 Gaming keyboard  It reports as a Joystick when its a keyboard...
            new Guid("1b091b1c-0000-0000-0000-504944564944"), // Corsair K70R Gaming Keyboard
            new Guid("1b1e1b1c-0000-0000-0000-504944564944"), //Corsair Gaming Scimitar RGB Mouse
            new Guid("16a40951-0000-0000-0000-504944564944"), //HyperX 7.1 Audio
            new Guid("b660044f-0000-0000-0000-504944564944"),// T500 RS Gear Shift
            new Guid("00f2068e-0000-0000-0000-504944564944"), //CH PRO PEDALS USB 

        };

        private WindowInteropHelper WindowHelper { get; set; }

        public InputDeviceManager(Window window)
        {
            InputConfig = new InputConfiguration();
            _directInput = new DirectInput();
            var deviceInstances = _directInput.GetDevices();

            WindowHelper =
                new WindowInteropHelper(window);

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (!IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found " + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    if (deviceInstance.Usage == UsageId.GenericJoystick)
                    {
                        Logger.Info("Adding " + deviceInstance.ProductGuid + " " +
                                    deviceInstance.ProductName.Trim().Replace("\0", ""));
                        var device = new Joystick(_directInput, deviceInstance.ProductGuid);


                        device.SetCooperativeLevel(WindowHelper.Handle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        device.Acquire();

                        _inputDevices.Add(device);
                    }
                }
                else
                {
                    Logger.Info("Found but ignoring " + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                }
            }
        }

        public InputConfiguration InputConfig { get; set; }

        public bool IsBlackListed(Guid device)
        {
            return _blacklistedDevices.Contains(device);
        }


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
                var initial = new int[_inputDevices.Count, 128 + 4]; // for POV

                for (var i = 0; i < _inputDevices.Count; i++)
                {
                    if (_inputDevices[i] is Joystick)
                    {
                        _inputDevices[i].Poll();

                        var state = (_inputDevices[i] as Joystick).GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.Buttons[j] ? 1 : 0;
                        }
                        var pov = state.PointOfViewControllers;

                        for (int j = 0; j < pov.Length; j++)
                        {
                            initial[i, j + 128] = pov[j];
                        }
                    }
                }

                var device = string.Empty;
                var button = 0;
                var deviceGuid = Guid.Empty;
                var buttonValue = -1;
                var found = false;

                while (!found)
                {
                    Thread.Sleep(100);

                    for (var i = 0; i < _inputDevices.Count; i++)
                    {
                        if (_inputDevices[i] is Joystick)
                        {
                            _inputDevices[i].Poll();

                            var state = (_inputDevices[i] as Joystick).GetCurrentState();

                            for (var j = 0; j < 128 + 4; j++)
                            {
                                if (j >= 128)
                                {
                                    //handle POV
                                    var pov = state.PointOfViewControllers;

                                    if (pov[j - 128] != initial[i, j])
                                    {
                                        found = true;

                                        InputDevice inputDevice = new InputDevice
                                        {
                                            DeviceName =
                                                _inputDevices[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = _inputDevices[i].Information.InstanceGuid,
                                            ButtonValue = pov[j - 128]
                                        };
                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });
                                        return;
                                    }
                                }
                                else
                                {
                                    int buttonState = (state.Buttons[j] ? 1 : 0);

                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;
 
                                        InputDevice inputDevice = new InputDevice
                                        {
                                            DeviceName =
                                                _inputDevices[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = _inputDevices[i].Information.InstanceGuid,
                                            ButtonValue = buttonState
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });


                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }

        public void StartDetectPtt(DetectPttCallback callback)
        {
            _detectPtt = true;
            var bindingsCount = Enum.GetValues(typeof(InputBinding)).Length;
            //detect the state of all current buttons
            var pttInputThread = new Thread(() =>
            {
                while (_detectPtt)
                {
                    var buttonStates = new bool[bindingsCount];
                    for (int i = 0; i < bindingsCount; i++)
                    {
                        //init to true so the modifier bindings logic is easier
                        buttonStates[i] = true;
                    }

                    bool noDevices = true;
                    for(int j = 0; j < InputConfig.InputDevices.Length; j++)
                    {
                        var device = InputConfig.InputDevices[j];

                        if (device != null)
                        {
                            noDevices = false;
                            for (var i = 0; i < _inputDevices.Count; i++)
                            {
                                if (_inputDevices[i].Information.InstanceGuid.Equals(device.InstanceGuid))
                                {
                                    if (_inputDevices[i] is Joystick)
                                    {
                                        _inputDevices[i].Poll();
                                        var state = (_inputDevices[i] as Joystick).GetCurrentState();

                                        if (device.Button >= 128) //its a POV!
                                        {
                                            var pov = state.PointOfViewControllers;
                                            buttonStates[(int) device.InputBind] = pov[device.Button - 128] ==
                                                                                   device.ButtonValue;
                                        }
                                        else
                                        {
                                            buttonStates[(int) device.InputBind] = state.Buttons[device.Button];
                                        }


                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            if (j < 4)
                            {
                                // set to false as its its a main button, not a modifier
                                buttonStates[j] = false;
                            }
                       
                        }
                    }
                    //if no buttons are bound then dont call callback
                    if (noDevices)
                    {
                        callback(new bool[bindingsCount]);
                    }
                    else
                    {
                        callback(buttonStates);
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