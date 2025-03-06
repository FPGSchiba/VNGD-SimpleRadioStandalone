using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Ciribob.DCS.SimpleRadio.Standalone.Client.Settings;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Sentry;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Ciribob.DCS.SimpleRadio.Standalone.Client
{
    /// <summary>
    ///     Interaction logic for App.xaml
    /// </summary>
    public class App : Application
    {
        private NotifyIcon _notifyIcon;
        private bool _loggingReady;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly string _version;

        public App()
        {
#if !DEBUG
            FreeConsole();
#endif

            Assembly assembly = Assembly.GetExecutingAssembly();
            _version = Regex.Replace(AssemblyName.GetAssemblyName(assembly.Location).Version.ToString(), @"(?<=\d\.\d\.\d)(.*)(?=)", "");

            SentrySdk.Init(o =>
            {
                o.Dsn = "https://6cf3a69b53596bb57aff005e6fd54296@o4507794910543872.ingest.de.sentry.io/4507795022217296";
                o.AutoSessionTracking = true;
#if DEBUG
                o.Debug = true;
                o.TracesSampleRate = 1.0;
                o.Release = $"vngd-srs-client@{_version}";
                o.Environment = "development";
#endif
#if !DEBUG
                o.Debug = false;
                o.TracesSampleRate = 0.25;
                o.Release = $"vngd-srs@{_version}";
                o.Environment = "production";
#endif
            });
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandlerAsync;

            var location = AppDomain.CurrentDomain.BaseDirectory;

            //check for opus.dll
            if (!File.Exists(location + "\\opus.dll"))
            {
                MessageBox.Show(
                    $"You are missing the opus.dll - Reinstall using the Installer and don't move the client from the installation directory!",
                    "Installation Error!", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Environment.Exit(1);
            }
            if (!File.Exists(location + "\\speexdsp.dll"))
            {

                MessageBox.Show(
                    $"You are missing the speexdsp.dll - Reinstall using the Installer and don't move the client from the installation directory!",
                    "Installation Error!", MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Environment.Exit(1);
            }

            SetupLogging();

            ListArgs();


#if !DEBUG

            if (IsClientRunning())
            {
                //check environment flag

                var args = Environment.GetCommandLineArgs();
                var allowMultiple = false;

                foreach (var arg in args)
                {
                    if (arg.Contains("-allowMultiple"))
                    {
                        //restart flag to promote to admin
                        allowMultiple = true;
                    }
                }

                if (GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.AllowMultipleInstances) || allowMultiple)
                {
                    Logger.Warn("Another SRS instance is already running, allowing multiple instances due to config setting");
                }
                else
                {
                    Logger.Warn("Another SRS instance is already running, preventing second instance startup");

                    MessageBoxResult result = MessageBox.Show(
                    "Another instance of the SimpleRadio client is already running!\n\nThis one will now quit. Check your system tray for the SRS Icon",
                    "Multiple SimpleRadio clients started!",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);


                    Environment.Exit(0);
                    return;
                }
            }
#endif

            RequireAdmin();

            InitNotificationIcon();
        }

        private void ListArgs()
        {
            _logger.Info("Arguments:");
            var args = Environment.GetCommandLineArgs();
            foreach (var s in args)
            {
                _logger.Info(s);
            }
        }

        private void RequireAdmin()
        {
            if (!GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RequireAdmin))
            {
                return;
            }

            WindowsPrincipal principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            bool hasAdministrativeRight = principal.IsInRole(WindowsBuiltInRole.Administrator);

            if (!hasAdministrativeRight && GlobalSettingsStore.Instance.GetClientSettingBool(GlobalSettingsKeys.RequireAdmin))
            {
                Task.Factory.StartNew(() =>
                {
                    var location = AppDomain.CurrentDomain.BaseDirectory;

                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        WorkingDirectory = "\"" + location + "\"",
                        FileName = "SR-ClientRadio.exe",
                        Verb = "runas",
                        Arguments = GetArgsString() + " -allowMultiple"
                    };
                    try
                    {
                        Process.Start(startInfo);

                        //shutdown this process as another has started
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (_notifyIcon != null)
                                _notifyIcon.Visible = false;

                            Environment.Exit(0);
                        }));
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        MessageBox.Show(
                                "SRS Requires admin rights to be able to read keyboard input in the background. \n\nIf you do not use any keyboard binds for SRS and want to stop this message - Disable Require Admin Rights in SRS Settings\n\nSRS will continue without admin rights but keyboard binds will not work!",
                                "UAC Error", MessageBoxButton.OK, MessageBoxImage.Warning);

                    }
                });
            }
        }

        private static string GetArgsString()
        {
            StringBuilder builder = new StringBuilder();
            var args = Environment.GetCommandLineArgs();
            foreach (var s in args)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" ");
                }

                if (s.Contains("-cfg="))
                {
                    var str = s.Replace("-cfg=", "-cfg=\"");

                    builder.Append(str);
                    builder.Append("\"");
                }
                else
                {
                    builder.Append(s);
                }
            }

            return builder.ToString();
        }

        /* 
         * Changes to the logging configuration in this method must be replicated in
         * this VS project's NLog.config file
         */
        private void SetupLogging()
        {
            // If there is a configuration file then this will already be set
            if (LogManager.Configuration != null)
            {
                _loggingReady = true;
                return;
            }

            var config = new LoggingConfiguration();
            var consoleTarget = new ConsoleTarget
            {
                Encoding = Encoding.UTF8,
                WriteBuffer = false,
                DetectConsoleAvailable = true,
                Name = "consoleTarget",
                StdErr = false,
                Layout = @"${longdate} | ${logger} (${level}) | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=1}"
            };
            var consoleWrapper = new AsyncTargetWrapper(consoleTarget, 5000, AsyncTargetWrapperOverflowAction.Discard);
            config.AddTarget("asyncConsoleTarget", consoleWrapper);
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Trace, consoleWrapper));

            var fileTarget = new FileTarget
            {
                FileName = "clientlog.txt",
                ArchiveFileName = "clientlog.old.txt",
                MaxArchiveFiles = 1,
                ArchiveAboveSize = 104857600,
                Layout =
                @"${longdate} | ${logger} (${level}) | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=1}"
            };

            var fileWrapper = new AsyncTargetWrapper(fileTarget, 5000, AsyncTargetWrapperOverflowAction.Discard);
            config.AddTarget("asyncFileTarget", fileWrapper);
            
            // Default Log Level for File Logging is: Warning (LogLevel.Warn)
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Warn, fileWrapper));

            config.AddSentry(options =>
            {
                options.Layout = "${message}";
                // Optionally specify a separate format for breadcrumbs
                options.BreadcrumbLayout = "${logger}: ${message}";

                // Debug and higher are stored as breadcrumbs (default is Info)
                options.MinimumBreadcrumbLevel = LogLevel.Debug;
                // Error and higher is sent as event (default is Error)
                options.MinimumEventLevel = LogLevel.Error;

                // Send the logger name as a tag
                options.AddTag("logger", "${logger}");

                options.Dsn = "https://6cf3a69b53596bb57aff005e6fd54296@o4507794910543872.ingest.de.sentry.io/4507795022217296";

#if DEBUG
                options.Debug = true;
                options.TracesSampleRate = 1.0;
                options.Release = $"vngd-srs-client@{_version}";
                options.Environment = "development";
#endif
#if !DEBUG
                options.Debug = false;
                options.TracesSampleRate = 0.25;
                options.Release = $"vngd-srs-client@{_version}";
                options.Environment = "production";
#endif

            });

            LogManager.Configuration = config;
            _loggingReady = true;
        }


        private void InitNotificationIcon()
        {
            if (_notifyIcon != null)
            {
                return;
            }
            MenuItem notifyIconContextMenuShow = new MenuItem
            {
                Index = 0,
                Text = "Show"
            };
            notifyIconContextMenuShow.Click += NotifyIcon_Show;

            MenuItem notifyIconContextMenuQuit = new MenuItem
            {
                Index = 1,
                Text = "Quit"
            };
            notifyIconContextMenuQuit.Click += NotifyIcon_Quit;

            ContextMenu notifyIconContextMenu = new ContextMenu();
            notifyIconContextMenu.MenuItems.AddRange(new [] { notifyIconContextMenuShow, notifyIconContextMenuQuit });

            _notifyIcon = new NotifyIcon
            {
                Icon = Ciribob.DCS.SimpleRadio.Standalone.Client.Properties.Resources.audio_headset,
                Visible = true
            };
            _notifyIcon.ContextMenu = notifyIconContextMenu;
            _notifyIcon.DoubleClick += NotifyIcon_Show;

        }

        private void NotifyIcon_Show(object sender, EventArgs args)
        {
            if (MainWindow == null) return;
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
        }

        private void NotifyIcon_Quit(object sender, EventArgs args)
        {
            if (MainWindow == null) return;
            MainWindow.Close();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
                _notifyIcon.Visible = false;
            base.OnExit(e);
        }

        private void UnhandledExceptionHandlerAsync(object sender, UnhandledExceptionEventArgs e)
        {
            // First log generated Error
            if (_loggingReady)
            {
                Logger logger = LogManager.GetCurrentClassLogger();
                logger.Error((Exception)e.ExceptionObject, "Received unhandled exception, {0}", e.IsTerminating ? "exiting" : "continuing");
            }

#if DEBUG
            MessageBox.Show(
                "This was an SRS Crash!\nPlease open the logfile: `clientlog.txt` in the folder: `VNGD-SimpleRadioStandalone/DCS-SR-Client/bin/Debug/`. There you will find more information.",
                "Debug Crash", MessageBoxButton.OK);
#endif
#if !DEBUG
            // Request creates an Issue on GitHub with the LogFile
            var client = new HttpClient();
            var content = new MultipartFormDataContent();
            try
            {
                System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>> b =
 new List<KeyValuePair<string, string>>();
                b.Add(new KeyValuePair<string, string>("log", e.ExceptionObject.ToString()));
                b.Add(new KeyValuePair<string, string>("user", System.Security.Principal.WindowsIdentity.GetCurrent().Name));
                b.Add(new KeyValuePair<string, string>("time", DateTime.Now.ToString()));
                b.Add(new KeyValuePair<string, string>("version", Regex.Replace(AssemblyName.GetAssemblyName(Assembly.GetExecutingAssembly().Location).Version.ToString(), @"(?<=\d\.\d\.\d)(.*)(?=)", "")));
                var addMe = new FormUrlEncodedContent(b);

                content.Add(addMe);
                var task =
 Task.Run(() => client.PostAsync("https://06k9wc7197.execute-api.us-east-1.amazonaws.com/dev/issue", content));
                task.Wait();
                var result = task.Result;
                var resultCode = result.StatusCode;
                var readTask = Task.Run(() => result.Content.ReadAsStringAsync());
                readTask.Wait();
                var resultContent = readTask.Result;

                switch (resultCode)
                {
                    case HttpStatusCode.OK:
                        if (MessageBox.Show("This was an SRS Crash!\nThis cool new Feature now automatically created a Ticket reporting your Crash!\nWe will try to figure our your issue and maybe go to the Ticket and comment your Discord so we can reach out.\n\nWould you like to see the Issue?", "Open Issue", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            System.Diagnostics.Process.Start(resultContent);
                        }
                        break;
                    case HttpStatusCode.BadRequest:
                        MessageBox.Show("The SRS Crash Reporting tool did not create a Crash Report! \nThis is intentional, because this build does not have a valid Version.");
                        break;
                    case HttpStatusCode.InternalServerError:
                        MessageBox.Show($"The Creation of the Crash Report failed: \n\n{resultContent}\n\nPlease report this in the SRS Discord!");
                        break;
                }
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show("No log file found! Issue could not be created.");
            }
#endif
                
            SentrySdk.CaptureException((Exception)e.ExceptionObject, scope =>
            {
                scope.AddAttachment("clientlog.txt");
            });
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int FreeConsole();
    }
}