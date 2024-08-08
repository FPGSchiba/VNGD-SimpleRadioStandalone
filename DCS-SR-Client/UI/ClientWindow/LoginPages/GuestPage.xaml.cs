using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
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
            Logger.Info($"Guest Login with following Params: \nIP: {IpInput.Text}, Player Name: [{FleetCodeInput.Text}] {PlayerNameInput.Text}, Password: {PasswordInput.Password}");
            mainWindow.On_GuestLoginClicked();
        }
    }
}
