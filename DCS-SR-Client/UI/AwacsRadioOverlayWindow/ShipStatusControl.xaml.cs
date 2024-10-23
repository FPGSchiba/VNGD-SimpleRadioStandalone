using Ciribob.DCS.SimpleRadio.Standalone.Client.State;
using Ciribob.DCS.SimpleRadio.Standalone.Common.State;
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

        private void UpdateStatusDisplay(ShipCondition condition)
        {
            if (ShipConditionText != null)
            {
                ShipConditionText.Text = condition.ToString();
                ShipConditionText.Foreground = GetConditionBrush(condition);
            }
            _viewModel.UpdateComponents();
        }

        private static SolidColorBrush GetConditionBrush(ShipCondition condition)
        {
            switch (condition)
            {
                case ShipCondition.Normal:
                    return new SolidColorBrush(Colors.LimeGreen);
                case ShipCondition.Combat:
                    return new SolidColorBrush(Colors.Red);
                case ShipCondition.LowPower:
                    return new SolidColorBrush(Colors.Yellow);
                default:
                    return new SolidColorBrush(Colors.Orange);
            }
        }

        private void CombatMode_Click(object sender, RoutedEventArgs e)
        {
            _stateManager.SetCondition(ShipCondition.Combat);
            UpdateStatusDisplay(ShipCondition.Combat);
        }

        private void NormalMode_Click(object sender, RoutedEventArgs e)
        {
            _stateManager.SetCondition(ShipCondition.Normal);
            UpdateStatusDisplay(ShipCondition.Normal);
        }
    }
}