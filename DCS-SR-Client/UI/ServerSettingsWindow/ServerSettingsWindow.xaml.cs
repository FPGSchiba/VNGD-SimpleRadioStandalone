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
using System.Windows.Shapes;
using Ciribob.DCS.SimpleRadio.Standalone.Server;
using MahApps.Metro.Controls;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    /// <summary>
    /// Interaction logic for ServerSettingsWindow.xaml
    /// </summary>
    public partial class ServerSettingsWindow : MetroWindow
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public ServerSettingsWindow()
        {
            InitializeComponent();

            var settings = ClientSync.ServerSettings;

            try
            {
                SpectatorAudio.Content = settings[(int)ServerSettingType.SPECTATORS_AUDIO_DISABLED] == "DISABLED" ? "DISABLED" : "ENABLED";

                CoalitionSecurity.Content = settings[(int)ServerSettingType.COALITION_AUDIO_SECURITY] == "ON" ? "ON" : "OFF";

                LineOfSight.Content = settings[(int)ServerSettingType.LOS_ENABLED] == "ON" ? "ON" : "OFF";

                Distance.Content = settings[(int)ServerSettingType.DISTANCE_ENABLED] == "ON" ? "ON" : "OFF";

                RealRadio.Content = settings[(int)ServerSettingType.IRL_RADIO_TX] == "ON" ? "ON" : "OFF";
            }
            catch (IndexOutOfRangeException ex)
            {
                Logger.Warn("Missing Server Option - Connected to old server");
                
            }
           
        }

        private void CloseButton_OnClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
