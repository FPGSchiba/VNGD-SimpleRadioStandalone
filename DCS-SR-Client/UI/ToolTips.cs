using System.Windows;
using System.Windows.Controls;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public static class ToolTips
    {
        public static ToolTip ExternalAWACSMode;
        public static ToolTip ExternalAWACSModeName;
        public static ToolTip ExternalAWACSModePassword;

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
                Text = "Enter your First Fleet Identification Code followed by your Vanguard Playername.\nExample: \"[DI] FPGSchiba.\" Please reference the Vanguard SRS SOP to find the correct two-digit code"
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
        }
    }
}
