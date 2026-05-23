using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NLog;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages
{
    public partial class WelcomePage : Page
    {
        private readonly MainWindow _mainWindow;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public WelcomePage()
        {
            InitializeComponent();
            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        public void ShowError(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowError(message)); return; }
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        public void ShowKickReason(string reason)
        {
            var prefix = reason?.Contains("anned") == true ? "You were banned" : "You were kicked";
            ShowError($"{prefix}: {reason}");
        }

        public void SetLoginEnabled(bool enabled)
        {
            LoginCard.IsEnabled = enabled;
            LoginCard.Opacity = enabled ? 1.0 : 0.4;
        }

        public void SetGuestEnabled(bool enabled)
        {
            GuestCard.IsEnabled = enabled;
            GuestCard.Opacity = enabled ? 1.0 : 0.4;
        }

        public void ShowServerConnected(string serverAddress)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowServerConnected(serverAddress)); return; }
            ServerAddressText.Text = $"● Connected — {serverAddress}";
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private void LoginCard_Click(object sender, MouseButtonEventArgs e)
            => _mainWindow?.On_WelcomeLoginClicked();

        private void GuestCard_Click(object sender, MouseButtonEventArgs e)
            => _mainWindow?.On_WelcomeGuestCLicked();   // typo preserved until Task 3 renames it
    }
}
