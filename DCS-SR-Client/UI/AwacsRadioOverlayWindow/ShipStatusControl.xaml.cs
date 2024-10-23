using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.AwacsRadioOverlayWindow
{
    public partial class ShipStatusControl : UserControl
    {
        private readonly ShipStatusViewModel _viewModel;
        private readonly ShipStateManager _stateManager;

        public ShipStatusControl()
        {
            InitializeComponent();
            _stateManager = new ShipStateManager();
            _viewModel = new ShipStatusViewModel(_stateManager);
            DataContext = _viewModel;
        }

        private void UpdateStatusDisplay(State.Condition condition)
        {
            if (ShipConditionText != null)
            {
                ShipConditionText.Text = condition.ToString();
                ShipConditionText.Foreground = GetConditionBrush(condition);
            }
            _viewModel.UpdateComponents();
        }

        private static SolidColorBrush GetConditionBrush(State.Condition condition)
        {
            switch (condition)
            {
                case State.Condition.Normal:
                    return new SolidColorBrush(Colors.LimeGreen);
                case State.Condition.Combat:
                    return new SolidColorBrush(Colors.Red);
                case State.Condition.LowPower:
                    return new SolidColorBrush(Colors.Yellow);
                default:
                    return new SolidColorBrush(Colors.Orange);
            }
        }

        private void CombatMode_Click(object sender, RoutedEventArgs e)
        {
            _stateManager.SetCondition(State.Condition.Combat);
            UpdateStatusDisplay(State.Condition.Combat);
        }

        private void NormalMode_Click(object sender, RoutedEventArgs e)
        {
            _stateManager.SetCondition(State.Condition.Normal);
            UpdateStatusDisplay(State.Condition.Normal);
        }
    }
}
