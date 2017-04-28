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

            //I do this because at this point SettingConfig hasn't been set
            //but it has when this is called
            ChannelSelector.Loaded += InitComboBox;
        }

        public SettingType SettingConfig { get; set; }

        private void InitComboBox(object sender, RoutedEventArgs e)
        {
            ChannelSelector.SelectedValue = Settings.SettingsStore.Instance.UserSettings[(int) SettingConfig];
        }

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (string) ChannelSelector.SelectedValue;

            Settings.SettingsStore.Instance.WriteSetting(SettingConfig, selected);
        }
    }
}