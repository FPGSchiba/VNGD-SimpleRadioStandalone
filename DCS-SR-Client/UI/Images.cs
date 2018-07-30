using System;
using System.Windows.Media.Imaging;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client.UI
{
    public static class Images
    {
        public static BitmapImage IconConnected;
        public static BitmapImage IconDisconnected;
        public static BitmapImage IconDisconnectedError;

        public static void Init()
        {
            // Image taken from http://p.yusukekamiyamane.com/ @ 2018-08-30
            IconConnected = new BitmapImage(new Uri("pack://application:,,,/SR-ClientRadio;component/plug-connect.png"));
            // Image taken from http://p.yusukekamiyamane.com/ @ 2018-08-30
            IconDisconnected = new BitmapImage(new Uri("pack://application:,,,/SR-ClientRadio;component/plug-disconnect.png"));
            // Image taken from http://p.yusukekamiyamane.com/ @ 2018-08-30
            IconDisconnectedError = new BitmapImage(new Uri("pack://application:,,,/SR-ClientRadio;component/plug-disconnect-prohibition.png"));
        }
    }
}
