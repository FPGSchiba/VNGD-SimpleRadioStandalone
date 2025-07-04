using System.Collections.Generic;

namespace Vanguard.VCS.Client.Settings.RadioChannels
{
    public class MockPresetChannelsStore : IPresetChannelsStore
    {
        public IEnumerable<PresetChannel> LoadFromStore(string radioName)
        {
            IList<PresetChannel> _presetChannels = new List<PresetChannel>();

            _presetChannels.Add(new PresetChannel
            {
                Text = 127.1 + "",
                Value = 127.1
            });

            _presetChannels.Add(new PresetChannel
            {
                Text = 127.1 + "",
                Value = 127.1
            });

            _presetChannels.Add(new PresetChannel
            {
                Text = 127.1 + "",
                Value = 127.1
            });

            return _presetChannels;
        }
    }
}