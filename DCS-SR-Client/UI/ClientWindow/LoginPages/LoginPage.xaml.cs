using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Metadata;
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
    /// Interaction logic for LoginPage.xaml
    /// </summary>
    public partial class LoginPage : Page
    {
        private MainWindow mainWindow;
        private readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private SRSTeam _selectedTeam = SRSTeam.BlueTeam;

        public LoginPage()
        {
            InitializeComponent();

            SelectedTeamInit();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            
        }

        private void SelectedTeamInit()
        {
            switch (_selectedTeam)
            {
                case SRSTeam.BlueTeam:
                    BlueTeamRadio.IsChecked = true;
                    break;
                case SRSTeam.RedTeam:
                    RedTeamRadio.IsChecked = true;
                    break;
            }
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            Logger.Info($"Login pressed with following Information: \nEmail: {EmailInput.Text}, Password: {PasswordInput.Password}, Code: {FleetCodeInput.Text}, Team: {_selectedTeam}");
            // process hostname
            var resolvedAddresses = Dns.GetHostAddresses(GetAddressFromBackend());
            var loginType = GetLoginTypeFromBackend();
            var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4

            var playerName = $"[{FleetCodeInput.Text}] {GetPlayerNameFromBackend()}";

            if (ip != null)
            {
                mainWindow.On_LoginLoginClicked(ip, GetPortFrombackend(), playerName, "vngd", loginType);
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

        private void BlueTeamRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            _selectedTeam = SRSTeam.BlueTeam;
        }

        private void RedTeamRadio_OnChecked(object sender, RoutedEventArgs e)
        {
            _selectedTeam = SRSTeam.RedTeam;
        }

        private static string GetAddressFromBackend()
        {
            var addr = "";

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private static int GetPortFrombackend()
        {
            var addr = "";

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

        private static string GetPlayerNameFromBackend()
        {
            var playerName = "";

            return playerName;
        }

        private static LoginType GetLoginTypeFromBackend()
        {
            return LoginType.Member;
        }
    }
}
