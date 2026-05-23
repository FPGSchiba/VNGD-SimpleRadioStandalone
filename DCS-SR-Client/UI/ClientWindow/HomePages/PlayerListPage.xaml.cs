using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using NLog;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Singletons;

namespace Vanguard.VCS.Client.UI.ClientWindow.HomePages
{
    public class PlayerListItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string prop) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private string _name = "";
        public string Name
        {
            get => _name;
            set { _name = value ?? ""; Notify(nameof(Name)); }
        }

        public string CoalitionName { get; set; } = "Unassigned";
        public SolidColorBrush CoalitionColor { get; set; } = new SolidColorBrush(Colors.Gray);
        public string FfId { get; set; } = "";
        public string AllowsRecording { get; set; } = "N";

        private bool _isTransmitting;
        public bool IsTransmitting
        {
            get => _isTransmitting;
            set { _isTransmitting = value; Notify(nameof(IsTransmitting)); }
        }
    }

    public partial class PlayerListPage : Page
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ObservableCollection<PlayerListItem> _items = new ObservableCollection<PlayerListItem>();
        private readonly CollectionViewSource _grouped = new CollectionViewSource();
        private IDisposable _joinSub, _leftSub, _infoSub;
        private readonly DispatcherTimer _transmitTimer;

        public PlayerListPage()
        {
            InitializeComponent();

            _grouped.Source = _items;
            _grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlayerListItem.CoalitionName)));
            _grouped.SortDescriptions.Add(new SortDescription(nameof(PlayerListItem.CoalitionName), ListSortDirection.Ascending));
            _grouped.SortDescriptions.Add(new SortDescription(nameof(PlayerListItem.Name), ListSortDirection.Ascending));

            ClientList.ItemsSource = _grouped.View;

            _transmitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _transmitTimer.Tick += UpdateTransmitting;

            Loaded += (_, _) =>
            {
                RebuildList();
                _joinSub  = App.EventBus.Subscribe<ClientJoinedEvent>(_ => Dispatcher.Invoke(RebuildList));
                _leftSub  = App.EventBus.Subscribe<ClientLeftEvent>(_ => Dispatcher.Invoke(RebuildList));
                _infoSub  = App.EventBus.Subscribe<ClientInfoUpdatedEvent>(_ => Dispatcher.Invoke(RebuildList));
                _transmitTimer.Start();
            };

            Unloaded += (_, _) =>
            {
                _joinSub?.Dispose(); _leftSub?.Dispose(); _infoSub?.Dispose();
                _transmitTimer.Stop();
            };
        }

        private void RebuildList()
        {
            _items.Clear();
            foreach (var client in ConnectedClientsSingleton.Instance.Values.OrderBy(c => c.Name))
            {
                var fleetCode = Regex.Match(client.Name, "(?<=\\[)(.+)(?=\\])").Value;
                var playerName = Regex.Replace(client.Name, "\\[.+\\]\\s", "");

                _items.Add(new PlayerListItem
                {
                    Name          = playerName,
                    CoalitionName = string.IsNullOrEmpty(client.CoalitionName) ? "Unassigned" : client.CoalitionName,
                    CoalitionColor = client.ClientCoalitionColour,
                    FfId          = string.IsNullOrEmpty(fleetCode) ? "NaFID" : fleetCode,
                    AllowsRecording = client.AllowRecord ? "Y" : "N",
                });
            }
            UpdateSummary();
        }

        private void UpdateTransmitting(object sender, EventArgs e)
        {
            var receivingStates = ClientStateSingleton.Instance.RadioReceivingState;
            var activeNames = new HashSet<string>(
                receivingStates
                    .Where(s => s != null && s.IsReceiving && !string.IsNullOrEmpty(s.SentBy))
                    .Select(s => s.SentBy));

            foreach (var item in _items)
                item.IsTransmitting = activeNames.Contains(item.Name);
        }

        private void UpdateSummary()
        {
            var total = _items.Count;
            var byCoalition = _items
                .GroupBy(i => i.CoalitionName)
                .Select(g => $"{g.Key}: {g.Count()}")
                .ToList();
            SummaryText.Text = total == 0
                ? "No players connected"
                : $"{total} connected ({string.Join(", ", byCoalition)})";
        }
    }
}
