using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    internal class UDPVoiceRouter
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private HashSet<IPAddress> _bannedIps;
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private UdpClient _listener;

        private volatile bool _stop;

        public UDPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList)
        {
            _clientsList = clientsList;
        }

        public void Listen()
        {
            _listener = new UdpClient();
            _listener.AllowNatTraversal(true);
            _listener.ExclusiveAddressUse = true;
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, 5010));
            StartPing();
            while (!_stop)
            {
                try
                {
                    var groupEP = new IPEndPoint(IPAddress.Any, 5010);
                    var rawBytes = _listener.Receive(ref groupEP);
                    if (rawBytes.Length > 36)
                    {
                        Task.Run(() =>
                        {
                            //last 36 bytes are guid!
                            var guid = Encoding.ASCII.GetString(
                                rawBytes, rawBytes.Length - 36, 36);

                            if (_clientsList.ContainsKey(guid))
                            {
                                _clientsList[guid].voipPort = groupEP;

                                SendToOthers(rawBytes, guid);
                            }
                            else
                            {
                                SRClient value;
                                _clientsList.TryRemove(guid, out value);
                                //  logger.Info("Removing  "+guid+" From UDP pool");
                            }
                        });
                    }
                }
                catch (Exception e)
                {
                    //  logger.Error(e,"Error receving audio UDP for client " + e.Message);
                }
            }

            try
            {
                _listener.Close();
            }
            catch (Exception e)
            {
            }
        }

        public void RequestStop()
        {
            _stop = true;
            try
            {
                _listener.Close();
            }
            catch (Exception e)
            {
            }
        }

        private void SendToOthers(byte[] bytes, string guid)
        {
            foreach (var client in _clientsList)
            {
                try
                {
                    if (!client.Key.Equals(guid))
                    {
                        var ip = client.Value.voipPort;

                        if (ip != null)
                        {
                            //TODO only send to clients on the same team?
                            _listener.Send(bytes, bytes.Length, ip);
                        }
                    }
                    else
                    {
                        var ip = client.Value.voipPort;

                        if (ip != null)
                        {
                            //     _listener.Send(bytes, bytes.Length, ip);
                        }
                    }
                }
                catch (Exception e)
                {
                    //      IPEndPoint ip = client.Value;
                    //   logger.Error(e, "Error sending audio UDP for client " + e.Message);
                    SRClient value;
                    _clientsList.TryRemove(guid, out value);
                }
            }
        }


        private void StartPing()
        {
            Task.Run(() =>
            {
                byte[] message = {1, 2, 3, 4, 5};
                while (!_stop)
                {
                    _logger.Info("Pinging Clients");
                    try
                    {
                        foreach (var client in _clientsList)
                        {
                            try
                            {
                                var ip = client.Value.voipPort;

                                if (ip != null)
                                {
                                    _listener.Send(message, message.Length, ip);
                                }
                            }
                            catch (Exception e)
                            {
                            }
                        }
                    }
                    catch (Exception e)
                    {
                    }

                    Thread.Sleep(60*1000);
                }
            });
        }
    }
}