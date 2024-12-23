using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Sentry;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow
{
    /// <summary>
    /// Interaction logic for SupportPage.xaml
    /// </summary>
    public partial class SupportPage : Page
    {
        private readonly MainWindow _mainWindow;
        
        public SupportPage()
        {
            InitializeComponent();
            _mainWindow = Application.Current.MainWindow as MainWindow;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            // for .NET Core you need to add UseShellExecute = true
            // see https://learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.useshellexecute#property-value
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
            e.Handled = true;
        }

        private void Submit_OnClick(object sender, RoutedEventArgs e)
        {
            var eventId = SentrySdk.CaptureMessage($"Feedback: {FeedbackType.Text}");
            SentrySdk.CaptureUserFeedback(eventId, EmailText.Text, FeedbackText.Text, _mainWindow.GetPlayerName());
            
            FeedbackText.Clear();
            FeedbackType.Text = "";
            EmailText.Clear();
            
            MessageBox.Show("Successfully submitted your Feedback.", "Success!", MessageBoxButton.OK,
                MessageBoxImage.Information);
            
        }
    }
}
