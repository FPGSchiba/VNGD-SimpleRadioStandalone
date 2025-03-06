﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;
using NLog;
using SharpDX.DirectInput;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Input
{
    public class InputDeviceManager : IDisposable
    {
        public delegate void DetectButton(InputDevice inputDevice);

        public delegate void DetectPttCallback(List<InputBindState> buttonStates);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly HashSet<Guid> _blacklistedDevices = new HashSet<Guid>
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
        private readonly HashSet<Guid> _whitelistedDevices = new HashSet<Guid>
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
            new Guid("c2ab046d-0000-0000-0000-504944564944"),  // Logitech G13 Joystick
            new Guid("aaaa3344-0000-0000-0000-504944564944") //K51, it uses VPC board
        };

        private readonly DirectInput _directInput;
        private readonly Dictionary<Guid, dynamic> _inputDevices = new Dictionary<Guid, dynamic>();
        private readonly MainWindow.ToggleOverlayCallback _toggleOverlayCallback;
        private readonly MainWindow.UpdateChannelCallback _updateChannelCallback;

        private volatile bool _detectPtt;

        //used to trigger the update to a frequency
        private InputBinding _lastActiveBinding = InputBinding.ModifierIntercom
            ; //intercom used to represent null as we cant

        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;


        public InputDeviceManager(Window window, MainWindow.ToggleOverlayCallback toggleOverlayCallback, MainWindow.UpdateChannelCallback updateChannelCallback)
        {
            _directInput = new DirectInput();


            WindowHelper =
                new WindowInteropHelper(window);

            _toggleOverlayCallback = toggleOverlayCallback;
            _updateChannelCallback = updateChannelCallback;

            LoadWhiteList();

            LoadBlackList();

            InitDevices();
        }

        public void InitDevices()
        {
            var allowXInput = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AllowXInputController);
            Logger.Info("Starting Device Search. Expand Search: " +
            (_globalSettings.GetClientSettingBool(GlobalSettingsKeys.ExpandControls)) +
            ". Use XInput (for Xbox controllers): " + allowXInput);


            var deviceInstances = _directInput.GetDevices();

            _inputDevices.Clear();

            if (allowXInput)
            {
                _inputDevices.Add(XInputController.DeviceGuid, new XInputController());
            }
            

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

                Logger.Trace("Found Device ID:" + deviceInstance.ProductGuid +
                            " " +
                            deviceInstance.ProductName.Trim().Replace("\0", "") + " Usage: " +
                            deviceInstance.UsagePage + " Type: " +
                            deviceInstance.Type);
                if (_inputDevices.ContainsKey(deviceInstance.InstanceGuid))
                {
                    Logger.Trace("Already have device:" + deviceInstance.ProductGuid +
                                " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                    continue;
                }


                if (deviceInstance.Type == DeviceType.Keyboard)
                {

                    Logger.Trace("Adding Device ID:" + deviceInstance.ProductGuid +
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
                    Logger.Trace("Adding Device ID:" + deviceInstance.ProductGuid + " " +
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

                    Logger.Trace("Adding ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);
                }
                else if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.ExpandControls))
                {
                    Logger.Trace("Adding (Expanded Devices) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));

                    var device = new Joystick(_directInput, deviceInstance.InstanceGuid);

                    device.SetCooperativeLevel(WindowHelper.Handle,
                        CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                    device.Acquire();

                    _inputDevices.Add(deviceInstance.InstanceGuid, device);

                    Logger.Trace("Added (Expanded Device) ID:" + deviceInstance.ProductGuid + " " +
                                deviceInstance.ProductName.Trim().Replace("\0", ""));
                }
            }
        }

        private void LoadWhiteList()
        {
            var path = Environment.CurrentDirectory + "\\whitelist.txt";
            Logger.Info("Attempt to Load Whitelist from " + path);

            LoadGuidFromPath(path, _whitelistedDevices);
        }

        private void LoadBlackList()
        {
            var path = Environment.CurrentDirectory + "\\blacklist.txt";
            Logger.Info("Attempt to Load Blacklist from " + path);

            LoadGuidFromPath(path, _blacklistedDevices);
        }

        private static void LoadGuidFromPath(string path, HashSet<Guid> hashSet)
        {
            if (!File.Exists(path))
            {
                Logger.Info("File doesnt exist: " + path);
                return;
            }

            string[] lines = File.ReadAllLines(path);
            if (lines.Length <= 0)
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
                        hashSet.Add(new Guid(trimmed));
                        Logger.Trace("Added " + trimmed);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Failed to parse GUID from " + trimmed);
                    }
                }
            }
        }

        private WindowInteropHelper WindowHelper { get; }

        
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopPtt();
                foreach (var device in _inputDevices.Select(kpDevice => kpDevice is KeyValuePair<Guid, dynamic> ? kpDevice.Value : null).Where(device => device != null && !device.IsDisposed))
                {
                    device.Unacquire();
                    device.Dispose();
                }
            }
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool IsBlackListed(Guid device)
        {
            return _blacklistedDevices.Contains(device);
        }

        private bool IsWhiteListed(Guid device)
        {
            return _whitelistedDevices.Contains(device);
        }

        private void PollAllDevices()
        {
            var deviceList = _inputDevices.Values.ToList();

            for (var i = 0; i < deviceList.Count; i++)
            {
                if (deviceList[i] == null || deviceList[i].IsDisposed)
                {
                    continue;
                }

                try
                {
                    deviceList[i].Poll();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to get current state of input device {deviceList[i].Information.ProductName.Trim().Replace("\0", "")} " +
                                    $"(ID: {deviceList[i].Information.ProductGuid}) while assigning button, ignoring until next restart/rediscovery");
                    try
                    {
                        deviceList[i].Unacquire();
                        deviceList[i].Dispose();
                        deviceList[i] = null;
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Failed to unacquire and dispose device");
                    }
                }
            }
        }

        public void AssignButton(DetectButton callback)
        {
            //detect the state of all current buttons
            Task.Run(() =>
            {
                var deviceList = _inputDevices.Values.ToList();

                var initial = new int[deviceList.Count, 128 + 4]; // for POV

                PollAllDevices();

                for (var i = 0; i < deviceList.Count; i++)
                {
                    if (deviceList[i] == null || deviceList[i].IsDisposed)
                    {
                        continue;
                    }

                    try
                    {
                        if (deviceList[i] is XInputController deviceXInputController)
                        {
                            var state = (deviceXInputController).GetCurrentState();
                            var values = (SharpDX.XInput.GamepadButtonFlags[])Enum.GetValues(typeof(SharpDX.XInput.GamepadButtonFlags));
                            for (var j = 0; j < values.Length; j++)
                            {
                                initial[i, j] = state.HasFlag(values[j]) ? 1 : 0;
                            }
                        }
                        else if (deviceList[i] is Joystick joystick)
                        {

                            var state = (joystick).GetCurrentState();

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
                        else if (deviceList[i] is Keyboard keyboard)
                        {
                            var state = keyboard.GetCurrentState();

                            for (var j = 0; j < 128; j++)
                            {
                                initial[i, j] = state.IsPressed(state.AllKeys[j]) ? 1 : 0;
                            }
                        }
                        else if (deviceList[i] is Mouse mouse)
                        {
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
                
                var found = false;

                while (!found)
                {
                    Thread.Sleep(100);

                    PollAllDevices();

                    for (var i = 0; i < _inputDevices.Count; i++)
                    {
                        if (deviceList[i] == null || deviceList[i].IsDisposed)
                        {
                            continue;
                        }

                        try
                        {
                            if (deviceList[i] is XInputController xInputController)
                            {
                                var state = (xInputController).GetCurrentState();
                                var values = (SharpDX.XInput.GamepadButtonFlags[])Enum.GetValues(typeof(SharpDX.XInput.GamepadButtonFlags));
                                for (var j = 0; j < values.Length; j++)
                                {
                                    var buttonState = state.HasFlag(values[j]) ? 1 : 0;
                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;
                                        var inputDevice = new InputDevice
                                        {
                                            DeviceName = "XInputController",
                                            Button = (int)values[j],
                                            InstanceGuid = deviceList[i].Information.InstanceGuid,
                                            ButtonValue = buttonState
                                        };

                                        Application.Current.Dispatcher.Invoke(
                                            () => { callback(inputDevice); });

                                        return;
                                    }
                                }
                            }
                            else if (deviceList[i] is Joystick joystick)
                            {
                                var state = joystick.GetCurrentState();

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

                                        if (buttonState == 1 && buttonState != initial[i, j])
                                        {
                                            found = true;

                                            var inputDevice = new InputDevice
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
                            else if (deviceList[i] is Keyboard keyboard)
                            {
                                var state = keyboard.GetCurrentState();

                                for (var j = 0; j < 128; j++)
                                {
                                    if (initial[i, j] != (state.IsPressed(state.AllKeys[j]) ? 1 : 0))
                                    {
                                        found = true;

                                        var inputDevice = new InputDevice
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
                                }
                            }
                            else if (deviceList[i] is Mouse mouse)
                            {
                                var state = mouse.GetCurrentState();

                                //skip left mouse button - start at 1 with j 0 is left, 1 is right, 2 is middle
                                for (var j = 1; j < state.Buttons.Length; j++)
                                {
                                    var buttonState = state.Buttons[j] ? 1 : 0;

                                    if (buttonState != initial[i, j])
                                    {
                                        found = true;

                                        var inputDevice = new InputDevice
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
                foreach (var device in _inputDevices.Select(kpDevice => kpDevice is KeyValuePair<Guid, dynamic> ? kpDevice.Value : null).Where(device => device != null && !device.IsDisposed && device.Information.InstanceGuid.Equals(deviceGuid)))
                {
                    try
                    {
                        //poll the device as it has a bind
                        device.Poll();
                    }
                    catch (Exception e)
                    {
                        // ignored
                        DeviceError(device,e);
                    }
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

                //Poll devices with all current binds
                PollDevices(bindStates);


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

                foreach (var bindState in bindStates)
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

                        var dcsPlayerRadioInfo = ClientStateSingleton.Instance.DcsPlayerRadioInfo;

                        if (dcsPlayerRadioInfo != null && dcsPlayerRadioInfo.IsCurrent())
                        {
                            var currentChannel = GetCurrentChannel();
                            var currentBalance = 0.0f;
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
                                case InputBinding.RadioSwap:
                                    var radioId = dcsPlayerRadioInfo.selected;
                                    var freq = dcsPlayerRadioInfo.radios[dcsPlayerRadioInfo.selected].freq;
                                    var standbyFreq = dcsPlayerRadioInfo.radios[dcsPlayerRadioInfo.selected].standbyfreq ;
                                    RadioHelper.UpdateStandbyRadioFrequency(freq, radioId, false, false);
                                    RadioHelper.UpdateRadioFrequency(standbyFreq, radioId, false, false);
                                    break;
                                case InputBinding.Radio1VToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.OneVerticalIndex); });
                                    break;
                                case InputBinding.Radio1HToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.OneHorizontalIndex); });
                                    break;
                                case InputBinding.Radio2VToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TwoVerticalIndex); });
                                    break;
                                case InputBinding.Radio2HToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TwoVerticalIndex); });
                                    break;
                                case InputBinding.Radio3VToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.ThreeVerticalIndex); });
                                    break;
                                case InputBinding.Radio3HToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.ThreeHorizontalIndex); });
                                    break;
                                case InputBinding.Radio5VToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.FiveVerticalIndex); });
                                    break;
                                case InputBinding.Radio5HToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.FiveHorizontalIndex); });
                                    break;
                                case InputBinding.Radio10VToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TenVerticalIndex); });
                                    break;
                                case InputBinding.Radio10HToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TenHorizontalIndex); });
                                    break;
                                case InputBinding.Radio10VLToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TenVerticalLongIndex); });
                                    break;
                                case InputBinding.Radio10HWToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TenHorizontalWideIndex); });
                                    break;
                                case InputBinding.Radio10TToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.TransparentIndex); });
                                    break;
                                case InputBinding.Radio10SToggle:
                                    Application.Current.Dispatcher.Invoke(() => { _toggleOverlayCallback(false, MainWindow.SwitchIndex); });
                                    break;
                                case InputBinding.LeftBalance:
                                    currentChannel = GetCurrentChannel();
                                    currentBalance = Math.Max(_globalSettings.ProfileSettingsStore.GetClientSettingFloat(currentChannel) - 0.1f, -1.0f);
                                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(currentChannel, currentBalance);
                                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                                        new ThreadStart(delegate { _updateChannelCallback(currentChannel, currentBalance); }));
                                    break;
                                case InputBinding.RightBalance:
                                    currentChannel = GetCurrentChannel();
                                    currentBalance = Math.Min(_globalSettings.ProfileSettingsStore.GetClientSettingFloat(currentChannel) + 0.1f, 1.0f);
                                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(currentChannel, currentBalance);
                                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                                        new ThreadStart(delegate { _updateChannelCallback(currentChannel, currentBalance); }));
                                    break;
                                case InputBinding.CenterBalance:
                                    currentChannel = GetCurrentChannel();
                                    currentBalance = 0f;
                                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(currentChannel, currentBalance);
                                    Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
                                        new ThreadStart(delegate { _updateChannelCallback(currentChannel, currentBalance); }));
                                    break;
                                case InputBinding.PanelNightMode:
                                    // Call back UI for update
                                    //Select current panel and determine if background opacity is less than .2.
                                    //If true, then set background opacity and text to 1.0. 
                                    //Else, set background opacity and text .2
                                    //needs to be added
                                    Logger.Debug("Swapped night mode.");
                                    break;
                            }
                        }
                    }
                
                }

                Thread.Sleep(40);
            }
            });
            pttInputThread.Start();
        }


        private static ProfileSettingsKeys GetCurrentChannel()
        {
            switch (ClientStateSingleton.Instance.DcsPlayerRadioInfo.selected)
            {
                case 0:
                    return ProfileSettingsKeys.IntercomChannel;
                case 1:
                    return ProfileSettingsKeys.Radio1Channel;
                case 2:
                    return ProfileSettingsKeys.Radio2Channel;
                case 3:
                    return ProfileSettingsKeys.Radio3Channel;
                case 4:
                    return ProfileSettingsKeys.Radio4Channel;
                case 5:
                    return ProfileSettingsKeys.Radio5Channel;
                case 6:
                    return ProfileSettingsKeys.Radio6Channel;
                case 7:
                    return ProfileSettingsKeys.Radio7Channel;
                case 8:
                    return ProfileSettingsKeys.Radio8Channel;
                case 9:
                    return ProfileSettingsKeys.Radio9Channel;
                case 10:
                    return ProfileSettingsKeys.Radio10Channel;
                default: // This should not happen (we only have 11 radios (10 radios + 1 intercom))
                    return ProfileSettingsKeys.Radio1Channel;
            }
        }
        
        public void StopPtt()
        {
            _detectPtt = false;
        }

        private bool GetButtonState(InputDevice inputDeviceBinding)
        {
            foreach (var device in _inputDevices.Select(kpDevice => kpDevice is KeyValuePair<Guid, dynamic> ? kpDevice.Value : null).Where(device => device != null && !device.IsDisposed && device.Information.InstanceGuid.Equals(inputDeviceBinding.InstanceGuid)))
            {
                try
                {
                    if (device is XInputController xInputController)
                    {
                        var state = xInputController.GetCurrentState();
                        return state.HasFlag((SharpDX.XInput.GamepadButtonFlags)inputDeviceBinding.Button);
                    }
                    if (device is Joystick joystick)
                    {
                        var state = joystick.GetCurrentState();

                        if (inputDeviceBinding.Button >= 128) //its a POV!
                        {
                            var pov = state.PointOfViewControllers;
                            //-128 to get POV index
                            return pov[inputDeviceBinding.Button - 128] == inputDeviceBinding.ButtonValue;
                        }
                        return state.Buttons[inputDeviceBinding.Button];
                    }
                    if (device is Keyboard keyboard)
                    {
                        var state = keyboard.GetCurrentState();
                        return state.IsPressed(state.AllKeys[inputDeviceBinding.Button]);
                    }
                    if (device is Mouse mouse)
                    {
                        var state = mouse.GetCurrentState();

                        //just incase mouse changes number of buttons, like logitech can?
                        if (inputDeviceBinding.Button < state.Buttons.Length)
                        {
                            return state.Buttons[inputDeviceBinding.Button];
                        }
                    }
                }
                catch (Exception e)
                {
                   DeviceError(device, e);
                }

            }
            return false;
        }

        private static void DeviceError(Device device, Exception e)
        {
            Logger.Error(e, $"Failed to get current state of input device {device.Information.ProductName.Trim().Replace("\0", "")} " +
                            $"(ID: {device.Information.ProductGuid}) while retrieving button state, ignoring until next restart/rediscovery");

            try
            {
                device.Unacquire();
            }
            catch (Exception)
            {
                // ignored
            }

            try
            {
                device.Dispose();
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private List<InputBindState> GenerateBindStateList()
        {
            var bindStates = new List<InputBindState>();
            var currentInputProfile = _globalSettings.ProfileSettingsStore.GetCurrentInputProfile();

            //REMEMBER TO UPDATE THIS WHEN NEW BINDINGS ARE ADDED
            //MIN + MAX bind numbers
            for (int i = (int)InputBinding.Intercom; i <= (int)InputBinding.PanelNightMode; i++)  // schiba updated night mode
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

            return bindStates;
        }
    }
}