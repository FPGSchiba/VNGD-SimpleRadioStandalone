﻿using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons
{
    public class AudioInputSingleton
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        #region Singleton Definition
        private static volatile AudioInputSingleton _instance;
        private static object _lock = new Object();

        public static AudioInputSingleton Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new AudioInputSingleton();
                    }
                }

                return _instance;
            }
        }
        #endregion

        #region Instance Definition

        public List<AudioDeviceListItem> InputAudioDevices { get; }

        public AudioDeviceListItem SelectedAudioInput { get; set; }

        // Indicates whether a valid microphone is available - deactivating audio input controls and transmissions otherwise
        public bool MicrophoneAvailable { get; private set; }

        private AudioInputSingleton()
        {
            InputAudioDevices = BuildAudioInputs();
        }

        private List<AudioDeviceListItem> BuildAudioInputs()
        {
            Logger.Trace("Audio Input - Saved ID " +
                        GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.AudioInputDeviceId).RawValue);

            var inputs = new List<AudioDeviceListItem>();

            var deviceEnum = new MMDeviceEnumerator();
            var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

            if (devices.Count == 0)
            {
                MicrophoneAvailable = false;
                Logger.Trace("Audio Input - No audio input devices available, disabling mic preview");
                return inputs;
            }
            else
            {
                MicrophoneAvailable = true;

            }

            Logger.Trace("Audio Input - " + devices.Count + " audio input devices available, configuring as usual");

            inputs.Add(new AudioDeviceListItem()
            {
                Text = "Default Microphone",
                Value = null
            });
            SelectedAudioInput = inputs[0];

            foreach (var item in devices)
            {
                try
                {
                    var input = new AudioDeviceListItem()
                    {
                        Text = item.FriendlyName,
                        Value = item
                    };

                    Logger.Trace("Audio Input - " + item.DeviceFriendlyName + " " + item.ID + " CHN:" +
                                item.AudioClient.MixFormat.Channels + " Rate:" +
                                item.AudioClient.MixFormat.SampleRate.ToString());

                    inputs.Add(input);

                    if (item.ID.Trim().Equals(GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.AudioInputDeviceId).RawValue.Trim()))
                    {
                        SelectedAudioInput = input;
                        Logger.Trace("Audio Input - Found Saved ");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex,"Audio Input - " + item.DeviceFriendlyName );
                }
            
            }

            return inputs;
        }

        #endregion
    }
}
