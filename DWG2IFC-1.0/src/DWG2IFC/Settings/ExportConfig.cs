using System;
using System.IO;
using System.Text.Json;

namespace Plant3D.IfcExporter.Settings
{
    public enum IfcSchemaVersion
    {
        IFC4,
        IFC2X3
    }

    public enum ExportScopeOption
    {
        EntireDrawing,
        SelectedObjects,
        VisibleLayersOnly
    }

    public class ExportConfig
    {
        public IfcSchemaVersion Schema { get; set; } = IfcSchemaVersion.IFC4;
        public ExportScopeOption Scope { get; set; } = ExportScopeOption.EntireDrawing;
        public string FilterLineNumber { get; set; } = string.Empty;

        // Geometry settings
        public double WeldingToleranceMm { get; set; } = 0.5; // mm
        public bool UseAnalyticalPipes { get; set; } = true;
        public bool ExportInsulation { get; set; } = true;

        // Properties settings
        public bool ExportPlantProperties { get; set; } = true;
        public bool ExportCadProperties { get; set; } = true;
        public bool SkipEmptyProperties { get; set; } = true;
        public string PropertySetPrefix { get; set; } = "Pset_Plant3D";

        // Spatial & Units
        public string ProjectName { get; set; } = "Plant Project";
        public string SiteName { get; set; } = "Industrial Site";
        public string BuildingName { get; set; } = "Process Plant";
        public string StoreyName { get; set; } = "Level 0";
        public bool UseMillimetres { get; set; } = true;
        public bool OffsetToOrigin { get; set; } = false;
        public double CustomOffsetX { get; set; } = 0.0;
        public double CustomOffsetY { get; set; } = 0.0;
        public double CustomOffsetZ { get; set; } = 0.0;

        // Output
        public string OutputFilePath { get; set; } = string.Empty;

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Plant3D_IfcExporter",
            "config.json"
        );

        public static ExportConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<ExportConfig>(json) ?? new ExportConfig();
                }
            }
            catch
            {
                // Fallback to defaults
            }
            return new ExportConfig();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // Silently ignore configuration save errors
            }
        }
    }
}
