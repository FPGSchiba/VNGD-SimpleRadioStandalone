using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.Components
{
    public partial class ToggleProfileSetting : UserControl
    {
        public string Title { get; set; }
        
        public string SubTitle {get; set;}
        
        public ProfileSettingsKeys SettingKey { get; set; }
        
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        
        public ToggleProfileSetting()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }
        
        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            var isChecked = _globalSettings.ProfileSettingsStore.GetClientSettingBool(SettingKey);
            ToggleButton.IsChecked = isChecked;
        }

        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(SettingKey, true);
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.ProfileSettingsStore.SetClientSettingBool(SettingKey, false);
        }
    }
}