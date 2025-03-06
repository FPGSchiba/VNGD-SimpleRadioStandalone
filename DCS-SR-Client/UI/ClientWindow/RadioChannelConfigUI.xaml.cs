﻿using System;
using System.Globalization;
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
            ChannelSelector.Loaded += InitBalanceSlider;
        }

        public ProfileSettingsKeys ProfileSettingKey { get; set; }
        public float DefaultValue {  get; set; }

        private void InitBalanceSlider(object sender, RoutedEventArgs e)
        {
            ChannelSelector.IsEnabled = false;
            Reload();

            ChannelSelector.ValueChanged += ChannelSelector_SelectionChanged;
        }

        public void Reload()
        {
            ChannelSelector.IsEnabled = false;

            ChannelSelector.Value = GlobalSettingsStore.Instance.ProfileSettingsStore.GetClientSettingFloat(ProfileSettingKey);

            ChannelSelector.IsEnabled = true;
        }

        private void ChannelSelector_SelectionChanged(object sender, EventArgs eventArgs)
        {
            //the selected value changes when 
            if (ChannelSelector.IsEnabled)
            {
                GlobalSettingsStore.Instance.ProfileSettingsStore.SetClientSettingFloat(ProfileSettingKey,(float) ChannelSelector.Value);
            }
        }
    }
}