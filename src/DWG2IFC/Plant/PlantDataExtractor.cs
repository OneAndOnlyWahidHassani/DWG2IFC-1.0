using System;
using System.Collections.Generic;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;

namespace Plant3D.IfcExporter.Plant
{
    public class PlantComponentData
    {
        public string ClassName { get; set; } = "Solid";
        public string PartType { get; set; } = string.Empty;
        public string PartSubType { get; set; } = string.Empty;
        public string LineNumberTag { get; set; } = string.Empty;
        public string Spec { get; set; } = string.Empty;
        public string Size { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Material { get; set; } = string.Empty;
        public string Tag { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Layer { get; set; } = string.Empty;
        public string Handle { get; set; } = string.Empty;
        public Dictionary<string, string> CustomProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class PlantDataExtractor
    {
        private static bool _dataLinksInitialized = false;
        private static object? _cachedDataLinksManager = null;
        private static MethodInfo? _hasLinksMethod = null;
        private static MethodInfo? _findAcPpRowIdMethod = null;
        private static MethodInfo? _getAllPropertiesMethod = null;

        private static void InitializeDataLinks()
        {
            if (_dataLinksInitialized) return;
            _dataLinksInitialized = true;

            try
            {
                // Attempt to resolve PlantApplication and Piping project
                var plantAppType = Type.GetType("Autodesk.ProcessPower.PlantInstance.PlantApplication, PnPProjectManagerMgd");
                if (plantAppType != null)
                {
                    var currentProjectProp = plantAppType.GetProperty("CurrentProject", BindingFlags.Public | BindingFlags.Static);
                    var project = currentProjectProp?.GetValue(null);
                    if (project != null)
                    {
                        var partsProp = project.GetType().GetProperty("ProjectParts");
                        var parts = partsProp?.GetValue(project) as System.Collections.IDictionary;
                        if (parts != null && parts.Contains("Piping"))
                        {
                            var pipingPart = parts["Piping"];
                            var dlmProp = pipingPart?.GetType().GetProperty("DataLinksManager");
                            _cachedDataLinksManager = dlmProp?.GetValue(pipingPart);
                        }
                    }
                }

                if (_cachedDataLinksManager != null)
                {
                    var type = _cachedDataLinksManager.GetType();
                    _hasLinksMethod = type.GetMethod("HasLinks", new[] { typeof(ObjectId) });
                    _findAcPpRowIdMethod = type.GetMethod("FindAcPpRowId", new[] { typeof(ObjectId) });
                    _getAllPropertiesMethod = type.GetMethod("GetAllProperties", new[] { typeof(int), typeof(bool) });
                }
            }
            catch
            {
                // Plant 3D project not active or standalone drawing
                _cachedDataLinksManager = null;
            }
        }

        public static PlantComponentData Extract(Entity entity, Transaction tr)
        {
            var data = new PlantComponentData
            {
                Handle = entity.Handle.ToString(),
                Layer = entity.Layer,
                ClassName = entity.GetRXClass().Name
            };

            // CAD common properties
            data.CustomProperties["CAD_Layer"] = entity.Layer;
            data.CustomProperties["CAD_Handle"] = entity.Handle.ToString();
            data.CustomProperties["CAD_Color"] = entity.Color.ToString();

            // Try Plant 3D DataLinks
            try
            {
                InitializeDataLinks();

                if (_cachedDataLinksManager != null && _hasLinksMethod != null && _findAcPpRowIdMethod != null && _getAllPropertiesMethod != null)
                {
                    var hasLinks = (bool)(_hasLinksMethod.Invoke(_cachedDataLinksManager, new object[] { entity.ObjectId }) ?? false);
                    if (hasLinks)
                    {
                        int rowId = (int)(_findAcPpRowIdMethod.Invoke(_cachedDataLinksManager, new object[] { entity.ObjectId }) ?? 0);
                        if (rowId > 0)
                        {
                            var props = _getAllPropertiesMethod.Invoke(_cachedDataLinksManager, new object[] { rowId, true }) as IEnumerable<KeyValuePair<string, string>>;
                            if (props != null)
                            {
                                foreach (var kvp in props)
                                {
                                    if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value))
                                        continue;

                                    data.CustomProperties[kvp.Key] = kvp.Value;

                                    // Extract key engineering values into dedicated fields
                                    var k = kvp.Key.ToUpperInvariant();
                                    if (k.Contains("LINENUMBER") || k == "LINE NUMBER")
                                        data.LineNumberTag = kvp.Value;
                                    else if (k == "SPEC" || k.Contains("SPECIFICATION"))
                                        data.Spec = kvp.Value;
                                    else if (k.Contains("SIZE") || k.Contains("DIAMETER") || k == "ND")
                                        data.Size = kvp.Value;
                                    else if (k.Contains("DESCRIPTION"))
                                        data.Description = kvp.Value;
                                    else if (k.Contains("MATERIAL"))
                                        data.Material = kvp.Value;
                                    else if (k == "TAG")
                                        data.Tag = kvp.Value;
                                    else if (k == "STATUS")
                                        data.Status = kvp.Value;
                                    else if (k == "PARTTYPE" || k == "TYPE")
                                        data.PartType = kvp.Value;
                                    else if (k == "PARTSUBTYPE" || k == "SUBTYPE")
                                        data.PartSubType = kvp.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback to CAD properties if DataLinks query fails
            }

            return data;
        }
    }
}
