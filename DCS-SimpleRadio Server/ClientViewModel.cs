using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Caliburn.Micro;
using Ciribob.DCS.SimpleRadio.Standalone.Common;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Server.UI
{
    public class ClientViewModel:Screen
    {
        private readonly IEventAggregator _eventAggregator;
        private static readonly Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        public SRClient Client { get; }

        public string ClientName => Client.Name;

        public ClientViewModel(SRClient client, IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator;
            Client = client;
            Client.PropertyChanged+= ClientOnPropertyChanged;
        }

        private void ClientOnPropertyChanged(object sender, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            if (propertyChangedEventArgs.PropertyName == "Coalition")
            {
                NotifyOfPropertyChange(()=> ClientCoalitionColour);
            }
        }

        public SolidColorBrush ClientCoalitionColour 
        {
            get
            {
                switch (Client.Coalition)
                {
                    case 0:
                        return new SolidColorBrush(Colors.White);
                    case 1:
                        return new SolidColorBrush(Colors.Red);
                    case 2:
                        return new SolidColorBrush(Colors.Blue);
                    default:
                        return new SolidColorBrush(Colors.White);

                }
                
            }
            
        }

        public void KickClient()
        {
            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show($"Are you sure you want to Kick {Client.Name}?", "Ban Confirmation", System.Windows.MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                _eventAggregator.PublishOnBackgroundThread(new KickClientMessage(Client));
            }

        }
        public void BanClient()
        {
            MessageBoxResult messageBoxResult = System.Windows.MessageBox.Show($"Are you sure you want to Ban {Client.Name}?", "Ban Confirmation", System.Windows.MessageBoxButton.YesNo);
            if (messageBoxResult == MessageBoxResult.Yes)
            {
                _eventAggregator.PublishOnBackgroundThread(new BanClientMessage(Client));
            }
        }

    }
}
