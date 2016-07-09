using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace Installer
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone";
        private readonly string currentDirectory;

        private readonly string currentPath;

        public MainWindow()
        {
            InitializeComponent();

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
            currentPath = Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            currentDirectory = Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }
        }


        private string ReadPath(string key)
        {
            var srPath = (string) Registry.GetValue(REG_PATH,
                key,
                "");

            return srPath == null ? "" : srPath;
        }

        private void WritePath(string path, string key)
        {
            Registry.SetValue(REG_PATH,
                key,
                path);
        }


        private void DeleteRegKeys()
        {
            Registry.SetValue(REG_PATH,
                "SRPathStandalone",
                "");
            Registry.SetValue(REG_PATH,
                "ScriptsPath",
                "");
        }


        //
        private bool Is_SimpleRadio_running()
        {
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Equals("sr-overlay") ||
                    clsProcess.ProcessName.ToLower().Equals("sr-overlay"))
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

        private void Install_Release(object sender, RoutedEventArgs e)
        {
            if (Is_SimpleRadio_running())
            {
                MessageBox.Show("Please close SimpleRadio Overlay before updating!", "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            // string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Saved Games\\";
            var paths = FindValidDCSFolders(dcsScriptsPath.Text);

            if (paths.Count == 0)
            {
                MessageBox.Show(
                    "Unable to find DCS Profile in Saved Games!\n\nPlease check the path to Saved Games folder",
                    "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Error);


                return;
            }
            foreach (var path in paths)
            {
                InstallScripts(path + "\\Scripts");
            }

            //install program
            InstallProgram(srPath.Text);

            WritePath(srPath.Text, "SRPathStandalone");
            WritePath(dcsScriptsPath.Text, "ScriptsPath");


            MessageBox.Show("Installation / Update Completed Succesfully!", "SR Standalone Installer",
                MessageBoxButton.OK, MessageBoxImage.Information);

            //open to installation location
            Process.Start("explorer.exe", srPath.Text);
        }

        public List<string> FindValidDCSFolders(string path)
        {
            var paths = new List<string>();

            var variants = new List<string>();
            variants.Add("DCS");
            variants.Add("DCS.openbeta");
            variants.Add("DCS.openalpha");

            foreach (var variant in variants)
            {
                if (Directory.Exists(path + "\\" + variant))
                {
                    paths.Add(path + "\\" + variant);
                }
            }
            return paths;
        }

        public void InstallProgram(string path)
        {
            if (Directory.Exists(path) && File.Exists(path + "\\SR-ClientRadio.exe"))
            {
                DeleteDirectory(path);
            }
            Directory.CreateDirectory(path);


            File.Copy(currentDirectory + "\\opus.dll", path + "\\opus.dll", true);
            File.Copy(currentDirectory + "\\SR-ClientRadio.exe", path + "\\SR-ClientRadio.exe", true);
            File.Copy(currentDirectory + "\\SR-Server.exe", path + "\\SR-Server.exe", true);
            File.Copy(currentDirectory + "\\SR-Overlay.exe", path + "\\SR-Overlay.exe", true);
            File.Copy(currentDirectory + "\\Installer.exe", path + "\\Installer.exe", true);
            File.Copy(currentDirectory + "\\DCS-SimpleRadioStandalone.lua", path + "\\DCS-SimpleRadioStandalone.lua",
                true);
        }

        private void InstallScripts(string path)
        {
            //if scripts folder doesnt exist, create it
            Directory.CreateDirectory(path);

            var write = true;
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    contents =
                        contents.Replace(
                            "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                            "");
                    contents = contents.Trim();

                    File.WriteAllText(path + "\\Export.lua", contents);
                }
            }

            if (write)
            {
                var writer = File.AppendText(path + "\\Export.lua");

                writer.WriteLine(
                    "\n  local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])\n");
                writer.Close();
            }
            else
            {
                var writer = File.CreateText(path + "\\Export.lua");

                writer.WriteLine(
                    "\n  local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])\n");
                writer.Close();
            }

            try
            {
                File.Copy(currentDirectory + "\\DCS-SimpleRadioStandalone.lua",
                    path + "\\DCS-SimpleRadioStandalone.lua", true);
            }
            catch (FileNotFoundException ex)
            {
                MessageBox.Show(
                    "Install files not found - Unable to install! \n\nMake sure you extract all the files in the zip then run the Installer",
                    "Not Unzipped", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
        }

        //http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true
        //Recursive Directory Delete
        public static void DeleteDirectory(string target_dir)
        {
            var files = Directory.GetFiles(target_dir);
            var dirs = Directory.GetDirectories(target_dir);

            foreach (var file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (var dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
        }


        private void UninstallSR()
        {
            if (Is_SimpleRadio_running())
            {
                MessageBox.Show("Please close SimpleRadio Standalone Overlay before removing!",
                    "SR Standalone Installer", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            var savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Saved Games\\";

            var dcsPath = savedGamesPath + "DCS";

            var scriptsPath = dcsPath + "\\Scripts";

            RemoveScripts(dcsPath + ".openalpha\\Scripts");
            RemoveScripts(dcsPath + "\\Scripts");

            if (Directory.Exists(srPath.Text) && File.Exists(srPath.Text + "\\SR-ClientRadio.exe"))
            {
                DeleteDirectory(srPath.Text);
            }

            DeleteRegKeys();

            MessageBox.Show("SR Standalone Removed Successfully!", "SR Standalone Installer",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }


        private void RemoveScripts(string path)
        {
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    contents = contents.Replace("dofile(lfs.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])", "");
                    contents =
                        contents.Replace(
                            "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                            "");
                    contents = contents.Trim();

                    File.WriteAllText(path + "\\Export.lua", contents);
                }
            }

            File.Delete(path + "\\DCS-SimpleRadioStandalone.lua");
        }

        private void Remove_Plugin(object sender, RoutedEventArgs e)
        {
            UninstallSR();
        }
    }
}