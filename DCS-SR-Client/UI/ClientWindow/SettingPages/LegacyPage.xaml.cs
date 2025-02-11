using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Microsoft.Win32;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPages
{
    public partial class LegacyPage : Page
    {
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        
        public LegacyPage()
        {
            InitializeComponent();
            var isChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.NATOTone);
            ToggleButton.IsChecked = isChecked;
            isChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone);
            HaveToggleButton.IsChecked = isChecked;
            
            // FM Tone Volume
            NATOToneVolume.IsEnabled = false;
            NATOToneVolume.ValueChanged += (sender, e) =>
            {
                if (NATOToneVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume, (float)vol);
                }

            };
            NATOToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.NATOToneVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.NATOToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            NATOToneVolume.IsEnabled = true;
            
            // HAVEQUICK Tone Volume
            HQToneVolume.IsEnabled = false;
            HQToneVolume.ValueChanged += (sender, e) =>
            {
                if (HQToneVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HQToneVolume, (float)vol);
                }

            };
            HQToneVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HQToneVolume)
                                  / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HQToneVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HQToneVolume.IsEnabled = true;
            
            // UHF Effect Volume
            UHFEffectVolume.IsEnabled = false;
            UHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (UHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume, (float)vol);
                }

            };
            UHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.UHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.UHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            UHFEffectVolume.IsEnabled = true;
            
            // VHF Effect Volume
            VHFEffectVolume.IsEnabled = false;
            VHFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (VHFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume, (float)vol);
                }

            };
            VHFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.VHFNoiseVolume)
                                     / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.VHFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            VHFEffectVolume.IsEnabled = true;
            
            // HF Effect Volume
            HFEffectVolume.IsEnabled = false;
            HFEffectVolume.ValueChanged += (sender, e) =>
            {
                if (HFEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume, (float)vol);
                }

            };
            HFEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.HFNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.HFNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            HFEffectVolume.IsEnabled = true;
            
            // FM Effect Volume
            FMEffectVolume.IsEnabled = false;
            FMEffectVolume.ValueChanged += (sender, e) =>
            {
                if (FMEffectVolume.IsEnabled)
                {
                    var orig = double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture);

                    var vol = orig * (e.NewValue / 100);

                    _globalSettings.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume, (float)vol);
                }

            };
            FMEffectVolume.Value = (_globalSettings.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingsKeys.FMNoiseVolume)
                                    / double.Parse(ProfileSettingsStore.DefaultSettingsProfileSettings[ProfileSettingsKeys.FMNoiseVolume.ToString()], CultureInfo.InvariantCulture)) * 100;
            FMEffectVolume.IsEnabled = true;
        }
        
        private void SetSRSPath_Click(object sender, RoutedEventArgs e)
        {
            Registry.SetValue("HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone", "SRPathStandalone", Directory.GetCurrentDirectory());

            MessageBox.Show(
                "SRS Path set to: " + Directory.GetCurrentDirectory(),
                "SRS Client Path",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        
        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.NATOTone, true);
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.NATOTone, false);
        }
        
        private void HaveToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone, true);
        }

        private void HaveToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(ProfileSettingsKeys.HAVEQUICKTone, false);
        }
    }
}