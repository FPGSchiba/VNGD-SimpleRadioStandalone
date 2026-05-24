# Application Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove DCS integration and stale code, complete all gRPC client methods, and replace the multi-singleton state model with a single typed event bus and three injectable state stores.

**Architecture:** Four sequential workstreams (A → B → C → D). A removes dead code and introduces `RadioStateManager`. B is a code-review bug pass. C completes the five unimplemented gRPC methods. D introduces `IEventBus` (wrapping existing `Easy.MessageHub`), three state stores, and strips MainWindow from ~1,793 to ~700 lines.

**Tech Stack:** C# / .NET 8 / WPF, Grpc.Net.Client 2.71, Google.Protobuf 3.31, Easy.MessageHub 5.1.0, Newtonsoft.Json 13, MSTest 3.1, Moq 4.x (new dependency for client tests), NLog 5.4.

---

## File Map

### Created
| File | Responsibility |
|---|---|
| `DCS-SR-Client/Network/Models/ClientRadio.cs` | Immutable radio model records |
| `DCS-SR-Client/Network/RadioStateManager.cs` | File-based radio state, replaces DCSRadioSyncManager |
| `DCS-SR-Client/Network/IAuthServiceClient.cs` | Interface wrapping AuthService gRPC client for testability |
| `DCS-SR-Client/Network/ISrsServiceClient.cs` | Interface wrapping SRSService gRPC client for testability |
| `DCS-SR-Client/Events/IEventBus.cs` | Event bus interface |
| `DCS-SR-Client/Events/EventBus.cs` | Implementation wrapping Easy.MessageHub |
| `DCS-SR-Client/Events/Events.cs` | All typed event records |
| `DCS-SR-Client/Stores/ClientStateStore.cs` | Replaces server-state fields on ClientStateSingleton |
| `DCS-SR-Client/Stores/ConnectedClientsStore.cs` | Replaces ConnectedClientsSingleton |
| `DCS-SR-Client/Stores/ServerSettingsStore.cs` | Replaces SyncedServerSettings |
| `DCS-SR-ClientTests/DCS-SR-ClientTests.csproj` | New MSTest project for client logic |
| `DCS-SR-ClientTests/Network/RadioStateManagerTests.cs` | Tests for RadioStateManager |
| `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs` | Tests for gRPC methods |
| `DCS-SR-ClientTests/Events/EventBusTests.cs` | Tests for IEventBus |
| `DCS-SR-ClientTests/Stores/ClientStateStoreTests.cs` | Tests for ClientStateStore |
| `DCS-SR-ClientTests/Stores/ConnectedClientsStoreTests.cs` | Tests for ConnectedClientsStore |
| `DCS-SR-ClientTests/Stores/ServerSettingsStoreTests.cs` | Tests for ServerSettingsStore |

### Deleted
| File | Reason |
|---|---|
| `DCS-SR-Client/Network/DCS/DCSRadioSyncManager.cs` | DCS integration |
| `DCS-SR-Client/Network/DCS/DCSRadioSyncHandler.cs` | DCS integration |
| `DCS-SR-Client/Network/DCS/DCSAutoConnectHandler.cs` | DCS integration |
| `DCS-SR-Client/Network/DCS/Models/DCSLosCheckResult.cs` | DCS model |
| `DCS-SR-Client/Network/DCS/Models/DCSLosCheckRequest.cs` | DCS model |
| `DCS-SR-Client/Network/DCS/Models/CombinedRadioState.cs` | DCS model |
| `DCS-SR-Client/Network/SrsClientSyncHandler.cs` | Old .NET sync handler |

### Modified (major)
| File | Change summary |
|---|---|
| `DCS-SR-Client/Network/VcsClientSyncHandler.cs` | Add 5 missing gRPC methods, use interfaces, publish to event bus |
| `DCS-SR-Client/Singletons/ClientStateSingleton.cs` | Remove DCS fields |
| `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs` | Remove _srsClient, DCS wiring, VcsUiUpdate switch; add event bus subscriptions |
| `DCS-SR-Client/App.xaml.cs` | Register IEventBus, state stores, RadioStateManager |
| `DCS-SR-Client/DCS-SR-Client.csproj` | No DCS folder references remain |

---

## WORKSTREAM A — Stale Code & DCS Removal

---

### Task A1: Verify baseline build

**Files:** none changed

- [ ] **Step 1: Confirm build passes**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64 /p:Configuration=Debug
```

Expected: build succeeds with warnings, 0 errors. If errors exist, fix them before continuing.

- [ ] **Step 2: Commit baseline**

```
git add -A
git commit -m "chore: record baseline before application overhaul"
```

---

### Task A2: Create ClientRadio model

**Files:**
- Create: `DCS-SR-Client/Network/Models/ClientRadio.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Vanguard.VCS.Client.Network.Models
{
    public sealed record ClientRadio(string Name, double FrequencyHz, bool Enabled, bool IsIntercom);

    public sealed record ClientRadioState(System.Collections.Generic.IReadOnlyList<ClientRadio> Radios);
}
```

- [ ] **Step 2: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add DCS-SR-Client\Network\Models\ClientRadio.cs
git commit -m "feat: add ClientRadio and ClientRadioState models"
```

---

### Task A3: Create RadioStateManager

**Files:**
- Create: `DCS-SR-Client/Network/RadioStateManager.cs`

- [ ] **Step 1: Create the file**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Network
{
    public class RadioStateManager
    {
        public delegate void SendRadioUpdate();

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private const string RadioConfigFile = "radio-config.json";
        private const int UpdateIntervalSeconds = 60;

        private readonly SendRadioUpdate _radioUpdate;
        private volatile bool _stop;

        public ClientRadioState CurrentState { get; private set; }

        public RadioStateManager(SendRadioUpdate radioUpdate)
        {
            _radioUpdate = radioUpdate;
            CurrentState = LoadRadioConfig();
        }

        public void Start()
        {
            _stop = false;
            Task.Factory.StartNew(RunLoop, TaskCreationOptions.LongRunning);
        }

        public void Stop()
        {
            _stop = true;
        }

        private void RunLoop()
        {
            Logger.Info("RadioStateManager loop started");
            _radioUpdate();
            while (!_stop)
            {
                Thread.Sleep(UpdateIntervalSeconds * 1000);
                if (!_stop)
                    _radioUpdate();
            }
            Logger.Info("RadioStateManager loop stopped");
        }

        internal static ClientRadioState LoadRadioConfig()
        {
            try
            {
                if (File.Exists(RadioConfigFile))
                {
                    var json = File.ReadAllText(RadioConfigFile);
                    var radios = JsonConvert.DeserializeObject<List<ClientRadio>>(json);
                    if (radios != null && radios.Count > 0)
                    {
                        Logger.Info($"Loaded {radios.Count} radios from {RadioConfigFile}");
                        return new ClientRadioState(radios);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to load {RadioConfigFile}, using defaults");
            }
            return CreateDefaultState();
        }

        internal static ClientRadioState CreateDefaultState()
        {
            var radios = new List<ClientRadio>(11);
            for (int i = 0; i < 11; i++)
                radios.Add(new ClientRadio($"Radio {i + 1}", 1.0, false, false));
            return new ClientRadioState(radios);
        }
    }
}
```

- [ ] **Step 2: Add radio-config.json as content in csproj**

Open `DCS-SR-Client/DCS-SR-Client.csproj`. Inside the `<ItemGroup>` that contains the `awacs-radios.json` entry, add:

```xml
<None Include="radio-config.json" Condition="Exists('radio-config.json')">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 3: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add DCS-SR-Client\Network\RadioStateManager.cs DCS-SR-Client\DCS-SR-Client.csproj
git commit -m "feat: add RadioStateManager replacing DCSRadioSyncManager"
```

---

### Task A4: Create client test project

**Files:**
- Create: `DCS-SR-ClientTests/DCS-SR-ClientTests.csproj`
- Create: `DCS-SR-ClientTests/Network/RadioStateManagerTests.cs`

- [ ] **Step 1: Create project file**

Create `DCS-SR-ClientTests/DCS-SR-ClientTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>Vanguard.VCS.Client.Tests</RootNamespace>
    <AssemblyName>DCS-SR-ClientTests</AssemblyName>
    <IsPackable>false</IsPackable>
    <PlatformTarget>x64</PlatformTarget>
    <Platforms>x64</Platforms>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="MSTest.TestAdapter" Version="3.1.1" />
    <PackageReference Include="MSTest.TestFramework" Version="3.1.1" />
    <PackageReference Include="Moq" Version="4.20.72" />
    <PackageReference Include="NLog" Version="5.4.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Easy.MessageHub" Version="5.1.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DCS-SR-Client\DCS-SR-Client.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

```
dotnet sln DCS-SimpleRadioStandalone.sln add DCS-SR-ClientTests\DCS-SR-ClientTests.csproj
```

- [ ] **Step 3: Write RadioStateManager tests**

Create `DCS-SR-ClientTests/Network/RadioStateManagerTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Tests.Network
{
    [TestClass]
    public class RadioStateManagerTests
    {
        [TestMethod]
        public void LoadRadioConfig_FileAbsent_ReturnsElevenDefaultRadios()
        {
            var state = RadioStateManager.CreateDefaultState();
            Assert.AreEqual(11, state.Radios.Count);
        }

        [TestMethod]
        public void LoadRadioConfig_ValidFile_LoadsRadios()
        {
            var radios = new List<object>
            {
                new { name = "Primary", frequencyHz = 127500000.0, enabled = true, isIntercom = false },
                new { name = "Intercom", frequencyHz = 0.0, enabled = true, isIntercom = true }
            };
            var tmpFile = Path.GetTempFileName();
            File.WriteAllText(tmpFile, JsonConvert.SerializeObject(radios));

            // RadioStateManager.LoadRadioConfig reads from cwd; swap cwd temporarily
            var originalDir = Directory.GetCurrentDirectory();
            var tmpDir = Path.GetDirectoryName(tmpFile)!;
            File.Copy(tmpFile, Path.Combine(tmpDir, "radio-config.json"), overwrite: true);
            Directory.SetCurrentDirectory(tmpDir);
            try
            {
                var state = RadioStateManager.LoadRadioConfig();
                Assert.AreEqual(2, state.Radios.Count);
                Assert.AreEqual("Primary", state.Radios[0].Name);
                Assert.IsTrue(state.Radios[1].IsIntercom);
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
                File.Delete(tmpFile);
                File.Delete(Path.Combine(tmpDir, "radio-config.json"));
            }
        }

        [TestMethod]
        public void Start_CallsRadioUpdateImmediately()
        {
            var called = false;
            var manager = new RadioStateManager(() => called = true);
            manager.Start();
            System.Threading.Thread.Sleep(200);
            manager.Stop();
            Assert.IsTrue(called);
        }
    }
}
```

- [ ] **Step 4: Run tests (expect PASS — no implementation changes needed, logic already in)**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```
git add DCS-SR-ClientTests\
git commit -m "test: add client test project with RadioStateManager tests"
```

---

### Task A5: Remove DCS files

**Files:**
- Delete: `DCS-SR-Client/Network/DCS/DCSRadioSyncManager.cs`
- Delete: `DCS-SR-Client/Network/DCS/DCSRadioSyncHandler.cs`
- Delete: `DCS-SR-Client/Network/DCS/DCSAutoConnectHandler.cs`
- Delete: `DCS-SR-Client/Network/DCS/Models/DCSLosCheckResult.cs`
- Delete: `DCS-SR-Client/Network/DCS/Models/DCSLosCheckRequest.cs`
- Delete: `DCS-SR-Client/Network/DCS/Models/CombinedRadioState.cs`
- Delete: `DCS-SR-Client/Network/SrsClientSyncHandler.cs`

- [ ] **Step 1: Delete DCS directory and SrsClientSyncHandler**

```
git rm -r DCS-SR-Client\Network\DCS\
git rm DCS-SR-Client\Network\SrsClientSyncHandler.cs
```

- [ ] **Step 2: Attempt build to find all broken references**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error"
```

Record every file and line that fails. Do NOT fix anything yet — just record the errors.

- [ ] **Step 3: Commit deletion**

```
git commit -m "chore: delete DCS integration files and old SRS sync handler"
```

---

### Task A6: Remove DCS fields from ClientStateSingleton

**Files:**
- Modify: `DCS-SR-Client/Singletons/ClientStateSingleton.cs`

- [ ] **Step 1: Read the current file**

Open `DCS-SR-Client/Singletons/ClientStateSingleton.cs` and locate these members to remove:
- `DcsPlayerRadioInfo` property and its backing field / constructor init
- `PlayerCoaltionLocationMetadata` property and its backing field / constructor init
- `LastSent` property
- `IntercomOffset` property
- `DcsExportLastReceived` property
- `ExternalAWACSModelSelected` property
- `ExternalAWACSModeConnected` computed property and any `EamEnabled`, `IsGameExportConnected` it uses
- Any `using Vanguard.VCS.Common.DCSState;` that is now unused

Remove only those members. Leave everything else intact.

- [ ] **Step 2: Build and check remaining errors**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error"
```

- [ ] **Step 3: Commit**

```
git add DCS-SR-Client\Singletons\ClientStateSingleton.cs
git commit -m "refactor: remove DCS state fields from ClientStateSingleton"
```

---

### Task A7: Wire RadioStateManager into VcsClientSyncHandler

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`

- [ ] **Step 1: Replace `_radioDcsSync` field with `_radioStateManager`**

In `VcsClientSyncHandler.cs`:

Remove:
```csharp
private DCSRadioSyncManager _radioDcsSync = null;
```

Add:
```csharp
private RadioStateManager _radioStateManager;
```

- [ ] **Step 2: Update `InitializeConnection`**

Remove the line:
```csharp
_radioDcsSync = new DCSRadioSyncManager(UpdateRadioInformation, ClientCoalitionUpdate);
```

Add after `_clientStateSingleton.RegisterClientGuid(_clientGuid);`:
```csharp
_radioStateManager = new RadioStateManager(UpdateRadioInformation);
```

- [ ] **Step 3: Update `InitializeRadioSync`**

Replace:
```csharp
private void InitializeRadioSync()
{
    _radioDcsSync.Start();
    _radioDcsSync.StartExternalAWACSModeLoop();
    SyncClient();
}
```

With:
```csharp
private void InitializeRadioSync()
{
    _radioStateManager.Start();
    SyncClient();
}
```

- [ ] **Step 4: Remove `ClientCoalitionUpdate`**

Delete the entire `ClientCoalitionUpdate()` method.

- [ ] **Step 5: Update `Disconnect`**

Replace:
```csharp
try
{
    _radioDcsSync?.Stop();
}
catch (Exception ex)
{
    Logger.Warn(ex, "Error stopping DCS radio sync");
}
```

With:
```csharp
try
{
    _radioStateManager?.Stop();
}
catch (Exception ex)
{
    Logger.Warn(ex, "Error stopping radio state manager");
}
```

And in the null-clearing block at end of Disconnect:
```csharp
_radioStateManager = null;
```

- [ ] **Step 6: Update `GetRadioInfoFromState`**

Replace the entire method:
```csharp
private RadioInfo GetRadioInfoFromState()
{
    var radios = _radioStateManager.CurrentState.Radios
        .Select((radio, i) => new Radio
        {
            Id = (uint)i,
            Name = radio.Name,
            Frequency = (float)(radio.FrequencyHz / 1_000_000.0),
            Enabled = radio.Enabled,
            IsIntercom = radio.IsIntercom,
        })
        .ToList();

    Logger.Info($"Preparing radios for sync: {radios.Count} radios. Details: {string.Join(", ", radios.Select(r => $"{r.Name} ({r.Frequency} MHz)"))}");

    return new RadioInfo
    {
        Radios = { radios },
        Muted = false,
    };
}
```

- [ ] **Step 7: Remove unused `using` for DCS namespace**

Remove `using Vanguard.VCS.Client.Network.DCS;` if present.

- [ ] **Step 8: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error"
```

- [ ] **Step 9: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs
git commit -m "refactor: replace DCSRadioSyncManager with RadioStateManager in VcsClientSyncHandler"
```

---

### Task A8: Remove DCS and _srsClient from MainWindow

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`

- [ ] **Step 1: Remove _dcsAutoConnectListener field and its usages**

Remove field declaration:
```csharp
private DCSAutoConnectHandler _dcsAutoConnectListener;
```

Remove in `OnClosing`:
```csharp
_dcsAutoConnectListener?.Stop();
_dcsAutoConnectListener = null;
```

- [ ] **Step 2: Remove _srsClient field and all its usages**

Remove field declaration:
```csharp
private SrsClientSyncHandler _srsClient;
```

Remove from `Connect()`:
```csharp
_srsClient = new SrsClientSyncHandler(_guid, UpdateUiCallback, _hub);
```

Remove from `Stop()`:
```csharp
if (_srsClient != null)
{
    _srsClient.Disconnect();
    _srsClient = null;
}
```

Remove the entire `ConnectAwacsMode()` method.

Remove the entire `ExternalAwacsModeConnectionChanged()` method.

Remove the entire `UpdateUiCallback()` method.

- [ ] **Step 3: Remove DCS state writes throughout MainWindow**

Search for and remove all lines referencing:
- `ClientState.DcsPlayerRadioInfo`
- `ClientState.PlayerCoaltionLocationMetadata`
- `ClientState.ExternalAWACSModelSelected`
- `ClientState.LastSent`

In `Stop()`, remove:
```csharp
ClientState.DcsPlayerRadioInfo.Reset();
ClientState.PlayerCoaltionLocationMetadata.Reset();
```

In `HandleConnectionSuccess()`, remove:
```csharp
ClientState.ExternalAWACSModelSelected = true;
ClientState.PlayerCoaltionLocationMetadata.name = ClientState.LastSeenName;
ClientState.DcsPlayerRadioInfo.name = ClientState.LastSeenName;
```

In `OnHomeLogOutClicked()`, remove:
```csharp
ConnectAwacsMode();
```

In `OnClosing()`, remove:
```csharp
ConnectAwacsMode();
```

- [ ] **Step 4: Remove _hub field and IMessageHub usages tied to _srsClient**

The `_hub` field was only passed to `SrsClientSyncHandler` and `AudioManager`. Check if `AudioManager` still needs it. If not, remove:
```csharp
private IMessageHub _hub = new MessageHub();
```

And remove `_hub` from the `AudioManager` constructor call. If `AudioManager` signature requires it, keep the field — do not change AudioManager's constructor in this task.

- [ ] **Step 5: Remove unused `using` directives**

Remove:
```csharp
using Vanguard.VCS.Client.Network.DCS;
using Easy.MessageHub; // if _hub removed
```

- [ ] **Step 6: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error"
```

Fix any remaining errors (there may be references to removed DCS properties in other UI files — fix each by removing the reference).

- [ ] **Step 7: Commit**

```
git add DCS-SR-Client\UI\ClientWindow\MainWindow.xaml.cs
git commit -m "refactor: remove DCSAutoConnectHandler, SrsClientSyncHandler and all DCS state writes from MainWindow"
```

---

### Task A9: Fix remaining compile errors and clean AWACS UI

**Files:**
- Modify: any file with compile errors after A8

- [ ] **Step 1: List all remaining errors**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error CS"
```

- [ ] **Step 2: For each error, remove the offending reference**

Errors will be calls to removed methods/properties on `ClientStateSingleton` (DCS fields), `SrsClientSyncHandler`, or `DCSAutoConnectHandler`. Remove the calling code in each file. Do not add stubs — just delete the dead callers.

- [ ] **Step 3: Check AWACS overlay UI files**

The following files reference `ExternalAWACS` mode and may need cleanup:
- `DCS-SR-Client/UI/AwacsRadioOverlayWindow/AwacsRadioControlGroup*.xaml.cs` (4 files)
- `DCS-SR-Client/UI/ClientWindow/ServerSettingsWindow.xaml.cs`

For each: if the only references are to `ExternalAWACS` mode or `DCSRadioSyncManager`, either remove the broken lines or — if the whole file exists solely for AWACS — delete the file and its XAML.

- [ ] **Step 4: Full solution build**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
```

Expected: 0 errors.

- [ ] **Step 5: Run tests**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "refactor(A): complete DCS removal — solution builds clean"
```

---

## WORKSTREAM B — Bug Pass

---

### Task B1: Code review pass

**Files:** varies

- [ ] **Step 1: Dispatch code-reviewer agent**

Invoke the `code-reviewer` subagent on the following directories:
- `DCS-SR-Client/Network/`
- `DCS-SR-Client/Singletons/`
- `DCS-SR-Client/Audio/`

Focus: threading safety (shared state accessed from background threads without locking), swallowed exceptions (empty catch blocks), `.Wait()` / `.Result` blocking on async code, and resource disposal gaps (`IDisposable` not disposed).

- [ ] **Step 2: Fix CRITICAL and HIGH issues**

For each CRITICAL or HIGH issue found:
1. Write a minimal failing test that demonstrates the bug (where testable)
2. Fix the bug
3. Verify the test passes
4. Commit with message `fix: <issue description>`

- [ ] **Step 3: Fix MEDIUM issues**

For each MEDIUM issue: fix inline, commit grouped by file.

- [ ] **Step 4: Full build + test**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
```

Expected: 0 errors, all tests pass.

---

## WORKSTREAM C — Finish gRPC Implementation

---

### Task C1: Create gRPC service interfaces

**Files:**
- Create: `DCS-SR-Client/Network/IAuthServiceClient.cs`
- Create: `DCS-SR-Client/Network/ISrsServiceClient.cs`
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`

- [ ] **Step 1: Create IAuthServiceClient**

```csharp
using Grpc.Core;

namespace Vanguard.VCS.Client.Network
{
    public interface IAuthServiceClient
    {
        AuthInitResponse InitAuth(AuthInitRequest request, CallOptions options);
        FlowDiscoveryResponse DiscoverAuthenticationFlows(FlowDiscoveryRequest request, CallOptions options);
        GuestLoginResponse GuestLogin(GuestLoginRequest request, CallOptions options);
        AuthStepResponse StartAuth(StartAuthRequest request, CallOptions options);
        AuthStepResponse ContinueAuth(ContinueAuthRequest request, CallOptions options);
        UnitSelectResponse UnitSelect(UnitSelectRequest request, CallOptions options);
    }
}
```

- [ ] **Step 2: Create ISrsServiceClient**

```csharp
using Grpc.Core;

namespace Vanguard.VCS.Client.Network
{
    public interface ISrsServiceClient
    {
        SyncResponse SyncClient(Empty request, CallOptions options);
        ServerResponse UpdateClientInfo(ClientInfo request, CallOptions options);
        ServerResponse UpdateRadioInfo(RadioInfo request, CallOptions options);
        ServerResponse Disconnect(Empty request, CallOptions options);
        ServerSettings GetServerSettings(Empty request, CallOptions options);
        Grpc.Core.AsyncServerStreamingCall<ServerUpdate> SubscribeToUpdates(Empty request, CallOptions options);
    }
}
```

- [ ] **Step 3: Create concrete adapters that wrap the generated gRPC clients**

Add to `IAuthServiceClient.cs` after the interface:

```csharp
public sealed class AuthServiceClientAdapter : IAuthServiceClient
{
    private readonly AuthService.AuthServiceClient _inner;
    public AuthServiceClientAdapter(AuthService.AuthServiceClient inner) => _inner = inner;
    public AuthInitResponse InitAuth(AuthInitRequest r, CallOptions o) => _inner.InitAuth(r, o);
    public FlowDiscoveryResponse DiscoverAuthenticationFlows(FlowDiscoveryRequest r, CallOptions o) => _inner.DiscoverAuthenticationFlows(r, o);
    public GuestLoginResponse GuestLogin(GuestLoginRequest r, CallOptions o) => _inner.GuestLogin(r, o);
    public AuthStepResponse StartAuth(StartAuthRequest r, CallOptions o) => _inner.StartAuth(r, o);
    public AuthStepResponse ContinueAuth(ContinueAuthRequest r, CallOptions o) => _inner.ContinueAuth(r, o);
    public UnitSelectResponse UnitSelect(UnitSelectRequest r, CallOptions o) => _inner.UnitSelect(r, o);
}
```

Add to `ISrsServiceClient.cs` after the interface:

```csharp
public sealed class SrsServiceClientAdapter : ISrsServiceClient
{
    private readonly SRSService.SRSServiceClient _inner;
    public SrsServiceClientAdapter(SRSService.SRSServiceClient inner) => _inner = inner;
    public SyncResponse SyncClient(Empty r, CallOptions o) => _inner.SyncClient(r, o);
    public ServerResponse UpdateClientInfo(ClientInfo r, CallOptions o) => _inner.UpdateClientInfo(r, o);
    public ServerResponse UpdateRadioInfo(RadioInfo r, CallOptions o) => _inner.UpdateRadioInfo(r, o);
    public ServerResponse Disconnect(Empty r, CallOptions o) => _inner.Disconnect(r, o);
    public ServerSettings GetServerSettings(Empty r, CallOptions o) => _inner.GetServerSettings(r, o);
    public Grpc.Core.AsyncServerStreamingCall<ServerUpdate> SubscribeToUpdates(Empty r, CallOptions o) => _inner.SubscribeToUpdates(r, o);
}
```

- [ ] **Step 4: Update VcsClientSyncHandler to use the interfaces**

Replace the two concrete client fields:
```csharp
private SRSService.SRSServiceClient _srsServiceClient;
private AuthService.AuthServiceClient _authServiceClient;
```

With:
```csharp
private ISrsServiceClient _srsServiceClient;
private IAuthServiceClient _authServiceClient;
```

In `ConnectVcs`, replace instantiation:
```csharp
_srsServiceClient = new SrsServiceClientAdapter(new SRSService.SRSServiceClient(_channel));
_authServiceClient = new AuthServiceClientAdapter(new AuthService.AuthServiceClient(_channel));
```

- [ ] **Step 5: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64
```

Expected: 0 errors.

- [ ] **Step 6: Commit**

```
git add DCS-SR-Client\Network\IAuthServiceClient.cs DCS-SR-Client\Network\ISrsServiceClient.cs DCS-SR-Client\Network\VcsClientSyncHandler.cs
git commit -m "refactor: extract gRPC service interfaces for testability"
```

---

### Task C2: Implement DiscoverAuthenticationFlows

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Write failing test**

Add to `VcsClientSyncHandlerTests.cs`. First, add the test class skeleton if it doesn't exist:

```csharp
using System;
using Grpc.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Tests.Network
{
    [TestClass]
    public class VcsClientSyncHandlerTests
    {
        private Mock<IAuthServiceClient> _authMock;
        private Mock<ISrsServiceClient> _srsMock;
        private VcsClientSyncHandler _handler;
        private VcsUiUpdateType? _lastUpdateType;

        [TestInitialize]
        public void Setup()
        {
            _authMock = new Mock<IAuthServiceClient>();
            _srsMock = new Mock<ISrsServiceClient>();
            _lastUpdateType = null;
            _handler = new VcsClientSyncHandler(
                (type, msg) => _lastUpdateType = type,
                _authMock.Object,
                _srsMock.Object);
        }
    }
}
```

Then add the test method:

```csharp
[TestMethod]
public void DiscoverFlows_Success_ReturnsFlows()
{
    var expected = new FlowDiscoveryResponse
    {
        Success = true,
        Result = new FlowDiscoveryResult
        {
            Flows = { new AuthFlowDefinition { FlowId = "vanguard_email_password", Description = "Email + Password" } }
        }
    };
    _authMock.Setup(a => a.DiscoverAuthenticationFlows(
        It.Is<FlowDiscoveryRequest>(r => r.AuthenticationPlugin == "profile-vanguard"),
        It.IsAny<CallOptions>()))
        .Returns(expected);

    var result = _handler.DiscoverAuthenticationFlows("profile-vanguard");

    Assert.IsNotNull(result);
    Assert.AreEqual(1, result.Flows.Count);
    Assert.AreEqual("vanguard_email_password", result.Flows[0].FlowId);
}

[TestMethod]
public void DiscoverFlows_Timeout_ReturnsNull_AndPublishesConnectionError()
{
    _authMock.Setup(a => a.DiscoverAuthenticationFlows(It.IsAny<FlowDiscoveryRequest>(), It.IsAny<CallOptions>()))
        .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

    var result = _handler.DiscoverAuthenticationFlows("profile-vanguard");

    Assert.IsNull(result);
    Assert.AreEqual(VcsUiUpdateType.ConnectionError, _lastUpdateType);
}
```

- [ ] **Step 2: Run test — expect FAIL (method not yet implemented)**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "DiscoverFlows"
```

Expected: compile error or test failure.

- [ ] **Step 3: Add constructor overload to VcsClientSyncHandler for injection**

Add a second constructor to `VcsClientSyncHandler`:

```csharp
internal VcsClientSyncHandler(
    UpdateUiCallback uiCallback,
    IAuthServiceClient authClient,
    ISrsServiceClient srsClient)
{
    _callback = uiCallback;
    _authServiceClient = authClient;
    _srsServiceClient = srsClient;
}
```

- [ ] **Step 4: Implement DiscoverAuthenticationFlows**

Add to `VcsClientSyncHandler`:

```csharp
public FlowDiscoveryResult DiscoverAuthenticationFlows(string pluginName)
{
    try
    {
        var response = _authServiceClient.DiscoverAuthenticationFlows(
            new FlowDiscoveryRequest { AuthenticationPlugin = pluginName },
            DefaultCallOptions(10));

        if (!response.Success)
        {
            Logger.Error("Flow discovery failed: {0}", response.ErrorMessage);
            _callback?.Invoke(VcsUiUpdateType.ConnectionError, response.ErrorMessage);
            return null;
        }
        return response.Result;
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        Logger.Error(ex, "Flow discovery timed out");
        _callback?.Invoke(VcsUiUpdateType.ConnectionError, "Flow discovery timed out.");
        return null;
    }
    catch (RpcException ex)
    {
        Logger.Error(ex, "gRPC error during flow discovery");
        _callback?.Invoke(VcsUiUpdateType.ConnectionError, ex.Status.Detail);
        return null;
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "DiscoverFlows"
```

- [ ] **Step 6: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs DCS-SR-ClientTests\Network\VcsClientSyncHandlerTests.cs
git commit -m "feat: implement DiscoverAuthenticationFlows with tests"
```

---

### Task C3: Implement ContinueAuth

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
[TestMethod]
public void ContinueAuth_Success_ReturnsLoginResult()
{
    var loginResult = new LoginResult { PlayerName = "Pilot1", Secret = "abc" };
    _authMock.Setup(a => a.ContinueAuth(
        It.Is<ContinueAuthRequest>(r => r.SessionId == "sess1"),
        It.IsAny<CallOptions>()))
        .Returns(new AuthStepResponse { Success = true, Complete = loginResult });

    var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string> { { "otp", "123456" } });

    Assert.IsNotNull(result);
    Assert.AreEqual(LoginResult.ResultOneofCase.Complete, result.ResultCase);
    // Ensure InternalLoginSuccess is fired if Complete
    // (tested separately in integration flow)
}

[TestMethod]
public void ContinueAuth_Timeout_PublishesLoginError()
{
    _authMock.Setup(a => a.ContinueAuth(It.IsAny<ContinueAuthRequest>(), It.IsAny<CallOptions>()))
        .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

    var result = _handler.ContinueAuth("sess1", new System.Collections.Generic.Dictionary<string, string>());

    Assert.IsNull(result);
    Assert.AreEqual(VcsUiUpdateType.InternalLoginError, _lastUpdateType);
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ContinueAuth"
```

- [ ] **Step 3: Implement ContinueAuth**

```csharp
public AuthStepResponse ContinueAuth(string sessionId, System.Collections.Generic.Dictionary<string, string> stepData)
{
    var request = new ContinueAuthRequest { SessionId = sessionId, ClientGuid = _clientGuid.ToString() };
    foreach (var kv in stepData)
        request.StepData[kv.Key] = kv.Value;

    try
    {
        var response = _authServiceClient.ContinueAuth(request, DefaultCallOptions(15));
        if (!response.Success)
        {
            _callback?.Invoke(VcsUiUpdateType.InternalLoginError, response.ErrorMessage);
            return null;
        }

        if (response.ResultCase == AuthStepResponse.ResultOneofCase.Complete)
        {
            _tempSecret = response.Complete.Secret;
            _clientStateSingleton.LastSeenName = response.Complete.PlayerName;
            _callback?.Invoke(VcsUiUpdateType.InternalLoginSuccess, new InternalLoginResult
            {
                AvailableCoalitions = response.Complete.AvailableCoalitions,
                AvailableUnits = response.Complete.AvailableUnits,
                AvailableRoles = response.Complete.AvailableRoles,
                PlayerName = response.Complete.PlayerName,
            });
        }
        return response;
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        Logger.Error(ex, "ContinueAuth timed out");
        _callback?.Invoke(VcsUiUpdateType.InternalLoginError, "Login timed out — server did not respond.");
        return null;
    }
    catch (RpcException ex)
    {
        Logger.Error(ex, "gRPC error during ContinueAuth");
        _callback?.Invoke(VcsUiUpdateType.InternalLoginError, ex.Status.Detail);
        return null;
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ContinueAuth"
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs DCS-SR-ClientTests\Network\VcsClientSyncHandlerTests.cs
git commit -m "feat: implement ContinueAuth with tests"
```

---

### Task C4: Implement UpdateClientInfo

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Write failing test**

```csharp
[TestMethod]
public void UpdateClientInfo_Success_LogsAndReturnsTrue()
{
    _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
        .Returns(new ServerResponse { Success = true });

    var result = _handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

    Assert.IsTrue(result);
}

[TestMethod]
public void UpdateClientInfo_Failure_ReturnsFalse()
{
    _srsMock.Setup(s => s.UpdateClientInfo(It.IsAny<ClientInfo>(), It.IsAny<CallOptions>()))
        .Returns(new ServerResponse { Success = false, ErrorMessage = "not authenticated" });

    var result = _handler.UpdateClientInfo("Pilot1", "Blue", "unit-42", 1);

    Assert.IsFalse(result);
}
```

- [ ] **Step 2: Run test — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "UpdateClientInfo"
```

- [ ] **Step 3: Implement UpdateClientInfo**

```csharp
public bool UpdateClientInfo(string name, string coalition, string unitId, uint roleId)
{
    var info = new ClientInfo
    {
        Name = name,
        Coalition = coalition,
        UnitId = unitId,
        RoleId = roleId,
    };
    try
    {
        var response = _srsServiceClient.UpdateClientInfo(info, AuthCallOptions(5));
        if (!response.Success)
        {
            Logger.Error("UpdateClientInfo failed: {0}", response.ErrorMessage);
            return false;
        }
        Logger.Info("Client info updated successfully.");
        return true;
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        Logger.Warn(ex, "UpdateClientInfo timed out");
        return false;
    }
    catch (RpcException ex)
    {
        Logger.Error(ex, "gRPC error during UpdateClientInfo");
        return false;
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "UpdateClientInfo"
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs DCS-SR-ClientTests\Network\VcsClientSyncHandlerTests.cs
git commit -m "feat: implement UpdateClientInfo with tests"
```

---

### Task C5: Implement GetServerSettings

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Add VcsUiUpdateType for settings**

In `VcsClientSyncHandler.cs`, `VcsUiUpdateType` enum already has `ClientSyncSuccess`. Add:
```csharp
ServerSettingsFetched,
ServerSettingsError,
```

- [ ] **Step 2: Write failing test**

```csharp
[TestMethod]
public void GetServerSettings_Success_PublishesServerSettingsFetched()
{
    var settings = new ServerSettings();
    settings.TestFrequencies.Add(121.5f);
    _srsMock.Setup(s => s.GetServerSettings(It.IsAny<Empty>(), It.IsAny<CallOptions>()))
        .Returns(settings);

    _handler.FetchServerSettings();

    Assert.AreEqual(VcsUiUpdateType.ServerSettingsFetched, _lastUpdateType);
}

[TestMethod]
public void GetServerSettings_Timeout_PublishesServerSettingsError()
{
    _srsMock.Setup(s => s.GetServerSettings(It.IsAny<Empty>(), It.IsAny<CallOptions>()))
        .Throws(new RpcException(new Status(StatusCode.DeadlineExceeded, "timeout")));

    _handler.FetchServerSettings();

    Assert.AreEqual(VcsUiUpdateType.ServerSettingsError, _lastUpdateType);
}
```

- [ ] **Step 3: Run test — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "GetServerSettings"
```

- [ ] **Step 4: Implement FetchServerSettings**

```csharp
public void FetchServerSettings()
{
    try
    {
        var settings = _srsServiceClient.GetServerSettings(new Empty(), AuthCallOptions(10));
        _serverSettings.DecodeVcsSettings(settings);
        _callback?.Invoke(VcsUiUpdateType.ServerSettingsFetched, settings);
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        Logger.Warn(ex, "GetServerSettings timed out");
        _callback?.Invoke(VcsUiUpdateType.ServerSettingsError, "Settings fetch timed out.");
    }
    catch (RpcException ex)
    {
        Logger.Error(ex, "gRPC error during GetServerSettings");
        _callback?.Invoke(VcsUiUpdateType.ServerSettingsError, ex.Status.Detail);
    }
}
```

Note: `SyncedServerSettings.DecodeVcsSettings(ServerSettings)` may need to be added to `SyncedServerSettings` — add it as a method that copies fields from the protobuf `ServerSettings` into the local settings model, mirroring the existing `DecodeVcs` method.

- [ ] **Step 5: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "GetServerSettings"
```

- [ ] **Step 6: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs DCS-SR-ClientTests\Network\VcsClientSyncHandlerTests.cs
git commit -m "feat: implement GetServerSettings (FetchServerSettings) with tests"
```

---

### Task C6: Implement SubscribeToUpdates

**Files:**
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`
- Modify: `DCS-SR-ClientTests/Network/VcsClientSyncHandlerTests.cs`

- [ ] **Step 1: Add CancellationTokenSource for stream lifecycle**

Add field to `VcsClientSyncHandler`:
```csharp
private CancellationTokenSource _streamCts;
```

In `Disconnect`, after stopping _radioStateManager, add:
```csharp
_streamCts?.Cancel();
_streamCts = null;
```

- [ ] **Step 2: Write tests for event translation**

```csharp
[TestMethod]
public void TranslateServerUpdate_ClientJoined_PublishesClientJoinedEvent()
{
    VcsUiUpdateType? receivedType = null;
    object receivedMsg = null;
    var handler = new VcsClientSyncHandler(
        (t, m) => { receivedType = t; receivedMsg = m; },
        _authMock.Object, _srsMock.Object);

    var update = new ServerUpdate
    {
        Type = ServerUpdate.Types.UpdateType.ClientJoined,
        ClientUpdate = new ClientUpdate
        {
            ClientGuid = "guid-1",
            ClientInfo = new ClientInfo { Name = "Pilot1", Coalition = "Blue" }
        }
    };

    handler.ProcessServerUpdate(update);

    Assert.AreEqual(VcsUiUpdateType.ClientSyncUpdate, receivedType);
}

[TestMethod]
public void TranslateServerUpdate_ServerAction_Kick_PublishesKickEvent()
{
    VcsUiUpdateType? receivedType = null;
    var handler = new VcsClientSyncHandler(
        (t, m) => receivedType = t,
        _authMock.Object, _srsMock.Object);

    var update = new ServerUpdate
    {
        Type = ServerUpdate.Types.UpdateType.ServerAction,
        ServerAction = new ServerAction
        {
            Type = ServerAction.Types.ActionType.Kick,
            TargetClientGuid = "guid-1",
            Reason = "rule violation"
        }
    };

    handler.ProcessServerUpdate(update);

    Assert.AreEqual(VcsUiUpdateType.ConnectionLost, receivedType);
}
```

- [ ] **Step 3: Run tests — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "TranslateServerUpdate"
```

- [ ] **Step 4: Implement ProcessServerUpdate and StartSubscription**

Add `ProcessServerUpdate` (internal for testability):

```csharp
internal void ProcessServerUpdate(ServerUpdate update)
{
    switch (update.Type)
    {
        case ServerUpdate.Types.UpdateType.ClientJoined:
        case ServerUpdate.Types.UpdateType.ClientLeft:
        case ServerUpdate.Types.UpdateType.ClientRadioUpdate:
        case ServerUpdate.Types.UpdateType.ClientInfoUpdate:
            _clients.ApplyUpdate(update.ClientUpdate, update.Type);
            _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, update.ClientUpdate);
            break;
        case ServerUpdate.Types.UpdateType.ServerSettingsChanged:
            _serverSettings.DecodeVcsSettings(update.SettingsUpdate);
            _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, update.SettingsUpdate);
            break;
        case ServerUpdate.Types.UpdateType.ServerAction:
            HandleServerAction(update.ServerAction);
            break;
        default:
            Logger.Warn($"Unhandled ServerUpdate type: {update.Type}");
            break;
    }
}

private void HandleServerAction(ServerAction action)
{
    switch (action.Type)
    {
        case ServerAction.Types.ActionType.Kick:
        case ServerAction.Types.ActionType.Ban:
            Logger.Warn($"Received {action.Type} from server: {action.Reason}");
            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, $"Kicked: {action.Reason}");
            break;
        case ServerAction.Types.ActionType.Mute:
        case ServerAction.Types.ActionType.Unmute:
            Logger.Info($"Received {action.Type} for client {action.TargetClientGuid}");
            break;
    }
}
```

Add `StartSubscription`:

```csharp
private void StartSubscription()
{
    _streamCts = new CancellationTokenSource();
    Task.Factory.StartNew(() => RunSubscriptionLoop(_streamCts.Token), TaskCreationOptions.LongRunning);
}

private void RunSubscriptionLoop(CancellationToken cancellationToken)
{
    var backoffSeconds = 1;
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var callOptions = new CallOptions(
                headers: _authenticationMetadata,
                cancellationToken: cancellationToken);

            using var call = _srsServiceClient.SubscribeToUpdates(new Empty(), callOptions);
            backoffSeconds = 1;
            Logger.Info("SubscribeToUpdates stream connected");

            var enumerator = call.ResponseStream.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                ProcessServerUpdate(enumerator.Current);
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Info("SubscribeToUpdates cancelled");
            return;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            return;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"SubscribeToUpdates stream dropped, reconnecting in {backoffSeconds}s");
            _callback?.Invoke(VcsUiUpdateType.ConnectionLost, null);
            Thread.Sleep(backoffSeconds * 1000);
            backoffSeconds = Math.Min(backoffSeconds * 2, 30);
        }
    }
}
```

Update `InitializeRadioSync` to start the subscription:

```csharp
private void InitializeRadioSync()
{
    _radioStateManager.Start();
    SyncClient();
    StartSubscription();
}
```

- [ ] **Step 5: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "TranslateServerUpdate"
```

- [ ] **Step 6: Full test run**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
```

- [ ] **Step 7: Commit**

```
git add DCS-SR-Client\Network\VcsClientSyncHandler.cs DCS-SR-ClientTests\Network\VcsClientSyncHandlerTests.cs
git commit -m "feat: implement SubscribeToUpdates with reconnect loop and event translation tests"
```

---

## WORKSTREAM D — Event Bus + State Stores + MainWindow Cleanup

---

### Task D1: Create IEventBus and EventBus implementation

**Files:**
- Create: `DCS-SR-Client/Events/IEventBus.cs`
- Create: `DCS-SR-Client/Events/EventBus.cs`
- Create: `DCS-SR-ClientTests/Events/EventBusTests.cs`

- [ ] **Step 1: Write failing tests**

Create `DCS-SR-ClientTests/Events/EventBusTests.cs`:

```csharp
using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;

namespace Vanguard.VCS.Client.Tests.Events
{
    [TestClass]
    public class EventBusTests
    {
        private IEventBus _bus;

        [TestInitialize]
        public void Setup() => _bus = new EventBus(new MessageHub());

        [TestMethod]
        public void Subscribe_Publish_HandlerReceivesMessage()
        {
            string received = null;
            _bus.Subscribe<string>(msg => received = msg);
            _bus.Publish("hello");
            Assert.AreEqual("hello", received);
        }

        [TestMethod]
        public void Subscribe_Dispose_HandlerNoLongerReceives()
        {
            string received = null;
            var subscription = _bus.Subscribe<string>(msg => received = msg);
            subscription.Dispose();
            _bus.Publish("hello");
            Assert.IsNull(received);
        }

        [TestMethod]
        public void Publish_NoSubscribers_DoesNotThrow()
        {
            _bus.Publish("orphan");
        }
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL (types not yet defined)**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "EventBusTests"
```

- [ ] **Step 3: Create IEventBus**

Create `DCS-SR-Client/Events/IEventBus.cs`:

```csharp
using System;

namespace Vanguard.VCS.Client.Events
{
    public interface IEventBus
    {
        void Publish<T>(T message) where T : class;
        IDisposable Subscribe<T>(Action<T> handler) where T : class;
    }
}
```

- [ ] **Step 4: Create EventBus**

Create `DCS-SR-Client/Events/EventBus.cs`:

```csharp
using System;
using Easy.MessageHub;

namespace Vanguard.VCS.Client.Events
{
    public sealed class EventBus : IEventBus
    {
        private readonly IMessageHub _hub;

        public EventBus(IMessageHub hub) => _hub = hub;

        public void Publish<T>(T message) where T : class => _hub.Publish(message);

        public IDisposable Subscribe<T>(Action<T> handler) where T : class
        {
            var token = _hub.Subscribe(handler);
            return new SubscriptionToken(() => _hub.Unsubscribe(token));
        }

        private sealed class SubscriptionToken : IDisposable
        {
            private readonly Action _unsubscribe;
            public SubscriptionToken(Action unsubscribe) => _unsubscribe = unsubscribe;
            public void Dispose() => _unsubscribe();
        }
    }
}
```

- [ ] **Step 5: Run tests — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "EventBusTests"
```

- [ ] **Step 6: Commit**

```
git add DCS-SR-Client\Events\ DCS-SR-ClientTests\Events\
git commit -m "feat: add IEventBus and EventBus wrapping Easy.MessageHub with tests"
```

---

### Task D2: Define all typed events

**Files:**
- Create: `DCS-SR-Client/Events/Events.cs`

- [ ] **Step 1: Create the file**

```csharp
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Events
{
    // Server-pushed events
    public sealed record ClientJoinedEvent(string Guid, ClientInfo Info, RadioInfo Radios);
    public sealed record ClientLeftEvent(string Guid);
    public sealed record ClientRadioUpdatedEvent(string Guid, RadioInfo Radios);
    public sealed record ClientInfoUpdatedEvent(string Guid, ClientInfo Info);
    public sealed record ServerSettingsChangedEvent(ServerSettings Settings);
    public sealed record ServerActionEvent(
        ServerAction.Types.ActionType Type,
        string TargetGuid,
        string Reason,
        long? DurationSeconds);
    public sealed record DistributionUpdatedEvent(System.Collections.Generic.IReadOnlyList<VoiceHostDetails> VoiceHosts);

    // Internal lifecycle events
    public sealed record ConnectionStateChangedEvent(ConnectionState State);
    public sealed record AuthenticationCompletedEvent(string PlayerName, string Coalition, string UnitId, VcsRole Role);
    public sealed record LocalRadioStateChangedEvent(ClientRadioState State);

    public enum ConnectionState { Connecting, Connected, Disconnected, Error }
}
```

- [ ] **Step 2: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64
```

- [ ] **Step 3: Commit**

```
git add DCS-SR-Client\Events\Events.cs
git commit -m "feat: define all typed event records"
```

---

### Task D3: Create ClientStateStore

**Files:**
- Create: `DCS-SR-Client/Stores/ClientStateStore.cs`
- Create: `DCS-SR-ClientTests/Stores/ClientStateStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ClientStateStoreTests
    {
        private IEventBus _bus;
        private ClientStateStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ClientStateStore(_bus);
        }

        [TestMethod]
        public void ConnectionStateChanged_Connected_IsConnectedTrue()
        {
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
            Assert.IsTrue(_store.IsConnected);
            Assert.AreEqual(ConnectionState.Connected, _store.ConnectionState);
        }

        [TestMethod]
        public void ConnectionStateChanged_Disconnected_IsConnectedFalse()
        {
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
            _bus.Publish(new ConnectionStateChangedEvent(ConnectionState.Disconnected));
            Assert.IsFalse(_store.IsConnected);
        }

        [TestMethod]
        public void AuthenticationCompleted_SetsPlayerInfo()
        {
            _bus.Publish(new AuthenticationCompletedEvent("Pilot1", "Blue", "unit-42", VcsRole.Member));
            Assert.AreEqual("Pilot1", _store.PlayerName);
            Assert.AreEqual("Blue", _store.Coalition);
            Assert.AreEqual("unit-42", _store.UnitId);
            Assert.AreEqual(VcsRole.Member, _store.Role);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ClientStateStoreTests"
```

- [ ] **Step 3: Implement ClientStateStore**

Create `DCS-SR-Client/Stores/ClientStateStore.cs`:

```csharp
using System;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Network;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ClientStateStore : IDisposable
    {
        private readonly IDisposable _connectionSub;
        private readonly IDisposable _authSub;

        public System.Guid ClientGuid { get; private set; }
        public string PlayerName { get; private set; } = string.Empty;
        public string Coalition { get; private set; } = string.Empty;
        public string UnitId { get; private set; } = string.Empty;
        public VcsRole Role { get; private set; } = VcsRole.Guest;
        public ConnectionState ConnectionState { get; private set; } = ConnectionState.Disconnected;
        public bool IsConnected => ConnectionState == ConnectionState.Connected;

        public ClientStateStore(IEventBus bus)
        {
            _connectionSub = bus.Subscribe<ConnectionStateChangedEvent>(e => ConnectionState = e.State);
            _authSub = bus.Subscribe<AuthenticationCompletedEvent>(e =>
            {
                PlayerName = e.PlayerName;
                Coalition = e.Coalition;
                UnitId = e.UnitId;
                Role = e.Role;
            });
        }

        public void Dispose()
        {
            _connectionSub.Dispose();
            _authSub.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ClientStateStoreTests"
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\Stores\ClientStateStore.cs DCS-SR-ClientTests\Stores\ClientStateStoreTests.cs
git commit -m "feat: add ClientStateStore with tests"
```

---

### Task D4: Create ConnectedClientsStore

**Files:**
- Create: `DCS-SR-Client/Stores/ConnectedClientsStore.cs`
- Create: `DCS-SR-ClientTests/Stores/ConnectedClientsStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ConnectedClientsStoreTests
    {
        private IEventBus _bus;
        private ConnectedClientsStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ConnectedClientsStore(_bus);
        }

        [TestMethod]
        public void ClientJoined_AddsClient()
        {
            var info = new ClientInfo { Name = "Pilot1", Coalition = "Blue" };
            var radios = new RadioInfo();
            _bus.Publish(new ClientJoinedEvent("guid-1", info, radios));
            Assert.AreEqual(1, _store.Clients.Count);
            Assert.IsTrue(_store.Clients.ContainsKey("guid-1"));
        }

        [TestMethod]
        public void ClientLeft_RemovesClient()
        {
            var info = new ClientInfo { Name = "Pilot1" };
            _bus.Publish(new ClientJoinedEvent("guid-1", info, new RadioInfo()));
            _bus.Publish(new ClientLeftEvent("guid-1"));
            Assert.AreEqual(0, _store.Clients.Count);
        }

        [TestMethod]
        public void ClientRadioUpdated_UpdatesRadios()
        {
            _bus.Publish(new ClientJoinedEvent("guid-1", new ClientInfo { Name = "Pilot1" }, new RadioInfo()));
            var newRadios = new RadioInfo { Muted = true };
            _bus.Publish(new ClientRadioUpdatedEvent("guid-1", newRadios));
            Assert.IsTrue(_store.Clients["guid-1"].Radios.Muted);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ConnectedClientsStoreTests"
```

- [ ] **Step 3: Implement ConnectedClientsStore**

Create `DCS-SR-Client/Stores/ConnectedClientsStore.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Vanguard.VCS.Client.Events;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ConnectedClientsStore : IDisposable
    {
        public sealed record ClientEntry(ClientInfo Info, RadioInfo Radios);

        private readonly ConcurrentDictionary<string, ClientEntry> _clients = new();
        private readonly IDisposable _joinSub;
        private readonly IDisposable _leftSub;
        private readonly IDisposable _radioSub;
        private readonly IDisposable _infoSub;

        public IReadOnlyDictionary<string, ClientEntry> Clients => _clients;

        public ConnectedClientsStore(IEventBus bus)
        {
            _joinSub = bus.Subscribe<ClientJoinedEvent>(e =>
                _clients[e.Guid] = new ClientEntry(e.Info, e.Radios));

            _leftSub = bus.Subscribe<ClientLeftEvent>(e =>
                _clients.TryRemove(e.Guid, out _));

            _radioSub = bus.Subscribe<ClientRadioUpdatedEvent>(e =>
            {
                if (_clients.TryGetValue(e.Guid, out var existing))
                    _clients[e.Guid] = existing with { Radios = e.Radios };
            });

            _infoSub = bus.Subscribe<ClientInfoUpdatedEvent>(e =>
            {
                if (_clients.TryGetValue(e.Guid, out var existing))
                    _clients[e.Guid] = existing with { Info = e.Info };
            });
        }

        public void Dispose()
        {
            _joinSub.Dispose();
            _leftSub.Dispose();
            _radioSub.Dispose();
            _infoSub.Dispose();
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ConnectedClientsStoreTests"
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\Stores\ConnectedClientsStore.cs DCS-SR-ClientTests\Stores\ConnectedClientsStoreTests.cs
git commit -m "feat: add ConnectedClientsStore with tests"
```

---

### Task D5: Create ServerSettingsStore

**Files:**
- Create: `DCS-SR-Client/Stores/ServerSettingsStore.cs`
- Create: `DCS-SR-ClientTests/Stores/ServerSettingsStoreTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using Easy.MessageHub;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Stores;

namespace Vanguard.VCS.Client.Tests.Stores
{
    [TestClass]
    public class ServerSettingsStoreTests
    {
        private IEventBus _bus;
        private ServerSettingsStore _store;

        [TestInitialize]
        public void Setup()
        {
            _bus = new EventBus(new MessageHub());
            _store = new ServerSettingsStore(_bus);
        }

        [TestMethod]
        public void ServerSettingsChanged_UpdatesTestFrequencies()
        {
            var settings = new ServerSettings();
            settings.TestFrequencies.Add(121.5f);
            settings.TestFrequencies.Add(243.0f);
            _bus.Publish(new ServerSettingsChangedEvent(settings));
            Assert.AreEqual(2, _store.TestFrequencies.Count);
            Assert.AreEqual(121.5f, _store.TestFrequencies[0]);
        }

        [TestMethod]
        public void ServerSettingsChanged_UpdatesMaxRadios()
        {
            var settings = new ServerSettings
            {
                GeneralSettings = new GeneralServerSettings { MaxRadiosPerClient = 5 }
            };
            _bus.Publish(new ServerSettingsChangedEvent(settings));
            Assert.AreEqual(5, _store.MaxRadiosPerClient);
        }
    }
}
```

- [ ] **Step 2: Run — expect FAIL**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ServerSettingsStoreTests"
```

- [ ] **Step 3: Implement ServerSettingsStore**

Create `DCS-SR-Client/Stores/ServerSettingsStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using Vanguard.VCS.Client.Events;

namespace Vanguard.VCS.Client.Stores
{
    public sealed class ServerSettingsStore : IDisposable
    {
        private readonly IDisposable _settingsSub;

        public IReadOnlyList<float> TestFrequencies { get; private set; } = System.Array.Empty<float>();
        public IReadOnlyList<float> GlobalFrequencies { get; private set; } = System.Array.Empty<float>();
        public IReadOnlyList<Coalition> Coalitions { get; private set; } = System.Array.Empty<Coalition>();
        public int MaxRadiosPerClient { get; private set; }

        public ServerSettingsStore(IEventBus bus)
        {
            _settingsSub = bus.Subscribe<ServerSettingsChangedEvent>(e => Apply(e.Settings));
        }

        private void Apply(ServerSettings s)
        {
            TestFrequencies = new List<float>(s.TestFrequencies).AsReadOnly();
            GlobalFrequencies = new List<float>(s.GlobalFrequencies).AsReadOnly();
            Coalitions = new List<Coalition>(s.Coalitions).AsReadOnly();
            MaxRadiosPerClient = s.GeneralSettings?.MaxRadiosPerClient ?? 0;
        }

        public void Dispose() => _settingsSub.Dispose();
    }
}
```

- [ ] **Step 4: Run — expect PASS**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64 --filter "ServerSettingsStoreTests"
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\Stores\ServerSettingsStore.cs DCS-SR-ClientTests\Stores\ServerSettingsStoreTests.cs
git commit -m "feat: add ServerSettingsStore with tests"
```

---

### Task D6: Wire event bus in App.xaml.cs and update VcsClientSyncHandler to publish events

**Files:**
- Modify: `DCS-SR-Client/App.xaml.cs`
- Modify: `DCS-SR-Client/Network/VcsClientSyncHandler.cs`

- [ ] **Step 1: Read App.xaml.cs**

Open `DCS-SR-Client/App.xaml.cs`. Locate the `OnStartup` or constructor.

- [ ] **Step 2: Register event bus and stores as application-level singletons**

Add to `App.xaml.cs`:

```csharp
using Easy.MessageHub;
using Vanguard.VCS.Client.Events;
using Vanguard.VCS.Client.Stores;

// Inside App class:
public static IEventBus EventBus { get; private set; }
public static ClientStateStore ClientStateStore { get; private set; }
public static ConnectedClientsStore ConnectedClientsStore { get; private set; }
public static ServerSettingsStore ServerSettingsStore { get; private set; }

protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);
    EventBus = new EventBus(new MessageHub());
    ClientStateStore = new ClientStateStore(EventBus);
    ConnectedClientsStore = new ConnectedClientsStore(EventBus);
    ServerSettingsStore = new ServerSettingsStore(EventBus);
}

protected override void OnExit(ExitEventArgs e)
{
    ClientStateStore?.Dispose();
    ConnectedClientsStore?.Dispose();
    ServerSettingsStore?.Dispose();
    base.OnExit(e);
}
```

- [ ] **Step 3: Update VcsClientSyncHandler to accept and use IEventBus**

Add IEventBus as a constructor parameter to the primary constructor:

```csharp
private readonly IEventBus _eventBus;

public VcsClientSyncHandler(UpdateUiCallback uiCallback, IEventBus eventBus)
{
    _callback = uiCallback;
    _eventBus = eventBus;
}
```

Update `ProcessServerUpdate` to publish typed events in addition to (or replacing) the callback:

```csharp
case ServerUpdate.Types.UpdateType.ClientJoined:
    _eventBus.Publish(new ClientJoinedEvent(
        update.ClientUpdate.ClientGuid,
        update.ClientUpdate.ClientInfo,
        update.ClientUpdate.RadioInfo));
    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
    break;

case ServerUpdate.Types.UpdateType.ClientLeft:
    _eventBus.Publish(new ClientLeftEvent(update.ClientUpdate.ClientGuid));
    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
    break;

case ServerUpdate.Types.UpdateType.ClientRadioUpdate:
    _eventBus.Publish(new ClientRadioUpdatedEvent(
        update.ClientUpdate.ClientGuid,
        update.ClientUpdate.RadioInfo));
    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
    break;

case ServerUpdate.Types.UpdateType.ClientInfoUpdate:
    _eventBus.Publish(new ClientInfoUpdatedEvent(
        update.ClientUpdate.ClientGuid,
        update.ClientUpdate.ClientInfo));
    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
    break;

case ServerUpdate.Types.UpdateType.ServerSettingsChanged:
    _eventBus.Publish(new ServerSettingsChangedEvent(update.SettingsUpdate));
    _callback?.Invoke(VcsUiUpdateType.ClientSyncUpdate, null);
    break;
```

Also publish `ConnectionStateChangedEvent` from `InitializeConnection` (on success/failure) and `Disconnect`:

In `InitializeConnection`, after `_callback?.Invoke(VcsUiUpdateType.InitializationSuccess, ...)`:
```csharp
_eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Connecting));
```

In `GuestLogin` and `SelectUnit`, after `_callback?.Invoke(VcsUiUpdateType.GuestLoginSuccess/InternalUnitSelectionSuccess, ...)`:
```csharp
_eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Connected));
```

In `Disconnect`, before the final `_callback?.Invoke(VcsUiUpdateType.ConnectionLost, ...)`:
```csharp
_eventBus?.Publish(new ConnectionStateChangedEvent(ConnectionState.Disconnected));
```

- [ ] **Step 4: Update MainWindow to pass IEventBus to VcsClientSyncHandler**

In `MainWindow.Connect()`:
```csharp
_vcsClient = new VcsClientSyncHandler(VcsUiUpdate, App.EventBus);
```

- [ ] **Step 5: Update RadioStateManager to publish via event bus in Workstream D**

In `RadioStateManager`, add `IEventBus` as optional constructor parameter:
```csharp
private readonly IEventBus _eventBus;

public RadioStateManager(SendRadioUpdate radioUpdate, IEventBus eventBus = null)
{
    _radioUpdate = radioUpdate;
    _eventBus = eventBus;
    CurrentState = LoadRadioConfig();
}
```

In `RunLoop`, after `_radioUpdate()`:
```csharp
_eventBus?.Publish(new LocalRadioStateChangedEvent(CurrentState));
```

Update `VcsClientSyncHandler` to pass `_eventBus` when constructing `RadioStateManager`:
```csharp
_radioStateManager = new RadioStateManager(UpdateRadioInformation, _eventBus);
```

- [ ] **Step 6: Build**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
```

- [ ] **Step 7: Commit**

```
git add DCS-SR-Client\
git commit -m "feat: wire IEventBus into App, VcsClientSyncHandler, and RadioStateManager"
```

---

### Task D7: Strip VcsUiUpdate switch from MainWindow — replace with event bus subscriptions

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`

- [ ] **Step 1: Subscribe to ConnectionStateChangedEvent in MainWindow constructor**

Add to `MainWindow` constructor (after `InitializeComponent()`):

```csharp
App.EventBus.Subscribe<ConnectionStateChangedEvent>(e =>
    Dispatcher.Invoke(() => HandleConnectionStateChanged(e.State)));

App.EventBus.Subscribe<ServerActionEvent>(e =>
{
    if (e.Type == ServerAction.Types.ActionType.Kick || e.Type == ServerAction.Types.ActionType.Ban)
        Dispatcher.Invoke(() => HandleForcedDisconnect(e.Reason));
});
```

Add `HandleConnectionStateChanged`:
```csharp
private void HandleConnectionStateChanged(ConnectionState state)
{
    ConnectionStatus.Fill = state switch
    {
        ConnectionState.Connected => Brushes.Green,
        ConnectionState.Connecting => Brushes.Orange,
        ConnectionState.Error => Brushes.Red,
        _ => Brushes.Red
    };
}

private void HandleForcedDisconnect(string reason)
{
    Stop(connectionError: true);
    MessageBox.Show($"Disconnected by server: {reason}", "Server Action", MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

- [ ] **Step 2: Remove VcsUiUpdate switch and all Handle* helper methods it called**

Delete:
- `VcsUiUpdate` method
- `HandleConnectionError`
- `HandleInitializationError`
- `HandleInitializationSuccessEvent`
- `HandleGuestLoginError`
- `HandleGuestLoginSuccess`
- `HandleInternalLoginSuccessEvent`
- `HandleUnitSelectError`
- `HandleUnitSelectSuccess`

Keep only the portions of their logic that are NOT now handled by event bus subscriptions and cannot be removed (e.g., page navigation in response to auth events). Move those remnants inline into the connection flow methods (`Connect`, `Login`, `SelectUnit`).

- [ ] **Step 3: Collapse OpenPagePropertyChanged**

`OpenPagePropertyChanged` has a switch with empty cases for everything except `WelcomeIndex`. Reduce to:

```csharp
private static void OpenPagePropertyChanged(DependencyObject source, DependencyPropertyChangedEventArgs e)
{
    if (source is MainWindow mainWindow && Convert.ToInt32(e.NewValue) == WelcomeIndex)
    {
        mainWindow.HomeNavigation.IsEnabled = false;
        mainWindow.HomeNavigation.Visibility = Visibility.Hidden;
    }
}
```

- [ ] **Step 4: Build**

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64
```

- [ ] **Step 5: Commit**

```
git add DCS-SR-Client\UI\ClientWindow\MainWindow.xaml.cs
git commit -m "refactor: replace VcsUiUpdate switch with event bus subscriptions in MainWindow"
```

---

### Task D8: Replace singleton data-bindings in MainWindow XAML with store properties

**Files:**
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml.cs`
- Modify: `DCS-SR-Client/UI/ClientWindow/MainWindow.xaml`

- [ ] **Step 1: Replace singleton property exposures on MainWindow**

In `MainWindow.xaml.cs`, the following properties expose singletons for XAML data binding:

```csharp
public ClientStateSingleton ClientState { get; } = ClientStateSingleton.Instance;
public ConnectedClientsSingleton Clients { get; } = ConnectedClientsSingleton.Instance;
```

Replace with store references:

```csharp
public ClientStateStore ClientState => App.ClientStateStore;
public ConnectedClientsStore Clients => App.ConnectedClientsStore;
```

- [ ] **Step 2: Fix XAML bindings broken by property rename**

Search `MainWindow.xaml` for bindings to properties that no longer exist on `ClientStateStore` (e.g., `IsConnected`, `LastSeenName`). Update each binding to use the correct property name on the new store.

Run build and fix any XAML binding-related compile errors:

```
dotnet build DCS-SR-Client\DCS-SR-Client.csproj /p:Platform=x64 2>&1 | Select-String "error"
```

- [ ] **Step 3: Remove SyncedServerSettings singleton property**

Remove:
```csharp
private readonly SyncedServerSettings _serverSettings = SyncedServerSettings.Instance;
```

Replace usages in MainWindow with `App.ServerSettingsStore`.

- [ ] **Step 4: Full solution build**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
```

Expected: 0 errors.

- [ ] **Step 5: Full test run**

```
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
dotnet test DCS-SR-CommonTests\DCS-SR-CommonTests.csproj /p:Platform=x64
```

Expected: all pass.

- [ ] **Step 6: Commit**

```
git add DCS-SR-Client\UI\ClientWindow\MainWindow.xaml.cs DCS-SR-Client\UI\ClientWindow\MainWindow.xaml
git commit -m "refactor: replace singleton data-bindings with state store properties in MainWindow"
```

---

### Task D9: Final cleanup and smoke test

**Files:** varies

- [ ] **Step 1: Check MainWindow line count**

```
(Get-Content DCS-SR-Client\UI\ClientWindow\MainWindow.xaml.cs).Count
```

Expected: ≤ 800 lines. If still above 800, identify the largest remaining method block and extract it to a helper or page-specific handler.

- [ ] **Step 2: Remove any remaining empty or near-empty singleton usages**

Grep for remaining references to `ConnectedClientsSingleton.Instance` and `SyncedServerSettings.Instance` in non-store files:

```
dotnet grep -r "ConnectedClientsSingleton.Instance\|SyncedServerSettings.Instance" DCS-SR-Client\
```

For each hit outside the store files: replace with the appropriate store via `App.ConnectedClientsStore` or `App.ServerSettingsStore`.

- [ ] **Step 3: Run full solution build and all tests**

```
dotnet build DCS-SimpleRadioStandalone.sln /p:Platform=x64
dotnet test DCS-SR-ClientTests\DCS-SR-ClientTests.csproj /p:Platform=x64
dotnet test DCS-SR-CommonTests\DCS-SR-CommonTests.csproj /p:Platform=x64
```

Expected: 0 build errors, all tests pass.

- [ ] **Step 4: Final commit**

```
git add -A
git commit -m "refactor(D): complete event bus migration and MainWindow cleanup"
```

- [ ] **Step 5: Save memory entry**

Add a note to the project memory that the singleton migration is complete and what stores replaced what, so future sessions don't re-derive it.
