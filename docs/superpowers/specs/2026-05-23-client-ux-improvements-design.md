# Client UX Improvements — Design Spec

**Date:** 2026-05-23
**Branch:** feature/server-refactor
**Status:** Approved

---

## Overview

Six client-side issues identified through testing, covering feedback for server actions (mute, kick, ban), VOX reliability, transmitter name display, client list presentation, and the login/connection flow.

---

## 1. Login / Connection Flow Redesign

### Problem

The current flow has two hidden phases: Phase 1 silently connects to the server (gRPC) while displaying the Welcome page, then Phase 2 enables Login/Guest buttons once connected. Users cannot tell which phase is active, errors surface as modal dialogs that leave the UI in an ambiguous state, and the Custom Server path bypasses the flow in an inconsistent way.

### Design

Replace the current Welcome + Login + Guest + GuestSuccess + UnitSelection page arrangement with a **linear progress stepper** embedded in the existing `DisplayFrame`. The stepper header always shows the current step. Page content changes per step. There are two paths sharing Step 1:

**Guest path (3 steps):**

1. **Connect to Server** — auto-discovers default server via REST API; user can switch to a custom address. Spinner while connecting; inline error panel on failure with Retry.
2. **Guest Login** — callsign (required), fleet code (optional), coalition password (optional).
3. **Ready** — brief confirmation, then navigate to `HomePage`.

**Member path (4 steps):**

1. **Connect to Server** — same as Guest Step 1.
2. **Member Login** — email + password input.
3. **Select Unit & Coalition** — coalition list populated from server response (dynamic, see §6); unit/role selection.
4. **Ready** — brief confirmation, then navigate to `HomePage`.

### Server Discovery

Step 1 fetches the default server from the existing REST API (`WebsiteClient.GetServerInformation()`). The result is shown as a selectable entry: "VCS Alpha — vcs.vanguard.gg:5002". The user can:
- Select the auto-discovered entry (pre-selected by default)
- Enter a custom host:port instead
- The last used address is remembered in `GlobalSettingsStore`

If the REST API call fails, the field is left empty with a soft warning; manual entry still works.

### Error Handling

All errors are shown **inline** within the current step — no modal dialogs. Each step has an error panel below its form that appears on failure with the message and a Retry or Back action. This replaces all `MessageBox.Show` calls in the connection flow.

### Kick / Ban During Session

When a kick or ban server action is received while logged in:
- Client immediately calls `Stop()` and returns to Step 1
- Step 1 shows a red inline alert: `"You were kicked: [reason]"` or `"You are banned: [reason]"`
- The alert persists until the user retries or changes server

This requires fixing the broken event path (see §2).

### Implementation Notes

- The stepper lives in `MainWindow`. The existing page constants (`WelcomeIndex`, `LoginIndex`, etc.) are replaced by a `ConnectionStep` enum: `Connect`, `GuestLogin`, `MemberLogin`, `UnitSelection`, `Ready`.
- `OpenPageByIndex` is replaced by `NavigateToStep(ConnectionStep step)`.
- Back buttons on login steps call `Stop()` and return to `Connect`.
- The `LoggedIn` DependencyProperty and `HomeNavigation` visibility logic remain unchanged.

---

## 2. Kick / Ban — Forced Disconnect with Reason

### Problem

`VcsClientSyncHandler.HandleServerAction` calls `_callback(VcsUiUpdateType.ConnectionLost, reason)` for both Kick and Ban. `ConnectionLost` is not handled in `MainWindow.VcsUiUpdate`'s switch — it falls through to default logging. A `ServerActionEvent` EventBus subscription exists in `MainWindow` but the event is never published, so `HandleForcedDisconnect` never fires.

### Design

In `HandleServerAction`, for `Kick` and `Ban`:
1. Publish a `ServerActionEvent` to the EventBus with `Type` and `Reason`.
2. Keep the existing `_callback(VcsUiUpdateType.ConnectionLost, reason)` as a secondary signal.

In `MainWindow`:
- The existing `_serverActionSubscription` handler calls `HandleForcedDisconnect(reason)`.
- `HandleForcedDisconnect` is updated to navigate to the Connect step and set the inline kick/ban error message there, replacing the current `MessageBox.Show`.

No new event types required — `ServerActionEvent` already exists and is subscribed.

---

## 3. Mute — Persistent Server-Mute Indicator

### Problem

`HandleServerAction` logs Mute/Unmute but does nothing visible. Users transmit unaware that audio is blocked server-side.

### Design

**State:**
- Add `IsServerMuted` (bool, observable) to `ClientStateSingleton`.
- `HandleServerAction` sets `IsServerMuted = true` on Mute, `false` on Unmute.
- Publish a `ServerMuteChangedEvent` (new) via EventBus so UI components can react without polling.

**UI:**
- An amber banner is added to `MainWindow` above `DisplayFrame`, visible whenever the user is logged in (i.e. `LoggedIn = true`). It is not shown in the radio overlay windows (which are separate `Window` instances).
- Banner text: "You are muted by the server — your transmissions are blocked."
- Banner visibility is bound to `ClientStateSingleton.IsServerMuted`.
- Banner is hidden when `IsServerMuted` is false; no manual dismiss needed.
- PTT and VOX continue to function mechanically (local audio capture, sending packets) — blocking is server-side. The banner makes this clear to avoid user confusion.

---

## 4. VOX — Voice Activity Detection with Configurable Settings

### Problem

VOX has no threshold (any sound triggers it), no hold time (brief pauses break the stream), and no attack delay (word starts can be cut). Results in choppy, unreliable transmission.

### Design

**New settings** (added to `ProfileSettingsKeys`):

| Key | Type | Default | Range | Description |
|-----|------|---------|-------|-------------|
| `VoxThreshold` | float | 0.02 | 0.0–1.0 | RMS amplitude below which audio is considered silence |
| `VoxHoldTimeMs` | int | 500 | 100–2000 ms | Keep transmitting for this long after voice drops below threshold |
| `VoxAttackTimeMs` | int | 100 | 0–500 ms | Require this many ms of continuous voice before starting transmission |

**Logic changes in `UDPVoiceHandler.Send()`:**

The `CheckVOXActivation` method currently receives a boolean `voice` parameter. Replace this with an RMS value computed from the raw PCM bytes before encoding:

```
rms = sqrt(sum(sample^2) / count)
```

VOX state machine:
- **Idle → Active:** RMS ≥ threshold for `VoxAttackTimeMs` continuously → begin transmission
- **Active → Holding:** RMS drops below threshold → start hold timer
- **Holding → Idle:** hold timer expires without RMS recovering → end transmission
- **Holding → Active:** RMS recovers above threshold → cancel hold timer, remain active

The `_lastVOXSend` field (already present) is repurposed for the hold timer. A new `_voxAttackStart` field tracks the attack window.

**Settings UI:**
- Three sliders added to the VOX section in `Settings → General` (existing `GeneralPage.xaml`).
- Labels: "Sensitivity threshold", "Hold time (ms)", "Attack time (ms)".
- Settings take effect immediately on the next transmission — no reconnect required. The values are read from `ProfileSettingsStore` inside `Send()` on each call.

---

## 5. Sender Names — Fix "---" on Radio Overlay

### Problem

`ApplyClientUpdate` stores `Name = clientInfo?.Name ?? "---"`. When the server doesn't include a name, the client stores the string `"---"`. This flows into `radioReceivingState.SentBy` and displays as a literal label on the radio overlay.

Additionally, transmitter names are gated on `ServerSettingsKeys.SHOW_TRANSMITTER_NAME` being true. If this server flag is off, `SentBy` is never populated regardless of the name being available.

### Design

**In `ApplyClientUpdate`:**
- Change fallback to `""` (empty string) instead of `"---"`.
- Empty string correctly means "no name available" and the overlay already handles this — `RepaintRadioReceive` only shows `TransmitterName` when `SentBy.Length > 0`.

**Server setting override:**
- Add a client-side `GlobalSettingsKey`: `AlwaysShowTransmitterName` (bool, default false).
- In `UdpAudioDecode`, the transmitter name lookup becomes:

```
if ((serverSetting.SHOW_TRANSMITTER_NAME || globalSettings.AlwaysShowTransmitterName)
    && clients.TryGetValue(packet.ClientId, out var client))
{
    receiveState.SentBy = client.Name; // empty string if name unknown
}
```

- Exposed as a checkbox in `Settings → General`: "Always show caller name on radio (ignores server setting)".

No changes to `RepaintRadioReceive` — it already handles empty `SentBy` correctly.

---

## 6. Client List — Dynamic Coalition Grouping

### Problem

The client list shows all players in a flat alphabetical list, names default to "---" (same root cause as §5), and it refreshes on a 3-second polling timer rather than reacting to events.

### Coalitions

Coalitions are **dynamic**: the server can add or remove them at any time. Each coalition has:
- `Name` (string)
- `Description` (string)
- `Color` (server-provided, e.g. hex string or ARGB)
- `Password` (used during Guest login — already supported)

The client must not hardcode coalition names or colours.

### Design

**Data model:**

`PlayerListItem` gains:
- `Coalition` (string) — coalition name from server
- `CoalitionColor` (Color) — parsed from server-provided value, replaces the current `TeamColor` which always defaults to white
- `Role` (VcsRole) — Guest / Member / Officer / Administrator
- `IsTransmitting` (bool) — true when this client's GUID appears in any active `RadioReceivingState`

**Grouping:**

Replace the flat `ObservableCollection<PlayerListItem>` with a `CollectionViewSource` grouped by `Coalition`, sorted by `Coalition` then by `Name`. Coalition section headers use the server-provided coalition colour.

A summary line at the top shows: `"N connected (CoalitionA: x, CoalitionB: y, …)"` built dynamically from the grouped data.

Clients with no coalition are grouped under "Unassigned".

**Event-driven updates:**

Replace the `DispatcherTimer` (3-second polling) with EventBus subscriptions:
- `ClientJoinedEvent` → add to list
- `ClientLeftEvent` → remove from list
- `ClientInfoUpdatedEvent` → update name, coalition, role in place
- `ServerSettingsChangedEvent` → re-evaluate coalition list if server settings include coalition definitions

**Transmitting indicator:**

`IsTransmitting` is updated by a lightweight 100 ms `DispatcherTimer` that scans `ClientStateSingleton.RadioReceivingState` for GUIDs matching list items. This timer only runs while `HomePage` is visible.

**UI:**

Coalition section header: coloured left border strip + coalition name + count in parentheses.
Per-row: `[mic icon] [Name]  [role badge]  [fleet code]`
Mic icon is green and visible when `IsTransmitting = true`, otherwise invisible (not greyed out — hidden).

---

## Out of Scope

- Server-side coalition management UI (admin panel)
- Client list admin actions (mute/kick from list) — not requested
- WebRTC-based VAD — deferred; the RMS state machine is sufficient for this iteration
- Recording-allowed indicator — kept as-is
