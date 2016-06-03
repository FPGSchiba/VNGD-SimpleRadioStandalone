using System;
using System.Collections.Generic;
using System.Linq;
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
using MahApps.Metro.Controls;
using System.Diagnostics;
using Microsoft.Win32;
using System.IO;

namespace Installer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone";

        string currentPath;
        string currentDirectory;

        public MainWindow()
        {
            InitializeComponent();

            System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = fvi.FileVersion;

            this.intro.Content = this.intro.Content + " v" + version;

            //allows click and drag anywhere on the window
            this.containerPanel.MouseLeftButtonDown += GridPanel_MouseLeftButtonDown;

            string srPathStr = ReadSRPath();
            if (srPathStr != "")
            {
                srPath.Text = srPathStr;
            }

            //To get the location the assembly normally resides on disk or the install directory
            currentPath = System.Reflection.Assembly.GetExecutingAssembly().CodeBase;

            //once you have the path you get the directory with:
            currentDirectory = System.IO.Path.GetDirectoryName(currentPath);

            if (currentDirectory.StartsWith("file:\\"))
            {
                currentDirectory = currentDirectory.Replace("file:\\", "");
            }
        }


        private string ReadSRPath()
        {
            string srPath = (string)Registry.GetValue(REG_PATH,
              "SRPathStandalone",
              "");

            return srPath == null ? "" : srPath;
        }

        private void WriteSRPath(String path)
        {
            Registry.SetValue(REG_PATH,
              "SRPathStandalone",
              path);
        }

        private void DeleteRegKeys()
        {
            Registry.SetValue(REG_PATH,
              "SRPathStandalone",
              "");
        }


        //
        private bool Is_SimpleRadio_running()
        {
            foreach (Process clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Equals("sr-overlay") || clsProcess.ProcessName.ToLower().Equals("sr-overlay"))
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
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();
            System.Windows.Forms.DialogResult result = dlg.ShowDialog();
            if (result.ToString() == "OK")
            {
                // Open document
                string filename = dlg.SelectedPath;

                if (!filename.EndsWith("\\"))
                {
                    filename = filename + "\\";
                }
                filename = filename + "DCS-SimpleRadio-Standalone\\";

                srPath.Text = filename;
            }
        }

        private void Install_Beta(object sender, RoutedEventArgs e)
        {
            InstallSR(true);
        }

        private void Install_Release(object sender, RoutedEventArgs e)
        {
            InstallSR(false);
        }

        private void InstallSR(bool beta)
        {
            if (this.Is_SimpleRadio_running())
            {
                MessageBox.Show("Please close SimpleRadio Overlay before updating!", "SR Standalone Installer",
                  MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Saved Games\\";

            string dcsPath = savedGamesPath + "DCS";

            if (beta)
            {
                dcsPath = dcsPath + ".openalpha";
            }

            if (!Directory.Exists(dcsPath))
            {
                if (beta)
                {
                    MessageBox.Show("Unable to find DCS Open Beta Profile in Saved Games", "SR Standalone Installer",
                      MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show("Unable to find DCS Profile in Saved Games", "SR Standalone Installer",
                      MessageBoxButton.OK, MessageBoxImage.Error);
                }

                return;
            }

            string scriptsPath = dcsPath + "\\Scripts";

            InstallScripts(scriptsPath);

            //install program
            InstallProgram(this.srPath.Text);

            //TODO save registry settings
            WriteSRPath(this.srPath.Text);


            MessageBox.Show("Installation / Update Completed Succesfully!", "SR Standalone Installer",
              MessageBoxButton.OK, MessageBoxImage.Information);

            //open to installation location
            System.Diagnostics.Process.Start("explorer.exe", (this.srPath.Text));
        }

        public void InstallProgram(string path)
        {
            if (Directory.Exists(path) && File.Exists(path + "\\SR-ClientRadio.exe"))
            {
                DeleteDirectory(path);
            }
            Directory.CreateDirectory(path);


            File.Copy(currentDirectory + "\\opus.dll", path + "\\opus.dll",true);
            File.Copy(currentDirectory + "\\SR-ClientRadio.exe", path + "\\SR-ClientRadio.exe",true);
            File.Copy(currentDirectory + "\\SR-Server.exe", path + "\\SR-Server.exe",true);
            File.Copy(currentDirectory + "\\SR-Overlay.exe", path + "\\SR-Overlay.exe",true);
            File.Copy(currentDirectory + "\\Installer.exe", path + "\\Installer.exe",true);
            File.Copy(currentDirectory + "\\DCS-SimpleRadioStandalone.lua", path + "\\DCS-SimpleRadioStandalone.lua",true);
        }

        private void InstallScripts(string path)
        {
            //if scripts folder doesnt exist, create it
            Directory.CreateDirectory(path);

            bool write = true;
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                String contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    contents =
                      contents.Replace(
                        "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])", "");
                    contents = contents.Trim();

                    File.WriteAllText(path + "\\Export.lua", contents);
                }
            }

            if (write)
            {
                StreamWriter writer = File.AppendText(path + "\\Export.lua");

                writer.WriteLine(
                  "\n  local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])\n");
                writer.Close();
            }
            else
            {
                StreamWriter writer = File.CreateText(path + "\\Export.lua");

                writer.WriteLine(
                  "\n  local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])\n");
                writer.Close();
               
            }

            File.Copy(currentDirectory + "\\DCS-SimpleRadioStandalone.lua",
              path + "\\DCS-SimpleRadioStandalone.lua",true);
        }

        //http://stackoverflow.com/questions/329355/cannot-delete-directory-with-directory-deletepath-true
        //Recursive Directory Delete
        public static void DeleteDirectory(string target_dir)
        {
            string[] files = Directory.GetFiles(target_dir);
            string[] dirs = Directory.GetDirectories(target_dir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectory(dir);
            }

            Directory.Delete(target_dir, false);
        }


        private void UninstallSR()
        {
            if (this.Is_SimpleRadio_running())
            {
                MessageBox.Show("Please close SimpleRadio Standalone Overlay before removing!",
                  "SR Standalone Installer", MessageBoxButton.OK, MessageBoxImage.Error);

                return;
            }

            string savedGamesPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Saved Games\\";

            string dcsPath = savedGamesPath + "DCS";

            string scriptsPath = dcsPath + "\\Scripts";

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
                String contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    contents = contents.Replace("dofile(lfs.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])", "");
                    contents =
                      contents.Replace(
                        "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])", "");
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