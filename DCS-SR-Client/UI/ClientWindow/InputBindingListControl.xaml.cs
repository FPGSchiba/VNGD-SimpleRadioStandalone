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
