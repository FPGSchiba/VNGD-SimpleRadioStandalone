using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.SettingPage
{
    public partial class BalancingPage : Page
    {
        public BalancingPage()
        {
            InitializeComponent();
        }
        
        public void ReloadRadioAudioChannelSettings()
        {
            Radio1Config.Reload();
            Radio2Config.Reload();
            Radio3Config.Reload();
            Radio4Config.Reload();
            Radio5Config.Reload();
            Radio6Config.Reload();
            Radio7Config.Reload();
            Radio8Config.Reload();
            Radio9Config.Reload();
            Radio10Config.Reload();
            IntercomConfig.Reload();
        }
    }
}