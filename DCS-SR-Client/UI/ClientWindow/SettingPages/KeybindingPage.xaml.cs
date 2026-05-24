using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Vanguard.VCS.Client.Input;
using Vanguard.VCS.Client.UI.Components;

namespace Vanguard.VCS.Client.UI.ClientWindow.SettingPages
{
    public partial class KeybindingPage : Page
    {
        public KeybindingPage()
        {
            InitializeComponent();
            
            var mainWindow = (MainWindow) Application.Current.MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Could not Initialize Keybindings, because no MainWindow is open.\n Please tell this to FPG_Schiba!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var inputDeviceManager = new InputDeviceManager(mainWindow, mainWindow.ToggleOverlay, mainWindow.UpdateChannelSettings);
            InitInputBindings(inputDeviceManager);
        }

        private void InitInputBindings(InputDeviceManager inputDeviceManager)
        {
            // Set the input device manager for all keybinding controls
            // No need to manually set the input device manager for each keybinding control
            List<KeybindingControl> keybindingControls = GetLogicalChildCollection<KeybindingControl>(this);
            foreach (var keybindingControl in keybindingControls)
            {
                keybindingControl.InputDeviceManager = inputDeviceManager;
            }
        }
        
        private static List<T> GetLogicalChildCollection<T>(object parent) where T : DependencyObject
        {
            List<T> logicalCollection = new List<T>();
            GetLogicalChildCollection(parent as DependencyObject, logicalCollection);
            return logicalCollection;
        }
        private static void GetLogicalChildCollection<T>(DependencyObject parent, List<T> logicalCollection) where T : DependencyObject
        {
            IEnumerable children = LogicalTreeHelper.GetChildren(parent);
            foreach (object child in children)
            {
                if (child is DependencyObject dependencyObject)
                {
                    if (dependencyObject is T logicalChild)
                    {
                        logicalCollection.Add(logicalChild);
                    }
                    GetLogicalChildCollection(dependencyObject, logicalCollection);
                }
            }
        }
    }
}