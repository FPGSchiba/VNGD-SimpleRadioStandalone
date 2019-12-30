using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS.Models;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
using Newtonsoft.Json;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.Network.DCS
{
    public class DCSLineOfSightHandler
    {
        private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
        private readonly ConcurrentDictionary<string, SRClient> _clients;
        private readonly string _guid;
        private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private volatile bool _stop = false;
        private UdpClient _dcsLOSListener;

        public DCSLineOfSightHandler(ConcurrentDictionary<string, SRClient> clients, string guid)
        {
            _clients = clients;
            _guid = guid;
        }

        public void Start()
        {
            StartDCSLOSBroadcastListener();
            StartDCSLOSSender();
        }

        //used for the result
        private void StartDCSLOSBroadcastListener()
        {
            _dcsLOSListener = new UdpClient();
            _dcsLOSListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress,
                true);
            _dcsLOSListener.ExclusiveAddressUse = false; // only if you want to send/receive on same machine.

            var localEp = new IPEndPoint(IPAddress.Any, _globalSettings.GetNetworkSetting(GlobalSettingsKeys.DCSLOSIncomingUDP));
            _dcsLOSListener.Client.Bind(localEp);

            Task.Factory.StartNew(() =>
            {
                using (_dcsLOSListener)
                {
                    //    var count = 0;
                    while (!_stop)
                    {
                        try
                        {
                            var groupEp = new IPEndPoint(IPAddress.Any,
                            _globalSettings.GetNetworkSetting(GlobalSettingsKeys.DCSLOSIncomingUDP));
                            var bytes = _dcsLOSListener.Receive(ref groupEp);

                            /*   Logger.Debug(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));*/
                            var playerInfo =
                                JsonConvert.DeserializeObject<DCSLosCheckResult[]>(Encoding.UTF8.GetString(
                                    bytes, 0, bytes.Length));

                            foreach (var player in playerInfo)
                            {
                                SRClient client;

                                if (_clients.TryGetValue(player.id, out client))
                                {
                                    client.LineOfSightLoss = player.los;

                                    //  Logger.Debug(client.ToString());
                                }
                            }
                        }
                        catch (SocketException e)
                        {
                            // SocketException is raised when closing app/disconnecting, ignore so we don't log "irrelevant" exceptions
                            if (!_stop)
                            {
                                Logger.Error(e, "SocketException Handling DCS Los Result Message");
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Handling DCS Los Result Message");
                        }
                    }

                    try
                    {
                        _dcsLOSListener.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS LOS Result listener ");
                    }
                }
            });
        }

        private void StartDCSLOSSender()
        {
            var _udpSocket = new UdpClient();
            var _host = new IPEndPoint(IPAddress.Loopback, _globalSettings.GetNetworkSetting(GlobalSettingsKeys.DCSLOSOutgoingUDP));


            Task.Factory.StartNew(() =>
            {
                using (_udpSocket)
                {
                    while (!_stop)
                    {
                        try
                        {
                            //Chunk client list into blocks of 10 to stay below 8000 ish UDP socket limit
                            var clientsList = GenerateDcsLosCheckRequests();

                            if (clientsList.Count > 0)
                            {
                                var splitList = clientsList.ChunkBy(10);
                                foreach (var clientSubList in splitList)
                                {
                                    // Logger.Info( "Sending LOS Request: "+ JsonConvert.SerializeObject(clientSubList));
                                    var byteData =
                                        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(clientSubList) + "\n");

                                    _udpSocket.Send(byteData, byteData.Length, _host);

                                    //every 250 - Wait for the queue
                                    Thread.Sleep(250);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "Exception Sending DCS LOS Request Message");
                        }
                        //every 300 - Wait for the queue
                        Thread.Sleep(250);
                    }

                    try
                    {
                        _udpSocket.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Exception stoping DCS listener ");
                    }
                }
            });
        }

        private List<DCSLosCheckRequest> GenerateDcsLosCheckRequests()
        {
            var clients = _clients.Values.ToList();

            var requests = new List<DCSLosCheckRequest>();

            if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.LOS_ENABLED))
            {
                foreach (var client in clients)
                {
                    //only check if its worth it
                    if ((client.Position.x != 0) && (client.Position.z != 0) && (client.ClientGuid != _guid))
                    {
                        requests.Add(new DCSLosCheckRequest
                        {
                            id = client.ClientGuid,
                            x = client.Position.x,
                            y = client.Position.y,
                            z = client.Position.z
                        });
                    }
                }
            }
            return requests;
        }

        internal void Stop()
        {
            _stop = true;
            try
            {
                _dcsLOSListener?.Close();
            }
            catch (Exception ex)
            {
            }
        }
    }
}
