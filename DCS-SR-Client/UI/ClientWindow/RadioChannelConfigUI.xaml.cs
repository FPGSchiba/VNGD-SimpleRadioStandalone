using System;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for RadioChannelConfigUI.xaml
    /// </summary>
    public partial class RadioChannelConfigUi : UserControl
    {
        public RadioChannelConfigUi()
        {
            InitializeComponent();

            //I do this because at this point ProfileSettingKey hasn't been set
            //but it has when this is called
            ChannelSelector.Loaded += InitComboBox;
        }

        public ProfileSettingsKeys ProfileSettingKey { get; set; }

        private void InitComboBox(object sender, RoutedEventArgs e)
        {
            ChannelSelector.IsEnabled = false;
            //cannot be inlined or it causes an issue
            var value = GlobalSettingsStore.Instance.ProfileSettingsStore.GetClientSetting(ProfileSettingKey).StringValue;

            if (value == null || value == "")
            {
                ChannelSelector.SelectedValue = "Both";
            }
            else
            {
                ChannelSelector.SelectedValue = value;
            }
            
            ChannelSelector.SelectionChanged += ChannelSelector_SelectionChanged;

            ChannelSelector.IsEnabled = true;
        }

        public void Reload()
        {
            ChannelSelector.IsEnabled = false;
            var setting = GlobalSettingsStore.Instance.ProfileSettingsStore.GetClientSetting(ProfileSettingKey).StringValue;
            if (setting == null || setting == "")
            {
                ChannelSelector.SelectedValue = "Both";
            }
            else
            {
                ChannelSelector.SelectedValue = setting;
            }
            ChannelSelector.IsEnabled = true;
        }

        private void ChannelSelector_SelectionChanged(object sender, EventArgs eventArgs)
        {
            //the selected value changes when 
            if (ChannelSelector.IsEnabled)
            {
                var selected = (string)ChannelSelector.SelectedValue;

                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSetting(ProfileSettingKey, selected);
            }
        }
    }
}