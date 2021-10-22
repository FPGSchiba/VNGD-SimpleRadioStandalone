using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
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
    /// Interaction logic for FavouriteServersView.xaml
    /// </summary>
    public partial class FavouriteServersView : UserControl
    {
        public FavouriteServersView()
        {
            InitializeComponent();
        }

        private void DataGridRow_Drop(object sender, DragEventArgs e)
        {
            ServerAddress droppedAddress = (ServerAddress)e.Data.GetData(typeof(ServerAddress));

            DataGridRow targetGridRow = sender as DataGridRow;
            ServerAddress targetAddress = targetGridRow.DataContext as ServerAddress;



            ObservableCollection<ServerAddress> serverAddresses = FavouritesGrid.ItemsSource as ObservableCollection<ServerAddress>;

            serverAddresses.Move(serverAddresses.IndexOf(droppedAddress), serverAddresses.IndexOf(targetAddress));
        }

        private void DataGridRow_MouseMove(object sender, MouseEventArgs e)
        {
            DataGridCell selectedRow = sender as DataGridCell;

            if (selectedRow != null && e.LeftButton == MouseButtonState.Pressed)
            {
                ServerAddress serverAddress = selectedRow.DataContext as ServerAddress;
                
                try 
                {
                    DragDrop.DoDragDrop(FavouritesGrid, serverAddress, DragDropEffects.Move);
                }
                catch(Exception ex)
                {
                    // catches any out of bounds movements, should probably be replaced with validation at some point
                }
                               
            }
        }
        public void DataGridRow_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void DataGridRow_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
        private void FavouritesGrid_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private void FavouritesGrid_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }
    }
}