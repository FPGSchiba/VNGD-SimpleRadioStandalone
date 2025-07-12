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
        private readonly GlobalSettingsStore _settingsStore = GlobalSettingsStore.Instance;
        
        
        public LoginPage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            EmailInput.Text = _settingsStore.ProfileSettingsStore.GetClientSettingString(ProfileSettingsKeys.VngdEmail);
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
            Dispatcher.Invoke(() =>
            {
                _settingsStore.ProfileSettingsStore.SetClientSettingString(ProfileSettingsKeys.VngdEmail, EmailInput.Text);
                mainWindow.On_LoginLoginClicked(EmailInput.Text, PasswordInput.Password);
            });
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_LoginBackClicked();
        }
    }
}
