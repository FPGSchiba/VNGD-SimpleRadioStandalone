using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for RadioAudioConfigUI.xaml (created by Dabble)
    /// </summary>
    public partial class RadioAudioConfigUi : UserControl
    {

        public RadioAudioConfigUi()
        {
            InitializeComponent();

            //Copied from RadioChannelConfigUI.xaml by Dabble
            AudioSelector.Loaded += InitBalanceSlider;
        }

        public ProfileSettingsKeys ProfileSettingKey { get; set; }
        public float DefaultValue {  get; set; }

        private void InitBalanceSlider(object sender, RoutedEventArgs e)
        {
            AudioSelector.IsEnabled = false;
            GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingKey, DefaultValue);
            Reload();

            AudioSelector.ValueChanged += ChannelSelector_SelectionChanged;
        }

        public void Reload()
        {
            AudioSelector.IsEnabled = false;

            AudioSelector.Value = GlobalSettingsStore.Instance.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingKey);

            AudioSelector.IsEnabled = true;
        }

        private void ChannelSelector_SelectionChanged(object sender, EventArgs eventArgs)
        {
            //the selected value changes when 
            if (AudioSelector.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingKey,(float) AudioSelector.Value);
            }
        }
    }
}