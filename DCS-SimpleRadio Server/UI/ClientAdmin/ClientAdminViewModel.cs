using System;
using System.Collections.ObjectModel;
using System.Timers;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Network;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI.ClientAdmin
{
    public sealed class ClientAdminViewModel : Screen, IHandle<ServerStateMessage>
    {
        private static readonly TimeSpan LastTransmissionThreshold = TimeSpan.FromMilliseconds(200);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IEventAggregator _eventAggregator;
        private Timer _transmittingEndTimer;

        public ClientAdminViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            DisplayName = "SR Client List";

            _transmittingEndTimer = new Timer(200);
            _transmittingEndTimer.Elapsed += _transmittingEndTimer_Elapsed;
            _transmittingEndTimer.Start();
        }

        public ObservableCollection<ClientViewModel> Clients { get; } = new ObservableCollection<ClientViewModel>();

        public void Handle(ServerStateMessage message)
        {
            Clients.Clear();

            message.Clients.Apply(client => Clients.Add(new ClientViewModel(client, _eventAggregator)));
        }

        private void _transmittingEndTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            foreach (ClientViewModel client in Clients)
            {
                if ((DateTime.Now - client.Client.LastTransmissionReceived) >= LastTransmissionThreshold)
                {
                    client.Client.TransmittingFrequency = "---";
                }
            }
        }
    }
}