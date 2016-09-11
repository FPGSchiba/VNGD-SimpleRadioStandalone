using System.Collections.ObjectModel;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Server.Network;
using NLog;
using LogManager = NLog.LogManager;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI.ClientAdmin
{
    public sealed class ClientAdminViewModel : Screen, IHandle<ServerStateMessage>
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private readonly IEventAggregator _eventAggregator;

        public ClientAdminViewModel(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            DisplayName = "SR Client List";
        }

        public ObservableCollection<ClientViewModel> Clients { get; } = new ObservableCollection<ClientViewModel>();

        public void Handle(ServerStateMessage message)
        {
            Clients.Clear();

            message.Clients.Apply(client => Clients.Add(new ClientViewModel(client, _eventAggregator)));
        }
    }
}