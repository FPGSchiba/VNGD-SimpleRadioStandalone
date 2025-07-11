using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Vanguard.VCS.Client.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages;

public partial class CustomServer : Page
{
    private readonly MainWindow _mainWindow;

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
    
    public CustomServer()
    {
        InitializeComponent();
        _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        PortInput.Value = 5002; // Default port
        IpInput.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).RawValue;
        Continue.IsEnabled = false;
    }
    
    private void OnButtonPressed(object sender, KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            Connect_OnClick(sender, e);
        }
    }
    
    private void Back_OnClick(object sender, RoutedEventArgs e)
    {
        Continue.IsEnabled = false;
        _mainWindow?.On_CustomServerBackClicked();
    }
    
    private void Connect_OnClick(object sender, RoutedEventArgs e)
    {
        ConnectInProgress.Visibility = Visibility.Visible;
        IpInput.IsEnabled = false;
        PortInput.IsEnabled = false;
        Connect.IsEnabled = false;
        
        try
        {
            var resolvedAddresses = Dns.GetHostAddresses(IpInput.Text);
            var ip = resolvedAddresses.FirstOrDefault(xa =>
                xa.AddressFamily ==
                AddressFamily
                    .InterNetwork); // Ensure we get an IPv4 address in case the host resolves to both IPv6 and IPv4
            if (ip == null)
            {
                throw new SocketException(0, "No valid IPv4 address found for the provided host name.");
            }
            var endpoint = new IPEndPoint(ip, PortInput.Value);
            _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, endpoint.Address.ToString());
            Dispatcher.Invoke(() =>
            {
                _mainWindow.On_FetchedServerInformation(endpoint, true);
                ConnectionSuccessful();
            });
        }
        catch (SocketException ex)
        {
            MessageBox.Show("Invalid IP or Host Name!", "Host Name Error", MessageBoxButton.OK,
                MessageBoxImage.Error);
            _mainWindow.ClientState.IsConnected = false;
            ConnectionFailed();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching server information.");
            MessageBox.Show("An unexpected error occurred. Please try again later.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            ConnectionFailed();
        }
    }
    
    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        _mainWindow?.On_CustomServerContinueClicked();
    }
    
    private void ConnectionSuccessful()
    {
        ConnectInProgress.Visibility = Visibility.Hidden;
        IpInput.IsEnabled = true;
        PortInput.IsEnabled = true;
        Continue.IsEnabled = true;
        Connect.IsEnabled = true;
    }
    
    private void ConnectionFailed()
    {
        ConnectInProgress.Visibility = Visibility.Hidden;
        IpInput.IsEnabled = true;
        PortInput.IsEnabled = true;
        Continue.IsEnabled = false;
        Connect.IsEnabled = true;
    }
}