using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server
{
    internal class UDPVoiceRouter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
        private readonly IEventAggregator _eventAggregator;
        private UdpClient _listener;

        private volatile bool _stop;
        private ServerSettings _serverSettings = ServerSettings.Instance;

        public UDPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList, IEventAggregator eventAggregator)
        {
            _clientsList = clientsList;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);
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
                    if (rawBytes.Length >= 22)
                    {
                        Task.Run(() =>
                        {
                            //last 36 bytes are guid!
                            var guid = Encoding.ASCII.GetString(
                                rawBytes, rawBytes.Length - 22, 22);

                            if (_clientsList.ContainsKey(guid))
                            {
                                var client = _clientsList[guid];
                                client.voipPort = groupEP;

                                var spectatorAudio = _serverSettings.ServerSetting[(int)ServerSettingType.SPECTATORS_AUDIO_DISABLED];

                                if (client.Coalition == 0 && spectatorAudio == "DISABLED")
                                {
                                   // IGNORE THE AUDIO
                                }
                                else
                                {
                                    SendToOthers(rawBytes,client );
                                }
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

        private void SendToOthers(byte[] bytes, SRClient fromClient)
        {
            var coalitionSecurity =
                                    _serverSettings.ServerSetting[(int)ServerSettingType.COALITION_AUDIO_SECURITY] == "ON";
            var guid = fromClient.ClientGuid;

            foreach (var client in _clientsList)
            {
                try
                {
                    if (!client.Key.Equals(guid))
                    {
                        var ip = client.Value.voipPort;

                        // check that either coalition radio security is disabled OR the coalitions match
                        if (ip != null && (!coalitionSecurity || client.Value.Coalition == fromClient.Coalition))
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
                    Logger.Info("Pinging Clients");
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