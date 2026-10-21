using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.Linq;
using System.Text;
using Plant3D.IfcExporter.Geometry;
using Plant3D.IfcExporter.Ifc;
using Plant3D.IfcExporter.Plant;
using Plant3D.IfcExporter.Settings;

namespace Plant3D.IfcExporter.UI
{
    public partial class ExportSettingsWindow : Window
    {
        private readonly Document _doc;
        private readonly ExportConfig _config;

        public ExportSettingsWindow(Document doc)
        {
            InitializeComponent();
            _doc = doc;
            _config = ExportConfig.Load();

            // Populate UI from config
            if (string.IsNullOrWhiteSpace(_config.OutputFilePath) && !string.IsNullOrWhiteSpace(_doc.Name))
            {
                var dir = Path.GetDirectoryName(_doc.Name);
                var baseName = Path.GetFileNameWithoutExtension(_doc.Name);
                _config.OutputFilePath = Path.Combine(dir ?? string.Empty, baseName + ".ifc");
            }
            TxtOutputPath.Text = _config.OutputFilePath;
            CmbSchema.SelectedIndex = _config.Schema == IfcSchemaVersion.IFC4 ? 0 : 1;
            CmbScope.SelectedIndex = (int)_config.Scope;
            TxtLineFilter.Text = _config.FilterLineNumber;
            TxtTolerance.Text = _config.WeldingToleranceMm.ToString();
            ChkMillimetres.IsChecked = _config.UseMillimetres;
            ChkOffsetOrigin.IsChecked = _config.OffsetToOrigin;
            TxtOffX.Text = _config.CustomOffsetX.ToString();
            TxtOffY.Text = _config.CustomOffsetY.ToString();
            TxtOffZ.Text = _config.CustomOffsetZ.ToString();
            ChkPlantProps.IsChecked = _config.ExportPlantProperties;
            ChkCadProps.IsChecked = _config.ExportCadProperties;
            ChkSkipEmpty.IsChecked = _config.SkipEmptyProperties;
            TxtPsetPrefix.Text = _config.PropertySetPrefix;
            TxtProjectName.Text = _config.ProjectName;
            TxtBuildingName.Text = _config.BuildingName;
            ChkParametricPipes.IsChecked = _config.UseAnalyticalPipes;
            ChkExplodeFallback.IsChecked = _config.EnableExplodeFallback;
            ChkWriteLog.IsChecked = _config.WriteExportLog;

            PanelOffsets.Visibility = _config.OffsetToOrigin ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        }

        private void BtnWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0") { UseShellExecute = true });
            }
            catch
            {
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "IFC files (*.ifc)|*.ifc|All files (*.*)|*.*",
                FileName = Path.GetFileName(TxtOutputPath.Text),
                DefaultExt = ".ifc"
            };
            if (sfd.ShowDialog() == true)
            {
                TxtOutputPath.Text = sfd.FileName;
            }
        }

        private void ChkOffsetOrigin_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelOffsets != null)
            {
                PanelOffsets.Visibility = ChkOffsetOrigin.IsChecked == true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var outPath = TxtOutputPath.Text.Trim();
            if (string.IsNullOrWhiteSpace(outPath))
            {
                MessageBox.Show("Please specify the output path for the IFC file.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sync settings
            _config.OutputFilePath = outPath;
            _config.Schema = CmbSchema.SelectedIndex == 0 ? IfcSchemaVersion.IFC4 : IfcSchemaVersion.IFC2X3;
            _config.Scope = (ExportScopeOption)CmbScope.SelectedIndex;
            _config.FilterLineNumber = TxtLineFilter.Text.Trim();
            if (double.TryParse(TxtTolerance.Text, out double tol)) _config.WeldingToleranceMm = tol;
            _config.UseMillimetres = ChkMillimetres.IsChecked == true;
            _config.OffsetToOrigin = ChkOffsetOrigin.IsChecked == true;
            if (double.TryParse(TxtOffX.Text, out double ox)) _config.CustomOffsetX = ox;
            if (double.TryParse(TxtOffY.Text, out double oy)) _config.CustomOffsetY = oy;
            if (double.TryParse(TxtOffZ.Text, out double oz)) _config.CustomOffsetZ = oz;
            _config.ExportPlantProperties = ChkPlantProps.IsChecked == true;
            _config.ExportCadProperties = ChkCadProps.IsChecked == true;
            _config.SkipEmptyProperties = ChkSkipEmpty.IsChecked == true;
            _config.PropertySetPrefix = TxtPsetPrefix.Text.Trim();
            _config.ProjectName = TxtProjectName.Text.Trim();
            _config.BuildingName = TxtBuildingName.Text.Trim();
            _config.UseAnalyticalPipes = ChkParametricPipes.IsChecked == true;
            _config.EnableExplodeFallback = ChkExplodeFallback.IsChecked == true;
            _config.WriteExportLog = ChkWriteLog.IsChecked == true;
            _config.Save();

            BtnExport.IsEnabled = false;
            TxtStatus.Text = "Collecting drawing objects...";
            PrgExport.Value = 0;

            try
            {
                var items = new List<ExportItem>();
                var diag = new ExportDiagnostics();
                var db = _doc.Database;

                // Collect entities on UI thread with AutoCAD transaction
                using (_doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var entityIds = CollectTargetEntities(db, tr);
                    diag.TotalCandidates = entityIds.Count;
                    TxtStatus.Text = $"Processing geometry ({entityIds.Count} items)...";

                    int count = 0;
                    foreach (var id in entityIds)
                    {
                        count++;
                        if (count % 25 == 0 || count == entityIds.Count)
                        {
                            PrgExport.Value = (double)count / Math.Max(entityIds.Count, 1) * 50.0;
                            TxtStatus.Text = $"Processing geometry: {count} / {entityIds.Count}...";
                            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                        }

                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || !ent.Visible)
                        {
                            diag.RecordSkip(ent, "Hidden", FailureReason.None);
                            continue;
                        }

                        var plantData = PlantDataExtractor.Extract(ent, tr);

                        // Line number filter
                        if (!string.IsNullOrWhiteSpace(_config.FilterLineNumber))
                        {
                            if (!string.Equals(plantData.LineNumberTag, _config.FilterLineNumber, StringComparison.OrdinalIgnoreCase))
                            {
                                diag.FilteredOutByLineNumber++;
                                continue;
                            }
                        }

                        var outcome = BrepMeshExtractor.TryExtract(ent, tr, _config, plantData);
                        if (!outcome.HasGeometry)
                        {
                            diag.RecordSkip(ent, SafeRxName(ent), outcome.Failure);
                            continue;
                        }

                        var classification = PlantClassifier.Classify(
                            plantData.ClassName,
                            plantData.PartType,
                            plantData.PartSubType,
                            plantData.Description,
                            plantData.Layer
                        );

                        diag.RecordExport(classification.EntityType, outcome.Tier);

                        items.Add(new ExportItem
                        {
                            Mesh = outcome.Mesh,
                            SweptDisk = outcome.SweptDisk,
                            Data = plantData,
                            Classification = classification
                        });
                    }

                    tr.Commit();
                }

                if (items.Count == 0)
                {
                    TxtStatus.Text = "No suitable 3D elements found for export.";
                    MessageBox.Show("No suitable 3D solids or components matching the selected criteria were found in the drawing.", "Result", MessageBoxButton.OK, MessageBoxImage.Information);
                    BtnExport.IsEnabled = true;
                    return;
                }

                TxtStatus.Text = $"Writing IFC file ({items.Count} objects)...";

                // Write file asynchronously
                await Task.Run(() =>
                {
                    var writer = new IfcWriter(_config);
                    writer.Export(outPath, items, (done, total) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PrgExport.Value = 50.0 + ((double)done / total * 50.0);
                            TxtStatus.Text = $"Writing IFC: {done} / {total}";
                        });
                    });
                });

                PrgExport.Value = 100;
                TxtStatus.Text = "Export completed successfully!";
                var fileInfo = new FileInfo(outPath);
                double mb = fileInfo.Length / (1024.0 * 1024.0);

                string logPath = Path.ChangeExtension(outPath, ".export.log");
                if (_config.WriteExportLog)
                {
                    try { WriteExportLog(logPath, items.Count, mb, diag); } catch { }
                }

                _doc.Editor.WriteMessage("\n" + diag.BuildSummary(items.Count));

                string friendly = diag.BuildFriendlySummary(items.Count);
                string tail = $"\n\nSaved to:\n   {outPath}  ({mb:F2} MB)";
                if (_config.WriteExportLog) tail += $"\n\nDetails log:\n   {logPath}";
                tail += "\n\nOpen the output folder now?";

                var choice = MessageBox.Show(
                    "Export completed.\n\n" + friendly + tail,
                    "DWG2IFC — Export done",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (choice == MessageBoxResult.Yes)
                {
                    try
                    {
                        var folder = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{outPath}\"") { UseShellExecute = true });
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = "Error: " + ex.Message;
                MessageBox.Show($"Error during IFC export:\n\n{ex.Message}\n{ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExport.IsEnabled = true;
            }
        }

        private static bool Is3dCandidate(ObjectId id)
        {
            if (id.IsNull || id.IsErased) return false;
            var rx = id.ObjectClass;
            if (rx == null) return false;
            var name = rx.Name;

            return name == "AcDb3dSolid" ||
                   name == "AcDbSubDMesh" ||
                   name == "AcDbPolyFaceMesh" ||
                   name == "AcDbPolygonMesh" ||
                   name == "AcDbBlockReference" ||
                   name == "AcDbBody" ||
                   name == "AcDbRegion" ||
                   name == "AcDbFace" ||
                   name.Contains("Surface", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("AcPp", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("PnP", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Plant", StringComparison.OrdinalIgnoreCase);
        }

        private List<ObjectId> CollectTargetEntities(Database db, Transaction tr)
        {
            var result = new List<ObjectId>();
            var ed = _doc.Editor;

            if (_config.Scope == ExportScopeOption.SelectedObjects)
            {
                var selection = ed.SelectImplied();
                if (selection.Status == PromptStatus.OK && selection.Value != null)
                {
                    foreach (SelectedObject so in selection.Value)
                    {
                        if (so != null && Is3dCandidate(so.ObjectId)) result.Add(so.ObjectId);
                    }
                }
                if (result.Count == 0)
                {
                    // Fallback to picking objects if none pre-selected
                    var promptSel = ed.GetSelection();
                    if (promptSel.Status == PromptStatus.OK && promptSel.Value != null)
                    {
                        foreach (SelectedObject so in promptSel.Value)
                        {
                            if (so != null && Is3dCandidate(so.ObjectId)) result.Add(so.ObjectId);
                        }
                    }
                }
            }
            else
            {
                // Entire drawing / ModelSpace
                var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                if (bt != null && bt.Has(BlockTableRecord.ModelSpace))
                {
                    var ms = tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    if (ms != null)
                    {
                        var lt = tr.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

                        foreach (ObjectId id in ms)
                        {
                            if (!Is3dCandidate(id)) continue;

                            if (_config.Scope == ExportScopeOption.VisibleLayersOnly && lt != null)
                            {
                                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                                if (ent != null)
                                {
                                    var layer = tr.GetObject(ent.LayerId, OpenMode.ForRead) as LayerTableRecord;
                                    if (layer != null && (layer.IsOff || layer.IsFrozen))
                                        continue;
                                }
                            }
                            result.Add(id);
                        }
                    }
                }
            }

            return result;
        }

        private static string SafeRxName(Entity? ent)
        {
            try { return ent?.GetRXClass().Name ?? "<null>"; }
            catch { return "<unknown>"; }
        }

        private void WriteExportLog(string logPath, int exportedCount, double mb, ExportDiagnostics diag)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"DWG2IFC export log — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Drawing: {_doc.Name}");
            sb.AppendLine($"Settings: schema={_config.Schema}, scope={_config.Scope}, units={(_config.UseMillimetres ? "mm" : "m")}, " +
                          $"offset=({_config.CustomOffsetX},{_config.CustomOffsetY},{_config.CustomOffsetZ}) applied={_config.OffsetToOrigin}");
            sb.AppendLine($"Toggles: UseAnalyticalPipes={_config.UseAnalyticalPipes}, EnableExplodeFallback={_config.EnableExplodeFallback}, " +
                          $"UseAnalyticalPipeSupports={_config.UseAnalyticalPipeSupports}, IncludeXrefs={_config.IncludeXrefs}");
            sb.AppendLine();
            sb.AppendLine(diag.BuildSummary(exportedCount));
            sb.AppendLine($"IFC size: {mb:F2} MB");
            sb.AppendLine();
            sb.AppendLine("By extraction tier:");
            foreach (var kvp in diag.ByTier.OrderByDescending(k => k.Value))
            {
                sb.AppendLine($"  {kvp.Key,-25}  exported={kvp.Value}");
            }
            sb.AppendLine();
            sb.AppendLine("By IFC category:");
            foreach (var kvp in diag.ByCategory.OrderByDescending(k => k.Value))
            {
                sb.AppendLine($"  {kvp.Key,-25}  exported={kvp.Value}");
            }
            sb.AppendLine();
            if (diag.FilteredOutByLineNumber > 0)
            {
                sb.AppendLine($"Filtered out by line number filter: {diag.FilteredOutByLineNumber}");
                sb.AppendLine();
            }
            sb.AppendLine($"Skipped entries (first {Math.Min(diag.Skipped.Count, _config.MaxLoggedSkipped)} of {diag.Skipped.Count}):");
            sb.AppendLine($"  {"Handle",-12}  {"RXClass",-40}  Reason");
            int limit = Math.Min(diag.Skipped.Count, _config.MaxLoggedSkipped);
            for (int i = 0; i < limit; i++)
            {
                var s = diag.Skipped[i];
                sb.AppendLine($"  {s.Handle,-12}  {s.RxClass,-40}  {s.Reason}");
            }
            File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(false));
        }
    }

    public sealed class ExportDiagnostics
    {
        public int TotalCandidates { get; set; }
        public int FilteredOutByLineNumber { get; set; }

        public Dictionary<string, int> ByCategory { get; } = new();
        public Dictionary<string, int> ByTier { get; } = new();

        public List<SkipEntry> Skipped { get; } = new();

        public void RecordExport(IfcEntityType cat, ExtractionTier tier)
        {
            var k = cat.ToString();
            ByCategory[k] = ByCategory.TryGetValue(k, out var v) ? v + 1 : 1;
            var tk = tier.ToString();
            ByTier[tk] = ByTier.TryGetValue(tk, out var tv) ? tv + 1 : 1;
        }

        public void RecordSkip(Entity? ent, string rxClass, FailureReason reason)
        {
            string handle = "<null>";
            try { if (ent != null) handle = ent.Handle.ToString(); } catch { }
            Skipped.Add(new SkipEntry
            {
                Handle = handle,
                RxClass = rxClass,
                Reason = reason.ToString()
            });
        }

        public string BuildSummary(int exportedCount)
        {
            int skipped = Skipped.Count;
            var sb = new StringBuilder();
            sb.AppendLine($"Totals: candidates={TotalCandidates}, exported={exportedCount}, skipped={skipped}, filtered={FilteredOutByLineNumber}");
            if (ByCategory.Count > 0)
            {
                sb.AppendLine("By category:");
                foreach (var kvp in ByCategory.OrderByDescending(k => k.Value))
                {
                    sb.AppendLine($"  {kvp.Key,-30} {kvp.Value}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        public string BuildFriendlySummary(int exportedCount)
        {
            int skipped = Skipped.Count;
            var sb = new StringBuilder();
            sb.AppendLine($"Exported: {exportedCount} 3D objects");
            if (skipped > 0) sb.AppendLine($"Skipped:  {skipped} objects (see log for the AutoCAD handles)");
            if (FilteredOutByLineNumber > 0) sb.AppendLine($"Filtered out by line-number filter: {FilteredOutByLineNumber}");

            if (ByCategory.Count > 0)
            {
                sb.AppendLine();
                foreach (var kvp in ByCategory.OrderByDescending(k => k.Value))
                {
                    sb.AppendLine($"  {FriendlyCategory(kvp.Key)}: {kvp.Value}");
                }
            }
            return sb.ToString().TrimEnd();
        }

        private static string FriendlyCategory(string ifcTypeName)
        {
            return ifcTypeName switch
            {
                "IfcPipeSegment" => "Pipes",
                "IfcPipeFitting" => "Fittings (elbows, tees, reducers, flanges)",
                "IfcValve" => "Valves",
                "IfcPump" => "Pumps",
                "IfcFlowMovingDevice" => "Flow-moving devices (compressors, fans)",
                "IfcEnergyConversionDevice" => "Equipment (tanks, vessels, exchangers)",
                "IfcDiscreteAccessory" => "Pipe supports / hangers",
                "IfcFastener" => "Bolts / gaskets / fasteners",
                "IfcBeam" => "Beams",
                "IfcColumn" => "Columns",
                "IfcMember" => "Structural members",
                "IfcBuildingElementProxy" => "Generic 3D objects",
                _ => ifcTypeName
            };
        }

        public sealed class SkipEntry
        {
            public string Handle { get; set; } = string.Empty;
            public string RxClass { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
        }
    }
}
