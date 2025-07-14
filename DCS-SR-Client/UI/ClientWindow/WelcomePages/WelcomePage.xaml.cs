using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NLog;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Utils;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages
{
    /// <summary>
    /// Interaction logic for WelcomePage.xaml
    /// </summary>
    public partial class WelcomePage : Page
    {
        private MainWindow mainWindow;

        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        public delegate void ServerInformationFetchedCallback(ServerInformation serverInformation);

        public WelcomePage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            // per default, disabled and check which Login types are available
            Guest.IsEnabled = false;
            Login.IsEnabled = false;
            
            GetServerInformation(ServerInformationFetched);
        }
        
        private void GetServerInformation(ServerInformationFetchedCallback callback)
        {
            WebsiteClient.GetServerInformation().ContinueWith((task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    callback(task.Result);
                }
                else
                {
                    MessageBox.Show("Failed to retrieve server information.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ConnectionFailed();
                }
            }));
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_WelcomeLoginClicked();
        }

        private void Guest_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_WelcomeGuestCLicked();
        }
        private void EasterEgg_Click(object sender, RoutedEventArgs e)
        {
            EasterEggWindow window = new EasterEggWindow();
            window.Show();
        }
        
        private void ServerInformationFetched(ServerInformation serverInformation)
        {
            Logger.Info("Server Information fetched successfully: \n\tAddress: {0}\n\tControlPort: {1}", serverInformation.Address, serverInformation.ControlPort);
            
            try
            {
                var resolvedAddresses = Dns.GetHostAddresses(serverInformation.Address);
                var ip = resolvedAddresses.FirstOrDefault(xa =>
                    xa.AddressFamily ==
                    AddressFamily
                        .InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
                if (ip == null)
                {
                    throw new SocketException(0, "No valid IPv4 address found for the provided host name.");
                }
                var endpoint = new IPEndPoint(ip, serverInformation.ControlPort);
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, endpoint.Address.ToString());
                Dispatcher.Invoke(() =>
                {
                    mainWindow.On_FetchedServerInformation(endpoint);
                    ConnectionSuccessful();
                });
            }
            catch (SocketException ex)
            {
                MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                mainWindow.ClientState.IsConnected = false;
                Dispatcher.Invoke(() =>
                {
                    ConnectionFailed();
                });
                
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred while fetching server information.");
                MessageBox.Show("Could not connect to the Server, please try again by reloading with the button below.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Dispatcher.Invoke(() =>
                {
                    ConnectionFailed();
                });
            }
        }
        
        private void ConnectionSuccessful()
        {
            ServerInfoProgress.Visibility = Visibility.Hidden;
            LoadLabel.Visibility = Visibility.Hidden;
            Refresh.Visibility = Visibility.Visible;
        }
        
        private void ConnectionFailed()
        {
            ServerInfoProgress.Visibility = Visibility.Hidden;
            Login.IsEnabled = false;
            Guest.IsEnabled = false;
            Refresh.Visibility = Visibility.Visible;
            Logger.Info("Button re-enabled after connection failure.");
        }
        
        public void Refresh_Click(object sender, RoutedEventArgs e)
        {
            ServerInfoProgress.Visibility = Visibility.Visible;
            LoadLabel.Visibility = Visibility.Visible;
            Refresh.Visibility = Visibility.Hidden;
            GetServerInformation(ServerInformationFetched);
        }
        
        private void Custom_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_WelcomeCustomServerClicked();
        }
        
        public void SetLoginEnabled(bool enabled)
        {
            Login.IsEnabled = enabled;
        }
        
        public void SetGuestEnabled(bool enabled)
        {
            Guest.IsEnabled = enabled;
        }
    }
}
