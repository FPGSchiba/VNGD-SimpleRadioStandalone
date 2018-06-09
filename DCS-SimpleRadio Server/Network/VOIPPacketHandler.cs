using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using DotNetty.Transport.Channels.Groups;
using NLog;
using Xceed.Wpf.Toolkit.Converters;


namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    using System;
    using System.Collections.Concurrent;
    using System.Text;
    using Ciribob.DCS.SimpleRadio.Standalone.Common.Setting;
    using DotNetty.Buffers;
    using DotNetty.Transport.Channels;


    public class VOIPPacketHandler : ChannelHandlerAdapter
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        static volatile IChannelGroup group;
        private ConcurrentDictionary<string, SRClient> _clientsList;
        private readonly ServerSettings _serverSettings = ServerSettings.Instance;

        public VOIPPacketHandler(ConcurrentDictionary<string, SRClient> clientsList)
        {
            _clientsList = clientsList;
        }

        public override void ChannelActive(IChannelHandlerContext contex)
        {
            IChannelGroup g = group;
            if (g == null)
            {
                lock (this)
                {
                    if (group == null)
                    {
                        g = group = new DefaultChannelGroup(contex.Executor);
                    }
                }
            }

            g.Add(contex.Channel);
        }

        class AllMatchingChannels : IChannelMatcher
        {
            private HashSet<string> matchingClients;


            public AllMatchingChannels(HashSet<string> matchingClients)
            {
                this.matchingClients = matchingClients;
            }

            public bool Matches(IChannel channel)
            {
                return matchingClients.Contains(channel.Id.AsShortText());
            }
        }

        public override void ChannelRead(IChannelHandlerContext context, object message)
        {
            var byteBuffer = message as IByteBuffer;
            if (byteBuffer != null)
            {
                byte[] udpData = new byte[byteBuffer.ReadableBytes];
                byteBuffer.GetBytes(0, udpData);

                var decodedPacket = UDPVoicePacket.DecodeVoicePacket(udpData, false);

                SRClient srClient;
                if (_clientsList.TryGetValue(decodedPacket.Guid, out srClient))
                {
                    srClient.ClientChannelId = context.Channel.Id.AsShortText();

                    var spectatorAudioDisabled =
                        _serverSettings.GetGeneralSetting(ServerSettingsKeys.SPECTATORS_AUDIO_DISABLED).BoolValue;

                    if ((srClient.Coalition == 0) && spectatorAudioDisabled)
                    {
                        return;
                    }
                    else
                    {
                        HashSet<string> matchingClients = new HashSet<string>();

                        for (int i = 0; i < decodedPacket.Frequencies.Length; i++)
                        {
                            // Magical ignore message 4 - just used for ping
                            if (decodedPacket.Modulations[0] == 4) {
                                continue;
                            }

                            var coalitionSecurity =
                                _serverSettings.GetGeneralSetting(ServerSettingsKeys.COALITION_AUDIO_SECURITY).BoolValue;

                            foreach (KeyValuePair<string, SRClient> _client in _clientsList)
                            {
                                //dont send to receiver
                                if (_client.Value.ClientGuid != decodedPacket.Guid)
                                {
                                    //check frequencies
                                    if ((!coalitionSecurity || (_client.Value.Coalition == srClient.Coalition)))
                                    {
                                        var radioInfo = _client.Value.RadioInfo;

                                        if (radioInfo != null)
                                        {
                                            RadioReceivingState radioReceivingState = null;
                                            bool decryptable;
                                            var receivingRadio = radioInfo.CanHearTransmission(decodedPacket.Frequencies[i],
                                                (RadioInformation.Modulation)decodedPacket.Modulations[i],
                                                decodedPacket.Encryptions[i],
                                                decodedPacket.UnitId, out radioReceivingState, out decryptable);

                                            //only send if we can hear!
                                            if (receivingRadio != null && !matchingClients.Contains(_client.Value.ClientChannelId))
                                            {
                                                matchingClients.Add(_client.Value.ClientChannelId);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        //send to other connected clients
                        if (matchingClients.Count > 0)
                        {
                            group.WriteAndFlushAsync(message, new AllMatchingChannels(matchingClients));
                        }
                    }
                }
            }
        }


        public override void ChannelReadComplete(IChannelHandlerContext context) => context.Flush();

        public override void ExceptionCaught(IChannelHandlerContext context, Exception exception)
        {
            Logger.Log(LogLevel.Error, exception, "Exception processing voip: " + exception.Message);
            context.CloseAsync();
        }

        public override bool IsSharable => true;
    }
}