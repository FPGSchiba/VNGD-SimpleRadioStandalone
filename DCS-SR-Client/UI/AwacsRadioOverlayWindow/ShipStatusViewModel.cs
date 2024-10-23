using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow
{
    public class ShipStatusViewModel : INotifyPropertyChanged
    {
        private readonly ShipStateManager _stateManager;
        private ObservableCollection<ComponentStatus> _components;

        public ObservableCollection<ComponentStatus> Components
        {
            get => _components;
            set
            {
                _components = value;
                OnPropertyChanged(nameof(Components));
            }
        }

        public ShipStatusViewModel(ShipStateManager stateManager)
        {
            _stateManager = stateManager;
            Components = new ObservableCollection<ComponentStatus>();
            UpdateComponents();
        }

        public void UpdateComponents()
        {
            Components.Clear();

            AddComponentStatus(State.Component.PP, "Power");
            AddComponentStatus(State.Component.EG, "Engines");
            AddComponentStatus(State.Component.SH, "Shield");
            AddComponentStatus(State.Component.WP, "Weapons");
            AddComponentStatus(State.Component.QD, "QDrive");
        }

        private void AddComponentStatus(State.Component component, string label)
        {
            Components.Add(new ComponentStatus
            {
                Component = label,
                State = _stateManager.GetComponentState(component),
                StateColor = GetStateColor(_stateManager.GetComponentState(component))
            });
        }

        private static SolidColorBrush GetStateColor(string state)
        {
            string stateLower = state.ToLower();

            if (stateLower == "nominal")
                return new SolidColorBrush(Colors.LimeGreen);

            if (stateLower == "maximum" || stateLower == "maximum output" || stateLower == "combat ready")
                return new SolidColorBrush(Colors.Red);

            if (stateLower == "offline")
                return new SolidColorBrush(Colors.Gray);

            if (stateLower == "reduced output" || stateLower == "low power" || stateLower == "standby")
                return new SolidColorBrush(Colors.Yellow);

            if (stateLower == "needs inspection")
                return new SolidColorBrush(Colors.Orange);

            return new SolidColorBrush(Colors.White);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ComponentStatus
    {
        public string Component { get; set; }
        public string State { get; set; }
        public SolidColorBrush StateColor { get; set; }
    }
}