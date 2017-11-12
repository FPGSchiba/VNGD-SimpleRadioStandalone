//using System;
//using System.Collections.Concurrent;
//using System.Collections.Generic;
//using System.Net;
//using System.Net.Sockets;
//using System.Text;
//using System.Threading;
//using Caliburn.Micro;
//using Ciribob.DCS.SimpleRadio.Standalone.Common;
//using NLog;
//using LogManager = NLog.LogManager;
//
//namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
//{
//    internal class UDPVoiceRouter
//    {
//        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
//        private readonly ConcurrentDictionary<string, SRClient> _clientsList;
//        private readonly IEventAggregator _eventAggregator;
//
//        private readonly BlockingCollection<OutgoingVoice> _outGoing = new BlockingCollection<OutgoingVoice>();
//        private readonly CancellationTokenSource _outgoingCancellationToken = new CancellationTokenSource();
//
//        private readonly CancellationTokenSource _pendingProcessingCancellationToken = new CancellationTokenSource();
//
//        private readonly BlockingCollection<PendingPacket> _pendingProcessingPackets =
//            new BlockingCollection<PendingPacket>();
//
//        private readonly ServerSettings _serverSettings = ServerSettings.Instance;
//        private UdpClient _listener;
//
//        private volatile bool _stop;
//
//        public UDPVoiceRouter(ConcurrentDictionary<string, SRClient> clientsList, IEventAggregator eventAggregator)
//        {
//            _clientsList = clientsList;
//            _eventAggregator = eventAggregator;
//            _eventAggregator.Subscribe(this);
//        }
//
//        public void Listen()
//        {
//            //start threads
//            //packets that need processing
//            new Thread(ProcessPackets).Start();
//            //outgoing packets
//            new Thread(SendPendingPackets).Start();
//
//            var port = _serverSettings.ServerListeningPort();
//            _listener = new UdpClient();
//            _listener.AllowNatTraversal(true);
//            _listener.ExclusiveAddressUse = true;
//            _listener.DontFragment = true;
//            _listener.Client.DontFragment = true;
//            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, _serverSettings.ServerListeningPort()));
//            while (!_stop)
//                try
//                {
//                    var groupEP = new IPEndPoint(IPAddress.Any, port);
//                    var rawBytes = _listener.Receive(ref groupEP);
//                    if ((rawBytes != null) && (rawBytes.Length >= 22))
//                        _pendingProcessingPackets.Add(new PendingPacket
//                        {
//                            RawBytes = rawBytes,
//                            ReceivedFrom = groupEP
//                        });
//                    else if ((rawBytes != null) && (rawBytes.Length == 15) && (rawBytes[0] == 1) && (rawBytes[14] == 15))
//                        try
//                        {
//                            //send back ping UDP
//                            _listener.Send(rawBytes, rawBytes.Length, groupEP);
//                        }
//                        catch (Exception ex)
//                        {
//                            //dont log because it slows down thread too much...
//                        }
//                }
//                catch (Exception e)
//                {
//                    //  logger.Error(e,"Error receving audio UDP for client " + e.Message);
//                }
//
//            try
//            {
//                _listener.Close();
//            }
//            catch (Exception e)
//            {
//            }
//        }
//
//        public void RequestStop()
//        {
//            _stop = true;
//            try
//            {
//                _listener.Close();
//            }
//            catch (Exception e)
//            {
//            }
//
//            _outgoingCancellationToken.Cancel();
//            _pendingProcessingCancellationToken.Cancel();
//        }
//
//        private void ProcessPackets()
//        {
//            while (!_stop)
//                try
//                {
//                    PendingPacket udpPacket = null;
//                    _pendingProcessingPackets.TryTake(out udpPacket, 100000, _pendingProcessingCancellationToken.Token);
//
//                    if (udpPacket != null)
//                    {
//                        //last 36 bytes are guid!
//                        var guid = Encoding.ASCII.GetString(
//                            udpPacket.RawBytes, udpPacket.RawBytes.Length - 22, 22);
//
//                        if (_clientsList.ContainsKey(guid))
//                        {
//                            var client = _clientsList[guid];
//                            client.VoipPort = udpPacket.ReceivedFrom;
//
//                            var spectatorAudioDisabled =
//                                _serverSettings.ServerSetting[(int) ServerSettingType.SPECTATORS_AUDIO_DISABLED];
//
//                            if ((client.Coalition == 0) && spectatorAudioDisabled)
//                            {
//                                // IGNORE THE AUDIO
//                            }
//                            else
//                            {
//                                try
//                                {
//                                    //decode
//                                    var udpVoicePacket = UDPVoicePacket.DecodeVoicePacket(udpPacket.RawBytes);
//
//                                    if ((udpVoicePacket != null) && (udpVoicePacket.Modulation != 4))
//                                        //magical ignore message 4
//                                    {
//                                        var outgoingVoice = GenerateOutgoingPacket(udpVoicePacket, udpPacket, client);
//
//                                        if (outgoingVoice != null)
//                                            _outGoing.Add(outgoingVoice);
//                                    }
//                                }
//                                catch (Exception)
//                                {
//                                    //Hide for now, slows down loop to much....
//                                }
//                            }
//                        }
//                        else
//                        {
//                            SRClient value;
//                            _clientsList.TryRemove(guid, out value);
//                            //  logger.Info("Removing  "+guid+" From UDP pool");
//                        }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Logger.Info("Failed to Process UDP Packet: " + ex.Message);
//                }
//        }
//
//        private
//            void SendPendingPackets()
//        {
//            //_listener.Send(bytes, bytes.Length, ip);
//            while (!_stop)
//                try
//                {
//                    OutgoingVoice udpPacket = null;
//                    _outGoing.TryTake(out udpPacket, 100000, _pendingProcessingCancellationToken.Token);
//
//                    if (udpPacket != null)
//                    {
//                        var bytes = udpPacket.ReceivedPacket;
//                        var bytesLength = bytes.Length;
//                        foreach (var outgoingEndPoint in udpPacket.OutgoingEndPoints)
//                            try
//                            {
//                                _listener.Send(bytes, bytesLength, outgoingEndPoint);
//                            }
//                            catch (Exception ex)
//                            {
//                                //dont log, slows down too much...
//                            }
//                    }
//                }
//                catch (Exception ex)
//                {
//                    Logger.Info("Error processing Sending Queue UDP Packet: " + ex.Message);
//                }
//        }
//
//        private OutgoingVoice GenerateOutgoingPacket(UDPVoicePacket udpVoice, PendingPacket pendingPacket,
//            SRClient fromClient)
//        {
//            var outgoingList = new List<IPEndPoint>();
//            var outgoingVoice = new OutgoingVoice
//            {
//                OutgoingEndPoints = outgoingList,
//                ReceivedPacket = pendingPacket.RawBytes
//            };
//
//            var coalitionSecurity =
//                _serverSettings.ServerSetting[(int) ServerSettingType.COALITION_AUDIO_SECURITY];
//            var guid = fromClient.ClientGuid;
//
//            foreach (var client in _clientsList)
//            {
//                if (!client.Key.Equals(guid))
//                {
//                    var ip = client.Value.VoipPort;
//
//                    // check that either coalition radio security is disabled OR the coalitions match
//                    if ((ip != null) && (!coalitionSecurity || (client.Value.Coalition == fromClient.Coalition)))
//                    {
//                        var radioInfo = client.Value.RadioInfo;
//
//                        if (radioInfo != null)
//                        {
//                            RadioReceivingState radioReceivingState = null;
//                            var receivingRadio = radioInfo.CanHearTransmission(udpVoice.Frequency,
//                                (RadioInformation.Modulation)udpVoice.Modulation,
//                                udpVoice.UnitId, out radioReceivingState);
//
//                            //only send if we can hear!
//                            if (receivingRadio != null)
//                                outgoingList.Add(ip);
//                        }
//                    }
//                }
//                else
//                {
//                    var ip = client.Value.VoipPort;
//
//                    if (ip != null)
//                    {
//                        // outgoingList.Add(ip);
//                    }
//                }
//            }
//
//            return outgoingList.Count > 0 ? outgoingVoice : null;
//        }
//    }
//}