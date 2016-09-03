using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network
{
    public class DCSAutoConnectListener
    {
        private readonly MainWindow.ReceivedAutoConnect _receivedAutoConnect;
        private Logger Logger = LogManager.GetCurrentClassLogger();
        private UdpClient _dcsUdpListener;

        private volatile bool _stop;


        public DCSAutoConnectListener(MainWindow.ReceivedAutoConnect receivedAutoConnect)
        {
            _receivedAutoConnect = receivedAutoConnect;

            StartDcsBroadcastListener();
        }


        private void StartDcsBroadcastListener()
        {
            _dcsUdpListener = new UdpClient();
            _dcsUdpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _dcsUdpListener.ExclusiveAddressUse = false; 

            var localEp = new IPEndPoint(IPAddress.Any, 5069);
            _dcsUdpListener.Client.Bind(localEp);


            Task.Factory.StartNew(() =>
            {
                using (_dcsUdpListener)
                {
                    while (!_stop)
                    {
                        var groupEp = new IPEndPoint(IPAddress.Any, 5069);
                        var bytes = _dcsUdpListener.Receive(ref groupEp);

                        try
                        {
                            var message = (Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));

                            HandleMessage(message);
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS AutoConnect Message");
                        }
                    }

                    try
                    {
                        _dcsUdpListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS AutoConnect listener ");
                    }
                }
            });
        }

        private void HandleMessage(String message)
        {
            var address = message.Split(':');
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background,
             new ThreadStart(delegate {
          
            message = message.Trim();
            if (message.Contains(':'))
            {
                this._receivedAutoConnect(address[0].Trim(), int.Parse(address[1].Trim()));

            }
            else
            {
                this._receivedAutoConnect(message, 5002);
            }
                
             }));
        }


        public void Stop()
        {
            _stop = true;

            try
            {
                _dcsUdpListener.Close();
            }
            catch (Exception ex)
            {
                //IGNORE
            }
        }
    }
}
