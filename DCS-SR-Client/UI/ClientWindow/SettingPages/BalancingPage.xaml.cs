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
            this.Radio1Config.Reload();
            this.Radio2Config.Reload();
            this.Radio3Config.Reload();
            this.Radio4Config.Reload();
            this.Radio5Config.Reload();
            this.Radio6Config.Reload();
            this.Radio7Config.Reload();
            this.Radio8Config.Reload();
            this.Radio9Config.Reload();
            this.Radio10Config.Reload();
            this.IntercomConfig.Reload();
        }
    }
}