using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Audio.Managers;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Helpers;
using NAudio.CoreAudioApi;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPages
{
    public partial class AudioPage : Page
    {
        public AudioInputSingleton AudioInput { get; } = AudioInputSingleton.Instance;
        public AudioOutputSingleton AudioOutput { get; } = AudioOutputSingleton.Instance;
        
        private AudioPreview _audioPreview;
        private readonly AudioManager _audioManager;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        private readonly DispatcherTimer _updateTimer;
        
        public AudioPage()
        {
            InitializeComponent();

            DataContext = this;
            
            SpeakerBoost.Value = _globalSettings.GetClientSetting(GlobalSettingsKeys.SpeakerBoost).DoubleValue;

            Speaker_VU.Value = -100;
            Mic_VU.Value = -100;
            
            _audioManager = new AudioManager(AudioOutput.WindowsN);
            _audioManager.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);

            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(_audioManager.SpeakerBoost);
            }
            
            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _updateTimer.Tick += UpdatePlayerLocationAndVUMeters;
            _updateTimer.Start();
            
            VOXMinimimumTXTime.Value = _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMinimumTime);
            VOXMode.Value = _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMode);
            VOXMinimumRMS.Value = _globalSettings.GetClientSettingDouble(GlobalSettingsKeys.VOXMinimumDB);
            PTTReleaseDelay.Value = _globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay);
            PTTStartDelay.Value = _globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.PTTStartDelay);
            RecordingQuality.Value = double.Parse(_globalSettings.GetClientSetting(GlobalSettingsKeys.RecordingQuality).StringValue[1].ToString());
        }

        private void SpeakerBoost_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var convertedValue = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);

            if (_audioPreview != null)
            {
                _audioPreview.SpeakerBoost = convertedValue;
            }
            if (_audioManager != null)
            {
                _audioManager.SpeakerBoost = convertedValue;
            }

            _globalSettings.SetClientSetting(GlobalSettingsKeys.SpeakerBoost,
                SpeakerBoost.Value.ToString(CultureInfo.InvariantCulture));


            if ((SpeakerBoostLabel != null) && (SpeakerBoost != null))
            {
                SpeakerBoostLabel.Content = VolumeConversionHelper.ConvertLinearDiffToDB(convertedValue);
            }
        }

        private void PreviewAudio(object sender, RoutedEventArgs e)
        {
            if (_audioPreview == null)
            {
                if (!AudioInput.MicrophoneAvailable)
                {
                    _logger.Info("Unable to preview audio, no valid audio input device available or selected");
                    return;
                }

                //get device
                try
                {
                    SaveSelectedInputAndOutput();

                    _audioPreview = new AudioPreview();
                    _audioPreview.SpeakerBoost = VolumeConversionHelper.ConvertVolumeSliderToScale((float)SpeakerBoost.Value);
                    _audioPreview.StartPreview(AudioOutput.WindowsN);

                    Preview.Content = "Stop Preview";
                }
                catch (Exception ex)
                {
                    _logger.Error(ex,
                        "Unable to preview audio - likely output device error - Pick another. Error:" + ex.Message);

                }
            }
            else
            {
                Preview.Content = "Audio Preview";
                _audioPreview.StopEncoding();
                _audioPreview = null;
            }
        }
        
        private void SaveSelectedInputAndOutput()
        {
            //save app settings
            // Only save selected microphone if one is actually available, resulting in a crash otherwise
            if (AudioInput.MicrophoneAvailable)
            {
                if (AudioInput.SelectedAudioInput.Value == null)
                {
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, "default");

                }
                else
                {
                    var input = ((MMDevice)AudioInput.SelectedAudioInput.Value).ID;
                    _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioInputDeviceId, input);
                }
            }

            if (AudioOutput.SelectedAudioOutput.Value == null)
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId, "default");
            }
            else
            {
                var output = (MMDevice)AudioOutput.SelectedAudioOutput.Value;
                _globalSettings.SetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId, output.ID);
            }

            //check if we have optional output
            if (AudioOutput.SelectedMicAudioOutput.Value != null)
            {
                var micOutput = (MMDevice)AudioOutput.SelectedMicAudioOutput.Value;
                _globalSettings.SetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId, micOutput.ID);
            }
            else
            {
                _globalSettings.SetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId, "");
            }

            ShowMicPassthroughWarning();
        }

        private void ShowMicPassthroughWarning()
        {
            if (_globalSettings.GetClientSetting(GlobalSettingsKeys.MicAudioOutputDeviceId).RawValue
                .Equals(_globalSettings.GetClientSetting(GlobalSettingsKeys.AudioOutputDeviceId).RawValue))
            {
                MessageBox.Show("Mic Output and Speaker Output should not be set to the same device!\n\nMic Output is just for recording and not for use as a sidetone. You will hear yourself with a small delay!\n\nHit disconnect and change Mic Output / Passthrough", "Warning", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        
        private void UpdatePlayerLocationAndVUMeters(object sender, EventArgs e)
        {
            if (_audioPreview != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (AudioInput.MicrophoneAvailable)
                {
                    Mic_VU.Value = _audioPreview.MicMax;
                }
                Speaker_VU.Value = _audioPreview.SpeakerMax;
            }
            else if (_audioManager != null)
            {
                // Only update mic volume output if an audio input device is available - sometimes the value can still change, leaving the user with the impression their mic is working after all
                if (AudioInput.MicrophoneAvailable)
                {
                    Mic_VU.Value = _audioManager.MicMax;
                }
                Speaker_VU.Value = _audioManager.SpeakerMax;
            }
            else
            {
                Mic_VU.Value = -100;
                Speaker_VU.Value = -100;
            }

            ConnectedClientsSingleton.Instance.NotifyAll();
        }
        
        private void VOXMinimumTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMinimimumTXTime.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMinimumTime, (int)e.NewValue);
        }
        
        private void VOXMode_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMode.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMode, (int)e.NewValue);
        }
        
        private void VOXMinimumRMS_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VOXMinimumRMS.IsEnabled)
                _globalSettings.SetClientSetting(GlobalSettingsKeys.VOXMinimumDB, e.NewValue);
        }
        
        private void PushToTalkReleaseDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PTTReleaseDelay.IsEnabled)
                _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.PTTReleaseDelay, (float)e.NewValue);
        }
        
        private void PushToTalkStartDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PTTStartDelay.IsEnabled)
                _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.PTTStartDelay, (float)e.NewValue);
        }
        
        private void RecordingQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.RecordingQuality, $"V{(int)e.NewValue}");
        }
    }
}