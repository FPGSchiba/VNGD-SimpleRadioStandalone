using NLog;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Utils;

namespace Vanguard.VCS.Client.UI.ClientWindow.LoginPages
{
    /// <summary>
    /// Interaction logic for LoginPage.xaml
    /// </summary>
    public partial class LoginPage : Page
    {
        private MainWindow mainWindow;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        public delegate void ServerInformationFetchedCallback(ServerInformation serverInformation, string email, string password);
        private readonly GlobalSettingsStore _settingsStore = GlobalSettingsStore.Instance;
        
        
        public LoginPage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            EmailInput.Text = _settingsStore.ProfileSettingsStore.GetClientSettingString(ProfileSettingsKeys.VngdEmail);
        }
        
        private void GetServerInformation(ServerInformationFetchedCallback callback)
        {
            var password = PasswordInput.Password;
            var email = EmailInput.Text;
            WebsiteClient.GetServerInformation().ContinueWith((task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    callback(task.Result, email, password);
                }
                else
                {
                    MessageBox.Show("Failed to retrieve server information.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }));
        }
        
        public void LoginFailed()
        {
            Login.IsEnabled = true;
            Progress.Visibility = Visibility.Hidden;
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            Login.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            Logger.Info("Beginning to fetch Server Information.");
            GetServerInformation(ServerInformationFetched);
        }

        private void ServerInformationFetched(ServerInformation serverInformation, string email, string password)
        {
            Logger.Info("Server Information fetched successfully: \n\tAddress: {0}\n\tControlPort: {1}", serverInformation.Address, serverInformation.ControlPort);
            
            try
            {
                var resolvedAddresses = Dns.GetHostAddresses(serverInformation.Address);
                var ip = resolvedAddresses.FirstOrDefault(xa =>
                    xa.AddressFamily ==
                    AddressFamily
                        .InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
                Dispatcher.Invoke(() =>
                {
                    mainWindow.ServerIp.Text = serverInformation.Address;
                    _settingsStore.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.VngdEmail, EmailInput.Text);
                    mainWindow.On_LoginLoginClicked(ip, serverInformation.ControlPort, email, password);
                });
                
            }
            catch (SocketException ex)
            {
                MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                mainWindow.ClientState.IsConnected = false;
                LoginFailed();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "An error occurred while fetching server information.");
                MessageBox.Show("An unexpected error occurred. Please try again later.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LoginFailed();
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_LoginBackClicked();
        }
    }
}
