using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public class ServerAddress : INotifyPropertyChanged
    {
        private bool _isDefault;

        public ServerAddress(string name, string address, string eamCoalitionPassword, bool isDefault)
        {
            Name = name;
            Address = address;
            EAMCoalitionPassword = eamCoalitionPassword;
            IsDefault = isDefault;
        }

        public string Name { get; set; }

        public string Address { get; set; }

        public string EAMCoalitionPassword { get; set; }

        public bool IsDefault
        {
            get { return _isDefault; }
            set
            {
                _isDefault = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}