using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NLog;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Utils;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages;

public partial class ServerSelectPage : Page
{
    private readonly MainWindow _mainWindow;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

    private IPEndPoint _vanguardEndpoint;
    private bool _vanguardSelected = true;
    private bool _vanguardReady = false;

    public ServerSelectPage()
    {
        InitializeComponent();
        _mainWindow = Application.Current.MainWindow as MainWindow;
        IpInput.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).RawValue;
        PortInput.Value = 5002;
        SelectVanguardCard();
        StartDiscovery();
    }

    private void StartDiscovery()
    {
        WebsiteClient.GetServerInformation().ContinueWith(task =>
        {
            if (Dispatcher.HasShutdownStarted) return;

            if (!task.IsCompletedSuccessfully || task.Result == null)
            {
                Dispatcher.InvokeAsync(ShowDiscoveryFailed);
                return;
            }

            try
            {
                var resolvedAddresses = Dns.GetHostAddresses(task.Result.Address);
                if (Dispatcher.HasShutdownStarted) return;
                var ip = resolvedAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip == null) { Dispatcher.InvokeAsync(ShowDiscoveryFailed); return; }

                var endpoint = new IPEndPoint(ip, task.Result.ControlPort);
                Dispatcher.InvokeAsync(() => ApplyDiscoveryResult(endpoint));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to resolve Vanguard server address during discovery.");
                if (Dispatcher.HasShutdownStarted) return;
                Dispatcher.InvokeAsync(ShowDiscoveryFailed);
            }
        });
    }

    private void ApplyDiscoveryResult(IPEndPoint endpoint)
    {
        _vanguardEndpoint = endpoint;
        _vanguardReady = true;
        VanguardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        VanguardStatusLabel.Text = "vcs.vngd.net";
        UpdateConnectButton();
    }

    private void ShowDiscoveryFailed()
    {
        _vanguardReady = false;
        VanguardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        VanguardStatusLabel.Text = "Unreachable";
        UpdateConnectButton();
    }

    private void SelectVanguardCard()
    {
        _vanguardSelected = true;
        VanguardCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        VanguardCard.BorderThickness = new Thickness(2);
        VanguardCard.Background = new SolidColorBrush(Color.FromRgb(0xf9, 0xf9, 0xf9));
        CustomCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
        CustomCard.BorderThickness = new Thickness(1);
        CustomCard.Background = new SolidColorBrush(Color.FromRgb(0xf7, 0xf7, 0xf7));
        CustomFieldsPanel.Visibility = Visibility.Collapsed;
        UpdateConnectButton();
    }

    private void SelectCustomCard()
    {
        _vanguardSelected = false;
        CustomCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        CustomCard.BorderThickness = new Thickness(2);
        CustomCard.Background = new SolidColorBrush(Color.FromRgb(0xfa, 0xfa, 0xfa));
        VanguardCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
        VanguardCard.BorderThickness = new Thickness(1);
        VanguardCard.Background = new SolidColorBrush(Color.FromRgb(0xf7, 0xf7, 0xf7));
        CustomFieldsPanel.Visibility = Visibility.Visible;
        UpdateConnectButton();
    }

    private void UpdateConnectButton()
    {
        ConnectButton.IsEnabled = _vanguardSelected
            ? _vanguardReady
            : !string.IsNullOrWhiteSpace(IpInput.Text);
    }

    public void ShowError(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowError(message)); return; }
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    public void ShowIdle()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(ShowIdle); return; }
        ErrorPanel.Visibility = Visibility.Collapsed;
        ConnectingPanel.Visibility = Visibility.Collapsed;
        UpdateConnectButton();
    }

    private void ShowConnecting(string serverAddress)
    {
        ConnectButton.IsEnabled = false;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ConnectingLabel.Text = $"Connecting to {serverAddress}…";
        ConnectingPanel.Visibility = Visibility.Visible;
    }

    private void VanguardCard_Click(object sender, MouseButtonEventArgs e) => SelectVanguardCard();

    private void CustomCard_Click(object sender, MouseButtonEventArgs e) => SelectCustomCard();

    private void IpInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateConnectButton();

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect_Click(sender, e);
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_vanguardSelected)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, _vanguardEndpoint.Address.ToString());
            ShowConnecting("vcs.vngd.net");
            _mainWindow?.On_ServerConnectClicked(_vanguardEndpoint, isCustom: false);
        }
        else
        {
            var hostText = IpInput.Text;
            var port = (int)PortInput.Value;
            ShowConnecting(hostText);
            Task.Run(() =>
            {
                if (Dispatcher.HasShutdownStarted) return;
                try
                {
                    var resolvedAddresses = Dns.GetHostAddresses(hostText);
                    if (Dispatcher.HasShutdownStarted) return;
                    var ip = resolvedAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ip == null) throw new SocketException((int)System.Net.Sockets.SocketError.HostNotFound);
                    var endpoint = new IPEndPoint(ip, port);
                    Dispatcher.InvokeAsync(() =>
                    {
                        _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, ip.ToString());
                        _mainWindow?.On_ServerConnectClicked(endpoint, isCustom: true);
                    });
                }
                catch (SocketException)
                {
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.InvokeAsync(() => { ShowIdle(); ShowError("Invalid IP or Host Name!"); });
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error resolving custom server address.");
                    if (Dispatcher.HasShutdownStarted) return;
                    Dispatcher.InvokeAsync(() => { ShowIdle(); ShowError("Could not resolve server address. Please try again."); });
                }
            });
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow?.On_ServerSelectCancelled();
        ShowIdle();
    }
}
