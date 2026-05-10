using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MahApps.Metro.Controls;
using NLog;
using Vanguard.VCS.Client.Settings;

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
            var profile = GlobalSettingsStore.Instance.ProfileSettingsStore;

            try
            {
                Desc.Text = "";
                DCSPTT.Content = "Not Available - SRS Controls Only ";
                DCSRadioSwitch.Content = "Not Available - SRS Controls Only";
                DCSIFF.Content = "Not Available - SRS Controls Only";
                IntercomHotMic.Content = "Not Available";
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
