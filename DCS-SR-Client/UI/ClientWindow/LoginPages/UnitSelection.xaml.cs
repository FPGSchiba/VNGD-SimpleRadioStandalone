using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.UI.ClientWindow.LoginPages
{
    public partial class UnitSelectionPage : Page
    {
        private readonly MainWindow _mainWindow;
        private readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();
        private UnitSelectionViewModel _unitSelectionViewModel;
    
        public UnitSelectionPage()
        {
            InitializeComponent();
        
            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        public void SetSelectionData(InternalLoginResult result)
        {
            List<KeyValuePair<string, string>> availableUnits = new List<KeyValuePair<string, string>>();
            foreach (var unit in result.AvailableUnits)
            {
                availableUnits.Add(new KeyValuePair<string, string>(unit.UnitId, unit.UnitName));
            }
            List<string> availableCoalitions = new List<string>();
            foreach (var coalition in result.AvailableCoalitions)
            {
                availableCoalitions.Add(coalition.Name);
            }
            List<KeyValuePair<int, string>> availableRoles = new List<KeyValuePair<int, string>>();
            foreach (var role in result.AvailableRoles)
            {
                availableRoles.Add(new KeyValuePair<int, string>((int)role.Id, role.Name));
            }
            
            _unitSelectionViewModel = new UnitSelectionViewModel(availableUnits, availableCoalitions, availableRoles);
            this.DataContext = _unitSelectionViewModel;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_UnitSelectionBackClicked();
        }

        private void Continue_Click(object sender, RoutedEventArgs e)
        {
            Progress.Visibility = Visibility.Visible;
            Continue.IsEnabled = false;
            RoleSelect.IsEnabled = false;
            UnitSelect.IsEnabled = false;
            CoalitionSelect.IsEnabled = false;
            
            _logger.Info($"Selected Unit: {_unitSelectionViewModel.UnitText}, Coalition: {_unitSelectionViewModel.SelectedCoalition}, Role: {_unitSelectionViewModel.SelectedRole}");
            uint selectedRoleId = _unitSelectionViewModel.SelectedRole != null 
                ? (uint)_unitSelectionViewModel.Roles.IndexOf(_unitSelectionViewModel.SelectedRole) 
                : 100;
            if (string.IsNullOrEmpty(_unitSelectionViewModel.UnitText) ||
                string.IsNullOrEmpty(_unitSelectionViewModel.SelectedCoalition) || selectedRoleId == 100)
            {
                ShowError("Please select a unit, coalition and role before continuing.");
                return;
            }
            _mainWindow.On_UnitSelectionContinueClicked(_unitSelectionViewModel.UnitText, _unitSelectionViewModel.SelectedCoalition, selectedRoleId);
        }

        public void ShowError(string message)
        {
            Dispatcher.Invoke(() =>
            {
                ErrorText.Text = message;
                ErrorPanel.Visibility = Visibility.Visible;
                Progress.Visibility = Visibility.Hidden;
                Continue.IsEnabled = true;
                RoleSelect.IsEnabled = true;
                UnitSelect.IsEnabled = true;
                CoalitionSelect.IsEnabled = true;
            });
        }
    }
}

