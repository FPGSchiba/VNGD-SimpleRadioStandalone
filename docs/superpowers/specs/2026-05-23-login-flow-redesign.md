# Login Flow Redesign — ServerSelectPage + AuthMethodPage

**Goal:** Replace the single WelcomePage that conflated server discovery with auth-method choice into two focused pages: a new `ServerSelectPage` (server selection + connecting) and a repurposed `WelcomePage` (auth method choice shown only after a successful connection).

**Architecture:** Page-based navigation within `DisplayFrame` in `MainWindow`. Two new pages, one deleted page, updates to `ConnectionStep` enum and `MainWindow.xaml.cs` routing logic.

**Tech Stack:** WPF, MaterialDesignInXaml, C# 10, existing `GlobalSettingsStore`, `WebsiteClient.GetServerInformation()`, `MainWindow.On_FetchedServerInformation()` entry point.

---

## Navigation Flow

```
App Start → ServerSelectPage (auto-discovers Vanguard VCS)
               ↓ user selects server + clicks CONNECT
            [Connecting state — same page, spinner view]
               ↓ gRPC initialization success (HandleInitializationSuccess)
            WelcomePage (auth method — LOGIN vs GUEST)
               ↓ LOGIN                        ↓ GUEST
            LoginPage                        GuestPage
               ↓                               ↓
            UnitSelectionPage               HomePage
               ↓
            HomePage
```

Errors that break the gRPC connection → navigate back to `ServerSelect`.  
Errors that are auth-level (post-connection) → navigate back to `Auth` (WelcomePage).

---

## ConnectionStep Enum

```csharp
public enum ConnectionStep
{
    ServerSelect,   // new — ServerSelectPage
    Auth,           // was Connect — WelcomePage (auth method choice)
    GuestLogin,
    MemberLogin,
    UnitSelection,
    Ready
}
```

---

## Stepper

The stepper header (`StepperHeader`) is hidden during `ServerSelect` and shown from `Auth` onward (same as today — shown in `HandleInitializationSuccess`).

**Guest path:** ✓ Server · **Auth** · Guest Login · Ready  
**Member path:** ✓ Server · **Auth** · Login · Unit · Ready

When the stepper is first shown, `ServerSelect` is always already completed (✓).

---

## File: ServerSelectPage.xaml (NEW)

**Path:** `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml`

### Layout (white background, single-column centered)

| Row | Height | Content |
|-----|--------|---------|
| 0 | 70 | Header: "Vanguard Communications System" 24pt Heavy + "powered by SRS" 16pt + "Select a server to connect to" 10pt light |
| 1 | 160 | Server cards (Vanguard + Custom) |
| 2 | 40 | Error panel (collapsed by default) |
| 3 | 50 | CONNECT button area |
| 4 | 1* | Footer: disclaimer, 9pt, `#aaa` |

**Connecting state** replaces rows 1–3 with a centered spinner + server name label + CANCEL button. Achieved by toggling `Visibility` of a `ConnectingPanel` overlay on top of the cards.

### Vanguard VCS Card States

```
[ Discovering ]  grey left-border (3px), spinner dot, "Connecting to vcs.vngd.net…" 9pt grey
[ Ready       ]  green left-border (3px), green dot, "Vanguard VCS Server", "vcs.vngd.net" 9pt grey
[ Unreachable ]  red left-border (3px), red dot, "Vanguard VCS Server", "Unreachable" 9pt red
```

Card has a selection state (2px border on entire card, `#333`) vs unselected (1px `#e0e0e0`). Default selection: Vanguard card.

### Custom Server Card

**Collapsed** (default): 1-row card showing "Custom Server" label + "Click to enter IP and port" subtitle + `+` icon on right.  
**Expanded** (on click): same card grows to show IP `TextBox` (MaterialDesignOutlinedTextBox, hint "IP Address") and Port `NumericUpDown` (MaterialDesignOutlinedNumericUpDown, hint "Port", default 5002) side by side.

Clicking Vanguard card collapses Custom Server and vice versa. Selecting a card sets it as active (dark 2px border).

CONNECT enabled when:
- Vanguard selected AND state is Ready (server info fetched)
- Custom selected AND IP field is non-empty

### XAML Named Elements

```xml
x:Name="VanguardCard"          <!-- Border, the whole card -->
x:Name="VanguardStatusDot"     <!-- Ellipse, 10x10 -->
x:Name="VanguardStatusLabel"   <!-- TextBlock, subtitle -->
x:Name="CustomCard"            <!-- Border, the whole card -->
x:Name="CustomFieldsPanel"     <!-- StackPanel, hidden when collapsed -->
x:Name="IpInput"               <!-- TextBox -->
x:Name="PortInput"             <!-- materialDesign:NumericUpDown -->
x:Name="ConnectButton"         <!-- Button "CONNECT" -->
x:Name="ConnectingPanel"       <!-- Grid overlay, Visibility=Collapsed -->
x:Name="ConnectingLabel"       <!-- TextBlock "Connecting to [server]…" -->
x:Name="CancelButton"          <!-- Button "CANCEL" -->
x:Name="ErrorPanel"            <!-- Border, Background=#2a1a1a BorderBrush=#ef4444 -->
x:Name="ErrorText"             <!-- TextBlock, Foreground=#ef4444 -->
```

### C# Public Interface

```csharp
// Called by MainWindow after gRPC connection fails (re-enter idle state)
public void ShowError(string message)   // CheckAccess guard, shows ErrorPanel
public void ShowIdle()                  // re-shows cards, hides ConnectingPanel, re-enables inputs

// Internal state transitions
private void ShowDiscoveryResult(string address, int port)  // sets Vanguard card to Ready
private void ShowDiscoveryFailed()                          // sets Vanguard card to Unreachable
private void ShowConnecting(string serverAddress)           // switches to spinner view
```

### C# Key Behaviors

**`ServerSelectPage()`** constructor:
- Loads last server from `GlobalSettingsStore` (`GlobalSettingsKeys.LastServer`) into `IpInput.Text`
- Sets `PortInput.Value = 5002`
- Calls `StartDiscovery()` — fires off `WebsiteClient.GetServerInformation()` async; on success calls `ShowDiscoveryResult()`; on failure calls `ShowDiscoveryFailed()`

**`Connect_Click()`**:
- If Vanguard selected: uses the resolved endpoint from `StartDiscovery()` result
- If Custom selected: resolves `IpInput.Text` via `Dns.GetHostAddresses()`, uses `PortInput.Value`; shows `ErrorPanel` on `SocketException`
- Saves resolved IP to `GlobalSettingsKeys.LastServer`
- Calls `ShowConnecting(serverAddress)`
- Calls `_mainWindow.On_ServerConnectClicked(endpoint, isCustom: bool)`

**`Cancel_Click()`**: calls `_mainWindow.On_ServerSelectCancelled()`, then `ShowIdle()`

**Enter key** on IP/Port fields triggers `Connect_Click`.

---

## File: WelcomePage.xaml (REPURPOSED — Auth Method Page)

**Path:** `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml` (same file, new content)

### Layout (white background, single-column centered)

| Row | Height | Content |
|-----|--------|---------|
| 0 | 70 | Header: "Vanguard Communications System" 24pt Heavy + "powered by SRS" 16pt |
| 1 | 60 | Server info card (green bg `#f0f7f0`, border `#c3dfc3`, shows connected server address) |
| 2 | 10 | "How would you like to connect?" label, 10pt, `#888` |
| 3 | 80 | AUTH CHOICE: LOGIN card + GUEST card side-by-side (see below) |
| 4 | 1* | Error panel + footer disclaimer |

**Auth cards:**
- **LOGIN card**: `Border` with 2px `#222` border, full click area, "LOGIN" 14pt Bold + "profile.vngd.net" 9pt `#888` subtitle. `IsEnabled` controlled by `SetLoginEnabled()`.
- **GUEST card**: `Border` with 1px `#e0e0e0` border, background `#f7f7f7`, "GUEST" 14pt + "No account needed" 9pt `#aaa` subtitle. `IsEnabled` controlled by `SetGuestEnabled()`.

### XAML Named Elements (keep existing + add)

```xml
x:Name="ServerInfoCard"     <!-- Border, green, shows connected server -->
x:Name="ServerAddressText"  <!-- TextBlock inside ServerInfoCard -->
x:Name="LoginCard"          <!-- Border — replaces Login Button -->
x:Name="GuestCard"          <!-- Border — replaces Guest Button -->
x:Name="ErrorPanel"         <!-- existing, keep -->
x:Name="ErrorText"          <!-- existing, keep -->
```

### XAML Elements Removed

`ServerInfoProgress`, `LoadLabel`, `Refresh` button, `Server` (CustomServer) button, `EasterEgg` button, disclaimer `StackPanel` in Row 3 (move to Row 4), all `Grid.Row="3"` easter egg content.

### C# Public Interface — Kept

```csharp
public void ShowError(string message)          // CheckAccess guard — keep as-is
public void ShowKickReason(string reason)      // keep as-is
public void SetLoginEnabled(bool enabled)      // keep, now targets LoginCard.IsEnabled
public void SetGuestEnabled(bool enabled)      // keep, now targets GuestCard.IsEnabled
```

### C# Public Interface — New

```csharp
public void ShowServerConnected(string serverAddress)
// Sets ServerInfoCard content to "● Connected — {serverAddress}"
// Clears ErrorPanel
```

### C# Methods Removed

`ConnectionSuccessful()`, `ConnectionFailed()`, `ConnectionReset()`, `Refresh_Click()`, `Custom_Click()`, `EasterEgg_Click()`, `GetServerInformation()`, `ServerInformationFetched()`

### C# Click Handlers

```csharp
private void LoginCard_Click(object sender, MouseButtonEventArgs e)
    => _mainWindow.On_WelcomeLoginClicked();

private void GuestCard_Click(object sender, MouseButtonEventArgs e)
    => _mainWindow.On_WelcomeGuestClicked();
```

---

## File: CustomServer.xaml + CustomServer.xaml.cs (DELETED)

Both files removed. No other file references them except `MainWindow.xaml.cs`.

---

## File: MainWindow.xaml.cs (MODIFIED)

### Fields — Add / Remove

```csharp
// ADD
private ServerSelectPage _serverSelectPage;
private const int ServerSelectPageIndex = 8;   // reuses freed CustomServerIndex slot
private string _connectedServerAddress = "";   // stores resolved server address for AuthMethodPage

// REMOVE
private CustomServer _customServerPage;
private const int CustomServerIndex = 8;
```

### `InitPages()` changes

```csharp
// ADD
_serverSelectPage = new ServerSelectPage();

// REMOVE
_customServerPage = new CustomServer();

// CHANGE: initial page is ServerSelectPage
DisplayFrame.Content = _serverSelectPage;
OpenPage = ServerSelectPageIndex;
```

### `OpenPageByIndex()` changes

```csharp
// ADD
case ServerSelectPageIndex:
    DisplayFrame.Content = _serverSelectPage;
    break;

// REMOVE
case CustomServerIndex:
    DisplayFrame.Content = _customServerPage;
    break;
```

### `OpenPagePropertyChanged()` changes

```csharp
// CHANGE: hide HomeNavigation when on ServerSelectPage OR WelcomePage
if (Convert.ToInt32(e.NewValue) == ServerSelectPageIndex || Convert.ToInt32(e.NewValue) == WelcomeIndex)
{
    mainWindow.HomeNavigation.IsEnabled = false;
    mainWindow.HomeNavigation.Visibility = Visibility.Hidden;
}
```

### `NavigateToStep()` changes

```csharp
case ConnectionStep.ServerSelect:
    OpenPageByIndex(ServerSelectPageIndex);
    break;
case ConnectionStep.Auth:                           // was Connect
    if (_pendingKickReason != null)
    {
        _welcomePage.ShowKickReason(_pendingKickReason);
        _pendingKickReason = null;
    }
    OpenPageByIndex(WelcomeIndex);
    break;
// Remove: case ConnectionStep.Connect
```

### `UpdateStepperHeader()` changes

```csharp
// Guest path
var steps = (step == ConnectionStep.GuestLogin || step == ConnectionStep.Auth)
    ? new[] {
        ("Server",     ConnectionStep.ServerSelect),
        ("Auth",       ConnectionStep.Auth),
        ("Guest Login",ConnectionStep.GuestLogin),
        ("Ready",      ConnectionStep.Ready)
      }
    : new[] {
        ("Server",     ConnectionStep.ServerSelect),
        ("Auth",       ConnectionStep.Auth),
        ("Login",      ConnectionStep.MemberLogin),
        ("Select Unit",ConnectionStep.UnitSelection),
        ("Ready",      ConnectionStep.Ready)
      };
```

### `HandleInitializationSuccess()` changes

```csharp
// CHANGE: show auth method page, not connect page
_welcomePage.SetLoginEnabled(isVanguardLoginEnabled);
_welcomePage.SetGuestEnabled(isGuestLoginEnabled);
_welcomePage.ShowServerConnected(_connectedServerAddress);   // new call
StepperHeader.Visibility = Visibility.Visible;
NavigateToStep(ConnectionStep.Auth);                         // was Connect
```

### `HandleInitializationError()` changes

```csharp
// CHANGE: error goes back to server select, not auth
Stop(true);
_serverSelectPage.ShowError(errorMsg);
_serverSelectPage.ShowIdle();
NavigateToStep(ConnectionStep.ServerSelect);
```

### `HandleConnectionError()` changes

```csharp
// CHANGE: after Stop() the gRPC connection is dead, go back to ServerSelect
// so the user reconnects cleanly rather than seeing a stale "Connected" card
Stop(true);
_serverSelectPage.ShowError(errorMsg);
_serverSelectPage.ShowIdle();
NavigateToStep(ConnectionStep.ServerSelect);
```

### `HandleForcedDisconnect()` changes

```csharp
// CHANGE: kick/ban goes back to ServerSelect (connection is dead)
_pendingKickReason = reason;
Stop(connectionError: true);
NavigateToStep(ConnectionStep.ServerSelect);    // was Connect
```

`_pendingKickReason` is stored and shown when the user reconnects and `Auth` step is reached (existing mechanism in `NavigateToStep`). No change needed there.

### New Methods

```csharp
public void On_ServerConnectClicked(IPEndPoint endpoint, bool isCustom)
{
    _usingCustomServer = isCustom;
    _resolvedIp = endpoint.Address;
    _port = endpoint.Port;
    _connectedServerAddress = endpoint.ToString();
    Connect(endpoint.Address, endpoint.Port);
}

public void On_ServerSelectCancelled()
{
    Stop();
    // _serverSelectPage.ShowIdle() is called by the page itself after Cancel_Click
}
```

### Methods Removed

```csharp
On_WelcomeCustomServerClicked()
On_CustomServerBackClicked()
On_CustomServerContinueClicked()
```

### Methods Renamed (fix typo)

```csharp
On_WelcomeGuestCLicked() → On_WelcomeGuestClicked()
// Update call site in WelcomePage.xaml.cs accordingly
```

### `On_FetchedServerInformation()` — REMOVED

`On_ServerConnectClicked` is the direct replacement. `On_FetchedServerInformation` had two callers: `WelcomePage.xaml.cs` and `CustomServer.xaml.cs` — both are gone. Remove the method entirely.

---

## Color Consistency Pass

All new/modified pages use:
- Background: `Color="White"`
- Buttons: `MaterialDesignFlatDarkBgButton` with `CornerRadius="15"`
- Error panel: `Background="#2a1a1a"`, `BorderBrush="#ef4444"`, `Foreground="#ef4444"`
- Muted text: `#888` or `#aaa`
- Connected/success accent: `#f0f7f0` background, `#c3dfc3` border, `#2e6b2e` text
- Unreachable/failure accent: standard error panel colors above

No changes required to GuestPage, LoginPage, UnitSelectionPage, or PlayerListPage — all already consistent.
