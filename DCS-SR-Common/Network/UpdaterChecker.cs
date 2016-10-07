using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using NLog;

namespace Ciribob.DCS.SimpleRadio.Standalone.Common
{
    //Quick and dirty update checker based on GitHub Published Versions
    public class UpdaterChecker
    {
        public static readonly string VERSION = "1.2.7.2";

        public static async void CheckForUpdate()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var currentVersion = Version.Parse(VERSION);
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
                        var githubVersion = path.Split('/').Last().ToLower().Replace("v", "");
                        Version ghVersion = null;

                        if (Version.TryParse(githubVersion, out ghVersion))
                        {
                            //comparse parts
                            if (ghVersion.CompareTo(currentVersion) > 0)
                            {
                                logger.Warn("Update Available on GitHub: " + githubVersion);
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
                            }
                            else if (ghVersion.CompareTo(currentVersion) == 0)
                            {
                                logger.Warn("Running Latest Version: " + githubVersion);
                            }
                            else
                            {
                                logger.Warn("Running TESTING Version!! : " + VERSION);
                            }
                        }
                        else
                        {
                            logger.Warn("Failed to Parse version: " + githubVersion);
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