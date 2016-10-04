using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using MahApps.Metro.Controls;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    ///     Interaction logic for ServerSettingsWindow.xaml
    /// </summary>
    public partial class ServerSettingsWindow : MetroWindow
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly DispatcherTimer _updateTimer;

        public ServerSettingsWindow()
        {
            InitializeComponent();

            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};
            _updateTimer.Tick += UpdateUI;
            _updateTimer.Start();

            UpdateUI(null, null);
        }

        private void UpdateUI(object sender, EventArgs e)
        {
            var settings = ClientSync.ServerSettings;

            try
            {
                SpectatorAudio.Content = settings[(int) ServerSettingType.SPECTATORS_AUDIO_DISABLED]
                    ? "DISABLED"
                    : "ENABLED";

                CoalitionSecurity.Content = settings[(int) ServerSettingType.COALITION_AUDIO_SECURITY]
                    ? "ON"
                    : "OFF";

                LineOfSight.Content = settings[(int) ServerSettingType.LOS_ENABLED] ? "ON" : "OFF";

                Distance.Content = settings[(int) ServerSettingType.DISTANCE_ENABLED] ? "ON" : "OFF";

                RealRadio.Content = settings[(int) ServerSettingType.IRL_RADIO_TX] ? "ON" : "OFF";

                RadioRXInterference.Content = settings[(int) ServerSettingType.IRL_RADIO_RX_INTERFERENCE] ? "ON" : "OFF";

                RadioExpansion.Content = settings[(int) ServerSettingType.RADIO_EXPANSION] ? "ON" : "OFF";

                ServerVersion.Content = ClientSync.ServerVersion;
            }
            catch (IndexOutOfRangeException ex)
            {
                Logger.Warn("Missing Server Option - Connected to old server");
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