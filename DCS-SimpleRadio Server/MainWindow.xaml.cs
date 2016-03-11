using Ciribob.DCS.SimpleRadio.Standalone.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
using System.Windows.Threading;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        UDPVoiceRouter serverListener;

        ConcurrentDictionary<String, SRClient> connectedClients = new ConcurrentDictionary<string, SRClient>();
        private ServerSync serverSync;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if(serverListener!=null)
            {
                serverSync.RequestStop();
                serverListener.RequestStop();
                serverListener = null;
                button.Content = "Start Server";
            }
            else
            {

                button.Content = "Stop Server";
                serverListener = new UDPVoiceRouter(connectedClients);
                Thread listenerThread = new Thread(serverListener.Listen);
                listenerThread.Start();

               
                serverSync = new ServerSync(connectedClients);
                Thread serverSyncThread = new Thread(serverSync.StartListening);
                serverSyncThread.Start();
            }
        }
    }

   


}
