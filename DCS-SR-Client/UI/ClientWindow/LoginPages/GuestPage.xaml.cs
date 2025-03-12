﻿using NLog;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using MaterialDesignThemes.Wpf;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages
{
    /// <summary>
    /// Interaction logic for GuestPage.xaml
    /// </summary>
    public partial class GuestPage : Page
    {
        private readonly MainWindow _mainWindow;
        private FFIDInformation _ffidInformation;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

        public GuestPage()
        {
            InitializeComponent();

            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            var lastSeenName = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastSeenName).RawValue;
            var fleetCode = Regex.Match(lastSeenName, "(?<=\\[)([A-Z0-9]{2,4})(?=\\])").Value;
            FleetCodeInput.Text = fleetCode;
            var playerName = Regex.Replace(lastSeenName, "\\[[A-Z0-9]{2,4}\\]\\s", "");
            PlayerNameInput.Text = playerName;
            IpInput.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).RawValue;
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_GuestBackClicked();
        }

        private void Login_OnClick(object sender, RoutedEventArgs e)
        {
            if (Regex.Match(FleetCodeInput.Text, "^[A-Z0-9]{2,4}$").Success)
            {
                var coalitionPassword = PasswordInput.Password;
                if (string.IsNullOrEmpty(coalitionPassword))
                {
                    System.Windows.Forms.MessageBox.Show("Please enter a password. It is needed to connect to VCS-SRS.", "Missing Password",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var playerName = $"[{FleetCodeInput.Text}] {PlayerNameInput.Text}";
                _logger.Info($"Guest Login with following Params: \nIP: {IpInput.Text}, Player Name: {playerName}, Password: {coalitionPassword}");
            
                // process hostname
                string address = GetAddressFromTextBox();
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, address);
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, playerName);
                try
                {
                    var resolvedAddresses = Dns.GetHostAddresses(address);
                    var ip = resolvedAddresses.FirstOrDefault(xa =>
                        xa.AddressFamily ==
                        AddressFamily
                            .InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
                    _mainWindow.ServerIp.Text = address;
                    _mainWindow.On_GuestLoginClicked(ip, GetPortFromTextBox(), playerName, coalitionPassword);
                }
                catch (SocketException ex)
                {
                    MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    _mainWindow.ClientState.IsConnected = false;
                }
            }
            else
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Invalid Fleet-Code: {FleetCodeInput.Text}, must be 2-4 uppercase Letters", "Invalid Fleet-Code",
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

        private void OnButtonPressed(object sender, KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Login_OnClick(sender, e);
            }
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            _ffidInformation = new FFIDInformation();
            _ffidInformation.ShowDialog(); // ShowDialog blocks the main window
        }
    }
}
