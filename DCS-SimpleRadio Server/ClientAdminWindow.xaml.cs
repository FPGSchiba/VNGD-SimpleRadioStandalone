using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    ///     Interaction logic for ClientAdminWindow.xaml
    /// </summary>
    public partial class ClientAdminWindow : Window
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly HashSet<IPAddress> _bannedIps;
        private readonly ConcurrentDictionary<string, SRClient> _connectedClients;

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
                var itm = new ListBoxItem();
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
                var item = (ListBoxItem) clientsListBox.Items.GetItemAt(clientsListBox.SelectedIndex);

                clientsListBox.Items.RemoveAt(clientsListBox.SelectedIndex);

                if (item != null)
                {
                    var client = (SRClient) item.Content;

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
                var remoteIpEndPoint = client.ClientSocket.RemoteEndPoint as IPEndPoint;

                _bannedIps.Add(remoteIpEndPoint.Address);

                File.AppendAllText(MainWindow.GetCurrentDirectory() + "\\banned.txt",
                    remoteIpEndPoint.Address + "\r\n");
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
                var item = (ListBoxItem) clientsListBox.Items.GetItemAt(clientsListBox.SelectedIndex);

                clientsListBox.Items.RemoveAt(clientsListBox.SelectedIndex);

                if (item != null)
                {
                    var client = (SRClient) item.Content;
                    client.ClientSocket.Disconnect(false);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error kicking client");
            }
        }
    }
}