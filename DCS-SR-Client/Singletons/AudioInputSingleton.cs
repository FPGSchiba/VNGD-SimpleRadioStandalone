using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow;
using NAudio.Wave;
using NLog;
using System;
using System.Collections.Generic;

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
        public bool MicrophoneAvailable { get; }

        private AudioInputSingleton()
        {
            InputAudioDevices = BuildAudioInputs();
            MicrophoneAvailable = DetectMicrophone();
        }

        private List<AudioDeviceListItem> BuildAudioInputs()
        {
            Logger.Info("Audio Input - Saved ID " +
            GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.AudioInputDeviceId).StringValue);

            var inputs = new List<AudioDeviceListItem>();

            if (WaveIn.DeviceCount == 0)
            {
                Logger.Info("Audio Input - No audio input devices available, disabling mic preview");
                return inputs;
            }

            Logger.Info("Audio Input - " + WaveIn.DeviceCount.ToString() + " audio input devices available, configuring as usual");

            inputs.Add(new AudioDeviceListItem()
            {
                Text = "Default Microphone",
                Value = null
            });
            SelectedAudioInput = inputs[0];

            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {

                var item = WaveIn.GetCapabilities(i);
                inputs.Add(new AudioDeviceListItem()
                {
                    Text = item.ProductName,
                    Value = item
                });

                Logger.Info("Audio Input - " + item.ProductName + " " + item.ProductGuid.ToString() + " - Name GUID" +
                            item.NameGuid + " - CHN:" + item.Channels);

                if (item.ProductName.Trim().StartsWith(GlobalSettingsStore.Instance.GetClientSetting(GlobalSettingsKeys.AudioInputDeviceId).StringValue.Trim()))
                {
                    SelectedAudioInput = inputs[i + 1];
                    Logger.Info("Audio Input - Found Saved ");
                }
            }

            return inputs;
        }

        private bool DetectMicrophone()
        {
            return (WaveIn.DeviceCount > 0);
        }
        #endregion
    }
}
