using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using SharpDX.DirectInput;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButtonInput(InputButtonDevice inputDevice);

        public delegate void DetectAxisInput(InputAxisDevice inputDevice);

        public delegate void DetectPttCallback(List<InputBindState> buttonStates);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
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
        public static HashSet<Guid> _whitelistDevices = new HashSet<Guid>
        {
            new Guid("1105231d-0000-0000-0000-504944564944"), //GTX Throttle
            new Guid("b351044f-0000-0000-0000-504944564944"), //F16 MFD 1 Usage: Generic Type: Supplemental
            new Guid("b352044f-0000-0000-0000-504944564944"), //F16 MFD 2 Usage: Generic Type: Supplemental
            new Guid("b353044f-0000-0000-0000-504944564944"), //F16 MFD 3 Usage: Generic Type: Supplemental
            new Guid("b354044f-0000-0000-0000-504944564944"), //F16 MFD 4 Usage: Generic Type: Supplemental
            new Guid("b355044f-0000-0000-0000-504944564944"), //F16 MFD 5 Usage: Generic Type: Supplemental
            new Guid("b356044f-0000-0000-0000-504944564944"), //F16 MFD 6 Usage: Generic Type: Supplemental
            new Guid("b357044f-0000-0000-0000-504944564944"), //F16 MFD 7 Usage: Generic Type: Supplemental
            new Guid("b358044f-0000-0000-0000-504944564944"), //F16 MFD 8 Usage: Generic Type: Supplemental
            new Guid("11401dd2-0000-0000-0000-504944564944"), //Leo Bodnar BUtton Box
            new Guid("204803eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("204303eb-0000-0000-0000-504944564944"), // VPC Stick
            new Guid("205403eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("205603eb-0000-0000-0000-504944564944"), // VPC Throttle
            new Guid("205503eb-0000-0000-0000-504944564944"),  // VPC Throttle
            new Guid("82c43344-0000-0000-0000-504944564944"),  //  LEFT VPC Rotor TCS
            new Guid("c2ab046d-0000-0000-0000-504944564944")  // Logitech G13 Joystick
            

        };

        private readonly DirectInput _directInput;
        private readonly Dictionary<Guid, Device> _inputDevices = new Dictionary<Guid, Device>();
        private readonly MainWindow.ToggleOverlayCallback _toggleOverlayCallback;
        private readonly string[] propertyList = new[] { "X", "Y", "Z", "RotationX", "RotationY", "RotationZ" };

        private volatile bool _detectPtt;

        //used to trigger the update to a frequency
        private InputBinding _lastActiveBinding = InputBinding.ModifierIntercom
            ; //intercom used to represent null as we cant

        private Settings.GlobalSettingsStore _globalSettings = Settings.GlobalSettingsStore.Instance;


        public InputDeviceManager(Window window, MainWindow.ToggleOverlayCallback _toggleOverlayCallback)
        {
            _directInput = new DirectInput();


            WindowHelper =
                new WindowInteropHelper(window);

            this._toggleOverlayCallback = _toggleOverlayCallback;

            LoadWhiteList();

            LoadBlackList();

            InitDevices();


        }

        public void InitDevices()
        {
            Logger.Info("Starting Device Search. Expand Search: " +
            (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.ExpandControls)));

            var deviceInstances = _directInput.GetDevices();

            foreach (var deviceInstance in deviceInstances)
            {
                //Workaround for Bad Devices that pretend to be joysticks
                if (IsBlackListed(deviceInstance.ProductGuid))
                {
                    Logger.Info("Found but ignoring blacklist device  " + deviceInstance.ProductGuid + " Instance: " +
                        deviceInstance.InstanceGuid + " " +
                        deviceInstance.ProductName.Trim().Replace("\0", "") + " Type: " + deviceInstance.Type);
                    continue;
                }

                Logger.Info("Found Device ID:" + deviceInstance.ProductGuid +
                            " " +
                            deviceInstance.ProductName.Trim().Replace("\0", "") + " Usage: " +
                            deviceInstance.UsagePage + " Type: " +
                            deviceInstance.Type);
                if (_inputDevices.ContainsKey(deviceInstance.InstanceGuid))
                {
                    Logger.Info("Already have device:" + deviceInstance.ProductGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    continue;
                }


                if (deviceInstance.Type == DeviceType.Keyboard)
                {

                    Logger.Info("Adding Device ID:" + deviceInstance.ProductGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    var device = new Keyboard(_directInput);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (deviceInstance.Type == DeviceType.Mouse)
                {
                    Logger.Info("Adding Device ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    var device = new Mouse(_directInput);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (((deviceInstance.Type >= DeviceType.Joystick) &&
                            (deviceInstance.Type <= DeviceType.FirstPerson)) ||
                            IsWhiteListed(deviceInstance.ProductGuid))
                {
                    var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    Logger.Info("Adding ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.ExpandControls))
                {
                    Logger.Info("Adding (Expanded Devices) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);

                    Logger.Info("Added (Expanded Device) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                }
            }
        }

        private void LoadWhiteList()
        {
            var path = Environment.CurrentDirectory + "\\whitelist.txt";
            Logger.Info("Attempt to Load Whitelist from " + path);

            LoadGuidFromPath(path, _whitelistDevices);
        }

        private void LoadBlackList()
        {
            var path = Environment.CurrentDirectory + "\\blacklist.txt";
            Logger.Info("Attempt to Load Blacklist from " + path);

            LoadGuidFromPath(path, _blacklistedDevices);
        }

        private void LoadGuidFromPath(string path, HashSet<Guid> _hashSet)
        {
            if (!File.Exists(path))
            {
                Logger.Info("File doesnt exist: " + path);
                return;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines?.Length <= 0)
            {
                return;

            }

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    try
                    {
                        _hashSet.Add(new Guid(trimmed));
                        Logger.Info("Added " + trimmed);
                    }
                    catch (Exception ex)
                    {
                    }
                }
            }
        }

        private WindowInteropHelper WindowHelper { get; }


        public void Dispose()
        {
            StopPtt();
            foreach (var kpDevice in _inputDevices)
            {
                if (kpDevice.Value != null)
                {
                    kpDevice.Value.Unacquire();
                    kpDevice.Value.Dispose();
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

        public void AssignButton(DetectButtonInput callback)
        {
            //detect the state of all current buttons
            Task.Run(() =>
            {
                var deviceList = _inputDevices.Values.ToList();

                var initial = new int[deviceList.Count, 128 + 4]; // for POV

                for (var i = 0; i < deviceList.Count; i++)
                {
                    if (deviceList[i] == null || deviceList[i].IsDisposed)
                    {
                        continue;
                    }

                    try
                    {
                        if (deviceList[i] is Joystick)
                        {
                            deviceList[i].Poll();

                            var state = (deviceList[i] as Joystick).GetCurrentState();

                            for (var j = 0; j < state.Buttons.Length; j++)
                            {
                                initial[i, j] = state.Buttons[j] ? 1 : 0;
                            }
                            var pov = state.PointOfViewControllers;

                            for (var j = 0; j < pov.Length; j++)
                            {
                                initial[i, j + 128] = pov[j];
                            }
                        }
                        else if (deviceList[i] is Keyboard)
                        {
                            var keyboard = deviceList[i] as Keyboard;
                            keyboard.Poll();
                            var state = keyboard.GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                initial[i, j] = state.IsPressed(state.AllKeys[j]) ? 1 : 0;
                            }
                        }
                        else if (deviceList[i] is Mouse)
                        {
                            var mouse = deviceList[i] as Mouse;
                            mouse.Poll();

                            var state = mouse.GetCurrentState();

                            for (var j = 0; j < state.Buttons.Length; j++)
                            {
                                initial[i, j] = state.Buttons[j] ? 1 : 0;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                            $"(ID: {deviceList[i].Information.ProductGuid}) while assigning button, ignoring until next restart/rediscovery");

                        deviceList[i].Unacquire();
                        deviceList[i].Dispose();
                        deviceList[i] = null;
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
                        if (deviceList[i] == null || deviceList[i].IsDisposed)
                        {
                            continue;
                        }

                        try
                        {
                            if (deviceList[i] is Joystick)
                            {
                                deviceList[i].Poll();

                                var state = (deviceList[i] as Joystick).GetCurrentState();

                                for (var j = 0; j < 128 + 4; j++)
                                {
                                    if (j >= 128)
                                    {
                                        //handle POV
                                        var pov = state.PointOfViewControllers;

                                        if (pov[j - 128] != initial[i, j])
                                        {
                                            found = true;

                                            var inputDevice = new InputButtonDevice
                                            {
                                                DeviceName =
                                                    deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                                Button = j,
                                                InstanceGuid = deviceList[i].Information.InstanceGuid,
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

                                            var inputDevice = new InputButtonDevice
                                            {
                                                DeviceName =
                                                    deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                                Button = j,
                                                InstanceGuid = deviceList[i].Information.InstanceGuid,
                                                ButtonValue = buttonState
                                            };

                                            Application.Current.Dispatcher.Invoke(
                                                () => { callback(inputDevice); });


                                            return;
                                        }
                                    }
                                }
                            }
                            else if (deviceList[i] is Keyboard)
                            {
                                var keyboard = deviceList[i] as Keyboard;
                                keyboard.Poll();
                                var state = keyboard.GetCurrentState();

                                for (var j = 0; j < 128; j++)
                                {
                                    if (initial[i, j] != (state.IsPressed(state.AllKeys[j]) ? 1 : 0))
                                    {
                                        found = true;

                                        var inputDevice = new InputButtonDevice
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
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
                            else if (deviceList[i] is Mouse)
                            {
                                deviceList[i].Poll();

                                var state = (deviceList[i] as Mouse).GetCurrentState();

                                //skip left mouse button - start at 1 with j 0 is left, 1 is right, 2 is middle
                                for (var j = 1; j < state.Buttons.Length; j++)
                                {
                                    var buttonState = state.Buttons[j] ? 1 : 0;

                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;

                                        var inputDevice = new InputButtonDevice
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Button = j,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            ButtonValue = buttonState
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                                $"(ID: {deviceList[i].Information.ProductGuid}) while discovering button press while assigning, ignoring until next restart/rediscovery");

                            deviceList[i].Unacquire();
                            deviceList[i].Dispose();
                            deviceList[i] = null;
                        }
                    }
                }
            });
        }

        public void AssignAxis(DetectAxisInput callback)
        {
            Task.Run(() =>
            {
                var deviceList = _inputDevices.Values.ToList();
                Dictionary<string, int> initialAxisState = new Dictionary<string, int>();

                for (int i = 0; i < deviceList.Count; i++)
                {
                    if (deviceList[i] == null || deviceList[i].IsDisposed)
                    {
                        continue;
                    }
                    try
                    {
                        if (deviceList[i] is Joystick)
                        {
                            deviceList[i].Poll();
                            JoystickState state = (deviceList[i] as Joystick).GetCurrentState();

                            foreach (string property in propertyList)
                            {
                                initialAxisState.Add(i.ToString() + property, (int)state.GetType().GetProperty(property).GetValue(state));
                            }

                            var z = deviceList[i];
                            var sliders = state.Sliders;
                            for (int j = 0; j < sliders.Length; j++)
                            {
                                initialAxisState.Add(i.ToString() + "Sliders" + j.ToString(), sliders[j]);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                            $"(ID: {deviceList[i].Information.ProductGuid}) while assigning axis, ignoring until next restart/rediscovery");

                        deviceList[i].Unacquire();
                        deviceList[i].Dispose();
                        deviceList[i] = null;
                    }
                }

                bool found = false;

                while (!found)
                {
                    Thread.Sleep(100);

                    for (var i = 0; i < _inputDevices.Count; i++)
                    {
                        if (deviceList[i] == null || deviceList[i].IsDisposed)
                        {
                            continue;
                        }

                        try
                        {
                            if (deviceList[i] is Joystick)
                            {
                                deviceList[i].Poll();

                                var state = (deviceList[i] as Joystick).GetCurrentState();

                                foreach (string property in propertyList)
                                {
                                    int current = (int)state.GetType().GetProperty(property).GetValue(state);

                                    if (AxisDifference(initialAxisState[i.ToString() + property], current))
                                    {
                                        found = true;

                                        InputAxisDevice axisDevice = new InputAxisDevice()
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Axis = property,
                                            Invert = false,
                                            Curvature = 0,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            AxisCenterValue = initialAxisState[i.ToString() + property] // Assume the initial state of an axis ~centered
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(axisDevice); });
                                        return;
                                    }
                                }

                                var sliders = state.Sliders;
                                for (int j = 0; j < sliders.Length; j++)
                                {
                                    var d = deviceList[i].Capabilities;

                                    int current = sliders[j];

                                    if (AxisDifference(initialAxisState[i.ToString() + "Sliders" + j.ToString()], current))
                                    {
                                        found = true;

                                        InputAxisDevice axisDevice = new InputAxisDevice()
                                        {
                                            DeviceName =
                                                deviceList[i].Information.ProductName.Trim().Replace("\0", ""),
                                            Axis = "Slider" + j.ToString(),
                                            Invert = false,
                                            Curvature = 0,
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            AxisCenterValue = initialAxisState[i.ToString() + "Sliders" + j.ToString()] // Assume the initial state of an axis ~centered
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(axisDevice); });
                                        return;
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                                $"(ID: {deviceList[i].Information.ProductGuid}) while discovering button press while assigning, ignoring until next restart/rediscovery");

                            deviceList[i].Unacquire();
                            deviceList[i].Dispose();
                            deviceList[i] = null;
                        }
                    }
                }
            });
        }

        private void PollDevices(List<InputBindState> states)
        {
            //generate a unique list of devices - only poll once around the loop - not for each keybind
            var uniqueDevices = new HashSet<Guid>();

            foreach (var inputBindState in states)
            {
                if (inputBindState.MainDevice != null)
                {
                    uniqueDevices.Add(inputBindState.MainDevice.InstanceGuid);
                }
                if (inputBindState.ModifierDevice != null)
                {
                    uniqueDevices.Add(inputBindState.ModifierDevice.InstanceGuid);
                }
            }

            foreach (var deviceGuid in uniqueDevices)
            {
                foreach (var kpDevice in _inputDevices)
                {
                    var device = kpDevice.Value;
                    if (device == null ||
                        device.IsDisposed ||
                        !device.Information.InstanceGuid.Equals(deviceGuid))
                    {
                        continue;
                    }
                    //poll the device as it has a bind
                    device.Poll();
                }
            }

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

                //Poll devices
                PollDevices(bindStates);


                for (var i = 0; i < bindStates.Count; i++)
                {
                    //contains main binding and optional modifier binding + states of each
                    var bindState = bindStates[i];

                    if (bindState.MainDevice is InputAxisDevice)
                    {
                        bindState.MainDeviceState = GetAxisState(bindState.MainDevice as InputAxisDevice);
                    }
                    else
                    {
                        bindState.MainDeviceState = GetButtonState(bindState.MainDevice as InputButtonDevice);
                    }

                    if (bindState.ModifierDevice != null)
                    {
                        bindState.ModifierState = GetButtonState(bindState.ModifierDevice as InputButtonDevice);
                        bindState.IsActive = bindState.MainDeviceState && bindState.ModifierState;
                    }
                    else
                    {
                        bindState.IsActive = bindState.MainDeviceState;
                    }

                    //now check this is the best binding and no previous ones are better
                    //Means you can have better binds like PTT  = Space and Radio 1 is Space +1 - holding space +1 will actually trigger radio 1 not PTT
                    if (bindState.IsActive && !(bindState.MainDevice is InputAxisDevice))
                    {
                        for (int j = 0; j < i; j++)
                        {
                            //check previous bindings
                            var previousBind = bindStates[j];

                            if (!previousBind.IsActive)
                            {
                                continue;
                            }

                            if (previousBind.ModifierDevice == null && bindState.ModifierDevice != null)
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

                callback(bindStates);
                //handle overlay
                var dcsPlayerRadioInfo = ClientStateSingleton.Instance.DcsPlayerRadioInfo;
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
                             (int)bindState.MainDevice.InputBind <= (int)InputBinding.IntercomPTT)
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
                                    case InputBinding.RadioChannelUp:
                                        RadioHelper.RadioChannelUp(dcsPlayerRadioInfo.selected);
                                        break;
                                    case InputBinding.RadioChannelDown:
                                        RadioHelper.RadioChannelDown(dcsPlayerRadioInfo.selected);
                                        break;
                                    case InputBinding.TransponderIDENT:
                                        TransponderHelper.ToggleIdent();
                                        break;
                                    case InputBinding.RadioVolumeUp:
                                        RadioHelper.RadioVolumeUp(dcsPlayerRadioInfo.selected);
                                        break;
                                    case InputBinding.RadioVolumeDown:
                                        RadioHelper.RadioVolumeDown(dcsPlayerRadioInfo.selected);
                                        break;


                                    default:
                                        break;
                                }
                            }


                            break;
                        }
                    }

                    if ((int)bindState.MainDevice.InputBind >= (int)InputBinding.IntercomVolume)
                    {
                        switch (bindState.MainDevice.InputBind)
                        {
                            case InputBinding.IntercomVolume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 0);
                                break;
                            case InputBinding.Radio1Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 1);
                                break;
                            case InputBinding.Radio2Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 2);
                                break;
                            case InputBinding.Radio3Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 3);
                                break;
                            case InputBinding.Radio4Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 4);
                                break;
                            case InputBinding.Radio5Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 5);
                                break;
                            case InputBinding.Radio6Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 6);
                                break;
                            case InputBinding.Radio7Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 7);
                                break;
                            case InputBinding.Radio8Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 8);
                                break;
                            case InputBinding.Radio9Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 9);
                                break;
                            case InputBinding.Radio10Volume:
                                RadioHelper.SetRadioVolume((float)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (float)UInt16.MaxValue, 10);
                                break;
                            case InputBinding.Radio1Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    1, false, false, true);
                                break;
                            case InputBinding.Radio2Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    2, false, false, true);
                                break;
                            case InputBinding.Radio3Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    3, false, false, true);
                                break;
                            case InputBinding.Radio4Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    4, false, false, true);
                                break;
                            case InputBinding.Radio5Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    5, false, false, true);
                                break;
                            case InputBinding.Radio6Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    6, false, false, true);
                                break;
                            case InputBinding.Radio7Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    7, false, false, true);
                                break;
                            case InputBinding.Radio8Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    8, false, false, true);
                                break;
                            case InputBinding.Radio9Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    9, false, false, true);
                                break;
                            case InputBinding.Radio10Frequency:
                                RadioHelper.UpdateRadioFrequency(
                                    (double)GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue,
                                    10, false, false, true);
                                break;
                            case InputBinding.Radio1Encryption:
                                RadioHelper.SetEncryptionKey(1,
                                    (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio2Encryption:
                                    RadioHelper.SetEncryptionKey(2,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio3Encryption:
                                    RadioHelper.SetEncryptionKey(3,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio4Encryption:
                                    RadioHelper.SetEncryptionKey(4,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio5Encryption:
                                    RadioHelper.SetEncryptionKey(5,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio6Encryption:
                                    RadioHelper.SetEncryptionKey(6,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio7Encryption:
                                    RadioHelper.SetEncryptionKey(7,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio8Encryption:
                                    RadioHelper.SetEncryptionKey(8,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio9Encryption:
                                    RadioHelper.SetEncryptionKey(9,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;
                                case InputBinding.Radio10Encryption:
                                    RadioHelper.SetEncryptionKey(10,
                                        (int)Math.Round(GetAxisValue(bindState.MainDevice as InputAxisDevice) / (double)UInt16.MaxValue * 254));
                                    break;

                                default:
                                    break;


                            }

                            Thread.Sleep(40);
                        }
                    }
                }
            });
            pttInputThread.Start();
        }


        public void StopPtt()
        {
            _detectPtt = false;
        }

        private bool GetButtonState(InputButtonDevice inputDeviceBinding)
        {
            foreach (var kpDevice in _inputDevices)
            {
                var device = kpDevice.Value;
                if (device == null ||
                    device.IsDisposed ||
                    !device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid))
                {
                    continue;
                }

                try
                {
                    if (device is Joystick)
                    {
                        //device.Poll();
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
                        //keyboard.Poll();
                        var state = keyboard.GetCurrentState();
                        return
                            state.IsPressed(state.AllKeys[inputDeviceBinding.Button]);
                    }
                    else if (device is Mouse)
                    {
                        //device.Poll();
                        var state = (device as Mouse).GetCurrentState();

                        //just incase mouse changes number of buttons, like logitech can?
                        if (inputDeviceBinding.Button < state.Buttons.Length)
                        {
                            return state.Buttons[inputDeviceBinding.Button];
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Failed to get current state of input device {device.Information.ProductName.Trim().Replace("\0", "")} " +
                        $"(ID: {device.Information.ProductGuid}) while retrieving button state, ignoring until next restart/rediscovery");

                    MessageBox.Show(
                        $"An error occurred while querying your {device.Information.ProductName.Trim().Replace("\0", "")} input device.\nThis could for example be caused by unplugging " +
                        $"your joystick or disabling it in the Windows settings.\n\nAll controls bound to this input device will not work anymore until your restart SRS.",
                        "Input device error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    device.Unacquire();
                    device.Dispose();
                }

            }
            return false;
        }

        private int GetAxisValue(InputAxisDevice inputDeviceBinding)
        {
            // TODO: This loop is repeated in GetAxisState simplify so only one is required
            foreach (var kpDevice in _inputDevices)
            {
                var device = kpDevice.Value;
                if (device == null ||
                    device.IsDisposed ||
                    !device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid))
                {
                    //TODO: Store the previous axis value and return true if not same
                    continue;
                }
                try
                {
                    if (device is Joystick)
                    {
                        //device.Poll();
                        var state = (device as Joystick).GetCurrentState();
                        int value;
                        if (inputDeviceBinding.Axis.Contains("Slider"))
                        {
                            int[] sliders = (int[])state.GetType().GetProperty("Sliders").GetValue(state);

                            value = sliders[int.Parse(inputDeviceBinding.Axis.Substring(inputDeviceBinding.Axis.Length - 1))];
                        }
                        else
                        {
                            value = (int)state.GetType().GetProperty(inputDeviceBinding.Axis).GetValue(state);
                        }

                        AxisTuningHelper.GetCurvaturePointValue(value / (double)ushort.MaxValue, inputDeviceBinding.Curvature, inputDeviceBinding.Invert);
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"Failed to get current state of input device {device.Information.ProductName.Trim().Replace("\0", "")} " +
                        $"(ID: {device.Information.ProductGuid}) while retrieving axis state, ignoring until next restart/rediscovery");

                    MessageBox.Show(
                        $"An error occurred while querying your {device.Information.ProductName.Trim().Replace("\0", "")} input device.\nThis could for example be caused by unplugging " +
                        $"your joystick or disabling it in the Windows settings.\n\nAll controls bound to this input device will not work anymore until your restart SRS.",
                        "Input device error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    device.Unacquire();
                    device.Dispose();
                }
            }
            return -1;
        }

        private bool GetAxisState(InputAxisDevice inputDeviceBinding)
        {
            foreach (var kpDevice in _inputDevices)
            {
                var device = kpDevice.Value;
                if (device == null ||
                    device.IsDisposed ||
                    !device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid))
                {
                    continue;
                }

                return true;
            }
            return false;
        }

        public List<InputBindState> GenerateBindStateList()
        {
            var bindStates = new List<InputBindState>();
            var currentInputProfile = _globalSettings.ProfileSettingsStore.GetCurrentInputProfile();

            //REMEMBER TO UPDATE THIS WHEN NEW BINDINGS ARE ADDED
            //MIN + MAX bind numbers
            for (int i = (int)InputBinding.Intercom; i <= (int)InputBinding.IntercomPTT; i++)
            {
                if (!currentInputProfile.ContainsKey((InputBinding)i))
                {
                    continue;
                }

                var input = currentInputProfile[(InputBinding)i];
                //construct InputBindState

                var bindState = new InputBindState()
                {
                    IsActive = false,
                    MainDevice = input,
                    MainDeviceState = false,
                    ModifierDevice = null,
                    ModifierState = false
                };

                if (currentInputProfile.ContainsKey((InputBinding)i + 100))
                {
                    bindState.ModifierDevice = currentInputProfile[(InputBinding)i + 100];
                }

                bindStates.Add(bindState);
            }
            for (int i = (int)InputBinding.IntercomVolume; i <= (int)InputBinding.Radio10Encryption; i++)
            {
                if (!currentInputProfile.ContainsKey((InputBinding)i))
                {
                    continue;
                }
                var input = currentInputProfile[(InputBinding)i];

                var bindState = new InputBindState()
                {
                    IsActive = false,
                    MainDevice = input,
                    MainDeviceState = false,
                    ModifierDevice = null,
                    ModifierState = false
                };

                bindStates.Add(bindState);
            }

            return bindStates;
        }

        private bool AxisDifference(int initial, int current)
        {
            return current != 0 ? Math.Abs(initial - current) > 10000 : Math.Abs(current - initial) > 10000;
        }

        public void UpdateAxisTune(InputBinding binding, double curvature, bool inverted)
        {
            InputAxisDevice inputAxisDevice = _globalSettings.ProfileSettingsStore.GetCurrentInputProfile()[binding] as InputAxisDevice;
            inputAxisDevice.Curvature = curvature;
            inputAxisDevice.Invert = inverted;

            _globalSettings.ProfileSettingsStore.SetControlSetting(inputAxisDevice);
        }
    }
}