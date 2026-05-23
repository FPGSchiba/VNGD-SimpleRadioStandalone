using System.Collections.Generic;
using Vanguard.VCS.Client.Network;
using Vanguard.VCS.Client.Network.Models;

namespace Vanguard.VCS.Client.Events
{
    // Server-pushed client events
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
    public sealed record ServerMuteChangedEvent(bool IsMuted);
    public sealed record DistributionUpdatedEvent(IReadOnlyList<VoiceHostDetails> VoiceHosts);

    // Internal lifecycle events
    public sealed record ConnectionStateChangedEvent(ConnectionState State);
    public sealed record AuthenticationCompletedEvent(string PlayerName, string Coalition, string UnitId, VcsRole Role);
    public sealed record LocalRadioStateChangedEvent(ClientRadioState State);

    public enum ConnectionState { Connecting, Connected, Disconnected, Error }
}
