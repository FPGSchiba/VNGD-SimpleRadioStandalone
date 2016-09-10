using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using NLog;
using SharpDX.DirectInput;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(bool[] buttonStates);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static readonly HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
        {
            new Guid("1b171b1c-0000-0000-0000-504944564944"),
            //Corsair K65 Gaming keyboard  It reports as a Joystick when its a keyboard...
            new Guid("1b091b1c-0000-0000-0000-504944564944"), // Corsair K70R Gaming Keyboard
            new Guid("1b1e1b1c-0000-0000-0000-504944564944"), //Corsair Gaming Scimitar RGB Mouse
            new Guid("16a40951-0000-0000-0000-504944564944"), //HyperX 7.1 Audio
            new Guid("b660044f-0000-0000-0000-504944564944"), // T500 RS Gear Shift
            new Guid("00f2068e-0000-0000-0000-504944564944") //CH PRO PEDALS USB 
        };

        //devices that report incorrectly but SHOULD work?
        public static readonly HashSet<Guid> _whitelistDevices = new HashSet<Guid>
        {
            new Guid("1105231d-0000-0000-0000-504944564944") //GTX Throttle 
        };

        private readonly DirectInput _directInput;
        private readonly List<Device> _inputDevices = new List<Device>();
        private readonly MainWindow.ToggleOverlayCallback _toggleOverlayCallback;

        private volatile bool _detectPtt;

        public InputDeviceManager(Window window, MainWindow.ToggleOverlayCallback _toggleOverlayCallback)
        {
            InputConfig = new InputConfiguration();
            _directInput = new DirectInput();
            var deviceInstances = _directInput.GetDevices();

            WindowHelper =
                new WindowInteropHelper(window);

            this._toggleOverlayCallback = _toggleOverlayCallback;

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (!IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found " + deviceInstance.ProductGuid + " Instance: " + deviceInstance.InstanceGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", "") + " Usage: " +
                                deviceInstance.UsagePage);


                    if (deviceInstance.Type == DeviceType.Keyboard)
                    {
                        Logger.Info("Adding " + deviceInstance.ProductGuid + " Instance: " + deviceInstance.InstanceGuid +
                                    " " +
                                    deviceInstance.ProductName.Trim().Replace("\0", ""));
                        var device = new Keyboard(_directInput);

                        device.SetCooperativeLevel(WindowHelper.Handle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        device.Acquire();

                        _inputDevices.Add(device);
                    }
                    else if (((deviceInstance.Type >= DeviceType.Joystick) && (deviceInstance.Type <= DeviceType.FirstPerson)) ||
                             IsWhiteListed(deviceInstance.ProductGuid))
                    {
                        var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                        Logger.Info("Adding " + deviceInstance.ProductGuid + " Instance: " +
                                    deviceInstance.InstanceGuid + " " +
                                    deviceInstance.ProductName.Trim().Replace("\0", ""));

                        device.SetCooperativeLevel(WindowHelper.Handle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        device.Acquire();

                        _inputDevices.Add(device);
                    }
                }
                else
                {
                    Logger.Info("Found but ignoring " + deviceInstance.ProductGuid + " Instance: " +
                                deviceInstance.InstanceGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", "") + " Type: " + deviceInstance.Type);
                }
            }
        }

        private WindowInteropHelper WindowHelper { get; }

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

        public bool IsBlackListed(Guid device)
        {
            return _blacklistedDevices.Contains(device);
        }

        public bool IsWhiteListed(Guid device)
        {
            return _whitelistDevices.Contains(device);
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

                        for (var j = 0; j < pov.Length; j++)
                        {
                            initial[i, j + 128] = pov[j];
                        }
                    }
                    else if (_inputDevices[i] is Keyboard)
                    {
                        var keyboard = _inputDevices[i] as Keyboard;
                        keyboard.Poll();
                        var state = keyboard.GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.IsPressed(state.AllKeys[j]) ? 1 : 0;
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

                                        var inputDevice = new InputDevice
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
                                    var buttonState = state.Buttons[j] ? 1 : 0;

                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;

                                        var inputDevice = new InputDevice
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
                        else if (_inputDevices[i] is Keyboard)
                        {
                            var keyboard = _inputDevices[i] as Keyboard;
                            keyboard.Poll();
                            var state = keyboard.GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                if (initial[i, j] != (state.IsPressed(state.AllKeys[j]) ? 1 : 0))
                                {
                                    found = true;

                                    var inputDevice = new InputDevice
                                    {
                                        DeviceName =
                                            _inputDevices[i].Information.ProductName.Trim().Replace("\0", ""),
                                        Button = j,
                                        InstanceGuid = _inputDevices[i].Information.InstanceGuid,
                                        ButtonValue = 1
                                    };

                                    Application.Current.Dispatcher.Invoke(
                                        () => { callback(inputDevice); });


                                    return;
                                }

//                                if (initial[i, j] == 1)
//                                {
//                                    Console.WriteLine("Pressed: "+j);
//                                    MessageBox.Show("Keyboard!");
//                                }
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
                    for (var i = 0; i < bindingsCount; i++)
                    {
                        //init to true so the modifier bindings logic is easier
                        buttonStates[i] = true;
                    }

                    var noDevices = true;
                    for (var j = 0; j < InputConfig.InputDevices.Length; j++)
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
                                    if (_inputDevices[i] is Keyboard)
                                    {
                                        var keyboard = _inputDevices[i] as Keyboard;
                                        keyboard.Poll();
                                        var state = keyboard.GetCurrentState();
                                        buttonStates[(int) device.InputBind] =
                                            state.IsPressed(state.AllKeys[device.Button]);
                                    }
                                }
                            }
                        }
                        else
                        {
                            if ((j < 4) || (j == 8) || (j == 10)) //==8 is for OverlayToggle == 10 is for intercom
                            {
                                // set to false as its its a main button, not a modifier
                                buttonStates[j] = false;
                            }
                        }
                    }
                    //if no buttons are bound then  call callback with false for everything
                    if (noDevices)
                    {
                        callback(new bool[bindingsCount]);
                    }
                    else
                    {
                        callback(buttonStates);
                        //handle overlay
                        if (buttonStates[(int) InputBinding.OverlayToggle] &&
                            buttonStates[(int) InputBinding.ModifierOverlayToggle])
                        {
                            //run on main
                            Application.Current.Dispatcher.Invoke(
                                () => { _toggleOverlayCallback(false); });
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