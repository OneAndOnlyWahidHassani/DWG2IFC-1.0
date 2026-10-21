using System;
using System.IO;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Plant3D.IfcExporter.UI;

[assembly: ExtensionApplication(typeof(Plant3D.IfcExporter.Commands.ExportPluginApp))]

namespace Plant3D.IfcExporter.Commands
{
    public class ExportPluginApp : IExtensionApplication
    {
        public void Initialize()
        {
            var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n=======================================================");
            ed?.WriteMessage("\n[AutoCAD & Plant 3D IFC Exporter] Loaded successfully!");
            ed?.WriteMessage("\nOriginal author: baidurovlabs.ru — thank you for the upstream work.");
            ed?.WriteMessage("\nEnglish fork maintained by: Wahid Hassani");
            ed?.WriteMessage("\nRepository: github.com/OneAndOnlyWahidHassani/DWG2IFC-1.0");
            ed?.WriteMessage("\nLicense: GNU GPLv3");
            ed?.WriteMessage("\nRibbon tab: 'DWG2IFC'");
            ed?.WriteMessage("\nCommands: IFCEXPORT, PLANTTOIFC, or IFCEXPORT_PLANT");
            ed?.WriteMessage("\n=======================================================\n");

            // Build Ribbon Tab
            RibbonManager.Initialize();
        }

        public void Terminate()
        {
        }
    }

    public class ExportCommand
    {
        [CommandMethod("DWG2IFC")]
        [CommandMethod("PLANTTOIFC")]
        [CommandMethod("IFCEXPORT_PLANT")]
        [CommandMethod("IFCEXPORT")]
        public void ShowExportDialog()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                var win = new ExportSettingsWindow(doc);
                AcApp.ShowModalWindow(win);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nFailed to open IFC export window: {ex.Message}\n");
            }
        }

        [CommandMethod("IFCRIBBON")]
        public void ShowRibbonTab()
        {
            RibbonManager.EnsureRibbonTab(activate: true);
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[DWG2IFC] Ribbon tab refreshed and shown.\n");
        }

        [CommandMethod("DWG2IFC_DIAG")]
        public void RunDiagnostics()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var config = Settings.ExportConfig.Load();
            string drawingDir;
            try { drawingDir = Path.GetDirectoryName(doc.Name) ?? Path.GetTempPath(); }
            catch { drawingDir = Path.GetTempPath(); }
            string logPath = Path.Combine(drawingDir, Path.GetFileNameWithoutExtension(doc.Name ?? "drawing") + ".diag.log");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"DWG2IFC diagnostics — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Drawing: {doc.Name}");
            sb.AppendLine($"Toggles: UseAnalyticalPipes={config.UseAnalyticalPipes}, EnableExplodeFallback={config.EnableExplodeFallback}, UseAnalyticalPipeSupports={config.UseAnalyticalPipeSupports}");
            sb.AppendLine();
            sb.AppendLine($"{"Handle",-12}  {"RXClass",-45}  {"Tier",-24}  {"Faces/AxisPts",-14}  Reason");

            int total = 0, exported = 0, skipped = 0;
            var byTier = new System.Collections.Generic.Dictionary<string, int>();

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt![BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    foreach (ObjectId id in ms!)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;
                        total++;
                        string rx;
                        try { rx = ent.GetRXClass().Name ?? ""; } catch { rx = ""; }

                        var plantData = Plant.PlantDataExtractor.Extract(ent, tr);
                        var outcome = Geometry.BrepMeshExtractor.TryExtract(ent, tr, config, plantData);

                        string tier = outcome.Tier.ToString();
                        string reason = outcome.Failure.ToString();
                        string count = outcome.SweptDisk != null
                            ? $"axis={outcome.SweptDisk.AxisPolyline.Count}"
                            : outcome.Mesh != null
                                ? $"faces={outcome.Mesh.Faces.Count}"
                                : "0";

                        string handle;
                        try { handle = ent.Handle.ToString(); } catch { handle = "?"; }

                        sb.AppendLine($"{handle,-12}  {rx,-45}  {tier,-24}  {count,-14}  {reason}");

                        byTier[tier] = byTier.TryGetValue(tier, out var tv) ? tv + 1 : 1;
                        if (outcome.HasGeometry) exported++;
                        else skipped++;
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"ERROR: {ex.Message}");
                sb.AppendLine(ex.StackTrace);
            }

            sb.AppendLine();
            sb.AppendLine($"Totals: candidates={total}, extractable={exported}, skipped={skipped}");
            foreach (var kvp in byTier)
            {
                sb.AppendLine($"  Tier {kvp.Key,-24}  {kvp.Value}");
            }

            try { File.WriteAllText(logPath, sb.ToString(), new System.Text.UTF8Encoding(false)); } catch { }

            ed.WriteMessage($"\n[DWG2IFC_DIAG] candidates={total}, extractable={exported}, skipped={skipped}\n");
            ed.WriteMessage($"[DWG2IFC_DIAG] Log written to: {logPath}\n");
            foreach (var kvp in byTier)
            {
                ed.WriteMessage($"  Tier {kvp.Key}: {kvp.Value}\n");
            }
        }

        [CommandMethod("TESTEXPORT")]
        public void RunTestExport()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            ed.WriteMessage("\n--- START TEST EXPORT --- \n");
            try
            {
                var config = Settings.ExportConfig.Load();
                var db = doc.Database;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = tr.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
                    var ms = tr.GetObject(bt![BlockTableRecord.ModelSpace], OpenMode.ForRead) as BlockTableRecord;
                    int total = 0;
                    int processed = 0;
                    foreach (ObjectId id in ms!)
                    {
                        total++;
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null || !ent.Visible) continue;
                        var mesh = Geometry.BrepMeshExtractor.ExtractMesh(ent, tr, config);
                        if (mesh.IsValid)
                        {
                            processed++;
                        }
                    }
                    tr.Commit();
                    ed.WriteMessage($"\n--- FINISHED: {processed} / {total} objects meshed successfully! ---\n");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[TESTEXPORT ERROR] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        [CommandMethod("INSPECT_BLOCK")]
        public void InspectBlock()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            var logFile = @"c:\IFC2RVT\plant3d\debug_trace_block.log";
            File.WriteAllText(logFile, "STARTING BLOCK INSPECTION\n");
            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Find object by handle A3C17 (or search for it)
                    var handle = new Handle(Convert.ToInt64("A3C17", 16));
                    if (db.TryGetObjectId(handle, out ObjectId objId))
                    {
                        var ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                        File.AppendAllText(logFile, $"Found entity handle {handle}: Type={ent?.GetType().FullName}\n");
                        if (ent is BlockReference br)
                        {
                            var btr = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            File.AppendAllText(logFile, $"Block name: {btr?.Name}, Entity count: {btr?.Cast<ObjectId>().Count()}\n");
                            int subIdx = 0;
                            foreach (ObjectId subId in btr!)
                            {
                                subIdx++;
                                var subEnt = tr.GetObject(subId, OpenMode.ForRead) as Entity;
                                File.AppendAllText(logFile, $"Sub #{subIdx}: Type={subEnt?.GetType().FullName}, Class={subId.ObjectClass.Name}, Handle={subEnt?.Handle}\n");
                                var config = Settings.ExportConfig.Load();
                                File.AppendAllText(logFile, $"Extracting mesh for sub #{subIdx}...\n");
                                var m = Geometry.BrepMeshExtractor.ExtractMesh(subEnt!, tr, config);
                                File.AppendAllText(logFile, $"Sub #{subIdx} mesh result: valid={m.IsValid}, faces={m.Faces.Count}\n");
                            }
                        }
                    }
                    else
                    {
                        File.AppendAllText(logFile, "Handle A3C17 not found!\n");
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                File.AppendAllText(logFile, $"ERROR: {ex.Message}\n{ex.StackTrace}\n");
            }
        }
    }
}
