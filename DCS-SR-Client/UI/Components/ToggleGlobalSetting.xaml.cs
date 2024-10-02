using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.Components
{
    public partial class ToggleGlobalSetting : UserControl
    {
        public string Title { get; set; }
        
        public string SubTitle {get; set;}
        
        public GlobalSettingsKeys SettingKey { get; set; }
        
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        
        
        public ToggleGlobalSetting()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }
        
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var isChecked = _globalSettings.GetClientSettingBool(SettingKey);
            ToggleButton.IsChecked = isChecked;
        }
        
        private void ToggleButton_OnChecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(SettingKey, true);
        }

        private void ToggleButton_OnUnchecked(object sender, RoutedEventArgs e)
        {
            _globalSettings.SetClientSetting(SettingKey, false);
        }
    }
}