using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Preferences;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow
{
    public class FavouriteServersViewModel
    {
        private readonly ObservableCollection<ServerAddress> _addresses = new ObservableCollection<ServerAddress>();
        private readonly IFavouriteServerStore _favouriteServerStore;

        public FavouriteServersViewModel(IFavouriteServerStore favouriteServerStore)
        {
            _favouriteServerStore = favouriteServerStore;
            _favouriteServerStore = favouriteServerStore;

            foreach (var favourite in _favouriteServerStore.LoadFromStore())
            {
                _addresses.Add(favourite);
            }

            NewAddressCommand = new DelegateCommand(OnNewAddress);
            RemoveSelectedCommand = new DelegateCommand(OnRemoveSelected);
            SaveCommand = new DelegateCommand(OnSave);
        }

        public ObservableCollection<ServerAddress> Addresses => _addresses;

        public string NewName { get; set; }

        public string NewAddress { get; set; }

        public ICommand NewAddressCommand { get; }

        public ICommand SaveCommand { get; set; }

        public ICommand RemoveSelectedCommand { get; set; }

        public ServerAddress SelectedItem { get; set; }

        public ServerAddress DefaultServerAddress
        {
            get
            {
                var defaultAddress = _addresses.FirstOrDefault(x => x.IsDefault);
                if (defaultAddress == null && _addresses.Count > 0)
                {
                    defaultAddress = _addresses.First();
                }
                return defaultAddress;
            }
        }

        private void OnNewAddress()
        {
            var isDefault = _addresses.Count == 0;
            _addresses.Add(new ServerAddress(NewName, NewAddress, isDefault));
        }

        private void OnRemoveSelected()
        {
            if (SelectedItem == null)
            {
                return;
            }

            _addresses.Remove(SelectedItem);
        }

        private void OnSave()
        {
            var saveSucceeded = _favouriteServerStore.SaveToStore(_addresses);

            var messageBoxText = saveSucceeded ? "Favourite servers saved" : "Failed to save favourite servers. Please check logs for details.";
            var icon = saveSucceeded ? MessageBoxImage.Information : MessageBoxImage.Error;

            MessageBox.Show(Application.Current.MainWindow, messageBoxText, "Save result", MessageBoxButton.OK, icon);
        }
    }
}