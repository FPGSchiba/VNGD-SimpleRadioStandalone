using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    //Quick and dirty update checker based on GitHub Published Versions
    public class UpdaterChecker
    {
        private static readonly string VERSION = "1.1.6.0";

        public static async void CheckForUpdate()
        {
            try
            {
                var request = WebRequest.Create("https://github.com/ciribob/DCS-SimpleRadioStandalone/releases/latest");
                var response = (HttpWebResponse) await Task.Factory
                    .FromAsync(request.BeginGetResponse,
                        request.EndGetResponse,
                        null);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var path = response.ResponseUri.AbsolutePath;

                    if (path.Contains("tag/"))
                    {
                        var githubVersion = path.Split('/').Last().ToLower().Replace("v", "").Split('.');
                        //now compare major minor patch (ignore build)
                        var current = VERSION.Split('.');

                        for (var i = 0; i < 3; i++)
                        {
                            if (current[i] != githubVersion[i])
                            {
                                var result =
                                    MessageBox.Show("New Version Available!\n\nDo you want to Update?",
                                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                                // Process message box results
                                switch (result)
                                {
                                    case MessageBoxResult.Yes:
                                        //launch browser
                                        Process.Start(response.ResponseUri.ToString());
                                        break;
                                    case MessageBoxResult.No:

                                        break;
                                }
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Ignore for now
            }
        }
    }
}