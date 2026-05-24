namespace Vanguard.VCS.Client.Network.Models
{
    public sealed record ClientRadio(string Name, double FrequencyHz, bool Enabled, bool IsIntercom);

    public sealed record ClientRadioState(System.Collections.Generic.IReadOnlyList<ClientRadio> Radios);
}
