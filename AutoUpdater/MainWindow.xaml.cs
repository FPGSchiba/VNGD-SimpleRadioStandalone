using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Octokit;
using Path = System.IO.Path;


namespace AutoUpdater
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly string GITHUB_USERNAME = "ciribob";
        public static readonly string GITHUB_REPOSITORY = "DCS-SimpleRadioStandalone";
        // Required for all requests against the GitHub API, as per https://developer.github.com/v3/#user-agent-required
        public static readonly string GITHUB_USER_AGENT = $"{GITHUB_USERNAME}_{GITHUB_REPOSITORY}";
        private Uri _uri;
        private string _directory;
        private string _file;
        private bool _cancel = false;

        public MainWindow()
        {
            InitializeComponent();

            DownloadLatestVersion();
        }

        private async Task<Uri> GetPathToLatestVersion()
        {
            var githubClient = new GitHubClient(new ProductHeaderValue(GITHUB_USER_AGENT, "1.0.0.0"));

            var releases = await githubClient.Repository.Release.GetAll(GITHUB_USERNAME, GITHUB_REPOSITORY);

            // Retrieve last stable and beta branch release as tagged on GitHub
            foreach (Release release in releases)
            {
                if (!release.Prerelease)
                {
                    var releaseAsset = release.Assets.First();
                    return new System.Uri(releaseAsset.BrowserDownloadUrl);
                }
            }

            return null;
        }

        public async void DownloadLatestVersion()
        {
            //  System.Diagnostics.Process.Start(releaseAsset.BrowserDownloadUrl);

            _uri = await GetPathToLatestVersion();

            _directory = GetTemporaryDirectory();
            _file = _directory + "\\temp.zip";

            using (WebClient wc = new WebClient())
            {
                wc.DownloadProgressChanged += DownloadProgressChanged;
                wc.DownloadFileAsync(_uri, _file);
                wc.DownloadFileCompleted += DownloadComplete;
            }
        }

        private void DownloadComplete(object sender, AsyncCompletedEventArgs e)
        {

            if (!_cancel)
            {
                ZipFile.ExtractToDirectory(_file, _directory + "\\extract");

                Process.Start(_directory + "\\extract\\installer.exe", "-autoupdate");
            }
            
            Close();
        }

        public string GetTemporaryDirectory()
        {
            string tempFolder = Path.GetTempFileName();
            File.Delete(tempFolder);
            Directory.CreateDirectory(tempFolder);

            return tempFolder;
        }

        private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgress.Value = e.ProgressPercentage;
        }

        private void CancelButtonClick(object sender, RoutedEventArgs e)
        {
            _cancel = true;
            Close();
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _cancel = true;
        }
    }
}
