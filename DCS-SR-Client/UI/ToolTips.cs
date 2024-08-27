using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public static class ToolTips
    {
        public static ToolTip ExternalAWACSMode;
        public static ToolTip ExternalAWACSModeName;
        public static ToolTip ExternalAWACSModePassword;
        public static ToolTip ServerIp;

        public static void Init()
        {
            ExternalAWACSMode = new ToolTip();
            StackPanel externalAWACSModeContent = new StackPanel();

            externalAWACSModeContent.Children.Add(new TextBlock
            {
                Text = "Radio Connection",
                FontWeight = FontWeights.Bold
            });
            externalAWACSModeContent.Children.Add(new TextBlock
            {
                Text = "You must connect your radio in order for them to function. If your radio panel says unknown, be sure to connect your radios."
            });
            externalAWACSModeContent.Children.Add(new TextBlock
            {
                Text = "Enter the operation password provided to you by the SRS server admin to confirm your operation."
            });

            ExternalAWACSMode.Content = externalAWACSModeContent;


            ExternalAWACSModeName = new ToolTip();
            StackPanel externalAWACSModeNameContent = new StackPanel();

            externalAWACSModeNameContent.Children.Add(new TextBlock
            {
                Text = "[FFID] Player Name",
                FontWeight = FontWeights.Bold
            });
            externalAWACSModeNameContent.Children.Add(new TextBlock
            {
                Text = "Enter your First Fleet Identification Code followed by your Vanguard Playername.\nExample: \"[DI] FPGSchiba.\" \n\n First Fleet ID Codes as follows: \n Benevolence = [BE] \n Chaos = [CH] \n Command= [CO] \n Defiant = [DF] \n Discovery = [DI] \n Fleet Services = [FS] \n Jackals = [JA] \n Kraken Trading Company = [KT] \n Mako Media = [MM]  \n Obsidian = [OB] \n Rock Raiders = [RR] \n Rogue Racing = [RG] \n Shinobi = [SH] \n Taccom = [TC] \n Vice = [VC] \n Witcher = [WT] \n\n The operation may require you to use a custom FFID. Check with the operation commander for more info. \n See Vanguard SRS SOP for more details "
            });

            ExternalAWACSModeName.Content = externalAWACSModeNameContent;


            ExternalAWACSModePassword = new ToolTip();
            StackPanel externalAWACSModePasswordContent = new StackPanel();

            externalAWACSModePasswordContent.Children.Add(new TextBlock
            {
                Text = "Operation Password",
                FontWeight = FontWeights.Bold
            });
            externalAWACSModePasswordContent.Children.Add(new TextBlock
            {
                Text = "The Operation password is provided to you by the SRS server admin."
            });
            externalAWACSModePasswordContent.Children.Add(new TextBlock
            {
                Text = "Entering the correct password for an operation allows you to access that operation's comms."
            });

            ExternalAWACSModePassword.Content = externalAWACSModePasswordContent;

            ServerIp = new ToolTip();
            StackPanel ServerIpcontent = new StackPanel();

            ServerIpcontent.Children.Add(new TextBlock
            {
                Text = "Server IP Address",
                FontWeight = FontWeights.Bold
            });
            ServerIpcontent.Children.Add(new TextBlock
            {
                Text = "You can find the current Vanguard SRS IP address under the #SRS-Status Channel on the Vanguard Discord."
            });
            ServerIpcontent.Children.Add(new TextBlock
            {
                Text = "Note: You must be at least a Talon Cadet to view the channel."
            });

            ServerIp.Content = ServerIpcontent;

        }
    }
}
