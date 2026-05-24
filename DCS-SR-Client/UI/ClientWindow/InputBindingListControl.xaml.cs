using System.Windows;
using System.Windows.Controls;

namespace Vanguard.VCS.Client.UI.ClientWindow
{
    /// <summary>
    /// Interaction logic for InputBindingListControl.xaml
    /// </summary>
    public partial class InputBindingListControl : UserControl
    {
        public InputBindingListControl()
        {
            InitializeComponent();
        }

        private void AddBindingButton_Click(object sender, RoutedEventArgs e)
        {
            Button sentButton = sender as Button;
            string name = sentButton.Name.Split('_')[0];
            int num = int.Parse(sentButton.Name.Split('_')[1]);

            ControlsBindGrid.RowDefinitions.Add(new RowDefinition());
        }
    }
}
