using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media;
using System.Windows.Threading;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Singletons;
using Ciribob.DCS.SimpleRadio.Standalone.Common.Network;
using NLog;
using Sentry;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.HomePages
{
    class PlayerListItem : INotifyPropertyChanged
    {
        private string _name;
        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                if(value == null || value == "")
                {
                    value = "---";
                }

                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Name"));
                }
            }
        }

        private string _allowsRecording;
        
        public string AllowsRecording {
            get
            {

                return _allowsRecording;
            }
            set
            {
                _allowsRecording = String.IsNullOrEmpty(value) ? "N" : value;
            }
        }

        private SolidColorBrush _teamColor;

        public SolidColorBrush TeamColor
        {
            get
            {
                return _teamColor;
            }
            set
            {
                if (value != null)
                {
                    _teamColor = value;
                }
                else
                {
                    _teamColor = new SolidColorBrush(Colors.White);
                }
            }
        }
        
        
        private string _ffId;
        
        public string FfId {
            get
            {

                return _ffId;
            }
            set
            {
                _ffId = String.IsNullOrEmpty(value) ? "N" : value;
            }
        }

        public override string ToString()
        {
            return $"[Name: '{Name}', AllowsRecording: {AllowsRecording}, TeamColor: {TeamColor}, FfId: {FfId} ]";
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    public partial class PlayerListPage : Page
    {
        private readonly DispatcherTimer _updateTimer;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ObservableCollection<PlayerListItem> _clientList = new ObservableCollection<PlayerListItem>();
        public PlayerListPage()
        {
            InitializeComponent();
            UpdateList();
            
            ClientList.DataContext = _clientList;
            ClientList.ItemsSource = _clientList;

            _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();
        }
        
        private void UpdateList()
        {
            _clientList.Clear();
        
            // first create temporary list to sort
            var tempList = new List<SRClient>();


            foreach (var srClient in ConnectedClientsSingleton.Instance.Values)
            {
                tempList.Add(srClient);
            }

            foreach (var clientListModel in tempList.OrderByDescending(model => model.Coalition)
                         .ThenBy(model => model.Name.ToLower()).ToList())
            {
                var fleetCode = Regex.Match(clientListModel.Name, "(?<=\\[)([A-Z]{2,4})(?=\\])").Value;
                var playerName = Regex.Replace(clientListModel.Name, "\\[[A-Z]{2,4}\\]\\s", "");
                
                var item = new PlayerListItem
                {
                    Name = playerName,
                    FfId = fleetCode == "" ? "NoN" : fleetCode,
                    AllowsRecording = clientListModel.AllowRecord ? "Y" : "N",
                    TeamColor = clientListModel.ClientCoalitionColour,
                };
                _clientList.Add(item);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                UpdateList();
            }
            catch (Exception ex)
            {
                _logger.ForExceptionEvent(ex, LogLevel.Error);
                SentrySdk.CaptureException(ex, scope =>
                {
                    scope.AddAttachment("clientlog.txt");
                });
            }
        }
    }
}