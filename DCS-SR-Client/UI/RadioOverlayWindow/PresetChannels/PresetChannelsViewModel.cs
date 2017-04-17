using System.Collections.ObjectModel;
using System.Windows.Input;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings.RadioChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.PresetChannels;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Utils;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels
{
    public class PresetChannelsViewModel
    {
        public PresetChannelsViewModel(IPresetChannelsStore channels)
        {
            foreach (var channel in channels.LoadFromStore())
            {
                PresetChannels.Add(channel);
            }

            ReloadCommand = new DelegateCommand(OnReload);
            LoadFromFileCommand = new DelegateCommand(OnLoadFile);
         
        }

        public ObservableCollection<PresetChannel> PresetChannels { get; } = new ObservableCollection<PresetChannel>();

        public ICommand ReloadCommand { get; }

        public ICommand LoadFromFileCommand { get; }

        public PresetChannel SelectedPresetChannel { get; set; }

        private void OnReload()
        {

        }

        private void OnLoadFile()
        {
            
        }
    }
}
