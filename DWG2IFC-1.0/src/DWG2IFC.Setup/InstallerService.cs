using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Plant3D.IfcExporter.Setup
{
    public static class InstallerService
    {
        public const string BundleName = "DWG2IFC.bundle";
        public const string LegacyBundleName = "Plant3D_IfcExporter.bundle";

        public static string TargetPluginsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Autodesk",
            "ApplicationPlugins"
        );

        public static string InstalledBundlePath => Path.Combine(TargetPluginsDir, BundleName);

        public static string ProgramDataPluginsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Autodesk",
            "ApplicationPlugins"
        );

        public static string ProgramDataBundlePath => Path.Combine(ProgramDataPluginsDir, BundleName);

        public static bool IsInstalled()
        {
            var manifest = Path.Combine(InstalledBundlePath, "PackageContents.xml");
            var dll = Path.Combine(InstalledBundlePath, "Contents", "net8.0", "DWG2IFC.dll");
            return File.Exists(manifest) && File.Exists(dll);
        }

        public static (bool Success, string Message) Install()
        {
            try
            {
                if (!Directory.Exists(TargetPluginsDir))
                {
                    Directory.CreateDirectory(TargetPluginsDir);
                }

                if (Directory.Exists(InstalledBundlePath))
                {
                    Directory.Delete(InstalledBundlePath, true);
                }

                // Clean up any legacy bundles
                var legacyUserBundle = Path.Combine(TargetPluginsDir, LegacyBundleName);
                if (Directory.Exists(legacyUserBundle)) { try { Directory.Delete(legacyUserBundle, true); } catch { } }

                Directory.CreateDirectory(InstalledBundlePath);

                // Try to extract from embedded resource first
                var asm = Assembly.GetExecutingAssembly();
                string? resName = Array.Find(asm.GetManifestResourceNames(), n => n.EndsWith("bundle.zip", StringComparison.OrdinalIgnoreCase));
                using var stream = resName != null ? asm.GetManifestResourceStream(resName) : null;

                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(InstalledBundlePath, true);
                }
                else
                {
                    // Fallback to local files if running from dev directory
                    var localBundleDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bundle");
                    if (!Directory.Exists(localBundleDir))
                    {
                        localBundleDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "bundle", BundleName));
                    }

                    if (Directory.Exists(localBundleDir))
                    {
                        CopyDirectory(localBundleDir, InstalledBundlePath);
                    }
                    else
                    {
                        return (false, "bundle.zip resource or local bundle folder not found.");
                    }
                }

                if (!IsInstalled())
                {
                    return (false, "Files copied, but plugin integrity check failed.");
                }

                // Also sync to ProgramData if writable
                try
                {
                    if (!Directory.Exists(ProgramDataPluginsDir))
                    {
                        Directory.CreateDirectory(ProgramDataPluginsDir);
                    }
                    if (Directory.Exists(ProgramDataBundlePath))
                    {
                        Directory.Delete(ProgramDataBundlePath, true);
                    }
                    CopyDirectory(InstalledBundlePath, ProgramDataBundlePath);
                }
                catch
                {
                    // Non-admin mode might not write to ProgramData, which is totally fine
                }

                // Create Desktop shortcut
                CreateDesktopShortcut();

                return (true, $"Plugin installed and registered successfully at:\n{InstalledBundlePath}\n\nDesktop shortcut created.");
            }
            catch (Exception ex)
            {
                return (false, $"Installation error: {ex.Message}");
            }
        }

        public static (bool Success, string Message) Uninstall()
        {
            try
            {
                bool anyRemoved = false;

                if (Directory.Exists(InstalledBundlePath))
                {
                    Directory.Delete(InstalledBundlePath, true);
                    anyRemoved = true;
                }

                try
                {
                    if (Directory.Exists(ProgramDataBundlePath))
                    {
                        Directory.Delete(ProgramDataBundlePath, true);
                        anyRemoved = true;
                    }
                }
                catch { }

                // Clean up settings folder
                var settingsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Plant3D_IfcExporter"
                );
                if (Directory.Exists(settingsDir))
                {
                    try { Directory.Delete(settingsDir, true); } catch { }
                }

                // Remove Desktop shortcut
                RemoveDesktopShortcut();

                if (!anyRemoved)
                {
                    return (true, "Plugin was not installed (folder not found).");
                }

                return (true, "Plugin, shortcut, and settings removed successfully.");
            }
            catch (Exception ex)
            {
                return (false, $"Uninstall error: {ex.Message}");
            }
        }

        public static void CreateDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath)) return;

                string shortcutPath = Path.Combine(desktopPath, "DWG2IFC.lnk");

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DWG2IFC-Setup.exe");
                    if (!File.Exists(exePath))
                    {
                        exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plant3D_IfcExporter_Setup.exe");
                    }
                }

                if (!File.Exists(exePath)) return;

                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        dynamic shortcut = shell.CreateShortcut(shortcutPath);
                        shortcut.TargetPath = exePath;
                        shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
                        shortcut.Description = "DWG2IFC — AutoCAD & Plant 3D IFC Exporter. Original: baidurovlabs.ru. English fork: Wahid Hassani (github.com/OneAndOnlyWahidHassani). GPLv3.";
                        shortcut.IconLocation = $"{exePath},0";
                        shortcut.Save();
                    }
                }
            }
            catch
            {
                // Silently ignore if COM is blocked
            }
        }

        public static void RemoveDesktopShortcut()
        {
            try
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (string.IsNullOrEmpty(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                }

                if (!string.IsNullOrEmpty(desktopPath))
                {
                    string shortcutPath = Path.Combine(desktopPath, "DWG2IFC.lnk");
                    if (File.Exists(shortcutPath)) File.Delete(shortcutPath);
                    string legacyShortcut = Path.Combine(desktopPath, "AutoCAD IFC Exporter.lnk");
                    if (File.Exists(legacyShortcut)) File.Delete(legacyShortcut);
                }
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            foreach (DirectoryInfo subDir in dir.GetDirectories())
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir);
            }
        }
    }
}
