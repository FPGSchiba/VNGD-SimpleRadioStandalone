using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPage
{
    public partial class GeneralPage : Page
    {
        private readonly MainWindow _mainWindow;
        
        public GeneralPage()
        {
            InitializeComponent();
            _mainWindow = Application.Current.MainWindow as MainWindow;
        }

        private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.ResetRadioWindow_Click(sender, e);
        }
    }
}