using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using MessageBox = System.Windows.MessageBox;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages
{
    /// <summary>
    /// Interaction logic for GuestPage.xaml
    /// </summary>
    public partial class GuestPage : Page
    {
        private readonly MainWindow _mainWindow;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        public GuestPage()
        {
            InitializeComponent();

            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            var lastSeenName = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).RawValue;
            var fleetCode = Regex.Match(lastSeenName, "(?<=\\[)([A-Z]{2})(?=\\])").Value;
            FleetCodeInput.Text = fleetCode;
            var playerName = Regex.Replace(lastSeenName, "\\[[A-Z]{2}\\]\\s", "");
            PlayerNameInput.Text = playerName;
            IpInput.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).RawValue;
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_GuestBackClicked();
        }

        private void Login_OnClick(object sender, RoutedEventArgs e)
        {
            if (Regex.Match(FleetCodeInput.Text, "^[A-Z]{2}$").Success)
            {
                var coalitionPassword = PasswordInput.Password;
                var playerName = $"[{FleetCodeInput.Text}] {PlayerNameInput.Text}";
                _logger.Info($"Guest Login with following Params: \nIP: {IpInput.Text}, Player Name: {playerName}, Password: {coalitionPassword}");
            
                // process hostname
                string address = GetAddressFromTextBox();
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, address);
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, playerName);
                var resolvedAddresses = Dns.GetHostAddresses(address);
                var ip = resolvedAddresses.FirstOrDefault(xa => xa.AddressFamily == AddressFamily.InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4

                if (ip != null)
                {
                    _mainWindow.On_GuestLoginClicked(ip, GetPortFromTextBox(), playerName, coalitionPassword);
                }
                else
                {
                    //Invalid IP
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    _mainWindow.ClientState.IsConnected = false;
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Invalid Fleet-Code: {FleetCodeInput.Text}, must be 2 uppercase Letters", "Invalid Fleet-Code",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            
            
        }

        private string GetAddressFromTextBox()
        {
            var addr = this.IpInput.Text.Trim();

            if (addr.Contains(":"))
            {
                return addr.Split(':')[0];
            }

            return addr;
        }

        private int GetPortFromTextBox()
        {
            var addr = this.IpInput.Text.Trim();

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
