using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPage
{
    public partial class SettingsPage : Page
    {
        private readonly MainWindow _mainWindow;
        public SettingsPage()
        {
            InitializeComponent();
            _mainWindow = Application.Current.MainWindow as MainWindow;
        }

        private void Back_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_SettingsBackClicked();
        }
    }
}