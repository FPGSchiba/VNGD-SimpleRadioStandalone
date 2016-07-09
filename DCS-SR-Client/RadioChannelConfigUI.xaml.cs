using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for RadioChannelConfigUI.xaml
    /// </summary>
    public partial class RadioChannelConfigUI : UserControl
    {
        public RadioChannelConfigUI()
        {
            InitializeComponent();

            //I do this because at this point SettingConfig hasn't been set
            //but it has when this is called
            ChannelSelector.Loaded += InitComboBox;
        }

        public SettingType SettingConfig { get; set; }

        private void InitComboBox(object sender, RoutedEventArgs e)
        {
            ChannelSelector.SelectedValue = Settings.Instance.UserSettings[(int) SettingConfig];
        }

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = (string) ChannelSelector.SelectedValue;

            Settings.Instance.WriteSetting(SettingConfig, selected);
        }
    }
}