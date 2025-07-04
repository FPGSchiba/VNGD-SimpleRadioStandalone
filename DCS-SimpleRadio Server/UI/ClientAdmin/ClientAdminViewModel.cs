using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Caliburn.Micro;
using Vanguard.VCS.Server.Network;
using NLog;
using Vanguard.VCS.Server.Network.Models;
using LogManager = NLog.LogManager;

namespace Vanguard.VCS.Server.UI.ClientAdmin
{
    public sealed class ClientAdminViewModel : Screen, IHandle<ServerStateMessage>
    {
        private static readonly TimeSpan LastTransmissionThreshold = TimeSpan.FromMilliseconds(200);

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IEventAggregator _eventAggregator;
        private readonly DispatcherTimer _updateTimer;

        public ClientAdminViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            DisplayName = "SR Client List";

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _updateTimer.Tick += _updateTimer_Tick;
        }

        public ObservableCollection<ClientViewModel> Clients { get; } = new ObservableCollection<ClientViewModel>();

        protected override void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            _updateTimer?.Start();
        }
        
        protected override Task OnDeactivateAsync(bool close, CancellationToken cancellationToken)
        {
            if (close)
            {
                _updateTimer?.Stop();
            }
        
            return base.OnDeactivateAsync(close, cancellationToken);
        }

        public void Handle(ServerStateMessage message)
        {
            Clients.Clear();

            message.Clients.Apply(client => Clients.Add(new ClientViewModel(client, _eventAggregator)));
        }
        
        public async Task HandleAsync(ServerStateMessage message, CancellationToken cancellationToken)
        {
            Handle(message);
        }

        private void _updateTimer_Tick(object sender, EventArgs e)
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