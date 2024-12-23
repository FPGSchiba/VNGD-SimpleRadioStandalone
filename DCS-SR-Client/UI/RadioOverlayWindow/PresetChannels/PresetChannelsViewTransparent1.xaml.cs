using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.RadioOverlayWindow.PresetChannels
{
    /// <summary>
    /// Interaction logic for PresetChannelsViewTransparent1.xaml
    /// </summary>
    public partial class PresetChannelsViewTransparent1 : UserControl
    {
        public PresetChannelsViewTransparent1()
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