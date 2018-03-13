using System.Globalization;
using System.Threading;
using System.Windows;

namespace DCS_SR_Client
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