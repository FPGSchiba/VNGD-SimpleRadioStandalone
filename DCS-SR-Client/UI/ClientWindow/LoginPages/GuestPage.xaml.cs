using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Vanguard.VCS.Client.Settings;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Vanguard.VCS.Client.UI.ClientWindow.LoginPages
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
                    ShowError("Please enter a coalition password.");
                    LoginFailed();
                    return;
                }

                var playerName = PlayerNameInput.Text;
                var fleetCode = FleetCodeInput.Text;
                _logger.Info($"Guest Login with following Params: \nPlayer Name: {playerName}, Password: {coalitionPassword}");
            
                // process hostname
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastSeenName, playerName);
                _mainWindow.On_GuestLoginClicked(playerName, fleetCode, coalitionPassword);
            }
            else
            {
                ShowError($"Invalid Fleet Code '{FleetCodeInput.Text}': must be 2–4 uppercase letters.");
                LoginFailed();
            }
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

        public void LoginFailed()
        {
            Login.IsEnabled = true;
            LoginInProgress.Visibility = Visibility.Hidden;
            _logger.Error("Login failed, re-enabling login button.");
        }

        public void ShowError(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => ShowError(message));
                return;
            }
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }
}
