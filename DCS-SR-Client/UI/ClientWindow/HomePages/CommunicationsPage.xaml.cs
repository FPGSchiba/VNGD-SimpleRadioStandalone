using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages
{
    public partial class CommunicationsPage : Page
    {
        private readonly MainWindow _mainWindow;
        private readonly Dictionary<string, int> _panelIndexes;
        public CommunicationsPage()
        {
            InitializeComponent();
            
            _mainWindow = Application.Current.MainWindow as MainWindow;

            // Preparing opening a Panel by name
            _panelIndexes = new Dictionary<string, int>()
            {
                { Vertical1.Content.ToString(), MainWindow.OneVerticalIndex },
                { Vertical2.Content.ToString(), MainWindow.TwoVerticalIndex },
                { Vertical3.Content.ToString(), MainWindow.ThreeVerticalIndex },
                { Vertical5.Content.ToString(), MainWindow.FiveVerticalIndex },
                { Vertical10.Content.ToString(), MainWindow.TenVerticalIndex },
                { Horizontal1.Content.ToString(), MainWindow.OneHorizontalIndex },
                { Horizontal2.Content.ToString(), MainWindow.TwoHorizontalIndex },
                { Horizontal3.Content.ToString(), MainWindow.ThreeHorizontalIndex },
                { Horizontal5.Content.ToString(), MainWindow.FiveHorizontalIndex },
                { Horizontal10.Content.ToString(), MainWindow.TenHorizontalIndex },
                { Wide10.Content.ToString(), MainWindow.TenHorizontalWideIndex },
                { Long10.Content.ToString(), MainWindow.TenHorizontalWideIndex },
                { Compact.Content.ToString(), MainWindow.TransparentIndex },
                { Switch.Content.ToString(), MainWindow.SwitchIndex }
            };

        }
        
        private void Logout_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeLogOutClicked();
        }

        private void PanelButton_OnClick(object sender, RoutedEventArgs e)
        {
            // Closing dialog first looks better
            DialogHost.Close("PanelDialog");
            
            // Gets which Panel to open by name
            var panelName = sender.ToString().Replace("System.Windows.Controls.Button: ", "");
            var panelIndex = _panelIndexes[panelName];
            _mainWindow.ToggleOverlay(true, panelIndex);
        }
    }
}