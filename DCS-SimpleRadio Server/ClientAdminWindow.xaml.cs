using System;
using System.Collections.Concurrent;
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
using System.Windows.Shapes;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    /// Interaction logic for ClientAdminWindow.xaml
    /// </summary>
    public partial class ClientAdminWindow : Window
    {
        private ConcurrentDictionary<string, SRClient> _connectedClients;

        public ClientAdminWindow(ConcurrentDictionary<string, SRClient> _connectedClients)
        {
            this._connectedClients = _connectedClients;

            InitializeComponent();

            Refresh();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            clientsListBox.Items.Clear();

            foreach (var client in _connectedClients)
            {
                ListBoxItem itm = new ListBoxItem();
                itm.Content = client.Value;
               
                //itm.MouseDoubleClick += ClientSelected;


                clientsListBox.Items.Add(itm);
            }
        }
    }
}
