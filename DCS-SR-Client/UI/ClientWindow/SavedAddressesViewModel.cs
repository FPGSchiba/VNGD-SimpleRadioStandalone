using System.Collections.ObjectModel;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow
{
    public class SavedAddressesViewModel
    {
        private readonly ObservableCollection<SavedAddress> _savedAddresses = new ObservableCollection<SavedAddress>();

        public SavedAddressesViewModel()
        {
            _savedAddresses.Add(new SavedAddress("test 1", "123.456", true));
            _savedAddresses.Add(new SavedAddress("test 2", "123.456", false));
            _savedAddresses.Add(new SavedAddress("test 3", "123.456", false));

            NewAddressCommand = new DelegateCommand(OnNewAddress);
            SaveCommand = new DelegateCommand(OnSave);
            RemoveSelectedCommand = new DelegateCommand(OnRemoveSelected);
        }

        public ObservableCollection<SavedAddress> SavedAddresses => _savedAddresses;

        public string NewName { get; set; }

        public string NewAddress { get; set; }

        public ICommand NewAddressCommand { get; }

        public ICommand SaveCommand { get; set; }

        public ICommand RemoveSelectedCommand { get; set; }

        public SavedAddress SelectedItem { get; set; }

        private void OnNewAddress()
        {
            _savedAddresses.Add(new SavedAddress(NewName, NewAddress, false));
        }

        private void OnSave()
        {
            // todo
        }

        private void OnRemoveSelected()
        {
            if (SelectedItem == null)
            {
                return;
            }

            _savedAddresses.Remove(SelectedItem);
        }
    }
}