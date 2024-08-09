using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages
{
    /// <summary>
    /// Interaction logic for GuestPage.xaml
    /// </summary>
    public partial class GuestPage : Page
    {
        private MainWindow mainWindow;

        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public GuestPage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            mainWindow.On_GuestBackClicked();
        }

        private void Login_OnClick(object sender, RoutedEventArgs e)
        {
            var coalitionPassword = PasswordInput.Password;
            Logger.Info($"Guest Login with following Params: \nIP: {IpInput.Text}, Player Name: [{FleetCodeInput.Text}] {PlayerNameInput.Text}, Password: {coalitionPassword}");

            // process hostname
            var resolvedAddresses = Dns.GetHostAddresses(GetAddressFromTextBox());
            var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4

            var playerName = $"[{FleetCodeInput.Text}] {PlayerNameInput}";

            if (ip != null)
            {
                mainWindow.On_GuestLoginClicked(ip, GetPortFromTextBox(), playerName, coalitionPassword);
            }
            else
            {
                //Invalid IP
                MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                mainWindow.ClientState.IsConnected = false;
            }
        }

        private string GetAddressFromTextBox()
        {
            var addr = IpInput.Text.Trim();

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private int GetPortFromTextBox()
        {
            var addr = IpInput.Text.Trim();

            if (addr.Contains(":"))
            {
                int port;
                if (int.TryParse(addr.Split(':')[1], out port))
                {
                    return port;
                }
                throw new ArgumentException("specified port is not valid");
            }

            return 5002;
        }
    }
}
