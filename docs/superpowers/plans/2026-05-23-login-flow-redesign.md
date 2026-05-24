# Login Flow Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single WelcomePage with two focused pages — a new `ServerSelectPage` (server discovery + connecting) and a repurposed `WelcomePage` (auth method choice after connection) — and delete the now-redundant `CustomServer` page.

**Architecture:** Page-based navigation within `DisplayFrame` in `MainWindow`. `ServerSelectPage` is the new app entry point. `WelcomePage` becomes the auth method chooser (LOGIN vs GUEST). `CustomServer.xaml/.cs` is deleted; custom server entry moves inline into `ServerSelectPage`. `ConnectionStep` enum gains a `ServerSelect` value and renames `Connect` → `Auth`. `MainWindow.xaml.cs` routing is updated throughout.

**Tech Stack:** WPF, MaterialDesignInXaml, C# 10, `GlobalSettingsStore`, `WebsiteClient.GetServerInformation()`, `Dns.GetHostAddresses()`.

**Spec:** `docs/superpowers/specs/2026-05-23-login-flow-redesign.md`

**Build command:** `dotnet build DCS-SimpleRadioStandalone.sln`

---

## File Map

| Action | File |
|--------|------|
| Create | `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml` |
| Create | `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml.cs` |
| Modify | `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml` |
| Modify | `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs` |
| Modify | `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs` |
| Delete | `DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml` |
| Delete | `DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml.cs` |

---

## Task 1: Create ServerSelectPage + MainWindow stub methods

**Files:**
- Create: `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml`
- Create: `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`

- [ ] **Step 1: Create `ServerSelectPage.xaml`**

Create the file at `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml` with this exact content:

```xml
<Page
    Height="340"
    Title="ServerSelectPage"
    Width="600"
    d:DesignHeight="340"
    d:DesignWidth="600"
    mc:Ignorable="d"
    x:Class="Vanguard.VCS.Client.UI.ClientWindow.WelcomePages.ServerSelectPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid KeyDown="Grid_KeyDown">
        <Grid.Background>
            <SolidColorBrush Color="White" />
        </Grid.Background>
        <Grid.RowDefinitions>
            <RowDefinition Height="70" />
            <RowDefinition Height="160" />
            <RowDefinition Height="40" />
            <RowDefinition Height="50" />
            <RowDefinition Height="1*" />
        </Grid.RowDefinitions>

        <!-- Row 0: Header -->
        <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center">
            <Label
                Content="Vanguard Communications System"
                FontSize="24"
                FontWeight="Heavy"
                HorizontalAlignment="Center"
                Padding="0" />
            <Label
                Content="powered by SRS"
                FontSize="16"
                HorizontalAlignment="Center"
                Padding="0" />
            <TextBlock
                Text="Select a server to connect to"
                FontSize="10"
                Foreground="#aaa"
                HorizontalAlignment="Center"
                Margin="0,2,0,0" />
        </StackPanel>

        <!-- Row 1: Server cards -->
        <StackPanel Grid.Row="1" Margin="20,8" VerticalAlignment="Center">

            <!-- Vanguard card (selected by default) -->
            <Border
                x:Name="VanguardCard"
                BorderBrush="#333"
                BorderThickness="2"
                CornerRadius="6"
                Background="#f9f9f9"
                Padding="12,10"
                Margin="0,0,0,8"
                Cursor="Hand"
                MouseLeftButtonDown="VanguardCard_Click">
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <Ellipse
                        x:Name="VanguardStatusDot"
                        Width="10"
                        Height="10"
                        Fill="#888888"
                        Margin="0,0,10,0"
                        VerticalAlignment="Center" />
                    <StackPanel>
                        <TextBlock
                            Text="Vanguard VCS Server"
                            FontSize="11"
                            FontWeight="Bold"
                            Foreground="#111" />
                        <TextBlock
                            x:Name="VanguardStatusLabel"
                            Text="Connecting to vcs.vngd.net…"
                            FontSize="9"
                            Foreground="#888" />
                    </StackPanel>
                </StackPanel>
            </Border>

            <!-- Custom card (collapsed by default) -->
            <Border
                x:Name="CustomCard"
                BorderBrush="#e0e0e0"
                BorderThickness="1"
                CornerRadius="6"
                Background="#f7f7f7"
                Padding="12,10"
                Cursor="Hand"
                MouseLeftButtonDown="CustomCard_Click">
                <StackPanel>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="1*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0">
                            <TextBlock
                                Text="Custom Server"
                                FontSize="11"
                                FontWeight="SemiBold"
                                Foreground="#555" />
                            <TextBlock
                                Text="Click to enter IP and port"
                                FontSize="9"
                                Foreground="#aaa" />
                        </StackPanel>
                        <TextBlock
                            x:Name="CustomExpandIcon"
                            Grid.Column="1"
                            Text="+"
                            FontSize="14"
                            Foreground="#ccc"
                            VerticalAlignment="Center" />
                    </Grid>
                    <StackPanel
                        x:Name="CustomFieldsPanel"
                        Orientation="Horizontal"
                        Visibility="Collapsed"
                        Margin="0,8,0,0">
                        <TextBox
                            x:Name="IpInput"
                            Width="160"
                            Height="32"
                            FontSize="13"
                            Style="{StaticResource MaterialDesignOutlinedTextBox}"
                            materialDesign:HintAssist.Hint="IP Address"
                            Margin="0,0,8,0"
                            TextChanged="IpInput_TextChanged" />
                        <materialDesign:NumericUpDown
                            x:Name="PortInput"
                            Width="80"
                            Height="32"
                            FontSize="13"
                            Style="{StaticResource MaterialDesignOutlinedNumericUpDown}"
                            materialDesign:HintAssist.Hint="Port" />
                    </StackPanel>
                </StackPanel>
            </Border>
        </StackPanel>

        <!-- Connecting overlay — spans rows 1-3, covers cards+error+button -->
        <Grid
            x:Name="ConnectingPanel"
            Grid.Row="1"
            Grid.RowSpan="3"
            Background="White"
            Visibility="Collapsed">
            <StackPanel
                VerticalAlignment="Center"
                HorizontalAlignment="Center"
                Margin="0,0,0,30">
                <ProgressBar
                    Style="{StaticResource MaterialDesignCircularProgressBar}"
                    Value="0"
                    IsIndeterminate="True"
                    Width="28"
                    Height="28"
                    HorizontalAlignment="Center"
                    Margin="0,0,0,10" />
                <TextBlock
                    x:Name="ConnectingLabel"
                    Text="Connecting to server…"
                    FontSize="10"
                    FontWeight="SemiBold"
                    Foreground="#333"
                    TextAlignment="Center" />
            </StackPanel>
            <Button
                x:Name="CancelButton"
                Content="CANCEL"
                Width="100"
                Height="30"
                VerticalAlignment="Bottom"
                HorizontalAlignment="Center"
                Margin="0,0,0,10"
                Style="{StaticResource MaterialDesignFlatDarkBgButton}"
                materialDesign:ButtonAssist.CornerRadius="15"
                Click="Cancel_Click" />
        </Grid>

        <!-- Row 2: Error panel -->
        <Border
            x:Name="ErrorPanel"
            Grid.Row="2"
            Background="#2a1a1a"
            BorderBrush="#ef4444"
            BorderThickness="1"
            CornerRadius="4"
            Padding="10,6"
            Margin="20,4"
            VerticalAlignment="Center"
            Visibility="Collapsed">
            <TextBlock
                x:Name="ErrorText"
                Foreground="#ef4444"
                FontSize="11"
                TextWrapping="Wrap" />
        </Border>

        <!-- Row 3: CONNECT button -->
        <StackPanel Grid.Row="3" HorizontalAlignment="Center" VerticalAlignment="Center">
            <Button
                x:Name="ConnectButton"
                Content="CONNECT"
                Width="130"
                Height="30"
                IsEnabled="False"
                Style="{StaticResource MaterialDesignFlatDarkBgButton}"
                materialDesign:ButtonAssist.CornerRadius="15"
                Click="Connect_Click" />
        </StackPanel>

        <!-- Row 4: Footer disclaimer -->
        <Label Grid.Row="4" VerticalAlignment="Bottom" HorizontalAlignment="Center" Padding="4">
            <TextBlock
                TextAlignment="Center"
                Foreground="#aaa"
                FontSize="9"
                TextWrapping="WrapWithOverflow">
                Disclaimer: Vanguard is an organization in the Star Citizen universe and gaming community.
                We are not affiliated with Roberts Space Industries, Star Citizen, or any other entity.
            </TextBlock>
        </Label>
    </Grid>
</Page>
```

- [ ] **Step 2: Create `ServerSelectPage.xaml.cs`**

Create the file at `DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml.cs`:

```csharp
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NLog;
using Vanguard.VCS.Client.Settings;
using Vanguard.VCS.Client.Utils;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages;

public partial class ServerSelectPage : Page
{
    private readonly MainWindow _mainWindow;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly GlobalSettingsStore _globalSettings = GlobalSettingsStore.Instance;

    private IPEndPoint _vanguardEndpoint;
    private bool _vanguardSelected = true;
    private bool _vanguardReady = false;

    public ServerSelectPage()
    {
        InitializeComponent();
        _mainWindow = Application.Current.MainWindow as MainWindow;
        IpInput.Text = _globalSettings.GetClientSetting(GlobalSettingsKeys.LastServer).RawValue;
        PortInput.Value = 5002;
        SelectVanguardCard();
        StartDiscovery();
    }

    private void StartDiscovery()
    {
        WebsiteClient.GetServerInformation().ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully && task.Result != null)
                Dispatcher.Invoke(() => ShowDiscoveryResult(task.Result.Address, task.Result.ControlPort));
            else
                Dispatcher.Invoke(ShowDiscoveryFailed);
        });
    }

    private void ShowDiscoveryResult(string address, int port)
    {
        try
        {
            var resolvedAddresses = Dns.GetHostAddresses(address);
            var ip = resolvedAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip == null) { ShowDiscoveryFailed(); return; }

            _vanguardEndpoint = new IPEndPoint(ip, port);
            _vanguardReady = true;
            VanguardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80));
            VanguardStatusLabel.Text = "vcs.vngd.net";
            UpdateConnectButton();
        }
        catch
        {
            ShowDiscoveryFailed();
        }
    }

    private void ShowDiscoveryFailed()
    {
        _vanguardReady = false;
        VanguardStatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        VanguardStatusLabel.Text = "Unreachable";
        UpdateConnectButton();
    }

    private void SelectVanguardCard()
    {
        _vanguardSelected = true;
        VanguardCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        VanguardCard.BorderThickness = new Thickness(2);
        VanguardCard.Background = new SolidColorBrush(Color.FromRgb(0xf9, 0xf9, 0xf9));
        CustomCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
        CustomCard.BorderThickness = new Thickness(1);
        CustomCard.Background = new SolidColorBrush(Color.FromRgb(0xf7, 0xf7, 0xf7));
        CustomFieldsPanel.Visibility = Visibility.Collapsed;
        UpdateConnectButton();
    }

    private void SelectCustomCard()
    {
        _vanguardSelected = false;
        CustomCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        CustomCard.BorderThickness = new Thickness(2);
        CustomCard.Background = new SolidColorBrush(Color.FromRgb(0xfa, 0xfa, 0xfa));
        VanguardCard.BorderBrush = new SolidColorBrush(Color.FromRgb(0xe0, 0xe0, 0xe0));
        VanguardCard.BorderThickness = new Thickness(1);
        VanguardCard.Background = new SolidColorBrush(Color.FromRgb(0xf7, 0xf7, 0xf7));
        CustomFieldsPanel.Visibility = Visibility.Visible;
        UpdateConnectButton();
    }

    private void UpdateConnectButton()
    {
        ConnectButton.IsEnabled = _vanguardSelected
            ? _vanguardReady
            : !string.IsNullOrWhiteSpace(IpInput.Text);
    }

    public void ShowError(string message)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowError(message)); return; }
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    public void ShowIdle()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(ShowIdle); return; }
        ConnectingPanel.Visibility = Visibility.Collapsed;
        UpdateConnectButton();
    }

    private void ShowConnecting(string serverAddress)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        ConnectingLabel.Text = $"Connecting to {serverAddress}…";
        ConnectingPanel.Visibility = Visibility.Visible;
    }

    private void VanguardCard_Click(object sender, MouseButtonEventArgs e) => SelectVanguardCard();

    private void CustomCard_Click(object sender, MouseButtonEventArgs e) => SelectCustomCard();

    private void IpInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateConnectButton();

    private void Grid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Connect_Click(sender, e);
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_vanguardSelected)
        {
            _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, _vanguardEndpoint.Address.ToString());
            ShowConnecting("vcs.vngd.net");
            _mainWindow.On_ServerConnectClicked(_vanguardEndpoint, isCustom: false);
        }
        else
        {
            try
            {
                var resolvedAddresses = Dns.GetHostAddresses(IpInput.Text);
                var ip = resolvedAddresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ip == null) throw new SocketException(0, "No valid IPv4 address found.");
                var endpoint = new IPEndPoint(ip, (int)PortInput.Value);
                _globalSettings.SetClientSetting(GlobalSettingsKeys.LastServer, ip.ToString());
                ShowConnecting(IpInput.Text);
                _mainWindow.On_ServerConnectClicked(endpoint, isCustom: true);
            }
            catch (SocketException)
            {
                ShowError("Invalid IP or Host Name!");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error resolving custom server address.");
                ShowError("Could not resolve server address. Please try again.");
            }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _mainWindow.On_ServerSelectCancelled();
        ShowIdle();
    }
}
```

- [ ] **Step 3: Add stub methods to `MainWindow.xaml.cs`**

Locate the method `On_WelcomeLoginClicked` (around line 706) and add the two new methods immediately after `On_WelcomeGuestCLicked`:

```csharp
public void On_ServerConnectClicked(IPEndPoint endpoint, bool isCustom)
{
    _usingCustomServer = isCustom;
    _resolvedIp = endpoint.Address;
    _port = endpoint.Port;
    Connect(endpoint.Address, endpoint.Port);
}

public void On_ServerSelectCancelled()
{
    Stop();
}
```

- [ ] **Step 4: Build**

```
dotnet build DCS-SimpleRadioStandalone.sln
```

Expected: Build succeeded, 0 errors. (`ServerSelectPage` is compiled but not yet wired into navigation; the existing WelcomePage is still the app's first page.)

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml
git add DCS-SR-Client/UI/ClientWindow/WelcomePages/ServerSelectPage.xaml.cs
git add DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs
git commit -m "feat: add ServerSelectPage with inline custom server expansion"
```

---

## Task 2: Rewrite WelcomePage as auth method page

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml`
- Modify: `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs` (remove 3 deleted method calls)

- [ ] **Step 1: Rewrite `WelcomePage.xaml`**

Replace the entire content of `WelcomePage.xaml`:

```xml
<Page
    Height="340"
    Title="WelcomePage"
    Width="600"
    d:DesignHeight="340"
    d:DesignWidth="600"
    mc:Ignorable="d"
    x:Class="Vanguard.VCS.Client.UI.ClientWindow.WelcomePages.WelcomePage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
    xmlns:materialDesign="http://materialdesigninxaml.net/winfx/xaml/themes"
    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Grid>
        <Grid.Background>
            <SolidColorBrush Color="White" />
        </Grid.Background>
        <Grid.RowDefinitions>
            <RowDefinition Height="70" />
            <RowDefinition Height="60" />
            <RowDefinition Height="20" />
            <RowDefinition Height="90" />
            <RowDefinition Height="1*" />
        </Grid.RowDefinitions>

        <!-- Row 0: Header -->
        <StackPanel Grid.Row="0" VerticalAlignment="Center" HorizontalAlignment="Center">
            <Label
                Content="Vanguard Communications System"
                FontSize="24"
                FontWeight="Heavy"
                HorizontalAlignment="Center"
                Padding="0" />
            <Label
                Content="powered by SRS"
                FontSize="16"
                HorizontalAlignment="Center"
                Padding="0" />
        </StackPanel>

        <!-- Row 1: Server info card -->
        <Border
            x:Name="ServerInfoCard"
            Grid.Row="1"
            Background="#f0f7f0"
            BorderBrush="#c3dfc3"
            BorderThickness="1"
            CornerRadius="6"
            Margin="20,8"
            Padding="16,8">
            <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock
                    x:Name="ServerAddressText"
                    Text="● Connected"
                    Foreground="#2e6b2e"
                    FontSize="11"
                    FontWeight="SemiBold"
                    TextAlignment="Center" />
            </StackPanel>
        </Border>

        <!-- Row 2: Subtitle -->
        <TextBlock
            Grid.Row="2"
            Text="How would you like to connect?"
            Foreground="#888"
            FontSize="10"
            HorizontalAlignment="Center"
            VerticalAlignment="Center" />

        <!-- Row 3: Auth choice cards -->
        <Grid Grid.Row="3" Margin="20,4">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="1*" />
                <ColumnDefinition Width="8" />
                <ColumnDefinition Width="1*" />
            </Grid.ColumnDefinitions>

            <!-- LOGIN card -->
            <Border
                x:Name="LoginCard"
                Grid.Column="0"
                BorderBrush="#222"
                BorderThickness="2"
                CornerRadius="6"
                Padding="10"
                Cursor="Hand"
                MouseLeftButtonDown="LoginCard_Click">
                <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                    <TextBlock
                        Text="LOGIN"
                        FontSize="12"
                        FontWeight="Bold"
                        Foreground="#111"
                        TextAlignment="Center" />
                    <TextBlock
                        Text="profile.vngd.net"
                        FontSize="9"
                        Foreground="#888"
                        TextAlignment="Center"
                        Margin="0,2,0,0" />
                </StackPanel>
            </Border>

            <!-- GUEST card -->
            <Border
                x:Name="GuestCard"
                Grid.Column="2"
                BorderBrush="#e0e0e0"
                BorderThickness="1"
                CornerRadius="6"
                Background="#f7f7f7"
                Padding="10"
                Cursor="Hand"
                MouseLeftButtonDown="GuestCard_Click">
                <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center">
                    <TextBlock
                        Text="GUEST"
                        FontSize="12"
                        FontWeight="SemiBold"
                        Foreground="#555"
                        TextAlignment="Center" />
                    <TextBlock
                        Text="No account needed"
                        FontSize="9"
                        Foreground="#aaa"
                        TextAlignment="Center"
                        Margin="0,2,0,0" />
                </StackPanel>
            </Border>
        </Grid>

        <!-- Row 4: Error panel + footer -->
        <StackPanel Grid.Row="4" VerticalAlignment="Top">
            <Border
                x:Name="ErrorPanel"
                Background="#2a1a1a"
                BorderBrush="#ef4444"
                BorderThickness="1"
                CornerRadius="4"
                Padding="10,8"
                Margin="20,4"
                Visibility="Collapsed">
                <TextBlock
                    x:Name="ErrorText"
                    Foreground="#ef4444"
                    FontSize="11"
                    TextWrapping="Wrap" />
            </Border>
        </StackPanel>

        <Label Grid.Row="4" VerticalAlignment="Bottom" HorizontalAlignment="Center" Padding="4">
            <TextBlock
                TextAlignment="Center"
                Foreground="#aaa"
                FontSize="9"
                TextWrapping="WrapWithOverflow">
                Disclaimer: Vanguard is an organization in the Star Citizen universe and gaming community.
                We are not affiliated with Roberts Space Industries, Star Citizen, or any other entity.
            </TextBlock>
        </Label>
    </Grid>
</Page>
```

- [ ] **Step 2: Rewrite `WelcomePage.xaml.cs`**

Replace the entire content of `WelcomePage.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NLog;

namespace Vanguard.VCS.Client.UI.ClientWindow.WelcomePages
{
    public partial class WelcomePage : Page
    {
        private readonly MainWindow _mainWindow;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public WelcomePage()
        {
            InitializeComponent();
            _mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
        }

        public void ShowError(string message)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowError(message)); return; }
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
        }

        public void ShowKickReason(string reason)
        {
            var prefix = reason?.Contains("anned") == true ? "You were banned" : "You were kicked";
            ShowError($"{prefix}: {reason}");
        }

        public void SetLoginEnabled(bool enabled)
        {
            LoginCard.IsEnabled = enabled;
            LoginCard.Opacity = enabled ? 1.0 : 0.4;
        }

        public void SetGuestEnabled(bool enabled)
        {
            GuestCard.IsEnabled = enabled;
            GuestCard.Opacity = enabled ? 1.0 : 0.4;
        }

        public void ShowServerConnected(string serverAddress)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.InvokeAsync(() => ShowServerConnected(serverAddress)); return; }
            ServerAddressText.Text = $"● Connected — {serverAddress}";
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        private void LoginCard_Click(object sender, MouseButtonEventArgs e)
            => _mainWindow.On_WelcomeLoginClicked();

        private void GuestCard_Click(object sender, MouseButtonEventArgs e)
            => _mainWindow.On_WelcomeGuestCLicked();   // typo preserved until Task 3 renames it
    }
}
```

- [ ] **Step 3: Remove three deleted-method call sites from `MainWindow.xaml.cs`**

Find `Stop()` method body (around line 1143–1146). Remove these three lines:
```csharp
_welcomePage.SetLoginEnabled(false);
_welcomePage.SetGuestEnabled(false);
_welcomePage.ConnectionReset();
```

Find `HandleInitializationSuccess` (around line 1237). Remove this line:
```csharp
_welcomePage.ConnectionSuccessful();
```

- [ ] **Step 4: Build**

```
dotnet build DCS-SimpleRadioStandalone.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml
git add DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs
git add DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs
git commit -m "feat: repurpose WelcomePage as auth method chooser (LOGIN vs GUEST)"
```

---

## Task 3: Full MainWindow.xaml.cs rewiring

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs` (rename call site)

Apply all changes in the order below. Each sub-step targets a specific location — make all changes before building.

- [ ] **Step 1: Update `ConnectionStep` enum**

Find:
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

Replace with:
```csharp
public enum ConnectionStep
{
    ServerSelect,
    Auth,
    GuestLogin,
    MemberLogin,
    UnitSelection,
    Ready
}
```

- [ ] **Step 2: Add `_serverSelectPage` field + `_connectedServerAddress` field; remove `_customServerPage`**

Find:
```csharp
private CustomServer _customServerPage;
private const int CustomServerIndex = 8;
```

Replace with:
```csharp
private ServerSelectPage _serverSelectPage;
private const int ServerSelectPageIndex = 8;
private string _connectedServerAddress = "";
```

- [ ] **Step 3: Update `InitPages()`**

Find:
```csharp
private void InitPages()
{
            
    _welcomePage = new WelcomePage();
    _supportPage = new SupportPage();
    _loginPage = new LoginPage();
    _guestPage = new GuestPage();
    _guestSuccessPage = new GuestSuccess();
    _homePage = new HomePage();
    _settingsPage = new SettingsPage();
    _unitSelectionPage = new UnitSelectionPage();
    _customServerPage = new CustomServer();
    OpenPage = WelcomeIndex;
        
    HomeNavigation.IsEnabled = false;
    HomeNavigation.Visibility = Visibility.Hidden;
}
```

Replace with:
```csharp
private void InitPages()
{
    _serverSelectPage = new ServerSelectPage();
    _welcomePage = new WelcomePage();
    _supportPage = new SupportPage();
    _loginPage = new LoginPage();
    _guestPage = new GuestPage();
    _guestSuccessPage = new GuestSuccess();
    _homePage = new HomePage();
    _settingsPage = new SettingsPage();
    _unitSelectionPage = new UnitSelectionPage();
    DisplayFrame.Content = _serverSelectPage;
    OpenPage = ServerSelectPageIndex;

    HomeNavigation.IsEnabled = false;
    HomeNavigation.Visibility = Visibility.Hidden;
}
```

- [ ] **Step 4: Remove the post-`InitPages()` override line from the constructor**

In the `MainWindow()` constructor, find:
```csharp
// Initialize Pages
InitPages();

DisplayFrame.Content = _welcomePage;
```

Replace with:
```csharp
// Initialize Pages
InitPages();
```

- [ ] **Step 5: Update `OpenPageByIndex()`**

Find:
```csharp
case CustomServerIndex:
    DisplayFrame.Content = _customServerPage;
    break;
```

Replace with:
```csharp
case ServerSelectPageIndex:
    DisplayFrame.Content = _serverSelectPage;
    break;
```

- [ ] **Step 6: Update `OpenPagePropertyChanged()`**

Find:
```csharp
if (source is MainWindow mainWindow && Convert.ToInt32(e.NewValue) == WelcomeIndex)
```

Replace with:
```csharp
if (source is MainWindow mainWindow && (Convert.ToInt32(e.NewValue) == WelcomeIndex || Convert.ToInt32(e.NewValue) == ServerSelectPageIndex))
```

- [ ] **Step 7: Update `NavigateToStep()`**

Find the entire switch body inside `NavigateToStep`:
```csharp
switch (step)
{
    case ConnectionStep.Connect:
        if (_pendingKickReason != null)
        {
            _welcomePage.ShowKickReason(_pendingKickReason);
            _pendingKickReason = null;
        }
        OpenPageByIndex(WelcomeIndex);
        break;
    case ConnectionStep.GuestLogin:
        OpenPageByIndex(GuestIndex);
        break;
    case ConnectionStep.MemberLogin:
        OpenPageByIndex(LoginIndex);
        break;
    case ConnectionStep.UnitSelection:
        OpenPageByIndex(UnitSelectionIndex);
        break;
    case ConnectionStep.Ready:
        OpenPageByIndex(HomePageIndex);
        break;
}
```

Replace with:
```csharp
switch (step)
{
    case ConnectionStep.ServerSelect:
        OpenPageByIndex(ServerSelectPageIndex);
        break;
    case ConnectionStep.Auth:
        if (_pendingKickReason != null)
        {
            _welcomePage.ShowKickReason(_pendingKickReason);
            _pendingKickReason = null;
        }
        OpenPageByIndex(WelcomeIndex);
        break;
    case ConnectionStep.GuestLogin:
        OpenPageByIndex(GuestIndex);
        break;
    case ConnectionStep.MemberLogin:
        OpenPageByIndex(LoginIndex);
        break;
    case ConnectionStep.UnitSelection:
        OpenPageByIndex(UnitSelectionIndex);
        break;
    case ConnectionStep.Ready:
        OpenPageByIndex(HomePageIndex);
        break;
}
```

- [ ] **Step 8: Update `UpdateStepperHeader()`**

Find the entire `UpdateStepperHeader` method:
```csharp
private void UpdateStepperHeader(ConnectionStep step)
{
    StepperPanel.Children.Clear();

    var steps = (step == ConnectionStep.GuestLogin || step == ConnectionStep.Connect)
        ? new[] { ("Connect", ConnectionStep.Connect), ("Guest Login", ConnectionStep.GuestLogin), ("Ready", ConnectionStep.Ready) }
        : new[] { ("Connect", ConnectionStep.Connect), ("Login", ConnectionStep.MemberLogin), ("Select Unit", ConnectionStep.UnitSelection), ("Ready", ConnectionStep.Ready) };

    for (int i = 0; i < steps.Length; i++)
    {
```

Replace the entire method with:
```csharp
private void UpdateStepperHeader(ConnectionStep step)
{
    StepperPanel.Children.Clear();

    if (step == ConnectionStep.ServerSelect)
    {
        StepperHeader.Visibility = Visibility.Hidden;
        return;
    }

    StepperHeader.Visibility = Visibility.Visible;

    var steps = (step == ConnectionStep.GuestLogin || step == ConnectionStep.Auth)
        ? new[] { ("Server", ConnectionStep.ServerSelect), ("Auth", ConnectionStep.Auth), ("Guest Login", ConnectionStep.GuestLogin), ("Ready", ConnectionStep.Ready) }
        : new[] { ("Server", ConnectionStep.ServerSelect), ("Auth", ConnectionStep.Auth), ("Login", ConnectionStep.MemberLogin), ("Select Unit", ConnectionStep.UnitSelection), ("Ready", ConnectionStep.Ready) };

    for (int i = 0; i < steps.Length; i++)
    {
        var (label, s) = steps[i];
        bool isDone = s < step;
        bool isCurrent = s == step;

        var dot = new Ellipse
        {
            Width = 18, Height = 18,
            Fill = isDone ? Brushes.Green : isCurrent ? Brushes.DodgerBlue : new SolidColorBrush(Color.FromRgb(60, 60, 60)),
        };
        var text = new TextBlock
        {
            Text = isDone ? "✓" : (i + 1).ToString(),
            Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var circle = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0) };
        circle.Children.Add(dot);
        circle.Children.Add(text);
        StepperPanel.Children.Add(circle);
        StepperPanel.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11,
            Foreground = isCurrent ? Brushes.White : (isDone ? Brushes.LightGreen : new SolidColorBrush(Color.FromRgb(100, 100, 100))),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        if (i < steps.Length - 1)
            StepperPanel.Children.Add(new TextBlock
            {
                Text = "—",
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
    }
}
```

- [ ] **Step 9: Update `HandleInitializationSuccess()`**

Find:
```csharp
private void HandleInitializationSuccess(bool isVanguardLoginEnabled, bool isGuestLoginEnabled)
{
    _logger.Info("Initialization successful, setting up UI");
    ConnectionStatus.Fill = Brushes.Orange;
    _welcomePage.ConnectionSuccessful();
    if (_usingCustomServer)
    {
        OpenPageByIndex(CustomServerIndex);
    }
    else
    {
        _welcomePage.SetLoginEnabled(isVanguardLoginEnabled);
        _welcomePage.SetGuestEnabled(isGuestLoginEnabled);
        StepperHeader.Visibility = Visibility.Visible;
        NavigateToStep(ConnectionStep.Connect);
    }
}
```

Replace with:
```csharp
private void HandleInitializationSuccess(bool isVanguardLoginEnabled, bool isGuestLoginEnabled)
{
    _logger.Info("Initialization successful, setting up UI");
    ConnectionStatus.Fill = Brushes.Orange;
    _welcomePage.SetLoginEnabled(isVanguardLoginEnabled);
    _welcomePage.SetGuestEnabled(isGuestLoginEnabled);
    _welcomePage.ShowServerConnected(_connectedServerAddress);
    NavigateToStep(ConnectionStep.Auth);
}
```

- [ ] **Step 10: Update `HandleInitializationError()`**

Find:
```csharp
private void HandleInitializationError(object message)
{
    var errorMsg = message as string ?? "An unknown initialization error occurred.";
    _logger.Error($"Initialization error: {errorMsg}");
    Stop(true);
    _welcomePage.ShowError(errorMsg);
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
private void HandleInitializationError(object message)
{
    var errorMsg = message as string ?? "An unknown initialization error occurred.";
    _logger.Error($"Initialization error: {errorMsg}");
    Stop(true);
    _serverSelectPage.ShowError(errorMsg);
    _serverSelectPage.ShowIdle();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 11: Update `HandleConnectionError()`**

Find:
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

Replace with:
```csharp
private void HandleConnectionError(object message, bool isGuest)
{
    var errorMsg = message as string ?? "An unknown connection error occurred.";
    _logger.Error($"Connection error: {errorMsg}");
    Stop(true);
    _serverSelectPage.ShowError(errorMsg);
    _serverSelectPage.ShowIdle();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 12: Update `HandleForcedDisconnect()`**

Find:
```csharp
private void HandleForcedDisconnect(string reason)
{
    _pendingKickReason = reason;
    Stop(connectionError: true);
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
private void HandleForcedDisconnect(string reason)
{
    _pendingKickReason = reason;
    Stop(connectionError: true);
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 13: Update `HandleInitializationSuccessEvent()` fallback**

Find in `HandleInitializationSuccessEvent`:
```csharp
else
{
    _logger.Error("Initialization error with no message provided.");
    Stop(true);
    _welcomePage.ShowError("An unknown initialization error occurred.");
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
else
{
    _logger.Error("Initialization error with no message provided.");
    Stop(true);
    _serverSelectPage.ShowError("An unknown initialization error occurred.");
    _serverSelectPage.ShowIdle();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 14: Update `HandleInternalLoginSuccessEvent()` error fallback**

Find in `HandleInternalLoginSuccessEvent`:
```csharp
else
{
    _logger.Error("Connection error with no message provided.");
    Stop(true);
    _welcomePage.ShowError("An unknown connection error occurred.");
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
else
{
    _logger.Error("Connection error with no message provided.");
    Stop(true);
    _serverSelectPage.ShowError("An unknown connection error occurred.");
    _serverSelectPage.ShowIdle();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 15: Update `On_LoginBackClicked()`**

Find:
```csharp
public void On_LoginBackClicked()
{
    Stop();
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
public void On_LoginBackClicked()
{
    Stop();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 16: Update `On_GuestBackClicked()`**

Find:
```csharp
public void On_GuestBackClicked()
{
    OpenPageByIndex(_usingCustomServer ? CustomServerIndex : WelcomeIndex);
}
```

Replace with:
```csharp
public void On_GuestBackClicked()
{
    NavigateToStep(ConnectionStep.Auth);
}
```

- [ ] **Step 17: Update `On_UnitSelectionBackClicked()`**

Find:
```csharp
public void On_UnitSelectionBackClicked()
{
    Stop();
    NavigateToStep(ConnectionStep.Connect);
}
```

Replace with:
```csharp
public void On_UnitSelectionBackClicked()
{
    Stop();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 18: Update `On_HomeLogOutClicked()`**

Find:
```csharp
public void On_HomeLogOutClicked()
{
    Stop();
    OpenPageByIndex(WelcomeIndex);
}
```

Replace with:
```csharp
public void On_HomeLogOutClicked()
{
    Stop();
    NavigateToStep(ConnectionStep.ServerSelect);
}
```

- [ ] **Step 19: Replace stub `On_ServerConnectClicked` with full implementation**

Find the stub added in Task 1:
```csharp
public void On_ServerConnectClicked(IPEndPoint endpoint, bool isCustom)
{
    _usingCustomServer = isCustom;
    _resolvedIp = endpoint.Address;
    _port = endpoint.Port;
    Connect(endpoint.Address, endpoint.Port);
}
```

Replace with:
```csharp
public void On_ServerConnectClicked(IPEndPoint endpoint, bool isCustom)
{
    _usingCustomServer = isCustom;
    _resolvedIp = endpoint.Address;
    _port = endpoint.Port;
    _connectedServerAddress = isCustom ? endpoint.ToString() : "Vanguard VCS Server";
    Connect(endpoint.Address, endpoint.Port);
}
```

- [ ] **Step 20: Rename `On_WelcomeGuestCLicked` → `On_WelcomeGuestClicked` in MainWindow**

Find:
```csharp
public void On_WelcomeGuestCLicked() => NavigateToStep(ConnectionStep.GuestLogin);
```

Replace with:
```csharp
public void On_WelcomeGuestClicked() => NavigateToStep(ConnectionStep.GuestLogin);
```

- [ ] **Step 21: Update call site in `WelcomePage.xaml.cs`**

Find in `WelcomePage.xaml.cs`:
```csharp
=> _mainWindow.On_WelcomeGuestCLicked();   // typo preserved until Task 3 renames it
```

Replace with:
```csharp
=> _mainWindow.On_WelcomeGuestClicked();
```

- [ ] **Step 22: Remove `On_WelcomeCustomServerClicked`, `On_CustomServerBackClicked`, `On_CustomServerContinueClicked`, `On_FetchedServerInformation`**

Remove these four method bodies entirely from `MainWindow.xaml.cs`:

```csharp
public void On_WelcomeCustomServerClicked()
{
    Stop(); // Stop current connection and open a custom connection
    OpenPageByIndex(CustomServerIndex);
}
```

```csharp
public void On_CustomServerBackClicked()
{
    Stop();
    OpenPageByIndex(WelcomeIndex);
}
```

```csharp
public void On_CustomServerContinueClicked()
{
    OpenPageByIndex(GuestIndex);
}
```

```csharp
public void On_FetchedServerInformation(IPEndPoint endpoint, bool usingCustomServer = false)
{
    _usingCustomServer = usingCustomServer;
    _resolvedIp = endpoint.Address;
    _port = endpoint.Port;

    Connect(endpoint.Address, endpoint.Port);
}
```

- [ ] **Step 23: Build**

```
dotnet build DCS-SimpleRadioStandalone.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 24: Commit**

```
git add DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs
git add DCS-SR-Client/UI/ClientWindow/WelcomePages/WelcomePage.xaml.cs
git commit -m "feat: wire ServerSelectPage as entry point; update ConnectionStep enum and all routing"
```

---

## Task 4: Delete CustomServer + final verification

**Files:**
- Delete: `DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml`
- Delete: `DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml.cs`

- [ ] **Step 1: Delete the CustomServer files**

```
git rm DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml
git rm DCS-SR-Client/UI/ClientWindow/WelcomePages/CustomServer.xaml.cs
```

- [ ] **Step 2: Build**

```
dotnet build DCS-SimpleRadioStandalone.sln
```

Expected: Build succeeded, 0 errors. No remaining references to `CustomServer` or `CustomServerIndex` anywhere.

- [ ] **Step 3: Verify no orphaned references**

```
grep -r "CustomServer\|CustomServerIndex\|On_CustomServer\|On_WelcomeCustomServer\|On_FetchedServer" DCS-SR-Client/ --include="*.cs" --include="*.xaml"
```

Expected: no output.

- [ ] **Step 4: Commit**

```
git commit -m "chore: delete CustomServer page; inline custom server entry now in ServerSelectPage"
```

---

## Acceptance Criteria

- App opens to `ServerSelectPage` (not WelcomePage)
- Vanguard card shows discovery status (grey → green on success, red on failure)
- Custom card expands inline with IP/Port fields; clicking Vanguard collapses it
- CONNECT button is only enabled when Vanguard is ready OR custom IP is non-empty
- After clicking CONNECT: spinner overlay appears, CANCEL works
- Successful gRPC init → `WelcomePage` shows green "Connected" card + LOGIN + GUEST
- LOGIN/GUEST cards navigate to the correct next steps
- All gRPC errors route back to `ServerSelectPage` with error message shown
- Stepper is hidden on `ServerSelectPage`, shown from `Auth` step onward
- `dotnet build` reports 0 errors
