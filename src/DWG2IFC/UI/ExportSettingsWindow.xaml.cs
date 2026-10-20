using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
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
            _config.Save();

            BtnExport.IsEnabled = false;
            TxtStatus.Text = "Collecting drawing objects...";
            PrgExport.Value = 0;

            try
            {
                var items = new List<ExportItem>();
                var db = _doc.Database;

                // Collect entities on UI thread with AutoCAD transaction
                using (_doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var entityIds = CollectTargetEntities(db, tr);
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
                        if (ent == null || !ent.Visible) continue;

                        var plantData = PlantDataExtractor.Extract(ent, tr);

                        // Line number filter
                        if (!string.IsNullOrWhiteSpace(_config.FilterLineNumber))
                        {
                            if (!string.Equals(plantData.LineNumberTag, _config.FilterLineNumber, StringComparison.OrdinalIgnoreCase))
                                continue;
                        }

                        var mesh = BrepMeshExtractor.ExtractMesh(ent, tr, _config);
                        if (!mesh.IsValid) continue;

                        var classification = PlantClassifier.Classify(
                            plantData.ClassName,
                            plantData.PartType,
                            plantData.PartSubType,
                            plantData.Description,
                            plantData.Layer
                        );

                        items.Add(new ExportItem
                        {
                            Mesh = mesh,
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
                MessageBox.Show($"Export completed successfully!\n\nItems: {items.Count}\nFile: {outPath}\nSize: {mb:F2} MB", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
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
    }
}
