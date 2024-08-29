using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages
{
    public partial class CommunicationsPage : Page
    {
        private readonly MainWindow _mainWindow;
        public CommunicationsPage()
        {
            InitializeComponent();
            
            _mainWindow = Application.Current.MainWindow as MainWindow;
        }

        private void Switch_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeSwitchClicked();
        }

        private void Transparent_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeTransparentClicked();
        }

        private void Layout_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeLayoutClicked();
        }

        private void Logout_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeLogOutClicked();
        }
    }
}