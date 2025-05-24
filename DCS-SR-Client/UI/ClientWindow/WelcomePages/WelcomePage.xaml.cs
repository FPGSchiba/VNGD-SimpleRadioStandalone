using NLog;
using System.Windows;
using System.Windows.Controls;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages
{
    /// <summary>
    /// Interaction logic for WelcomePage.xaml
    /// </summary>
    public partial class WelcomePage : Page
    {
        private MainWindow mainWindow;

        private readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public WelcomePage()
        {
            InitializeComponent();

            mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_WelcomeLoginClicked();
        }

        private void Guest_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_WelcomeGuestCLicked();
        }
        private void EasterEgg_Click(object sender, RoutedEventArgs e)
        {
            EasterEggWindow window = new EasterEggWindow();
            window.Show();
        }
    }
}
