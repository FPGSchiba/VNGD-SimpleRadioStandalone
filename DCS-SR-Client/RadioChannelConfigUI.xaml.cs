using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for RadioChannelConfigUI.xaml
    /// </summary>
    public partial class RadioChannelConfigUI : UserControl
    {
        public SettingType SettingConfig {
            get;
            set;
        }


        public RadioChannelConfigUI()
        {
            InitializeComponent();

            //I do this because at this point SettingConfig hasn't been set
            //but it has when this is called
            ChannelSelector.Loaded += InitComboBox;

        }

        private void InitComboBox(object sender, RoutedEventArgs e)
        {
            ChannelSelector.SelectedValue = Settings.Instance.UserSettings[(int)SettingConfig];
        }

        private void ChannelSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            String selected = (String) ChannelSelector.SelectedValue;

            Settings.Instance.WriteSetting(SettingConfig,selected);
        }
    }
}
