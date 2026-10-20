using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Plant3D.IfcExporter.Setup
{
    public partial class App : Application
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            if (e.Args.Length > 0)
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
                var arg = e.Args[0].ToLowerInvariant();

                if (arg == "--install" || arg == "-i")
                {
                    var res = InstallerService.Install();
                    Console.WriteLine(res.Message);
                    Environment.Exit(res.Success ? 0 : 1);
                    return;
                }
                else if (arg == "--uninstall" || arg == "-u")
                {
                    var res = InstallerService.Uninstall();
                    Console.WriteLine(res.Message);
                    Environment.Exit(res.Success ? 0 : 1);
                    return;
                }
                else if (arg == "--status" || arg == "-s")
                {
                    bool inst = InstallerService.IsInstalled();
                    Console.WriteLine($"Plant 3D IFC Exporter installed: {inst}");
                    if (inst) Console.WriteLine($"Path: {InstallerService.InstalledBundlePath}");
                    Environment.Exit(0);
                    return;
                }
                else if (arg == "--help" || arg == "-h" || arg == "/?")
                {
                    Console.WriteLine("Plant 3D IFC Exporter Setup Utility");
                    Console.WriteLine("Usage:");
                    Console.WriteLine("  Plant3D_IfcExporter_Setup.exe            (Graphical interface)");
                    Console.WriteLine("  Plant3D_IfcExporter_Setup.exe --install   (Silent install)");
                    Console.WriteLine("  Plant3D_IfcExporter_Setup.exe --uninstall (Silent uninstall)");
                    Console.WriteLine("  Plant3D_IfcExporter_Setup.exe --status    (Check status)");
                    Environment.Exit(0);
                    return;
                }
            }

            // Otherwise, show GUI
            var mainWin = new MainWindow();
            mainWin.Show();
        }
    }
}
