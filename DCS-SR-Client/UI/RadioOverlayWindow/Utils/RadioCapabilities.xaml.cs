using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Singletons;
using MahApps.Metro.Controls;
using NLog;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace Vanguard.VCS.Client.UI.RadioOverlayWindow.Utils
{
    /// <summary>
    /// Interaction logic for RadioCapabilities.xaml
    /// </summary>
    public partial class RadioCapabilities : MetroWindow
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly DispatcherTimer _updateTimer;


        public RadioCapabilities()
        {
            InitializeComponent();

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _updateTimer.Tick += UpdateUI;
            _updateTimer.Start();

            UpdateUI(null, null);
        }

        private void UpdateUI(object sender, EventArgs e)
        {
            var radioInfo = ClientStateSingleton.Instance.DcsPlayerRadioInfo;

            var profile = GlobalSettingsStore.Instance.ProfileSettingsStore;

            try
            {
                if (radioInfo.IsCurrent())
                {

                    Desc.Text = radioInfo.capabilities.desc;

                    if (radioInfo.capabilities.dcsPtt)
                    {
                        DCSPTT.Content = "Available in Cockpit";

                        if (!profile.GetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT))
                        {
                            DCSPTT.Content += " - Disabled in SRS";
                        }
                        
                    }
                    else
                    {
                        DCSPTT.Content = "Not Available - SRS Controls Only ";
                    }

                    if (radioInfo.capabilities.dcsRadioSwitch)
                    {
                        DCSRadioSwitch.Content = "Available in Cockpit";

                        if (profile.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls))
                        {
                            DCSRadioSwitch.Content += " - Disabled in SRS";
                        }

                    }
                    else
                    {
                        DCSRadioSwitch.Content = "Not Available - SRS Controls Only";
                    }

                    if (radioInfo.capabilities.dcsIFF)
                    {
                        DCSIFF.Content = "Available in Cockpit";

                        if (profile.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowTransponderOverlay))
                        {
                            DCSIFF.Content += " - Disabled in SRS";
                        }

                    }
                    else
                    {
                        DCSIFF.Content = "Not Available - SRS Controls Only";
                    }

                    if (radioInfo.capabilities.intercomHotMic)
                    {
                        IntercomHotMic.Content = "Available in Cockpit";

                        if (!profile.GetClientSettingBool(ProfileSettingsKeys.AllowDCSPTT) || profile.GetClientSettingBool(ProfileSettingsKeys.AlwaysAllowHotasControls))
                        {
                            IntercomHotMic.Content += " - Disabled in SRS";
                        }

                    }
                    else
                    {
                        IntercomHotMic.Content = "Not Available";
                    }

                }
                else
                {
                    Desc.Text = "";
                    DCSPTT.Content = "Unknown";
                    DCSRadioSwitch.Content = "Unknown";
                    DCSIFF.Content = "Unknown";
                    IntercomHotMic.Content = "Unknown";

                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error showing capabilities");
            }
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            _updateTimer.Stop();
        }
    }
}