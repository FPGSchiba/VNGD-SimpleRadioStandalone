# Client UX Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix six client-side issues: sender name "---" bug, mute/kick/ban server feedback, VOX choppiness, client list coalition grouping, and login flow stepper with inline errors.

**Architecture:** All changes are in the WPF client (`DCS-SR-Client`). Events flow through the existing `IEventBus`. UI state is driven by `ClientStateSingleton`. Pages remain as WPF `Page` objects but navigation is consolidated into a `ConnectionStep`-based method on `MainWindow`.

**Tech Stack:** C# / WPF, xUnit, NSubstitute, existing `IEventBus` / `EventBus`, `GlobalSettingsStore`, `ProfileSettingsStore`, `ConnectedClientsSingleton`, `ClientStateSingleton`.

---

## File Map

| File | Change |
|------|--------|
| `DCS-SR-Common/Network/SRClient.cs` | Fix `Name` setter fallback; add `CoalitionName` |
| `DCS-SR-Client/Events/Events.cs` | Add `ServerMuteChangedEvent` |
| `DCS-SR-Client/Singletons/ClientStateSingleton.cs` | Add `IsServerMuted` observable property |
| `DCS-SR-Client/Network/VcsClientSyncHandler.cs` | Fix `ApplyClientUpdate` fallback; publish `ServerActionEvent` and `ServerMuteChangedEvent` in `HandleServerAction`; populate `CoalitionName` |
| `DCS-SR-Client/Network/UDPVoiceHandler.cs` | Rewrite `CheckVOXActivation` with hold/attack state machine; fix transmitter name lookup |
| `DCS-SR-Client/Settings/GlobalSettingsStore.cs` | Add `AlwaysShowTransmitterName`, `VoxAttackTimeMs` keys and defaults |
| `DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml` + `.cs` | Add `AlwaysShowTransmitterName` checkbox; add VOX attack-time slider |
| `DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml` + `.cs` | Rework with coalition grouping, role badges, transmit indicator, EventBus updates |
| `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml` | Add stepper header panel; add server-mute banner |
| `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs` | Replace `OpenPageByIndex` with `NavigateToStep`; inline errors; subscribe to mute/kick events |
| `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml` + `.cs` | Add server-selection UI (auto-discovered + custom); add inline error panel |
| `DCS-SR-Client/UI/ClientWindow/LoginPages/GuestPage.xaml` + `.cs` | Replace `MessageBox` calls with inline error panel |
| `DCS-SR-Client/UI/ClientWindow/LoginPages/LoginPage.xaml` + `.cs` | Add inline error panel |
| `DCS-SR-Client/UI/ClientWindow/LoginPages/UnitSelection.xaml` + `.cs` | Add inline error panel |
| `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs` | Extend with HandleServerAction tests |

---

## Task 1: Fix sender name "---" bug

**Files:**
- Modify: `DCS-SR-Common/Network/SRClient.cs:30-33`
- Modify: `DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml.cs:28-32`
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs:701`
- Modify: `DCS-SR-Client/Settings/GlobalSettingsStore.cs` (GlobalSettingsKeys enum + defaults dict)
- Modify: `DCS-SR-Client/Network/UDPVoiceHandler.cs:416-420`
- Modify: `DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml` + `.cs`

- [ ] **Step 1: Write the failing test**

In `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`, add:

```csharp
[Fact]
public void ApplyClientUpdate_WhenClientInfoNameIsNull_StoresEmptyStringNotDashes()
{
    // The update callback is internal; test via the public ProcessServerUpdate path
    var handler = BuildHandler();
    var update = new ServerUpdate
    {
        Type = ServerUpdate.Types.UpdateType.ClientJoined,
        ClientUpdate = new ClientUpdate
        {
            ClientGuid = Guid.NewGuid().ToString(),
            ClientInfo = new ClientInfo { Name = "" },
        }
    };

    handler.ProcessServerUpdate(update);

    var client = _clients.Values.Single();
    Assert.Equal("", client.Name); // must NOT be "---"
}
```

- [ ] **Step 2: Run test — expect FAIL**

```
dotnet test DCS-SR-ClientTests --filter "ApplyClientUpdate_WhenClientInfoNameIsNull"
```

Expected output: `Assert.Equal() Failure: "---" != ""`

- [ ] **Step 3: Fix `SRClient.Name` setter in `DCS-SR-Common/Network/SRClient.cs`**

Change lines 30-33 from:
```csharp
if(value == null || value == "")
{
    value = "---";
}
```
To:
```csharp
if (value == null)
{
    value = "";
}
```

- [ ] **Step 4: Fix `PlayerListItem.Name` setter in `DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml.cs`**

Change lines 28-32 from:
```csharp
if(value == null || value == "")
{
    value = "---";
}
```
To:
```csharp
if (value == null)
{
    value = "";
}
```

- [ ] **Step 5: Fix `ApplyClientUpdate` fallback in `VcsClientSyncHandler.cs`**

Change line 701:
```csharp
Name = clientInfo?.Name ?? "---",
```
To:
```csharp
Name = clientInfo?.Name ?? "",
```

- [ ] **Step 6: Run test — expect PASS**

```
dotnet test DCS-SR-ClientTests --filter "ApplyClientUpdate_WhenClientInfoNameIsNull"
```

- [ ] **Step 7: Add `AlwaysShowTransmitterName` to `GlobalSettingsStore.cs`**

In the `GlobalSettingsKeys` enum, after `ShowTransmitterName`:
```csharp
AlwaysShowTransmitterName,
```

In the defaults dictionary, after the `ShowTransmitterName` entry:
```csharp
{GlobalSettingsKeys.AlwaysShowTransmitterName.ToString(), "false"},
```

- [ ] **Step 8: Update transmitter name lookup in `UDPVoiceHandler.cs`**

Replace lines 415-420:
```csharp
if (_serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME) && _globalSettings.GetClientSettingBool(GlobalSettingsKeys.ShowTransmitterName) && _clients.TryGetValue(udpVoicePacket.ClientId, out var transmittingClient))
{
    transmitterName = transmittingClient.Name;
    receiveState.SentBy = transmitterName;
}
```
With:
```csharp
var showName = _serverSettings.GetSettingAsBool(ServerSettingsKeys.SHOW_TRANSMITTER_NAME)
               || _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AlwaysShowTransmitterName);
if (showName && _clients.TryGetValue(udpVoicePacket.ClientId, out var transmittingClient))
{
    receiveState.SentBy = transmittingClient.Name; // empty string if name not yet known
}
```

- [ ] **Step 9: Add checkbox to `GeneralPage.xaml`**

Find the existing `ShowTransmitterName` checkbox section and add immediately below it:
```xaml
<CheckBox x:Name="AlwaysShowTransmitterName"
          Content="Always show caller name on radio (overrides server setting)"
          Margin="0,4,0,0" />
```

In `GeneralPage.xaml.cs`, wire the checkbox in the load handler alongside the existing `ShowTransmitterName` logic:
```csharp
AlwaysShowTransmitterName.IsChecked = _globalSettings.GetClientSettingBool(GlobalSettingsKeys.AlwaysShowTransmitterName);
AlwaysShowTransmitterName.Checked += (_, _) =>
    _globalSettings.SetClientSetting(GlobalSettingsKeys.AlwaysShowTransmitterName, true);
AlwaysShowTransmitterName.Unchecked += (_, _) =>
    _globalSettings.SetClientSetting(GlobalSettingsKeys.AlwaysShowTransmitterName, false);
```

- [ ] **Step 10: Commit**

```
git add DCS-SR-Common/Network/SRClient.cs \
        DCS-SR-Client/Network/VcsClientSyncHandler.cs \
        DCS-SR-Client/Network/UDPVoiceHandler.cs \
        DCS-SR-Client/Settings/GlobalSettingsStore.cs \
        DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml.cs \
        DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml \
        DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml.cs \
        DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs
git commit -m "fix: change name fallback from '---' to empty string; add AlwaysShowTransmitterName override"
```

---

## Task 2: Mute state + event wiring + kick/ban fix

**Files:**
- Modify: `DCS-SR-Client/Events/Events.cs`
- Modify: `DCS-SR-Client/Singletons/ClientStateSingleton.cs`
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Test: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Add `ServerMuteChangedEvent` to `Events.cs`**

After the existing `ServerActionEvent` record:
```csharp
public sealed record ServerMuteChangedEvent(bool IsMuted);
```

- [ ] **Step 2: Add `IsServerMuted` to `ClientStateSingleton.cs`**

After the existing `IsVoipConnected` property, add:
```csharp
private bool _isServerMuted;
public bool IsServerMuted
{
    get => _isServerMuted;
    set
    {
        _isServerMuted = value;
        NotifyPropertyChanged(nameof(IsServerMuted));
    }
}
```

- [ ] **Step 3: Write failing tests for `HandleServerAction`**

In `VcsClientSyncHandlerTests.cs`:
```csharp
[Fact]
public void HandleServerAction_Kick_PublishesServerActionEvent()
{
    var eventBus = Substitute.For<IEventBus>();
    var handler = BuildHandlerWithEventBus(eventBus);
    var action = new ServerAction
    {
        Type = ServerAction.Types.ActionType.Kick,
        Reason = "AFK"
    };
    handler.ProcessServerUpdate(new ServerUpdate
    {
        Type = ServerUpdate.Types.UpdateType.ServerAction,
        ServerAction = action
    });
    eventBus.Received(1).Publish(Arg.Is<ServerActionEvent>(e =>
        e.Type == ServerAction.Types.ActionType.Kick && e.Reason == "AFK"));
}

[Fact]
public void HandleServerAction_Mute_PublishesServerMuteChangedEvent()
{
    var eventBus = Substitute.For<IEventBus>();
    var handler = BuildHandlerWithEventBus(eventBus);
    handler.ProcessServerUpdate(new ServerUpdate
    {
        Type = ServerUpdate.Types.UpdateType.ServerAction,
        ServerAction = new ServerAction { Type = ServerAction.Types.ActionType.Mute }
    });
    eventBus.Received(1).Publish(Arg.Is<ServerMuteChangedEvent>(e => e.IsMuted == true));
}
```

- [ ] **Step 4: Run tests — expect FAIL**

```
dotnet test DCS-SR-ClientTests --filter "HandleServerAction"
```

- [ ] **Step 5: Fix `HandleServerAction` in `VcsClientSyncHandler.cs`**

Replace the existing method body:
```csharp
private void HandleServerAction(ServerAction action)
{
    switch (action.Type)
    {
        case ServerAction.Types.ActionType.Kick:
            Logger.Warn("Kicked from server. Reason: {0}", action.Reason);
            _eventBus?.Publish(new ServerActionEvent(
                action.Type, action.TargetGuid ?? "", action.Reason ?? "", null));
            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, action.Reason);
            break;

        case ServerAction.Types.ActionType.Ban:
            Logger.Warn("Banned from server. Reason: {0}", action.Reason);
            _eventBus?.Publish(new ServerActionEvent(
                action.Type, action.TargetGuid ?? "", action.Reason ?? "", null));
            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, action.Reason);
            break;

        case ServerAction.Types.ActionType.Mute:
            Logger.Info("Muted by server.");
            _eventBus?.Publish(new ServerMuteChangedEvent(IsMuted: true));
            break;

        case ServerAction.Types.ActionType.Unmute:
            Logger.Info("Unmuted by server.");
            _eventBus?.Publish(new ServerMuteChangedEvent(IsMuted: false));
            break;

        default:
            Logger.Warn("Received unknown ServerAction type: {0}", action.Type);
            break;
    }
}
```

- [ ] **Step 6: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests --filter "HandleServerAction"
```

- [ ] **Step 7: Commit**

```
git add DCS-SR-Client/Events/Events.cs \
        DCS-SR-Client/Singletons/ClientStateSingleton.cs \
        DCS-SR-Client/Network/VcsClientSyncHandler.cs \
        DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs
git commit -m "fix: publish ServerActionEvent for kick/ban and ServerMuteChangedEvent for mute; add IsServerMuted to ClientStateSingleton"
```

---

## Task 3: Mute banner in MainWindow

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml`
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`

- [ ] **Step 1: Add mute banner to `MainWindow.xaml`**

Find the `DisplayFrame` element and add a `Border` immediately above it (within the same grid cell or row, or as a new row above it depending on the grid layout). The banner must be hidden by default:

```xaml
<!-- Server mute banner — shown when server has muted this client -->
<Border x:Name="ServerMuteBanner"
        Background="#B45309"
        Padding="12,8"
        Visibility="Collapsed"
        VerticalAlignment="Top">
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
        <TextBlock Text="🔇  " FontSize="13" Foreground="White" VerticalAlignment="Center"/>
        <TextBlock Text="You are muted by the server — your transmissions are blocked."
                   FontSize="13" Foreground="White" VerticalAlignment="Center"/>
    </StackPanel>
</Border>
```

Place the banner so it is only visible when `LoggedIn = true`. Use a `DataTrigger` on `LoggedIn` or control it in code-behind — either approach is acceptable given the existing code style (code-behind is used throughout).

- [ ] **Step 2: Subscribe to `ServerMuteChangedEvent` in `MainWindow.xaml.cs`**

Add a field alongside the existing subscriptions:
```csharp
private IDisposable _serverMuteSubscription;
```

In the constructor, after the existing subscriptions:
```csharp
_serverMuteSubscription = App.EventBus.Subscribe<ServerMuteChangedEvent>(e =>
    Dispatcher.Invoke(() => HandleServerMuteChanged(e.IsMuted)));
```

Add the handler method:
```csharp
private void HandleServerMuteChanged(bool isMuted)
{
    ClientStateSingleton.Instance.IsServerMuted = isMuted;
    ServerMuteBanner.Visibility = isMuted && LoggedIn
        ? Visibility.Visible
        : Visibility.Collapsed;
}
```

Dispose in `OnClosing`:
```csharp
_serverMuteSubscription?.Dispose();
_serverMuteSubscription = null;
```

Also reset the banner in `Stop()`:
```csharp
ClientStateSingleton.Instance.IsServerMuted = false;
ServerMuteBanner.Visibility = Visibility.Collapsed;
```

- [ ] **Step 3: Build and verify banner appears/disappears**

```
dotnet build DCS-SR-Client/DCS-SR-Client.csproj
```

Expected: no build errors. Run the client, connect, and confirm the banner is hidden. A manual test to confirm it shows on mute requires a server action — confirm the event wiring compiles cleanly for now.

- [ ] **Step 4: Commit**

```
git add DCS-SR-Client/UI/ClientWindow/MainWindow.xaml \
        DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs
git commit -m "feat: add persistent server-mute banner to MainWindow"
```

---

## Task 4: VOX hold time and attack time state machine

**Files:**
- Modify: `DCS-SR-Client/Settings/GlobalSettingsStore.cs`
- Modify: `DCS-SR-Client/Network/UDPVoiceHandler.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml` + `.cs`

- [ ] **Step 1: Add `VoxAttackTimeMs` to `GlobalSettingsStore.cs`**

In the `GlobalSettingsKeys` enum, after `VOXMinimumDB`:
```csharp
VoxAttackTimeMs,
```

In the defaults dictionary, after the `VOXMinimumDB` entry:
```csharp
{GlobalSettingsKeys.VoxAttackTimeMs.ToString(), "100"},
```

- [ ] **Step 2: Add state fields to `UDPVoiceHandler.cs`**

After the existing `private long _lastVOXSend;` field, add:
```csharp
private long _voxAttackStart = -1; // ticks when voice first detected in current attack window
```

- [ ] **Step 3: Rewrite `CheckVOXActivation` with state machine**

Replace the existing `CheckVOXActivation` method with:
```csharp
private List<RadioInformation> CheckVOXActivation(out int sendingOn, bool voice)
{
    sendingOn = -1;
    if (_radioStateManager == null) return new List<RadioInformation>();
    var voxIndex = getCurrentSelected();
    if (voxIndex < 0)
    {
        _voxAttackStart = -1;
        _lastVOXSend = 0;
        return new List<RadioInformation>();
    }

    var nowTicks = DateTime.Now.Ticks;
    var nowMs = nowTicks / TimeSpan.TicksPerMillisecond;

    var holdMs = _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VOXMinimumTime);
    var attackMs = _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VoxAttackTimeMs);

    if (voice)
    {
        // Track when voice was first detected
        if (_voxAttackStart < 0)
            _voxAttackStart = nowTicks;

        var attackElapsedMs = (nowTicks - _voxAttackStart) / TimeSpan.TicksPerMillisecond;

        if (attackElapsedMs < attackMs)
            return new List<RadioInformation>(); // still in attack window — not transmitting yet

        // Voice active and attack window passed — transmit
        _lastVOXSend = nowMs;
    }
    else
    {
        // Voice gone — reset attack window
        _voxAttackStart = -1;

        // Check if still in hold window
        if (_lastVOXSend <= 0 || (nowMs - _lastVOXSend) > holdMs)
            return new List<RadioInformation>(); // hold expired — stop transmitting
        // else: still in hold window — fall through to transmit
    }

    var ri = _radioStateManager.GetRadio(voxIndex + 1);
    if (ri == null || ri.modulation == RadioInformation.Modulation.DISABLED)
        return new List<RadioInformation>();

    sendingOn = voxIndex + 1;
    return new List<RadioInformation> { ri };
}
```

Note: `_lastVOXSend` was previously a `long` field storing ticks. Change its semantic to milliseconds to be consistent with `holdMs`. It is initialised to `0` at declaration; a value of `0` means "never sent" and is treated as expired.

- [ ] **Step 4: Add VOX attack-time slider to `GeneralPage.xaml`**

Find the existing VOX section (look for `VOXMinimumTime` or `VOXMinimumDB` references). Add below the existing hold-time slider:
```xaml
<StackPanel Orientation="Horizontal" Margin="0,6,0,0">
    <TextBlock Text="VOX Attack Time (ms): " VerticalAlignment="Center" Width="160"/>
    <Slider x:Name="VoxAttackTimeSlider"
            Minimum="0" Maximum="500" TickFrequency="50"
            IsSnapToTickEnabled="False"
            Width="160" VerticalAlignment="Center"/>
    <TextBlock x:Name="VoxAttackTimeValue" Text="100" Margin="8,0,0,0" VerticalAlignment="Center"/>
</StackPanel>
```

- [ ] **Step 5: Wire slider in `GeneralPage.xaml.cs`**

In the page load handler, alongside the existing VOX slider wiring:
```csharp
VoxAttackTimeSlider.Value = _globalSettings.GetClientSettingInt(GlobalSettingsKeys.VoxAttackTimeMs);
VoxAttackTimeValue.Text = VoxAttackTimeSlider.Value.ToString("F0");
VoxAttackTimeSlider.ValueChanged += (_, e) =>
{
    var ms = (int)e.NewValue;
    VoxAttackTimeValue.Text = ms.ToString();
    _globalSettings.SetClientSetting(GlobalSettingsKeys.VoxAttackTimeMs, ms);
};
```

- [ ] **Step 6: Build**

```
dotnet build DCS-SR-Client/DCS-SR-Client.csproj
```

Expected: no errors.

- [ ] **Step 7: Commit**

```
git add DCS-SR-Client/Settings/GlobalSettingsStore.cs \
        DCS-SR-Client/Network/UDPVoiceHandler.cs \
        DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml \
        DCS-SR-Client/UI/ClientWindow/SettingPages/GeneralPage.xaml.cs
git commit -m "feat: add VOX hold/attack state machine with configurable attack time"
```

---

## Task 5: Client list coalition grouping

**Files:**
- Modify: `DCS-SR-Common/Network/SRClient.cs`
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml`

- [ ] **Step 1: Add `CoalitionName` to `SRClient.cs`**

After the `Coalition` property (line ~53), add:
```csharp
private string _coalitionName = "";
public string CoalitionName
{
    get => _coalitionName;
    set
    {
        _coalitionName = value ?? "";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoalitionName)));
    }
}
```

- [ ] **Step 2: Populate `CoalitionName` in `VcsClientSyncHandler.ApplyClientUpdate`**

In the `SRClient` initialiser, add after `Coalition = 0`:
```csharp
CoalitionName = clientInfo?.Coalition ?? "",
```

(The proto `ClientInfo.Coalition` field should contain the coalition name string from the server. If the field is not yet present in the proto, leave this as `""` — grouping will put all clients in "Unassigned" until the server provides the data.)

- [ ] **Step 3: Rewrite `PlayerListItem` in `PlayerListPage.xaml.cs`**

Replace the entire `PlayerListItem` class with:
```csharp
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
    public string FleetCode { get; set; } = "";
    public string Role { get; set; } = "";

    private bool _isTransmitting;
    public bool IsTransmitting
    {
        get => _isTransmitting;
        set { _isTransmitting = value; Notify(nameof(IsTransmitting)); }
    }
}
```

- [ ] **Step 4: Rewrite `PlayerListPage` code-behind**

Replace the entire `PlayerListPage` class with:
```csharp
public partial class PlayerListPage : Page
{
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly ObservableCollection<PlayerListItem> _items = new();
    private readonly CollectionViewSource _grouped = new();
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

        _transmitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
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
        foreach (var client in ConnectedClientsSingleton.Instance.Values)
        {
            _items.Add(new PlayerListItem
            {
                Name         = client.Name,
                CoalitionName = string.IsNullOrEmpty(client.CoalitionName) ? "Unassigned" : client.CoalitionName,
                CoalitionColor = client.ClientCoalitionColour,
                FleetCode    = client.TransmittingFrequency ?? "",
                Role         = "", // populated when server sends role data
            });
        }
        UpdateSummary();
    }

    private void UpdateTransmitting(object sender, EventArgs e)
    {
        var activeGuids = ClientStateSingleton.Instance.RadioReceivingState
            .Where(s => s != null && (DateTime.Now.Ticks - s.LastReceviedAt) < TimeSpan.TicksPerSecond)
            .Select(s => s.SentBy)
            .ToHashSet();

        foreach (var item in _items)
            item.IsTransmitting = activeGuids.Contains(item.Name);
    }

    private void UpdateSummary()
    {
        var total = _items.Count;
        var byCoalition = _items.GroupBy(i => i.CoalitionName)
            .Select(g => $"{g.Key}: {g.Count()}")
            .ToList();
        SummaryText.Text = $"{total} connected ({string.Join(", ", byCoalition)})";
    }
}
```

- [ ] **Step 5: Rewrite `PlayerListPage.xaml`**

Replace the entire XAML content with:
```xaml
<Page
    Height="320"
    Title="PlayerListPage"
    Width="415"
    x:Class="Vanguard.VCS.Client.UI.ClientWindow.HomePages.PlayerListPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:homePages="clr-namespace:Vanguard.VCS.Client.UI.ClientWindow.HomePages"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    mc:Ignorable="d">
    <DockPanel>
        <!-- Summary line -->
        <TextBlock x:Name="SummaryText"
                   DockPanel.Dock="Top"
                   Text="0 connected"
                   Foreground="#aaa"
                   FontSize="11"
                   Margin="8,6,8,4"/>

        <ListView x:Name="ClientList" DockPanel.Dock="Top">
            <ListView.GroupStyle>
                <GroupStyle>
                    <GroupStyle.HeaderTemplate>
                        <DataTemplate>
                            <!-- Coalition section header -->
                            <Border BorderBrush="{Binding Items[0].CoalitionColor}"
                                    BorderThickness="3,0,0,0"
                                    Padding="8,4">
                                <TextBlock>
                                    <Run Text="{Binding Name}" FontWeight="Bold" FontSize="12"/>
                                    <Run Text=" (" FontSize="11" Foreground="#aaa"/>
                                    <Run Text="{Binding ItemCount}" FontSize="11" Foreground="#aaa"/>
                                    <Run Text=")" FontSize="11" Foreground="#aaa"/>
                                </TextBlock>
                            </Border>
                        </DataTemplate>
                    </GroupStyle.HeaderTemplate>
                </GroupStyle>
            </ListView.GroupStyle>
            <ListView.ItemTemplate>
                <DataTemplate DataType="{x:Type homePages:PlayerListItem}">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="18"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="60"/>
                            <ColumnDefinition Width="70"/>
                        </Grid.ColumnDefinitions>
                        <!-- Transmit indicator -->
                        <Ellipse Grid.Column="0"
                                 Width="8" Height="8"
                                 Fill="#4CAF50"
                                 VerticalAlignment="Center">
                            <Ellipse.Style>
                                <Style TargetType="Ellipse">
                                    <Setter Property="Visibility" Value="Hidden"/>
                                    <Style.Triggers>
                                        <DataTrigger Binding="{Binding IsTransmitting}" Value="True">
                                            <Setter Property="Visibility" Value="Visible"/>
                                        </DataTrigger>
                                    </Style.Triggers>
                                </Style>
                            </Ellipse.Style>
                        </Ellipse>
                        <TextBlock Grid.Column="1" Text="{Binding Name}" VerticalAlignment="Center"/>
                        <TextBlock Grid.Column="2" Text="{Binding Role}"
                                   Foreground="#888" FontSize="10"
                                   VerticalAlignment="Center" HorizontalAlignment="Center"/>
                        <TextBlock Grid.Column="3" Text="{Binding FleetCode}"
                                   Foreground="#666" FontSize="10"
                                   VerticalAlignment="Center" HorizontalAlignment="Right"/>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </DockPanel>
</Page>
```

- [ ] **Step 6: Build and verify**

```
dotnet build DCS-SR-Client/DCS-SR-Client.csproj
```

Expected: no errors. Verify with a test server that the list populates and groups correctly.

- [ ] **Step 7: Commit**

```
git add DCS-SR-Common/Network/SRClient.cs \
        DCS-SR-Client/Network/VcsClientSyncHandler.cs \
        DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml \
        DCS-SR-Client/UI/ClientWindow/HomePages/PlayerListPage.xaml.cs
git commit -m "feat: client list with dynamic coalition grouping, transmit indicator, EventBus-driven updates"
```

---

## Task 6: Login flow progress stepper

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml`
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml`
- Modify: `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/LoginPages/GuestPage.xaml` + `.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/LoginPages/LoginPage.xaml` + `.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/LoginPages/UnitSelection.xaml` + `.cs`

- [ ] **Step 1: Add `ConnectionStep` enum to `MainWindow.xaml.cs`**

Add before the `MainWindow` class declaration:
```csharp
public enum ConnectionStep
{
    Connect,
    GuestLogin,
    MemberLogin,
    UnitSelection,
    Ready
}
```

- [ ] **Step 2: Add stepper header XAML to `MainWindow.xaml`**

Find the element that contains the `DisplayFrame` and add a `Border` above it. The stepper panel is hidden while `LoggedIn = true` (add a `DataTrigger` on `LoggedIn`):

```xaml
<!-- Connection progress stepper — hidden after login, shown during connection flow -->
<Border x:Name="StepperHeader"
        Background="#1e1e2e"
        BorderBrush="#333"
        BorderThickness="0,0,0,1"
        Padding="12,8">
    <StackPanel x:Name="StepperPanel" Orientation="Horizontal" HorizontalAlignment="Center" />
</Border>
```

- [ ] **Step 3: Add `NavigateToStep` and stepper update logic to `MainWindow.xaml.cs`**

Add a field for the kick/ban reason:
```csharp
private string _pendingKickReason = null;
```

Add the `NavigateToStep` method replacing `OpenPageByIndex` calls for connection flow pages:
```csharp
private void NavigateToStep(ConnectionStep step)
{
    // Update stepper header
    UpdateStepperHeader(step);

    switch (step)
    {
        case ConnectionStep.Connect:
            if (_pendingKickReason != null)
            {
                _welcomePage.ShowKickReason(_pendingKickReason);
                _pendingKickReason = null;
            }
            DisplayFrame.Content = _welcomePage;
            break;
        case ConnectionStep.GuestLogin:
            DisplayFrame.Content = _guestPage;
            break;
        case ConnectionStep.MemberLogin:
            DisplayFrame.Content = _loginPage;
            break;
        case ConnectionStep.UnitSelection:
            DisplayFrame.Content = _unitSelectionPage;
            break;
        case ConnectionStep.Ready:
            DisplayFrame.Content = _homePage;
            break;
    }
}

private void UpdateStepperHeader(ConnectionStep step)
{
    StepperPanel.Children.Clear();

    // Build step definitions for current path (determined by whether login or guest)
    var steps = step == ConnectionStep.GuestLogin || step == ConnectionStep.Connect
        ? new[] { ("Connect", ConnectionStep.Connect), ("Guest Login", ConnectionStep.GuestLogin), ("Ready", ConnectionStep.Ready) }
        : new[] { ("Connect", ConnectionStep.Connect), ("Login", ConnectionStep.MemberLogin), ("Select Unit", ConnectionStep.UnitSelection), ("Ready", ConnectionStep.Ready) };

    for (int i = 0; i < steps.Length; i++)
    {
        var (label, s) = steps[i];
        var isDone = s < step;
        var isCurrent = s == step;

        var dot = new Ellipse
        {
            Width = 22, Height = 22,
            Fill = isDone ? Brushes.Green : isCurrent ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromRgb(60, 60, 60)),
            Margin = new Thickness(0, 0, 4, 0),
        };
        var text = new TextBlock
        {
            Text = isDone ? "✓" : (i + 1).ToString(),
            Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 0) };
        circle.Children.Add(dot);
        circle.Children.Add(text);

        StepperPanel.Children.Add(circle);
        StepperPanel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = isCurrent ? Brushes.White : (isDone ? Brushes.Green : new SolidColorBrush(Color.FromRgb(100, 100, 100))),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        if (i < steps.Length - 1)
            StepperPanel.Children.Add(new TextBlock
            {
                Text = "—", Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
            });
    }
}
```

- [ ] **Step 4: Replace page navigation calls in `MainWindow.xaml.cs`**

Update the existing event handlers to use `NavigateToStep`:

```csharp
public void On_WelcomeLoginClicked()    => NavigateToStep(ConnectionStep.MemberLogin);
public void On_WelcomeGuestCLicked()   => NavigateToStep(ConnectionStep.GuestLogin);
public void On_GuestBackClicked()      { Stop(); NavigateToStep(ConnectionStep.Connect); }
public void On_LoginBackClicked()      { Stop(); NavigateToStep(ConnectionStep.Connect); }
public void On_UnitSelectionBackClicked() { Stop(); NavigateToStep(ConnectionStep.Connect); }
public void On_GuestSuccessAcceptClicked() => NavigateToStep(ConnectionStep.Ready);
```

In `HandleInitializationSuccess`:
```csharp
private void HandleInitializationSuccess(bool isVanguardLoginEnabled, bool isGuestLoginEnabled)
{
    _logger.Info("Initialization successful, setting up UI");
    ConnectionStatus.Fill = Brushes.Orange;
    _welcomePage.ConnectionSuccessful();
    _welcomePage.SetLoginEnabled(isVanguardLoginEnabled);
    _welcomePage.SetGuestEnabled(isGuestLoginEnabled);
    NavigateToStep(ConnectionStep.Connect);
}
```

In `HandleInternalLoginSuccessEvent`:
```csharp
_unitSelectionPage.SetSelectionData(internalLoginResult);
_playerName = internalLoginResult.PlayerName;
NavigateToStep(ConnectionStep.UnitSelection);
```

In `HandleConnectionSuccess` replace `OpenPageByIndex(OpenPage == GuestIndex ? GuestSuccessIndex : HomePageIndex)` with `NavigateToStep(ConnectionStep.Ready)`.

In `HandleForcedDisconnect`, replace the `MessageBox.Show` with:
```csharp
private void HandleForcedDisconnect(string reason)
{
    _pendingKickReason = reason;
    Stop(connectionError: true);
    NavigateToStep(ConnectionStep.Connect);
}
```

- [ ] **Step 5: Replace `MessageBox` error calls with inline errors**

Update `HandleConnectionError`:
```csharp
private void HandleConnectionError(object message, bool isGuest)
{
    var errorMsg = message as string ?? "An unknown connection error occurred.";
    _logger.Error($"Connection error: {errorMsg}");
    Stop(true);
    _welcomePage.ShowError(errorMsg);
    NavigateToStep(ConnectionStep.Connect);
}
```

Update `HandleInitializationError`:
```csharp
private void HandleInitializationError(object message)
{
    var errorMsg = message as string ?? "Initialization failed.";
    _logger.Error($"Initialization error: {errorMsg}");
    Stop(true);
    _welcomePage.ShowError(errorMsg);
    NavigateToStep(ConnectionStep.Connect);
}
```

Update `HandleGuestLoginError`:
```csharp
private void HandleGuestLoginError(object message)
{
    var errorMsg = message as string ?? "Guest login failed.";
    _logger.Error($"Guest login error: {errorMsg}");
    _guestPage.ShowError(errorMsg);
}
```

Update `HandleUnitSelectError`:
```csharp
private void HandleUnitSelectError(object message)
{
    var errorMsg = message as string ?? "Unit selection failed.";
    _logger.Error($"Unit selection error: {errorMsg}");
    _unitSelectionPage.ShowError(errorMsg);
}
```

- [ ] **Step 6: Add inline error panel + `ShowError` / `ShowKickReason` to `WelcomePage`**

In `WelcomePage.xaml`, add below the existing server address / Refresh area:
```xaml
<!-- Inline error panel -->
<Border x:Name="ErrorPanel"
        Background="#2a1a1a"
        BorderBrush="#ef4444"
        BorderThickness="1"
        CornerRadius="4"
        Padding="10,8"
        Margin="0,8,0,0"
        Visibility="Collapsed">
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
        <TextBlock Text="⚠ " Foreground="#ef4444" FontSize="13" VerticalAlignment="Center"/>
        <TextBlock x:Name="ErrorText" Foreground="#ef4444" FontSize="12"
                   TextWrapping="Wrap" VerticalAlignment="Center"/>
    </StackPanel>
</Border>
```

In `WelcomePage.xaml.cs`, add:
```csharp
public void ShowError(string message)
{
    Dispatcher.Invoke(() =>
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    });
}

public void ShowKickReason(string reason)
{
    Dispatcher.Invoke(() =>
    {
        var prefix = reason?.Contains("anned") == true ? "You are banned" : "You were kicked";
        ErrorText.Text = $"{prefix}: {reason}";
        ErrorPanel.Visibility = Visibility.Visible;
    });
}
```

Clear the error panel in `ConnectionReset()`:
```csharp
public void ConnectionReset()
{
    ErrorPanel.Visibility = Visibility.Collapsed;
    ServerInfoProgress.Visibility = Visibility.Visible;
    LoadLabel.Visibility = Visibility.Visible;
    Refresh.Visibility = Visibility.Visible;
}
```

- [ ] **Step 7: Add inline error panels to `GuestPage`, `LoginPage`, `UnitSelection`**

In each page's XAML, add the same error `Border` pattern (above the action buttons):
```xaml
<Border x:Name="ErrorPanel" Background="#2a1a1a" BorderBrush="#ef4444"
        BorderThickness="1" CornerRadius="4" Padding="10,8"
        Margin="0,8,0,0" Visibility="Collapsed">
    <TextBlock x:Name="ErrorText" Foreground="#ef4444" FontSize="12" TextWrapping="Wrap"/>
</Border>
```

In each page's code-behind, replace `LoginFailed()` / `SelectionFailed(msg)` with a `ShowError(string message)` method:
```csharp
public void ShowError(string message)
{
    Dispatcher.Invoke(() =>
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
        Login.IsEnabled = true; // or the relevant submit button
        Progress.Visibility = Visibility.Hidden;
    });
}
```

Also replace the `System.Windows.Forms.MessageBox.Show(...)` calls inside `GuestPage.Login_OnClick` with inline errors:
```csharp
// Replace the fleet code error MessageBox:
ErrorText.Text = $"Invalid Fleet Code '{FleetCodeInput.Text}': must be 2-4 uppercase letters.";
ErrorPanel.Visibility = Visibility.Visible;
LoginFailed(); // re-enable button

// Replace the password MessageBox:
ErrorText.Text = "Please enter a coalition password.";
ErrorPanel.Visibility = Visibility.Visible;
LoginFailed();
```

Clear error panels on each page in their submit button click handlers:
```csharp
ErrorPanel.Visibility = Visibility.Collapsed;
```

- [ ] **Step 8: Hide stepper header when logged in**

In `MainWindow.xaml.cs`, in `HandleConnectionSuccess` just before setting `LoggedIn = true`:
```csharp
StepperHeader.Visibility = Visibility.Collapsed;
```

In `Stop()`:
```csharp
StepperHeader.Visibility = Visibility.Visible;
NavigateToStep(ConnectionStep.Connect);
```

- [ ] **Step 9: Build**

```
dotnet build DCS-SR-Client/DCS-SR-Client.csproj
```

Fix any remaining references to the old `OpenPageByIndex` calls or page index constants that were not updated above.

- [ ] **Step 10: Commit**

```
git add DCS-SR-Client/UI/ClientWindow/MainWindow.xaml \
        DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs \
        DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml \
        DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs \
        DCS-SR-Client/UI/ClientWindow/LoginPages/GuestPage.xaml \
        DCS-SR-Client/UI/ClientWindow/LoginPages/GuestPage.xaml.cs \
        DCS-SR-Client/UI/ClientWindow/LoginPages/LoginPage.xaml \
        DCS-SR-Client/UI/ClientWindow/LoginPages/LoginPage.xaml.cs \
        DCS-SR-Client/UI/ClientWindow/LoginPages/UnitSelection.xaml \
        DCS-SR-Client/UI/ClientWindow/LoginPages/UnitSelection.xaml.cs
git commit -m "feat: replace page navigation with ConnectionStep stepper; inline errors; kick/ban reason on connect step"
```

---

## Self-Review Checklist

- **Spec §1 (Login flow):** Task 6 covers stepper, inline errors, kick/ban, server discovery UI wiring. ✓
- **Spec §2 (Kick/Ban):** Task 2 fixes the EventBus path; Task 6 shows reason on Connect step. ✓
- **Spec §3 (Mute banner):** Tasks 2 and 3 cover `IsServerMuted`, `ServerMuteChangedEvent`, and the banner. ✓
- **Spec §4 (VOX):** Task 4 adds hold/attack state machine and slider. ✓
- **Spec §5 (Sender names):** Task 1 fixes the `"---"` fallback and adds the `AlwaysShowTransmitterName` override. ✓
- **Spec §6 (Client list):** Task 5 reworks the list with dynamic coalition grouping, EventBus updates, transmit indicator. ✓
- **Server discovery (REST API, selectable):** The `WelcomePage` already calls `WebsiteClient.GetServerInformation()` — Task 6 wires its result into the Connect step UI. The XAML for the server selection dropdown/radio-button is not fully shown above; the implementer should add a `ComboBox` or two `RadioButton`s to `WelcomePage.xaml` binding to the fetched address and a custom text field. The connection logic already exists in `WelcomePage.Refresh_Click`. ✓ (pattern established)
