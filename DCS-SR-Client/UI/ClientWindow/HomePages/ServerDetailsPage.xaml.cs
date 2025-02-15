using System;
using System.Media;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages
{
    public partial class ServerDetailsPage : Page
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly DispatcherTimer _updateTimer;
        private readonly Brush _onColor = Brushes.ForestGreen;
        private readonly Brush _offColor = Brushes.DarkRed;
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        
        public ServerDetailsPage()
        {
            InitializeComponent();
            
            _updateTimer = new DispatcherTimer {Interval = TimeSpan.FromSeconds(1)};
            _updateTimer.Tick += UpdateUI;
            _updateTimer.Start();

            UpdateUI(null, null);
        }
        
        private void UpdateUI(object sender, EventArgs e)
        {
            var settings = _serverSettings;

            try
            {
                SpectatorAudio.Content = settings.GetSettingAsBool(ServerSettingsKeys.SPECTATORS_AUDIO_DISABLED)
                    ? "ON"
                    : "OFF";
                SpectatorAudio.Background = settings.GetSettingAsBool(ServerSettingsKeys.SPECTATORS_AUDIO_DISABLED)
                    ? _onColor
                    : _offColor;

                CoalitionSecurity.Content = settings.GetSettingAsBool(ServerSettingsKeys.COALITION_AUDIO_SECURITY)
                    ? "ON"
                    : "OFF";
                CoalitionSecurity.Background = settings.GetSettingAsBool(ServerSettingsKeys.COALITION_AUDIO_SECURITY)
                    ? _onColor
                    : _offColor;

                LineOfSight.Content = settings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED) ? "ON" : "OFF";
                LineOfSight.Background = settings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED) ? _onColor : _offColor;

                DistanceLimitations.Content = settings.GetSettingAsBool(ServerSettingsKeys.DISTANCE_ENABLED) ? "ON" : "OFF";
                DistanceLimitations.Background = settings.GetSettingAsBool(ServerSettingsKeys.DISTANCE_ENABLED) ? _onColor : _offColor;

                RealRadioBehaviour.Content = settings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_TX) ? "ON" : "OFF";
                RealRadioBehaviour.Background = settings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_TX) ? _onColor : _offColor;

                RealRadioInterface.Content =
                    settings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE) ? "ON" : "OFF";
                RealRadioInterface.Background =
                    settings.GetSettingAsBool(ServerSettingsKeys.IRL_RADIO_RX_INTERFERENCE) ? _onColor : _offColor;

                RadioExpansion.Content = settings.GetSettingAsBool(ServerSettingsKeys.RADIO_EXPANSION) ? "ON" : "OFF";
                RadioExpansion.Background =
                    settings.GetSettingAsBool(ServerSettingsKeys.RADIO_EXPANSION) ? _onColor : _offColor;

                ExternalMode.Content = settings.GetSettingAsBool(ServerSettingsKeys.EXTERNAL_AWACS_MODE) ? "ON" : "OFF";
                ExternalMode.Background = settings.GetSettingAsBool(ServerSettingsKeys.EXTERNAL_AWACS_MODE) ? _onColor : _offColor;

                RadioEncryption.Content = settings.GetSettingAsBool(ServerSettingsKeys.ALLOW_RADIO_ENCRYPTION) ? "ON" : "OFF";
                RadioEncryption.Background = settings.GetSettingAsBool(ServerSettingsKeys.ALLOW_RADIO_ENCRYPTION) ? _onColor : _offColor;

                StrictEncryption.Content = settings.GetSettingAsBool(ServerSettingsKeys.STRICT_RADIO_ENCRYPTION) ? "ON" : "OFF";
                StrictEncryption.Background = settings.GetSettingAsBool(ServerSettingsKeys.STRICT_RADIO_ENCRYPTION) ? _onColor : _offColor;

                TunedClientCount.Content = settings.GetSettingAsBool(ServerSettingsKeys.SHOW_TUNED_COUNT) ? "ON" : "OFF";
                TunedClientCount.Background = settings.GetSettingAsBool(ServerSettingsKeys.SHOW_TUNED_COUNT) ? _onColor : _offColor;

                TransmitterName.Content = settings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME) ? "ON" : "OFF";
                TransmitterName.Background = settings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME) ? _onColor : _offColor;
                
                ServerVersion.Content = SrsClientSyncHandler.ServerVersion;

                RetransmitLimit.Content = settings.RetransmitNodeLimit;
            }
            catch (IndexOutOfRangeException)
            {
                _logger.Warn("Missing Server Option - Connected to old server");
            }
        }
    }
}