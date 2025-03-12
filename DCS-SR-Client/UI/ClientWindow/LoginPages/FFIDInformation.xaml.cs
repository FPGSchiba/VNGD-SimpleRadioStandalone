using System.Windows;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Vanguard;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI.ClientWindow.LoginPages
{
    public partial class FFIDInformation : Window
    {
        public Unit[] Units { get; set; }
        
        public FFIDInformation()
        {
            DataContext = this;
            LoadUnits();
            // TODO: Store position and size and reapply when opened
            InitializeComponent();
        }
        
        // TODO: Fetch this from Wix
        private void LoadUnits()
        {
            Units = new[]
            {
                new Unit { Name = "Command", Code = "CO" },
                new Unit { Name = "TacCom", Code = "TC" },
                new Unit { Name = "Vice", Code = "VC" },
                new Unit { Name = "Chaos", Code = "CH" },
                new Unit { Name = "Nighthawks", Code = "NH" },
                new Unit { Name = "Defiant", Code = "DF" },
                new Unit { Name = "Fenrir", Code = "FE" },
                new Unit { Name = "Obsidian", Code = "OB" },
                new Unit { Name = "Shinobi", Code = "SH" },
                new Unit { Name = "Discovery", Code = "DI" },
                new Unit { Name = "Benevolence", Code = "BE" },
                new Unit { Name = "Jackals", Code = "JA" },
                new Unit { Name = "Rock Raiders", Code = "RR" },
                new Unit { Name = "Kraken Trading", Code = "KT" },
                new Unit { Name = "Rogue Racing", Code = "RG" },
                new Unit { Name = "Mako Media", Code = "MM" },
            };
        }
    }
}