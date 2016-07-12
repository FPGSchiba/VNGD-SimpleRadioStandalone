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

        public delegate void DetectPttCallback(bool[] buttonStates);

        private volatile bool _detectPtt;
        private readonly DirectInput _directInput;
        private readonly List<Device> _inputDevices = new List<Device>();
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static readonly HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
        {
             new Guid("1b171b1c-0000-0000-0000-504944564944"), //Corsair K65 Gaming keyboard  It reports as a Joystick when its a keyboard...
             new Guid("1b091b1c-0000-0000-0000-504944564944") // Corsair K70R Gaming Keyboard
        };

        public InputDeviceManager(Window window)
        {
            InputConfig = new InputConfiguration();
            _directInput = new DirectInput();
            var deviceInstances = _directInput.GetDevices();

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (!IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found " + deviceInstance.ProductGuid + " "+deviceInstance.ProductName.Trim().Replace("\0", ""));

                    if (deviceInstance.Usage == UsageId.GenericJoystick || deviceInstance.Usage == UsageId.GenericGamepad)
                    {
                        Logger.Info("Adding " + deviceInstance.ProductGuid + " " + deviceInstance.ProductName.Trim().Replace("\0", ""));
                        var device = new Joystick(_directInput, deviceInstance.ProductGuid);

                        var helper =
                       new WindowInteropHelper(window);

                        device.SetCooperativeLevel(helper.Handle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        device.Acquire();

                        _inputDevices.Add(device);
                    }
                }
                else
                {
                    Logger.Info("Found but ignoring " + deviceInstance.ProductGuid + " " + deviceInstance.ProductName.Trim().Replace("\0", ""));
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
                var initial = new bool[_inputDevices.Count, 128];

                for (var i = 0; i < _inputDevices.Count; i++)
                {
                    if (_inputDevices[i] is Joystick)
                    {
                        _inputDevices[i].Poll();

                        var state = (_inputDevices[i] as Joystick).GetCurrentState();

                        for (var j = 0; j < 128; j++)
                        {
                            initial[i, j] = state.Buttons[j];
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
                        
                        if (_inputDevices[i] is Joystick)
                        {
                            _inputDevices[i].Poll();

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
            var bindingsCount = Enum.GetValues(typeof(InputBinding)).Length;
            //detect the state of all current buttons
            var pttInputThread = new Thread(() =>
            {
                while (_detectPtt)
                {
                    var buttonStates = new bool[bindingsCount];
                    
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
                                        
                                        buttonStates[(int)device.InputBind] = state.Buttons[device.Button];
                                        break;
                                    }
                                   
                                }
                            }
                        }
                    }

                    callback(buttonStates);

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