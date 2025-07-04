using System.Collections.Generic;

namespace Vanguard.VCS.Client.Settings.RadioChannels
{
    public interface IPresetChannelsStore
    {
        IEnumerable<PresetChannel> LoadFromStore(string radioName);
    }
}