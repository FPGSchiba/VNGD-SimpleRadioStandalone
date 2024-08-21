using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages
{
    public partial class GuestSuccess : Page
    {
        private readonly MainWindow _mainWindow;
        
        public GuestSuccess()
        {
            InitializeComponent();
            
            _mainWindow = Application.Current.MainWindow as MainWindow;

            Block.Text = Block.Text.Replace("[PlayerName]", ClientStateSingleton.Instance.LastSeenName);

        }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // for .NET Core you need to add UseShellExecute = true
            // see https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute#property-value
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Login_OnClick(object sender, RoutedEventArgs e)
        {
            // TODO: Sentry implementation with UserName & Acceptance -> Track Agreements
            _mainWindow.On_GuestSuccessAcceptClicked();
        }
    }
}