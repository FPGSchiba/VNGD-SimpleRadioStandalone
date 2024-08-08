using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
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
        private string email = "";

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
            mainWindow.On_LoginLoginClicked();
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
    }
}
