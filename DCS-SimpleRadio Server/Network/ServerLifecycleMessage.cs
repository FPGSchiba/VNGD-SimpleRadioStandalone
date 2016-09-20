﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using Ciribob.DCS.SimpleRadio.Standalone.Common;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.Network
{
    public class StartServerMessage
    {
    }

    public class StopServerMessage
    {
    }

    public class ServerStateMessage
    {
        private readonly List<SRClient> _srClients;

        public ServerStateMessage(bool isRunning, List<SRClient> srClients)
        {
            _srClients = srClients;
            IsRunning = isRunning;
        }

        //SUPER SAFE
        public ReadOnlyCollection<SRClient> Clients => new ReadOnlyCollection<SRClient>(_srClients);

        public bool IsRunning { get; private set; }
        public int Count => _srClients.Count;
    }

    public class KickClientMessage
    {
        public KickClientMessage(SRClient client)
        {
            Client = client;
        }

        public SRClient Client { get; }
    }

    public class BanClientMessage
    {
        public BanClientMessage(SRClient client)
        {
            Client = client;
        }

        public SRClient Client { get; }
    }

    public class ServerSettingsChangedMessage
    {
    }
}