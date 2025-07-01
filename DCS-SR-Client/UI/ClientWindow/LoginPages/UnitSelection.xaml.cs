using System.Windows.Controls;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.UI.ClientWindow.LoginPages
{
    public partial class UnitSelectionPage : Page
    {
        private readonly MainWindow _mainWindow;
        private InternalLoginResult _loginResult;
    
        public UnitSelectionPage()
        {
            InitializeComponent();
        
            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        public void SetSelectionData(InternalLoginResult result)
        {
            _loginResult = result;
        }
    }
}

