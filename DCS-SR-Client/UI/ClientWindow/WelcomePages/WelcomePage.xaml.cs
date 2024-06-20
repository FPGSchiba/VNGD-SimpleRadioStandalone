using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.Welcome;
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

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow
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
            mainWindow.On_LoginClicked();
        }

        private void Guest_Click(object sender, RoutedEventArgs e)
        {
            mainWindow.On_GuestCLicked();
        }
        private void EasterEgg_Click(object sender, RoutedEventArgs e)
        {
            EasterEggWindow window = new EasterEggWindow();
            window.Show();
        }
    }
}
