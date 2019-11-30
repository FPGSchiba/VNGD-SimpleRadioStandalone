using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using IWshRuntimeLibrary;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using File = System.IO.File;

namespace Installer
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone";
        private const string EXPORT_SRS_LUA = "pcall(function() local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Mods\\Tech\\DCS-SRS\\Scripts\\DCS-SimpleRadioStandalone.lua]]); end,nil);";
        private readonly string currentDirectory;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private ProgressBarDialog progressBarDialog = null;


        //   private readonly string currentPath;

        //TODO - Support new Mods/Tech/DCS-SRS method
        //Clear up old files and replace with new structure
        //Add button to set the SRS path in the registry
        //Merge pull request for enable / disable AGC

        public MainWindow()
        {
            SetupLogging();
            InitializeComponent();

            if (IsDCSRunning())
            {
                MessageBox.Show(
                    "DCS must now be closed before continuing the installation!\n\nClose DCS and please try again.",
                    "Please Close DCS",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Logger.Warn("DCS is Running - Installer quit");

                Environment.Exit(0);

                
                return;
            }

            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = fvi.FileVersion;

            intro.Content = intro.Content + " v" + version;

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += GridPanel_MouseLeftButtonDown;

            var srPathStr = ReadPath("SRPathStandalone");
            if (srPathStr != "")
            {
                srPath.Text = srPathStr;
            }

            var scriptsPath = ReadPath("ScriptsPath");
            if (scriptsPath != "")
            {
                dcsScriptsPath.Text = scriptsPath;
            }
            else
            {
                dcsScriptsPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                                      "\\Saved Games\\";
            }

            //To get the location the assembly normally resides on disk or the install directory
            var currentPath = Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            currentDirectory = Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }

            if (((App)Application.Current).Arguments.Length > 0)
            {
                if (((App)Application.Current).Arguments[0].Equals("-autoupdate"))
                {
                    Logger.Info("Silent Installer Running");
                    InstallReleaseButton(null, null);
                }
            }
        }


        private void SetupLogging()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string logFilePath = Path.Combine(baseDirectory, "installer-log.txt");
            string oldLogFilePath = Path.Combine(baseDirectory, "install-log.old.txt");

            FileInfo logFileInfo = new FileInfo(logFilePath);
            // Cleanup logfile if > 100MB, keep one old file copy
            if (logFileInfo.Exists && logFileInfo.Length >= 104857600)
            {
                if (File.Exists(oldLogFilePath))
                {
                    try
                    {
                        File.Delete(oldLogFilePath);
                    }
                    catch (Exception) { }
                }

                try
                {
                    File.Move(logFilePath, oldLogFilePath);
                }
                catch (Exception) { }
            }

            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget();

            fileTarget.FileName = "${basedir}/installer-log.txt";
            fileTarget.Layout =
                @"${longdate} | ${logger} | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=2}";

            var wrapper = new AsyncTargetWrapper(fileTarget, 5000, AsyncTargetWrapperOverflowAction.Discard);
            config.AddTarget("file", wrapper);

#if DEBUG
            config.LoggingRules.Add( new LoggingRule("*", LogLevel.Debug, fileTarget));
#else
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, fileTarget));
#endif

            LogManager.Configuration = config;

        }

        private async void  InstallReleaseButton(object sender, RoutedEventArgs e)
        {
            InstallButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;

            InstallButton.Content = "Installing...";

            progressBarDialog = new ProgressBarDialog();
            progressBarDialog.Owner = this;
            progressBarDialog.Show();

            var srsPath = srPath.Text;
            var dcScriptsPath = dcsScriptsPath.Text;
            var shortcut = CreateStartMenuShortcut.IsChecked ?? true;

            new Action(async () =>
            {
                int result = await Task.Run<int>(() => InstallRelease(srsPath,dcScriptsPath, shortcut));
                if (result == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        {
                            InstallButton.IsEnabled = true;
                            RemoveButton.IsEnabled = true;
                            InstallButton.Content = "Install";
                        }
                    ); //end-invoke
                    progressBarDialog.UpdateProgress(true, "Error");

                }
                else if (result == 1)
                {
                    progressBarDialog.UpdateProgress(true, "Installed SRS Successfully!");

                    Logger.Info($"Installed SRS Successfully!");
                
                    //open to installation location
                    Process.Start("explorer.exe", srPath.Text);
                    Environment.Exit(0);
                }
                else
                {
                    progressBarDialog.UpdateProgress(true, "Error with Installation");

                    MessageBox.Show(
                        "Error with installation - please post your installer-log.txt on the SRS Discord for Support",
                        "Installation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                
                    Process.Start("https://discord.gg/vqxAw7H");
                    Process.Start("explorer.exe", Directory.GetCurrentDirectory());
                    Environment.Exit(0);
                }
            }).Invoke();

        }

        private int InstallRelease(string srPath, string dcsScriptsPath, bool shortcut)
        {
            try
            {
                QuitSimpleRadio();

                var paths = FindValidDCSFolders(dcsScriptsPath);

                if (paths.Count == 0)
                {

                    MessageBox.Show(
                        "Unable to find DCS Folder in Saved Games!\n\nPlease check the path to the \"Saved Games\" folder\n\nMake sure you are selecting the \"Saved Games\" folder - NOT the DCS folder inside \"Saved Games\" and NOT the DCS installation directory",
                        "SR Standalone Installer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return 0;
                }

                Logger.Info($"Installing - Paths: \nProgram:{srPath} \nDCS:{dcsScriptsPath} ");

                ClearVersionPreModsTechDCS(srPath, dcsScriptsPath);
                ClearVersionPostModsTechDCS(srPath, dcsScriptsPath);

                foreach (var path in paths)
                {
                    InstallScripts(path);
                }

                //install program
                InstallProgram(srPath);

                WritePath(srPath, "SRPathStandalone");
                WritePath(dcsScriptsPath, "ScriptsPath");

                if (shortcut)
                {
                    InstallShortcuts(srPath);
                }

                InstallVCRedist();

                string message = "Installation / Update Completed Successfully!\nInstalled DCS Scripts to: \n";

                foreach (var path in paths)
                {
                    message += ("\n" + path);
                }

                MessageBox.Show(message, "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                return 1;

            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error Running Installer");

            
                return -1;
            }
        }

        private void InstallVCRedist()
        {
            progressBarDialog.UpdateProgress(false, $"Installing VC Redist x64");
            Process.Start(Directory.GetCurrentDirectory() + "\\VC_redist.x64.exe", "/install /quiet /log \"vc_redist_2017_x64.log\"");
            progressBarDialog.UpdateProgress(false, $"Finished installing VC Redist x64");

        }


        private void ClearVersionPreModsTechDCS(string programPath, string dcsPath)
        {
            Logger.Info($"Removed previous SRS Version at {programPath} and {dcsPath}");
           
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                progressBarDialog.UpdateProgress(false, $"Clearing Previous SRS at  {path}");
                RemoveScriptsPreModsTechDCS(path + "\\Scripts");
            }

            Logger.Info($"Removed SRS program files at {programPath}");
            progressBarDialog.UpdateProgress(false, $"Clearing Previous SRS at  {programPath}");
            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(programPath + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(programPath + "\\opus.dll");
                DeleteFileIfExists(programPath + "\\speexdsp.dll");
                DeleteFileIfExists(programPath + "\\awacs-radios.json");
                DeleteFileIfExists(programPath + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(programPath + "\\SR-Server.exe");
                DeleteFileIfExists(programPath + "\\DCS-SimpleRadioStandalone.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRSGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-AutoConnectGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-OverlayGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-Overlay.dlg");
                DeleteFileIfExists(programPath + "\\serverlog.txt");
                DeleteFileIfExists(programPath + "\\clientlog.txt");
                DeleteFileIfExists(programPath + "\\DCS-SRS-hook.lua");
                DeleteFileIfExists(programPath + "\\AudioEffects\\KY-58-RX-1600.wav");
                DeleteFileIfExists(programPath + "\\AudioEffects\\KY-58-TX-1600.wav");
                DeleteFileIfExists(programPath + "\\AudioEffects\\Radio-RX-1600.wav");
                DeleteFileIfExists(programPath + "\\AudioEffects\\Radio-TX-1600.wav");
            }
            Logger.Info($"Finished clearing scripts and program Pre Mods ");

        }

        private void ClearVersionPostModsTechDCS(string programPath, string dcsPath)
        {
            Logger.Info($"Removed SRS Version Post Mods at {programPath} and {dcsPath}");
            
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                progressBarDialog.UpdateProgress(false, $"Removing SRS at {path}");
                RemoveScriptsPostModsTechDCS(path);
            }

            Logger.Info($"Removed SRS program files at {programPath}");
            progressBarDialog.UpdateProgress(false, $"Removing SRS at {programPath}");
            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(programPath + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(programPath + "\\opus.dll");
                DeleteFileIfExists(programPath + "\\speexdsp.dll");
                DeleteFileIfExists(programPath + "\\awacs-radios.json");
                DeleteFileIfExists(programPath + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(programPath + "\\SR-Server.exe");
                DeleteFileIfExists(programPath + "\\serverlog.txt");
                DeleteFileIfExists(programPath + "\\clientlog.txt");

                DeleteDirectory(programPath + "\\AudioEffects");
                DeleteDirectory(programPath + "\\Scripts");
            }
            Logger.Info($"Finished clearing scripts and program Post Mods ");
        }

        private void RemoveScriptsPostModsTechDCS(string path)
        {
            Logger.Info($"Removing SRS Scripts at {path}");
            //SCRIPTS folder
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    var lines = contents.Split('\n');

                    StringBuilder sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (!line.Contains("SimpleRadioStandalone.lua") && line.Trim().Length > 0)
                        {
                            sb.Append(line);
                            sb.Append("\n");
                        }
                        else
                        {
                            Logger.Info($"Removed SRS Scripts from Export.lua");
                        }
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
            }

            Logger.Info($"Removed Hooks file");
            //Hooks Folder
            DeleteFileIfExists(path + "\\Hooks\\DCS-SRS-Hook.lua");

            //MODs folder
            if (Directory.Exists(path+"\\Mods\\Tech\\DCS-SRS"))
            {
                Logger.Info($"Removed Mods/Tech/DCS-SRS folder");
                Directory.Delete(path+"\\Mods\\Tech\\DCS-SRS",true);
            }

            Logger.Info($"Finished Removing Mods/Tech & Scripts for SRS");
        }

        private static string ReadPath(string key)
        {
            var srPath = (string) Registry.GetValue(REG_PATH,
                key,
                "");

            return srPath ?? "";
        }

        private static void WritePath(string path, string key)
        {
            Registry.SetValue(REG_PATH,
                key,
                path);
        }


        private static void DeleteRegKeys()
        {
            try
            {
                Registry.SetValue(REG_PATH,
                    "SRPathStandalone",
                    "");
                Registry.SetValue(REG_PATH,
                    "ScriptsPath",
                    "");
            }
            catch (Exception ex)
            {
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE", true))
                {
                    key.DeleteSubKeyTree("DCS-SimpleRadioStandalone", false);
                    key.DeleteSubKeyTree("DCS-SR-Standalone", false);
                }
            }
            catch (Exception ex)
            {
            }
        }

        private void QuitSimpleRadio()
        {
            Logger.Info($"Closing SRS Client & Server");
#if DEBUG
            return;
#endif
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-server") || clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-client"))
                {
                    Logger.Info($"Found & Terminating {clsProcess.ProcessName}");
                    clsProcess.Kill();
                    clsProcess.WaitForExit(5000);
                    clsProcess.Dispose();

                    
                }
            }
            Logger.Info($"Closed SRS Client & Server");
        }

        private bool IsDCSRunning()
        {
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().Equals("dcs"))
                {
                    Logger.Info($"DCS is running");
                    return true;
                }
            }

            return false;
        }

        private void GridPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }


        private void Set_Install_Path(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result.ToString() == "OK")
            {
                // Open document
                var filename = dlg.SelectedPath;

                if (!filename.EndsWith("\\"))
                {
                    filename = filename + "\\";
                }
                filename = filename + "DCS-SimpleRadio-Standalone\\";

                srPath.Text = filename;
            }
        }

        private void Set_Scripts_Path(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result.ToString() == "OK")
            {
                // Open document
                var filename = dlg.SelectedPath;

                if (!filename.EndsWith("\\"))
                {
                    filename = filename + "\\";
                }

                dcsScriptsPath.Text = filename;
            }
        }


        private static List<string> FindValidDCSFolders(string path)
        {
            Logger.Info($"Finding DCS Saved Games Path");
            var paths = new List<string>();

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                var dirs = directory.Split('\\');

                var end = dirs[dirs.Length - 1];

                if (end.ToUpper().StartsWith("DCS.") || end.ToUpper().Equals("DCS"))
                {
                    Logger.Info($"Found DCS Saved Games Path {directory}");
                    paths.Add(directory);
                }
            }

            Logger.Info($"Finished Finding DCS Saved Games Path");

            return paths;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                }
            }
        }

        private void InstallProgram(string path)
        {
            Logger.Info($"Installing SRS Program to {path}");
            progressBarDialog.UpdateProgress(false, $"Installing SRS at {path}");
            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();

            progressBarDialog.UpdateProgress(false, $"Creating Directories at {path}");

            Logger.Info($"Creating Directories");
            CreateDirectory(path);
            CreateDirectory(path + "\\AudioEffects");
            CreateDirectory(path + "\\Scripts");

            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();
            progressBarDialog.UpdateProgress(false, $"Copying Program Files at {path}");

            Logger.Info($"Copying binaries");
            File.Copy(currentDirectory + "\\opus.dll", path + "\\opus.dll", true);
            File.Copy(currentDirectory + "\\speexdsp.dll", path + "\\speexdsp.dll", true);
            File.Copy(currentDirectory + "\\awacs-radios.json", path + "\\awacs-radios.json", true);
            
            File.Copy(currentDirectory + "\\SR-ClientRadio.exe", path + "\\SR-ClientRadio.exe", true);
            File.Copy(currentDirectory + "\\SR-Server.exe", path + "\\SR-Server.exe", true);
            File.Copy(currentDirectory + "\\SRS-AutoUpdater.exe", path + "\\SRS-AutoUpdater.exe", true);

            Logger.Info($"Copying directories");
            DirectoryCopy(currentDirectory+"\\AudioEffects", path+"\\AudioEffects");
            DirectoryCopy(currentDirectory + "\\Scripts", path + "\\Scripts");

            Logger.Info($"Finished installing SRS Program to {path}");

        }

        private void InstallShortcuts(string path)
        {
            Logger.Info($"Adding SRS Shortcut");
            string executablePath = Path.Combine(path, "SR-ClientRadio.exe");
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DCS-SRS Client.lnk");

            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);

            shortcut.Description = "DCS-SimpleRadio Standalone Client";
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = path;
            shortcut.Save();
        }

        private void InstallScripts(string path)
        {
            Logger.Info($"Installing Scripts to {path}");
            progressBarDialog.UpdateProgress(false, $"Creating Script folders @ {path}");
            //Scripts Path
            CreateDirectory(path+"\\Scripts");
            CreateDirectory(path+"\\Scripts\\Hooks");
            
            //Make Tech Path
            CreateDirectory(path+"\\Mods"); 
            CreateDirectory(path+"\\Mods\\Tech");
            CreateDirectory(path+"\\Mods\\Tech\\DCS-SRS");

            Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();

            progressBarDialog.UpdateProgress(false, $"Updating / Creating Export.lua @ {path}");
            Logger.Info($"Handling Export.lua");
            //does it contain an export.lua?
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                contents.Split('\n');

                if (contents.Contains("SimpleRadioStandalone.lua") &&!contents.Contains("Mods\\Tech\\DCS-SRS\\Scripts\\DCS-SimpleRadioStandalone.lua"))
                {
                    Logger.Info($"Updating existing Export.lua with existing SRS install");
                    var lines = contents.Split('\n');

                    StringBuilder sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (line.Contains("SimpleRadioStandalone.lua") )
                        {
                            sb.Append("\n");
                            sb.Append(EXPORT_SRS_LUA);
                            sb.Append("\n");
                        }
                        else if(line.Trim().Length>0)
                        {
                            sb.Append(line);
                            sb.Append("\n");
                        }
                        
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
                else
                {
                    Logger.Info($"Appending to existing Export.lua");
                    var writer = File.AppendText(path + "\\Scripts\\Export.lua");

                    writer.WriteLine("\n" + EXPORT_SRS_LUA + "\n");
                    writer.Close();
                }
            }
            else
            {
                Logger.Info($"Creating new Export.lua");
                var writer = File.CreateText(path + "\\Scripts\\Export.lua");

                writer.WriteLine("\n"+EXPORT_SRS_LUA+"\n");
                writer.Close();
            }


            //Now sort out Scripts//Hooks folder contents
            Logger.Info($"Creating / installing Hooks & Mods");
            progressBarDialog.UpdateProgress(false, $"Creating / installing Hooks & Mods @ {path}");
            try
            {
                File.Copy(currentDirectory + "\\Scripts\\Hooks\\DCS-SRS-hook.lua", path + "\\Scripts\\Hooks\\DCS-SRS-hook.lua",
                    true);
                DirectoryCopy(currentDirectory + "\\Scripts\\DCS-SRS",path+"\\Mods\\Tech\\DCS-SRS");
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(
                    "Install files not found - Unable to install! \n\nMake sure you extract all the files in the zip then run the Installer",
                    "Not Unzipped", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
            Logger.Info($"Scripts installed to {path}");

            progressBarDialog.UpdateProgress(false, $"Installed Hooks & Mods @ {path}");
        }

        public static void DeleteDirectory(string target_dir)
        {
            if (Directory.Exists(target_dir))
            {
                Directory.Delete(target_dir, true);
            }
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, temppath);
            }
            
        }

        private async Task<bool> UninstallSR(string srPath, string dcsScriptsPath)
        {
            try
            {
                QuitSimpleRadio();
                Application.Current.Dispatcher.Invoke(() =>
                    {
                        InstallButton.IsEnabled = false;
                        RemoveButton.IsEnabled = false;

                        RemoveButton.Content = "Removing...";
                    }
                ); //end-invoke

                progressBarDialog.UpdateProgress(false, $"Removing SRS");
                Logger.Info($"Removing - Paths: \nProgram:{srPath} \nDCS:{dcsScriptsPath} ");
                ClearVersionPreModsTechDCS(srPath, dcsScriptsPath);
                ClearVersionPostModsTechDCS(srPath, dcsScriptsPath);

                DeleteRegKeys();

                RemoveShortcuts();

                return true;

            }
            catch (Exception ex) 
            {
                Logger.Error(ex, "Error Running Uninstaller");
            }

            return false;

        }

        private void RemoveShortcuts()
        {
            Logger.Info($"Removed SRS Shortcut");
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DCS-SRS Client.lnk");

            DeleteFileIfExists(shortcutPath);
        }

        private void RemoveScriptsPreModsTechDCS(string path)
        {
            Logger.Info($"Removing SRS Pre Mods Scripts at {path}");
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    Logger.Info($"Removed SRS from Export.lua");
                    contents = contents.Replace("dofile(lfs.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                        "");
                    contents =
                        contents.Replace(
                            "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                            "");
                    contents = contents.Trim();

                    File.WriteAllText(path + "\\Export.lua", contents);
                }
            }

            DeleteFileIfExists(path + "\\DCS-SimpleRadioStandalone.lua");
            DeleteFileIfExists(path + "\\DCS-SRSGameGUI.lua");
            DeleteFileIfExists(path + "\\DCS-SRS-AutoConnectGameGUI.lua");
            DeleteFileIfExists(path + "\\DCS-SRS-Overlay.dlg");
            DeleteFileIfExists(path + "\\DCS-SRS-OverlayGameGUI.lua");
            DeleteFileIfExists(path + "\\Hooks\\DCS-SRS-Hook.lua");

            Logger.Info($"Removed all SRS Scripts at {path}");
        }

        private void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);

                // Create the rules
                var writerule = new FileSystemAccessRule(sid, FileSystemRights.Write, AccessControlType.Allow);

                var dir = Directory.CreateDirectory(path);

                dir.Refresh();
                //sleep! WTF directory is lagging behind state here...
                Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();

                var dSecurity = dir.GetAccessControl();
                dSecurity.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.FullControl,
                    InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                    PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                dir.SetAccessControl(dSecurity);
                dir.Refresh();
            }
        }


        private async void Remove_Plugin(object sender, RoutedEventArgs e)
        {
            progressBarDialog = new ProgressBarDialog();
            progressBarDialog.Owner = this;
            progressBarDialog.Show();
            progressBarDialog.UpdateProgress(false, "Uninstalling SRS");

            var result = await UninstallSR(srPath.Text,dcsScriptsPath.Text);
            if (result)
            {
                progressBarDialog.UpdateProgress(true, "Removed SRS Successfully!");
                Logger.Info($"Removed SRS Successfully!");

                MessageBox.Show(
                    "SR Standalone Removed Successfully!\n\nContaining folder left just in case you want favourites or frequencies",
                    "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                progressBarDialog.UpdateProgress(true, "Error with Uninstaller");
                MessageBox.Show(
                    "Error with uninstaller - please post your installer-log.txt on the SRS Discord for Support",
                    "Installation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Process.Start("https://discord.gg/vqxAw7H");

                Process.Start("explorer.exe", Directory.GetCurrentDirectory());

            }
            Environment.Exit(0);
        }
    }
}