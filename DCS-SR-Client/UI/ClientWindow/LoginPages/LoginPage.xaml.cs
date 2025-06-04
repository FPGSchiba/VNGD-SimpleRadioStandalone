using NLog;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
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
        public delegate void ServerInformationFetchedCallback(ServerInformation serverInformation);
        
        
        public LoginPage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }
        
        private static void GetServerInformation(ServerInformationFetchedCallback callback)
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
            GetServerInformation(ServerInformationFetched);
        }

        private void ServerInformationFetched(ServerInformation serverInformation)
        {
            var resolvedAddresses = Dns.GetHostAddresses(serverInformation.Address);
            var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
            
            if (ip != null)
            {
                mainWindow.On_LoginLoginClicked(ip, serverInformation.ControlPort);
            }
            else
            {
                //Invalid IP
                MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                mainWindow.ClientState.IsConnected = false;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_LoginBackClicked();
        }
    }
}
