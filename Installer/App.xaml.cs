using System.Globalization;
using System.Threading;
using System.Windows;

namespace Installer
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            //set everything to invariant
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }
    }
}