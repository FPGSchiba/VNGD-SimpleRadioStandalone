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
using Microsoft.Win32;
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

        //   private readonly string currentPath;

        //TODO - Support new Mods/Tech/DCS-SRS method
        //Clear up old files and replace with new structure
        //Add button to set the SRS path in the registry
        //Merge pull request for enable / disable AGC

        public MainWindow()
        {
            InitializeComponent();

            if (IsDCSRunning())
            {
                MessageBox.Show(
                    "DCS must now be closed before continuing the installation!\n\nClose DCS and please try again.",
                    "Please Close DCS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
                    Install_Release(null, null);
                }
            }

        }


        private void Install_Release(object sender, RoutedEventArgs e)
        {
            QuitSimpleRadio();

            var paths = FindValidDCSFolders(dcsScriptsPath.Text);

            if (paths.Count == 0)
            {
                MessageBox.Show(
                    "Unable to find DCS Folder in Saved Games!\n\nPlease check the path to the \"Saved Games\" folder\n\nMake sure you are selecting the \"Saved Games\" folder - NOT the DCS folder inside \"Saved Games\" and NOT the DCS installation directory",
                    "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Error);


                return;
            }

            InstallButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;

            InstallButton.Content = "Installing...";

            ClearVersionPreModsTechDCS(srPath.Text, dcsScriptsPath.Text);
            ClearVersionPostModsTechDCS(srPath.Text, dcsScriptsPath.Text);

            foreach (var path in paths)
            {
                InstallScripts(path);
            }

            //install program
            InstallProgram(srPath.Text);

            WritePath(srPath.Text, "SRPathStandalone");
            WritePath(dcsScriptsPath.Text, "ScriptsPath");

            if (CreateStartMenuShortcut.IsChecked ?? true)
            {
                InstallShortcuts(srPath.Text);
            }

            string message = "Installation / Update Completed Succesfully!\nInstalled DCS Scripts to: \n";

            foreach (var path in paths)
            {
                message += ("\n" + path);
            }

            MessageBox.Show(message, "SR Standalone Installer",
                MessageBoxButton.OK, MessageBoxImage.Information);

            //open to installation location
            Process.Start("explorer.exe", srPath.Text);

            Environment.Exit(0);
        }

        private void ClearVersionPreModsTechDCS(string programPath, string dcsPath)
        {
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                RemoveScriptsPreModsTechDCS(path + "\\Scripts");
            }

            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(srPath.Text + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(srPath.Text + "\\opus.dll");
                DeleteFileIfExists(srPath.Text + "\\speexdsp.dll");
                DeleteFileIfExists(srPath.Text + "\\awacs-radios.json");
                DeleteFileIfExists(srPath.Text + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(srPath.Text + "\\SR-Server.exe");
                DeleteFileIfExists(srPath.Text + "\\DCS-SimpleRadioStandalone.lua");
                DeleteFileIfExists(srPath.Text + "\\DCS-SRSGameGUI.lua");
                DeleteFileIfExists(srPath.Text + "\\DCS-SRS-AutoConnectGameGUI.lua");
                DeleteFileIfExists(srPath.Text + "\\DCS-SRS-OverlayGameGUI.lua");
                DeleteFileIfExists(srPath.Text + "\\DCS-SRS-Overlay.dlg");
                DeleteFileIfExists(srPath.Text + "\\serverlog.txt");
                DeleteFileIfExists(srPath.Text + "\\clientlog.txt");
                DeleteFileIfExists(srPath.Text + "\\DCS-SRS-hook.lua");
                DeleteFileIfExists(srPath.Text + "\\AudioEffects\\KY-58-RX-1600.wav");
                DeleteFileIfExists(srPath.Text + "\\AudioEffects\\KY-58-TX-1600.wav");
                DeleteFileIfExists(srPath.Text + "\\AudioEffects\\Radio-RX-1600.wav");
                DeleteFileIfExists(srPath.Text + "\\AudioEffects\\Radio-TX-1600.wav");
            }
            

        }

        private void ClearVersionPostModsTechDCS(string programPath, string dcsPath)
        {
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                RemoveScriptsPostModsTechDCS(path);
            }

            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(srPath.Text + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(srPath.Text + "\\opus.dll");
                DeleteFileIfExists(srPath.Text + "\\speexdsp.dll");
                DeleteFileIfExists(srPath.Text + "\\awacs-radios.json");
                DeleteFileIfExists(srPath.Text + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(srPath.Text + "\\SR-Server.exe");
                DeleteFileIfExists(srPath.Text + "\\serverlog.txt");
                DeleteFileIfExists(srPath.Text + "\\clientlog.txt");

                DeleteDirectory(srPath.Text+"\\AudioEffects");
                DeleteDirectory(srPath.Text + "\\Scripts");
            }
        }

        private void RemoveScriptsPostModsTechDCS(string path)
        {
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
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
            }
            //Hooks Folder
            DeleteFileIfExists(path + "\\Hooks\\DCS-SRS-Hook.lua");

            //MODs folder
            if (Directory.Exists(path+"\\Mods\\Tech\\DCS-SRS"))
            {
                Directory.Delete(path+"\\Mods\\Tech\\DCS-SRS",true);
            }

            
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
#if DEBUG
            return;
#endif
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-server") || clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-client"))
                {
                    clsProcess.Kill();
                    clsProcess.WaitForExit(5000);
                    clsProcess.Dispose();
                }
            }
        }

        private bool IsDCSRunning()
        {
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().Equals("dcs"))
                {
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
            var paths = new List<string>();

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                var dirs = directory.Split('\\');

                var end = dirs[dirs.Length - 1];

                if (end.ToUpper().StartsWith("DCS.") || end.ToUpper().Equals("DCS"))
                {
                    paths.Add(directory);
                }
            }

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
            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();

            CreateDirectory(path);
            CreateDirectory(path + "\\AudioEffects");
            CreateDirectory(path + "\\Scripts");

            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();

            File.Copy(currentDirectory + "\\opus.dll", path + "\\opus.dll", true);
            File.Copy(currentDirectory + "\\speexdsp.dll", path + "\\speexdsp.dll", true);
            File.Copy(currentDirectory + "\\awacs-radios.json", path + "\\awacs-radios.json", true);
            
            File.Copy(currentDirectory + "\\SR-ClientRadio.exe", path + "\\SR-ClientRadio.exe", true);
            File.Copy(currentDirectory + "\\SR-Server.exe", path + "\\SR-Server.exe", true);
            File.Copy(currentDirectory + "\\SRS-AutoUpdater.exe", path + "\\SRS-AutoUpdater.exe", true);

            DirectoryCopy(currentDirectory+"\\AudioEffects", path+"\\AudioEffects");
            DirectoryCopy(currentDirectory + "\\Scripts", path + "\\Scripts");

        }

        private void InstallShortcuts(string path)
        {
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
            //Scripts Path
            CreateDirectory(path+"\\Scripts");
            CreateDirectory(path+"\\Scripts\\Hooks");
            
            //Make Tech Path
            CreateDirectory(path+"\\Mods"); 
            CreateDirectory(path+"\\Mods\\Tech");
            CreateDirectory(path+"\\Mods\\Tech\\DCS-SRS");

            Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();

            //does it contain an export.lua?
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                contents.Split('\n');

                if (contents.Contains("SimpleRadioStandalone.lua") &&!contents.Contains("Mods\\Tech\\DCS-SRS\\Scripts\\DCS-SimpleRadioStandalone.lua"))
                {
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
                    var writer = File.AppendText(path + "\\Scripts\\Export.lua");

                    writer.WriteLine("\n" + EXPORT_SRS_LUA + "\n");
                    writer.Close();
                }
            }
            else
            {
                var writer = File.CreateText(path + "\\Scripts\\Export.lua");

                writer.WriteLine("\n"+EXPORT_SRS_LUA+"\n");
                writer.Close();
            }


            //Now sort out Scripts//Hooks folder contents
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

        private void UninstallSR()
        {
            QuitSimpleRadio();

            InstallButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;

            InstallButton.Content = "Removing...";

            ClearVersionPreModsTechDCS(srPath.Text, dcsScriptsPath.Text);
            ClearVersionPostModsTechDCS(srPath.Text, dcsScriptsPath.Text);

            DeleteRegKeys();

            RemoveShortcuts();

            MessageBox.Show(
                "SR Standalone Removed Successfully!\n\nContaining folder left just in case you want favourites or frequencies",
                "SR Standalone Installer",
                MessageBoxButton.OK, MessageBoxImage.Information);

            Environment.Exit(0);
        }

        private void RemoveShortcuts()
        {
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DCS-SRS Client.lnk");

            DeleteFileIfExists(shortcutPath);
        }

        private void RemoveScriptsPreModsTechDCS(string path)
        {
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
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


        private void Remove_Plugin(object sender, RoutedEventArgs e)
        {
            UninstallSR();
        }
    }
}