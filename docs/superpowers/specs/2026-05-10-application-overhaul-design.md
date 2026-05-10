# Application Overhaul Design

**Date:** 2026-05-10  
**Branch:** feature/server-refactor  
**Status:** Approved

---

## Overview

A four-workstream overhaul of the VCS SRS Client, executed sequentially. Each workstream produces a shippable state before the next begins.

```
[Pre-work]  gRPC contract document
     ↓
[A]  Stale code & DCS removal
     ↓
[B]  Bug pass
     ↓
[C]  Finish gRPC implementation + unit tests
     ↓
[D]  Event bus + state stores + MainWindow cleanup
```

---

## Pre-work: gRPC Contract

Captured before any code changes so removal (A) never loses context for integration (C).

### AuthService

| Method | When called | Client responsibility |
|---|---|---|
| `InitAuth` | On connect | Send client capabilities; receive `ClientGuid` and available login plugins. Retry up to 3× on timeout. |
| `DiscoverAuthenticationFlows` | Before rendering login UI | Given a plugin name (e.g. `profile-vanguard`), returns `AuthFlowDefinition[]` with ordered steps and `FieldDefinition` per step. Dynamically build login form from returned fields — do not hardcode email/password. |
| `GuestLogin` | User submits guest form | Send name, hashed password, unit_id, client_guid. On success, store Bearer token in auth metadata. |
| `StartAuth` | User submits internal login form | Send plugin, flow_id, and first-step field map. Response is either `LoginResult` (done) or `NextStepRequired` (continue flow). |
| `ContinueAuth` | `StartAuth` or prior `ContinueAuth` returned `NextStepRequired` | Send session_id + step_data for the current step. Loop until response is `LoginResult`. Present each `NextStepRequired.required_fields` to user. |
| `UnitSelect` | After internal login, user selects unit | Send guid, secret, unit_id, coalition, role. On success, store Bearer token. |

### SRSService

| Method | When called | Client responsibility |
|---|---|---|
| `SyncClient` | Immediately after successful login | Fetch snapshot of all connected clients, their radio info, and server settings. Seed state stores from response. |
| `UpdateClientInfo` | After unit selection; when name or coalition changes | Send `ClientInfo` (name, coalition, unit_id, role_id). Keeps server metadata current. |
| `UpdateRadioInfo` | When local radio state changes; every 60 s as keepalive | Send `RadioInfo` (radio array + muted flag). |
| `GetServerSettings` | On settings screen refresh | Fetch latest `ServerSettings`; publish `ServerSettingsChangedEvent` into event bus. |
| `Disconnect` | On user logout or app close | Best-effort RPC; proceed with local cleanup regardless of outcome. |
| `SubscribeToUpdates` | Immediately after `SyncClient` succeeds | Open persistent server-to-client stream. Translate each `ServerUpdate` to a typed event and publish to event bus. Reconnect with exponential back-off if stream drops. |

#### SubscribeToUpdates event translation

| `UpdateType` | Typed event published |
|---|---|
| `CLIENT_JOINED` | `ClientJoinedEvent { Guid, ClientInfo, RadioInfo }` |
| `CLIENT_LEFT` | `ClientLeftEvent { Guid }` |
| `CLIENT_RADIO_UPDATE` | `ClientRadioUpdatedEvent { Guid, RadioInfo }` |
| `CLIENT_INFO_UPDATE` | `ClientInfoUpdatedEvent { Guid, ClientInfo }` |
| `SERVER_SETTINGS_CHANGED` | `ServerSettingsChangedEvent { ServerSettings }` |
| `SERVER_ACTION` | `ServerActionEvent { ActionType, TargetGuid, Reason, Duration? }` |
| `DISTRIBUTION_UPDATE` | `DistributionUpdatedEvent { VoiceHosts }` |

---

## Workstream A: Stale Code & DCS Removal

### What is deleted

| File / class | Reason |
|---|---|
| `Network/DCS/DCSRadioSyncManager.cs` | DCS UDP sync loop |
| `Network/DCS/DCSRadioSyncHandler.cs` | DCS radio state normalization |
| `Network/DCS/DCSAutoConnectHandler.cs` | DCS broadcast auto-connect |
| `Network/DCS/Models/DCSLosCheckResult.cs` | DCS-only model |
| `Network/DCS/Models/DCSLosCheckRequest.cs` | DCS-only model |
| `Network/DCS/Models/CombinedRadioState.cs` | DCS-only model |
| `SrsClientSyncHandler.cs` | Old .NET sync handler, superseded by `VcsClientSyncHandler` |

### Fields removed from ClientStateSingleton

`DcsPlayerRadioInfo`, `PlayerCoaltionLocationMetadata`, `LastSent`, `IntercomOffset`, `DcsExportLastReceived`, `ExternalAWACSModelSelected`, `ExternalAWACSModeConnected`, `EamEnabled`, `IsGameExportConnected`.

### Replacement: RadioStateManager

Promotes the useful core of `DCSRadioSyncManager.StartExternalAWACSModeLoop` into a standalone, DCS-free component.

**Model:**
```csharp
public sealed record ClientRadio(string Name, double FrequencyHz, bool Enabled, bool IsIntercom);
public sealed record ClientRadioState(IReadOnlyList<ClientRadio> Radios);
```

**RadioStateManager responsibilities:**
- Loads radio configuration from `radio-config.json` on startup (falls back to an empty 11-radio default if file absent)
- Exposes `Start()` / `Stop()`
- Runs a background loop that calls `_radioUpdate` callback immediately and then every 60 seconds, or on any state change
- In Workstream A: uses the same `SendRadioUpdate` delegate pattern as the old `DCSRadioSyncManager`
- In Workstream D: callback is replaced with `IEventBus.Publish<LocalRadioStateChangedEvent>` when the event bus is introduced

**radio-config.json format** (same directory as executable):
```json
[
  { "name": "Primary",   "frequencyHz": 127500000, "enabled": true,  "isIntercom": false },
  { "name": "Secondary", "frequencyHz": 243000000, "enabled": false, "isIntercom": false },
  { "name": "Intercom",  "frequencyHz": 0,         "enabled": true,  "isIntercom": true  }
]
```

`VcsClientSyncHandler.GetRadioInfoFromState()` maps from `ClientRadioState` instead of `DcsPlayerRadioInfo.radios`.

### VcsClientSyncHandler changes

- Remove `_radioDcsSync` field and all three callsites (`InitializeConnection`, `InitializeRadioSync`, `Disconnect`)
- Remove `ClientCoalitionUpdate()` stub
- Accept `RadioStateManager` via constructor injection

---

## Workstream B: Bug Pass

Reactive workstream — no upfront design. After A lands:

1. Run `code-reviewer` agent across surviving codebase
2. Focus areas: threading safety on shared state, silently swallowed exceptions, `.Wait()` / `.Result` blocking calls, resource disposal gaps
3. Each fix is isolated; no refactoring beyond the bug

---

## Workstream C: Finish gRPC Implementation

### New client-side implementations

**DiscoverAuthenticationFlows**
- Called before rendering the login page
- Response used to dynamically build the login form: render one UI control per `FieldDefinition` (type = `"password"` → masked field, otherwise plain text)
- Replace the hardcoded `"profile-vanguard"` / `"vanguard_email_password"` / `{email, password}` in `InternalLogin`

**ContinueAuth**
- After `StartAuth` returns `NextStepRequired`, enter a loop:
  1. Display `NextStepRequired.required_fields` to user
  2. Call `ContinueAuth` with `session_id` + collected `step_data`
  3. Repeat until response is `LoginResult` or error
- Session ID is threaded through all continuation calls

**UpdateClientInfo**
- Called once after `UnitSelect` succeeds
- Called again if the player's name or coalition changes at runtime
- Sends `ClientInfo { name, coalition, unit_id, role_id }`

**GetServerSettings**
- Called on demand from the settings screen
- Publishes `ServerSettingsChangedEvent` so `ServerSettingsStore` and UI react automatically

**SubscribeToUpdates**
- Called immediately after `SyncClient` succeeds, on a dedicated background `Task`
- Reads the `AsyncServerStreamingCall<ServerUpdate>` in a loop
- Translates each message to the typed event from the contract table above and publishes to `IEventBus`
- On `RpcException` (stream dropped): log, wait with exponential back-off (1 s, 2 s, 4 s, cap 30 s), re-subscribe
- Stops cleanly when a `CancellationToken` is signalled (set on `Disconnect`)

### Unit tests (MSTest, one test class per method)

Each method gets:
- Happy path: mock gRPC response → correct event published / state set
- Deadline exceeded: `RpcException(StatusCode.DeadlineExceeded)` → correct error callback invoked
- General RPC error: `RpcException(StatusCode.Internal)` → correct error callback invoked

`SubscribeToUpdates` additionally tests:
- Each of the 7 `UpdateType` values produces the correct typed event
- Stream drop triggers reconnect loop (not tested end-to-end, only that the reconnect path is entered)

---

## Workstream D: Event Bus + State Stores + MainWindow Cleanup

### IEventBus

```csharp
public interface IEventBus
{
    void Publish<T>(T message) where T : class;
    IDisposable Subscribe<T>(Action<T> handler) where T : class;
}
```

**Implementation:** thin wrapper over the existing `Easy.MessageHub` (`IMessageHub`) already in the project. No new dependency added.

Handlers that touch WPF controls must marshal to the UI thread themselves (`Application.Current.Dispatcher.Invoke`).

### Typed events

**Server-pushed:**
- `ClientJoinedEvent { string Guid, ClientInfo Info, RadioInfo Radios }`
- `ClientLeftEvent { string Guid }`
- `ClientRadioUpdatedEvent { string Guid, RadioInfo Radios }`
- `ClientInfoUpdatedEvent { string Guid, ClientInfo Info }`
- `ServerSettingsChangedEvent { ServerSettings Settings }`
- `ServerActionEvent { ServerAction.Types.ActionType Type, string TargetGuid, string Reason, long? DurationSeconds }`
- `DistributionUpdatedEvent { repeated VoiceHostDetails VoiceHosts }`

**Internal:**
- `ConnectionStateChangedEvent { ConnectionState State }` where `State` ∈ `{ Connecting, Connected, Disconnected, Error }`
- `AuthenticationCompletedEvent { string PlayerName, string Coalition, string UnitId, VcsRole Role }`
- `LocalRadioStateChangedEvent { ClientRadioState State }`

### State stores (replace singletons)

**ClientStateStore** replaces `ClientStateSingleton` server-state fields:
- Subscribes: `ConnectionStateChangedEvent`, `AuthenticationCompletedEvent`
- Exposes (read-only): `Guid`, `PlayerName`, `Coalition`, `UnitId`, `Role`, `ConnectionState`, `IsConnected`

**ConnectedClientsStore** replaces `ConnectedClientsSingleton`:
- Subscribes: `ClientJoinedEvent`, `ClientLeftEvent`, `ClientRadioUpdatedEvent`, `ClientInfoUpdatedEvent`
- Exposes: `IReadOnlyDictionary<string, (ClientInfo Info, RadioInfo Radios)> Clients`

**ServerSettingsStore** replaces `SyncedServerSettings`:
- Subscribes: `ServerSettingsChangedEvent`
- Exposes: `Coalitions`, `TestFrequencies`, `GlobalFrequencies`, `MaxRadiosPerClient`

All three registered once in `App.xaml.cs`, injected via constructor. No static accessors.

**GlobalSettingsStore stays as singleton** — local, persisted, not event-driven.

### MainWindow cleanup

Removals after event bus + DCS work lands:

| Removed | Replaced by |
|---|---|
| `_srsClient` (SrsClientSyncHandler) field + all usages | Nothing — old client fully superseded |
| `ConnectAwacsMode()` | Nothing — no AWACS mode without DCS |
| `ExternalAwacsModeConnectionChanged()` | Nothing |
| `UpdateUiCallback()` | Nothing — old SRS callback |
| `_dcsAutoConnectListener` field + `OnClosing` stop | Gone with Workstream A |
| `VcsUiUpdate` dispatch switch + all `Handle*` private methods | Event bus subscriptions in constructor |
| All `ClientState.DcsPlayerRadioInfo.*` writes | Gone with Workstream A |
| All `ClientState.PlayerCoaltionLocationMetadata.*` writes | Gone with Workstream A |
| All `ClientState.ExternalAWACSModelSelected` writes | Gone with Workstream A |
| `_connectionAwacsSpan` Sentry span | No AWACS connection path |
| `OpenPagePropertyChanged` static switch | Collapse to single-case (WelcomeIndex only) |

**Target:** MainWindow.xaml.cs from ~1,793 lines to ~600–700 lines.

After cleanup, MainWindow responsibilities are:
1. Page navigation (InitPages, OpenPageByIndex)
2. Radio overlay window management (open/close/position-reset)
3. Audio wiring (AudioManager start/stop)
4. Event bus subscriptions (connection state → UI, server action → kick/ban dialog)
5. Connect / Disconnect flow (delegate to VcsClientSyncHandler)

---

## Singletons: final state

| Class | Outcome |
|---|---|
| `ClientStateSingleton` | DCS fields removed; server-state fields migrated to `ClientStateStore`; audio/input state remains until audio workstream |
| `ConnectedClientsSingleton` | Replaced by `ConnectedClientsStore` |
| `SyncedServerSettings` | Replaced by `ServerSettingsStore` |
| `GlobalSettingsStore` | Unchanged — stays as singleton |
| `AudioInputSingleton` | Out of scope — stays as-is |
| `AudioOutputSingleton` | Out of scope — stays as-is |

---

## Out of Scope

- Audio device management (`AudioInputSingleton`, `AudioOutputSingleton`)
- Radio overlay window internals
- Sentry telemetry changes
- CI/CD pipeline
