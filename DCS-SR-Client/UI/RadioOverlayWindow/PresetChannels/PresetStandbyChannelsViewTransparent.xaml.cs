using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels
{
    /// <summary>
    /// Interaction logic for PresetStandbyChannelsView.xaml
    /// </summary>
    public partial class PresetStandbyChannelsViewTransparent : UserControl
    {
        public PresetStandbyChannelsViewTransparent()
        {
            InitializeComponent();

            //set to window width
            //FrequencyDropDown.Width = Width;
        }

        private void FrequencyDropDown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}