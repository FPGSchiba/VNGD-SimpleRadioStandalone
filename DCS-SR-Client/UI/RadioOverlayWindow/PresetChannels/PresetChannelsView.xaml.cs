using System.Windows.Controls;

namespace Vanguard.VCS.Client.UI.RadioOverlayWindow.PresetChannels
{
    /// <summary>
    /// Interaction logic for PresetChannelsView.xaml
    /// </summary>
    public partial class PresetChannelsView : UserControl
    {
        public PresetChannelsView()
        {
            InitializeComponent();

            //set to window width
            FrequencyDropDown.Width = Width;
        }
    }
}