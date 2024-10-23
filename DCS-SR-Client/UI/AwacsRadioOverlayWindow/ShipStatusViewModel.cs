using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow
{
    public class ShipStatusViewModel : INotifyPropertyChanged
    {
        private readonly ShipStateManager _stateManager;

        public event PropertyChangedEventHandler PropertyChanged;

        public ShipStatusViewModel(ShipStateManager stateManager)
        {
            _stateManager = stateManager;
            UpdateComponents();
        }

        public void UpdateComponents()
        {
            foreach (ShipComponent component in System.Enum.GetValues(typeof(ShipComponent)))
            {
                string state = _stateManager.GetComponentState(component);
                OnPropertyChanged($"Component_{component}");
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}