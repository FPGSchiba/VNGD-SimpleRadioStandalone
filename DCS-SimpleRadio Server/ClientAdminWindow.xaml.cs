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
using NLog;
using System.Net;
using System.IO;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    /// Interaction logic for ClientAdminWindow.xaml
    /// </summary>
    public partial class ClientAdminWindow : Window
    {
        private ConcurrentDictionary<string, SRClient> _connectedClients;
        private static Logger _logger = LogManager.GetCurrentClassLogger();
        private HashSet<IPAddress> _bannedIps;

        public ClientAdminWindow(ConcurrentDictionary<string, SRClient> _connectedClients, HashSet<IPAddress> _bannedIps)
        {
            this._connectedClients = _connectedClients;
            this._bannedIps = _bannedIps;

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
            
                clientsListBox.Items.Add(itm);
            }
        }

        private void ClientsListBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            clientsListBox.UnselectAll();
        }

        private void MenuItemBan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (clientsListBox.SelectedIndex == -1)
                {
                    return;
                }
                ListBoxItem item = (ListBoxItem)clientsListBox.Items.GetItemAt(clientsListBox.SelectedIndex);

                clientsListBox.Items.RemoveAt(clientsListBox.SelectedIndex);

                if (item != null)
                {
                    SRClient client = (SRClient)item.Content;

                    WriteBanIP(client);

                    client.ClientSocket.Disconnect(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error kicking client");
            }
        }

        private void WriteBanIP(SRClient client)
        {
            try
            {
                IPEndPoint remoteIpEndPoint = client.ClientSocket.RemoteEndPoint as IPEndPoint;

                _bannedIps.Add(remoteIpEndPoint.Address);

                File.AppendAllText(MainWindow.GetCurrentDirectory() + "\\banned.txt", remoteIpEndPoint.Address.ToString() + "\r\n");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error saving banned client");
            }
        }

      

        private void MenuItemKick_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (clientsListBox.SelectedIndex == -1)
                {
                    return;
                }
                ListBoxItem item = (ListBoxItem)clientsListBox.Items.GetItemAt(clientsListBox.SelectedIndex);

                clientsListBox.Items.RemoveAt(clientsListBox.SelectedIndex);

                if (item != null)
                {
                    SRClient client = (SRClient)item.Content;
                    client.ClientSocket.Disconnect(false);
                }
            }
            catch(Exception ex)
            {
                _logger.Error(ex, "Error kicking client");
            }
        }
    }
}
