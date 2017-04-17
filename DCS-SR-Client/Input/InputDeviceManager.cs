using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using SharpDX.DirectInput;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(List<InputBindState> buttonStates);

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

        //used to trigger the update to a frequency
        private InputBinding _lastActiveBinding = InputBinding.ModifierIntercom; //intercom used to represent null as we cant


        public InputDeviceManager(Window window, MainWindow.ToggleOverlayCallback _toggleOverlayCallback)
        {
            InputConfig = new InputConfiguration();
            _directInput = new DirectInput();
            var deviceInstances = _directInput.GetDevices();

            WindowHelper =
                new WindowInteropHelper(window);

            this._toggleOverlayCallback = _toggleOverlayCallback;

            Logger.Info("Starting Device Search. Expand Search: "+ ((SettingsStore.Instance.UserSettings[(int)SettingType.ExpandControls])=="ON").ToString() );

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (!IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found " + deviceInstance.ProductGuid + " Instance: " + deviceInstance.InstanceGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", "") + " Usage: " +
                                deviceInstance.UsagePage + " Type: " +
                                deviceInstance.Type);


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
                    else if (deviceInstance.Type == DeviceType.Mouse)
                    {
                        Logger.Info("Adding " + deviceInstance.ProductGuid + " Instance: " + deviceInstance.InstanceGuid +
                                    " " +
                                    deviceInstance.ProductName.Trim().Replace("\0", ""));
                        var device = new Mouse(_directInput);

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
                    else if (SettingsStore.Instance.UserSettings[(int)SettingType.ExpandControls] == "ON")
                    {

                        Logger.Info("Adding (Expanded Devices) " + deviceInstance.ProductGuid + " Instance: " +
                                 deviceInstance.InstanceGuid + " " +
                                 deviceInstance.ProductName.Trim().Replace("\0", ""));

                        var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                        device.SetCooperativeLevel(WindowHelper.Handle,
                            CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                        device.Acquire();

                        _inputDevices.Add(device);

                        Logger.Info("Added (Expanded Device) " + deviceInstance.ProductGuid + " Instance: " +
                                 deviceInstance.InstanceGuid + " " +
                                 deviceInstance.ProductName.Trim().Replace("\0", ""));
                    }
                    
                }
                else
                {
                    Logger.Info("Found but ignoring blacklist device  " + deviceInstance.ProductGuid + " Instance: " +
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
            //detect the state of all current buttons
            var pttInputThread = new Thread(() =>
            {
                while (_detectPtt)
                {
                    var bindStates = GenerateBindStateList();
                

                    for (var i = 0; i < bindStates.Count; i++)
                    {
                        //contains main binding and optional modifier binding + states of each
                        var bindState = bindStates[i];

                        bindState.MainDeviceState = GetButtonState(bindState.MainDevice);

                        if (bindState.ModifierDevice != null)
                        {
                            bindState.ModifierState = GetButtonState(bindState.ModifierDevice);

                            bindState.IsActive = bindState.MainDeviceState && bindState.ModifierState;
                        }
                        else
                        {
                            bindState.IsActive = bindState.MainDeviceState;
                        }

                        //now check this is the best binding and no previous ones are better
                        //Means you can have better binds like PTT  = Space and Radio 1 is Space +1 - holding space +1 will actually trigger radio 1 not PTT
                        if (bindState.IsActive)
                        {
                            for (int j = 0; j < i; j++)
                            {
                                //check previous bindings
                                var previousBind = bindStates[j];

                                if (previousBind.IsActive)
                                {
                                    if (previousBind.ModifierDevice == null && bindState.ModifierDevice !=null)
                                    {
                                        //set previous bind to off if previous bind Main == main or modifier of bindstate
                                        if (previousBind.MainDevice.IsSameBind(bindState.MainDevice))
                                        {
                                            previousBind.IsActive = false;
                                            break;
                                        }
                                        if (previousBind.MainDevice.IsSameBind(bindState.ModifierDevice))
                                        {
                                            previousBind.IsActive = false;
                                            break;
                                        }

                                    }
                                    else if (previousBind.ModifierDevice != null && bindState.ModifierDevice == null)
                                    {
                                        if (previousBind.MainDevice.IsSameBind(bindState.MainDevice))
                                        {
                                            bindState.IsActive = false;
                                            break;
                                        }
                                        if (previousBind.ModifierDevice.IsSameBind(bindState.MainDevice))
                                        {
                                            bindState.IsActive = false;
                                            break;
                                        }
                                    }

                                }
                            }
                        }
                    }

                    callback(bindStates);
                    //handle overlay

                    foreach (var bindState in bindStates)
                    {
                        if (bindState.IsActive && bindState.MainDevice.InputBind == InputBinding.OverlayToggle)
                        {
                            //run on main
                            Application.Current.Dispatcher.Invoke(
                                () => { _toggleOverlayCallback(false); });
                            break;
                        }
                        else if ((int)bindState.MainDevice.InputBind >= (int)InputBinding.Up100 &&
                          (int)bindState.MainDevice.InputBind <= (int)InputBinding.ModifierEncryptionEncryptionKeyDecrease)
                        {

                            if (bindState.MainDevice.InputBind == _lastActiveBinding && !bindState.IsActive)
                            {
                                //Assign to a totally different binding to mark as unassign
                                _lastActiveBinding = InputBinding.ModifierIntercom;
                            }

                            //key repeat
                            if (bindState.IsActive && (bindState.MainDevice.InputBind != _lastActiveBinding))
                            {
                                _lastActiveBinding = bindState.MainDevice.InputBind;

                                var dcsPlayerRadioInfo = RadioDCSSyncServer.DcsPlayerRadioInfo;

                                if (dcsPlayerRadioInfo != null && dcsPlayerRadioInfo.IsCurrent())
                                {

                                    switch (bindState.MainDevice.InputBind)
                                    {
                                        case InputBinding.Up100:
                                            RadioHelper.UpdateRadioFrequency(100, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Up10:
                                            RadioHelper.UpdateRadioFrequency(10, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Up1:
                                            RadioHelper.UpdateRadioFrequency(1, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Up01:
                                            RadioHelper.UpdateRadioFrequency(0.1, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Up001:
                                            RadioHelper.UpdateRadioFrequency(0.01, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Up0001:
                                            RadioHelper.UpdateRadioFrequency(0.001, dcsPlayerRadioInfo.selected);
                                            break;

                                        case InputBinding.Down100:
                                            RadioHelper.UpdateRadioFrequency(-100, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Down10:
                                            RadioHelper.UpdateRadioFrequency(-10, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Down1:
                                            RadioHelper.UpdateRadioFrequency(-1, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Down01:
                                            RadioHelper.UpdateRadioFrequency(-0.1, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Down001:
                                            RadioHelper.UpdateRadioFrequency(-0.01, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.Down0001:
                                            RadioHelper.UpdateRadioFrequency(-0.001, dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.ToggleGuard:
                                            RadioHelper.ToggleGuard(dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.ToggleEncryption:
                                            RadioHelper.ToggleEncryption(dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.NextRadio:
                                            RadioHelper.SelectNextRadio();
                                            break;
                                        case InputBinding.PreviousRadio:
                                            RadioHelper.SelectPreviousRadio();
                                            break;
                                        case InputBinding.EncryptionKeyIncrease:
                                            RadioHelper.IncreaseEncryptionKey(dcsPlayerRadioInfo.selected);
                                            break;
                                        case InputBinding.EncryptionKeyDecrease:
                                            RadioHelper.DecreaseEncryptionKey(dcsPlayerRadioInfo.selected);
                                            break;


                                        default:
                                            break;
                                    }
                                }


                                break;

                            }
                        }
                    }
                    
                    Thread.Sleep(40);
                }
            });
            pttInputThread.Start();
        }

       

        public void StopPtt()
        {
            _detectPtt = false;
        }

        private bool GetButtonState(InputDevice inputDeviceBinding)
        {
            foreach (var device in _inputDevices)
            {
                if (device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid))
                {
                    if (device is Joystick)
                    {
                        device.Poll();
                        var state = (device as Joystick).GetCurrentState();

                        if (inputDeviceBinding.Button >= 128) //its a POV!
                        {
                            var pov = state.PointOfViewControllers;
                            //-128 to get POV index
                            return pov[inputDeviceBinding.Button - 128] == inputDeviceBinding.ButtonValue;
                        }
                        else
                        {
                            return state.Buttons[inputDeviceBinding.Button];
                        }
                    }
                    else if (device is Keyboard)
                    {
                        var keyboard = device as Keyboard;
                        keyboard.Poll();
                        var state = keyboard.GetCurrentState();
                        return
                            state.IsPressed(state.AllKeys[inputDeviceBinding.Button]);
                    }
                }
            }
            return false;
        }

        public List<InputBindState> GenerateBindStateList()
        {
            var bindStates = new List<InputBindState>();

            //REMEMBER TO UPDATE THIS WHEN NEW BINDINGS ARE ADDED
            //MIN + MAX bind numbers
            for (int i = (int)InputBinding.Intercom; i <= (int) InputBinding.EncryptionKeyDecrease; i++)
            {
                var mainInputBind = InputConfig.InputDevices[(InputBinding) i];

                if (mainInputBind != null)
                {
                    //construct InputBindState

                    var bindState = new InputBindState()
                    {
                        IsActive = false,
                        MainDevice = mainInputBind,
                        MainDeviceState = false,
                        ModifierDevice = InputConfig.InputDevices[(InputBinding) i + 100], // can be null but OK
                        ModifierState = false
                    };

                    bindStates.Add(bindState);
                }

            }

            return bindStates;

        }
    }
}