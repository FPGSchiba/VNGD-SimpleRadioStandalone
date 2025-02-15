using System;
using System.Windows;
using System.Windows.Controls;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages
{
    public partial class HomePage : Page
    {
        private readonly MainWindow _mainWindow;
        
       public HomePage()
        {
            InitializeComponent();
            
            _mainWindow = Application.Current.MainWindow as MainWindow;
            
            // UI Setup
            if (_mainWindow != null)
            {
                ConnectedAsBlock.Text += ClientStateSingleton.Instance.LastSeenName;
                LoginTypeBlock.Text += _mainWindow.LoginType;
                On_TimerTick(null, null);
                System.Windows.Threading.DispatcherTimer dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
                dispatcherTimer.Tick += On_TimerTick;
                dispatcherTimer.Interval = new TimeSpan(0,0,1);
                dispatcherTimer.Start();
            }
        }
       
        private void On_TimerTick(object sender, EventArgs e)
        {
            if (!ClientStateSingleton.Instance.IsConnected)
            {
                ConnectionTimeBlock.Text = "Connection Time: ---";
                ConnectedAsBlock.Text = "Connected as: ---";
                LoginTypeBlock.Text = "Login Type: ---";
                return;
            }
            
            if (DateTime.UtcNow.Year - _mainWindow.ConnectedAt.Year > 1)
            {
                ConnectionTimeBlock.Text = "Connection Time: loading";
                return;
            }
            
            var diff = DateTime.UtcNow - _mainWindow.ConnectedAt;
            var hours = (int)Math.Round(Math.Floor(diff.TotalHours), 0);
            var hourS = hours == 1 ? "" : "s";
            var totalMinutes = (diff.TotalHours - hours) * 60;
            var minutes = (int)Math.Round(Math.Floor(totalMinutes), 0);
            var minuteS = minutes == 1 ? "" : "s";
            
            if (hours == 0)
            {
                var seconds = (int)Math.Round((totalMinutes - minutes) * 60, 0);
                var secondS = seconds == 1 ? "" : "s";
                ConnectionTimeBlock.Text = $"Connection Time: {minutes} minute{minuteS} {seconds} second{secondS}";
            }
            else
            {
                ConnectionTimeBlock.Text = $"Connection Time: {hours} hour{hourS} {minutes} minute{minuteS}";
            }
            
            ConnectedAsBlock.Text = $"Connected as: {ClientStateSingleton.Instance.LastSeenName}";
            LoginTypeBlock.Text = $"Login Type: {_mainWindow.LoginType}";
        }
        
        private void Logout_OnClick(object sender, RoutedEventArgs e)
        {
            _mainWindow.On_HomeLogOutClicked();
        }
    }
}