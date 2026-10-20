using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Plant3D.IfcExporter.Setup
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            bool installed = InstallerService.IsInstalled();
            TxtPath.Text = "Path: " + InstallerService.InstalledBundlePath;

            if (installed)
            {
                TxtStatus.Text = "✔ Installed and ready";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                BtnInstall.Content = "Reinstall / Update";
                BtnUninstall.IsEnabled = true;
            }
            else
            {
                TxtStatus.Text = "✖ Not installed";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
                BtnInstall.Content = "Install plugin";
                BtnUninstall.IsEnabled = false;
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            var res = InstallerService.Install();
            if (res.Success)
            {
                RefreshStatus();
                MessageBox.Show(
                    "Plugin installed successfully!\n\nAutoCAD and Plant 3D (versions 2025 and 2026) are supported.\n\nRibbon tab: 'DWG2IFC'\nCommand line commands:\nIFCEXPORT\nPLANTTOIFC\nIFCRIBBON",
                    "Installation complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(res.Message, "Installation error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Are you sure you want to uninstall the Plant 3D IFC Exporter from this system?",
                "Confirm uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (confirm != MessageBoxResult.Yes) return;

            var res = InstallerService.Uninstall();
            if (res.Success)
            {
                RefreshStatus();
                MessageBox.Show(res.Message, "Uninstall complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(res.Message, "Uninstall error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Directory.Exists(InstallerService.InstalledBundlePath)
                    ? InstallerService.InstalledBundlePath
                    : InstallerService.TargetPluginsDir;

                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open folder: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0") { UseShellExecute = true });
            }
            catch
            {
            }
        }
    }
}
