using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.BoundaryRepresentation;
using Plant3D.IfcExporter.Plant;
using Plant3D.IfcExporter.Settings;

namespace Plant3D.IfcExporter.Geometry
{
    public static class BrepMeshExtractor
    {
        public static ExtractionOutcome TryExtract(Entity entity, Transaction tr, ExportConfig config, PlantComponentData? plantData = null)
        {
            var outcome = new ExtractionOutcome();

            // Tier 1 — direct type switch (existing path)
            var mesh = ExtractMesh(entity, tr, config);
            if (mesh.IsValid)
            {
                outcome.Mesh = mesh;
                outcome.Tier = ExtractionTier.DirectTypeSwitch;
                return outcome;
            }

            string rxName = SafeRxClassName(entity);
            bool isPipeClass = PlantPipeGeometryHelper.IsPipeClassName(rxName);

            // Tier 2 — pipe geometry via Plant reflection (parametric or mesh)
            if (isPipeClass)
            {
                var data = plantData ?? PlantDataExtractor.Extract(entity, tr);
                if (PlantPipeGeometryHelper.TryBuildPipeGeometry(entity, data, config, out var swept, out var reason) && swept != null)
                {
                    if (config.UseAnalyticalPipes)
                    {
                        ApplyLayerColorIfNeeded(entity, tr, swept);
                        outcome.SweptDisk = swept;
                        outcome.Tier = ExtractionTier.AnalyticalPipe;
                        return outcome;
                    }

                    // User disabled parametric pipes — tessellate the same axis+radius into a mesh
                    var axisPts = swept.AxisPolyline.ConvertAll(v => new Point3d(v.X, v.Y, v.Z));
                    var tris = PlantPipeGeometryHelper.TessellateCylinderMesh(axisPts, swept.OuterRadius, Math.Max(3, config.PipeTessellationSides));
                    if (tris.Count > 0)
                    {
                        var meshOut = new ExtractedMesh { Red = swept.Red, Green = swept.Green, Blue = swept.Blue };
                        WeldVertices(tris, config.WeldingToleranceMm, meshOut);
                        if (meshOut.IsValid)
                        {
                            ApplyLayerColorToMesh(entity, tr, meshOut);
                            outcome.Mesh = meshOut;
                            outcome.Tier = ExtractionTier.AnalyticalPipe;
                            return outcome;
                        }
                    }
                }
                outcome.Failure = reason;
                // Record pipe-helper failure reason and still try explode fallback below
            }

            // Tier 3 — explode fallback
            if (config.EnableExplodeFallback)
            {
                var explodedMesh = TryExplodeToMesh(entity, tr, config);
                if (explodedMesh != null && explodedMesh.IsValid)
                {
                    outcome.Mesh = explodedMesh;
                    outcome.Tier = ExtractionTier.ExplodeFallback;
                    outcome.Failure = FailureReason.None;
                    return outcome;
                }
            }

            // Tier 4 (opt-in) — pipe-support axis fallback
            if (config.UseAnalyticalPipeSupports && (rxName.Contains("Support", StringComparison.OrdinalIgnoreCase) || rxName.Contains("Hanger", StringComparison.OrdinalIgnoreCase)))
            {
                var fallbackMesh = TrySupportAxisFallback(entity, config);
                if (fallbackMesh != null && fallbackMesh.IsValid)
                {
                    outcome.Mesh = fallbackMesh;
                    outcome.Tier = ExtractionTier.AnalyticalPipeSupport;
                    outcome.Failure = FailureReason.None;
                    return outcome;
                }
            }

            if (outcome.Failure == FailureReason.None)
            {
                outcome.Failure = FailureReason.UnknownClass;
            }
            return outcome;
        }

        private static string SafeRxClassName(Entity entity)
        {
            try { return entity.GetRXClass().Name ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static void ApplyLayerColorIfNeeded(Entity entity, Transaction tr, SweptDiskGeometry geom)
        {
            try
            {
                if (!entity.Color.IsByLayer) return;
                var layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null)
                {
                    geom.Red = layer.Color.ColorValue.R;
                    geom.Green = layer.Color.ColorValue.G;
                    geom.Blue = layer.Color.ColorValue.B;
                }
            }
            catch { }
        }

        private static void ApplyLayerColorToMesh(Entity entity, Transaction tr, ExtractedMesh mesh)
        {
            try
            {
                if (!entity.Color.IsByLayer) return;
                var layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layer != null)
                {
                    mesh.Red = layer.Color.ColorValue.R;
                    mesh.Green = layer.Color.ColorValue.G;
                    mesh.Blue = layer.Color.ColorValue.B;
                }
            }
            catch { }
        }

        private static ExtractedMesh? TryExplodeToMesh(Entity entity, Transaction tr, ExportConfig config)
        {
            DBObjectCollection? col = null;
            try
            {
                col = new DBObjectCollection();
                entity.Explode(col);
                if (col.Count == 0) return null;

                var raw = new List<(Point3d, Point3d, Point3d)>();
                foreach (DBObject obj in col)
                {
                    if (obj is Entity child && child.Visible)
                    {
                        ExtractIntoList(child, tr, config, raw, depth: 1);
                    }
                }

                if (raw.Count == 0) return null;

                var mesh = new ExtractedMesh();
                try
                {
                    if (entity.Color.IsByLayer)
                    {
                        var layer = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                        if (layer != null)
                        {
                            mesh.Red = layer.Color.ColorValue.R;
                            mesh.Green = layer.Color.ColorValue.G;
                            mesh.Blue = layer.Color.ColorValue.B;
                        }
                    }
                    else
                    {
                        mesh.Red = entity.Color.ColorValue.R;
                        mesh.Green = entity.Color.ColorValue.G;
                        mesh.Blue = entity.Color.ColorValue.B;
                    }
                }
                catch { }

                WeldVertices(raw, config.WeldingToleranceMm, mesh);
                return mesh;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (col != null)
                {
                    foreach (DBObject obj in col)
                    {
                        try { obj.Dispose(); } catch { }
                    }
                    try { col.Dispose(); } catch { }
                }
            }
        }

        private static void ExtractIntoList(Entity entity, Transaction tr, ExportConfig config, List<(Point3d, Point3d, Point3d)> triangles, int depth)
        {
            if (depth > 2) return;

            if (entity is Solid3d s)
            {
                ExtractFromSolid3d(s, triangles);
            }
            else if (entity is PolyFaceMesh p)
            {
                ExtractFromPolyFaceMesh(p, tr, triangles);
            }
            else if (entity is SubDMesh d)
            {
                ExtractFromSubDMesh(d, triangles);
            }
            else if (entity is Body body)
            {
                try { using var brep = new Brep(body); ExtractFromBrep(brep, triangles); } catch { }
            }
            else if (entity is Region region)
            {
                try { using var brep = new Brep(region); ExtractFromBrep(brep, triangles); } catch { }
            }
            else if (entity is Autodesk.AutoCAD.DatabaseServices.Surface surf)
            {
                try { using var brep = new Brep(surf); ExtractFromBrep(brep, triangles); } catch { }
            }
            else if (entity is Autodesk.AutoCAD.DatabaseServices.Face face)
            {
                ExtractFromFace(face, triangles);
            }
            else if (entity is BlockReference br)
            {
                ExtractFromBlockReference(br, tr, config, triangles);
            }
            else
            {
                // Try one more explode level for custom children (e.g. nested Plant assemblies)
                DBObjectCollection? childCol = null;
                try
                {
                    childCol = new DBObjectCollection();
                    entity.Explode(childCol);
                    if (childCol.Count > 0)
                    {
                        foreach (DBObject sub in childCol)
                        {
                            if (sub is Entity childEnt && childEnt.Visible)
                            {
                                ExtractIntoList(childEnt, tr, config, triangles, depth + 1);
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    if (childCol != null)
                    {
                        foreach (DBObject sub in childCol)
                        {
                            try { sub.Dispose(); } catch { }
                        }
                        try { childCol.Dispose(); } catch { }
                    }
                }
            }
        }

        private static ExtractedMesh? TrySupportAxisFallback(Entity entity, ExportConfig config)
        {
            try
            {
                var ext = entity.GeometricExtents;
                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                double dx = max.X - min.X, dy = max.Y - min.Y, dz = max.Z - min.Z;
                double smallest = Math.Min(Math.Min(dx, dy), dz);
                double radius = Math.Max(smallest * 0.5, 10.0);
                var axis = new List<Point3d>
                {
                    new Point3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, min.Z),
                    new Point3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, max.Z)
                };
                var tris = PlantPipeGeometryHelper.TessellateCylinderMesh(axis, radius, Math.Max(3, config.PipeTessellationSides));
                if (tris.Count == 0) return null;
                var mesh = new ExtractedMesh();
                WeldVertices(tris, config.WeldingToleranceMm, mesh);
                return mesh;
            }
            catch { return null; }
        }

        public static ExtractedMesh ExtractMesh(Entity entity, Transaction tr, ExportConfig config)
        {
            var mesh = new ExtractedMesh();

            // Extract color
            if (entity.Color.IsByLayer)
            {
                var layerTableRecord = tr.GetObject(entity.LayerId, OpenMode.ForRead) as LayerTableRecord;
                if (layerTableRecord != null)
                {
                    mesh.Red = layerTableRecord.Color.ColorValue.R;
                    mesh.Green = layerTableRecord.Color.ColorValue.G;
                    mesh.Blue = layerTableRecord.Color.ColorValue.B;
                }
            }
            else
            {
                mesh.Red = entity.Color.ColorValue.R;
                mesh.Green = entity.Color.ColorValue.G;
                mesh.Blue = entity.Color.ColorValue.B;
            }

            // Extract triangles based on entity type
            var rawTriangles = new List<(Point3d A, Point3d B, Point3d C)>();

            if (entity is Solid3d solid)
            {
                ExtractFromSolid3d(solid, rawTriangles);
            }
            else if (entity is PolyFaceMesh polyface)
            {
                ExtractFromPolyFaceMesh(polyface, tr, rawTriangles);
            }
            else if (entity is SubDMesh subDMesh)
            {
                ExtractFromSubDMesh(subDMesh, rawTriangles);
            }
            else if (entity is BlockReference blockRef)
            {
                ExtractFromBlockReference(blockRef, tr, config, rawTriangles);
            }
            else if (entity is Autodesk.AutoCAD.DatabaseServices.Surface surf)
            {
                try
                {
                    using var brep = new Brep(surf);
                    ExtractFromBrep(brep, rawTriangles);
                }
                catch { }
            }
            else if (entity is Body body)
            {
                try
                {
                    using var brep = new Brep(body);
                    ExtractFromBrep(brep, rawTriangles);
                }
                catch { }
            }
            else if (entity is Region region)
            {
                try
                {
                    using var brep = new Brep(region);
                    ExtractFromBrep(brep, rawTriangles);
                }
                catch { }
            }
            else if (entity is Autodesk.AutoCAD.DatabaseServices.Face face)
            {
                ExtractFromFace(face, rawTriangles);
            }

            // Weld vertices to eliminate duplicate coordinates and optimize IFC size
            WeldVertices(rawTriangles, config.WeldingToleranceMm, mesh);

            return mesh;
        }

        private static void ExtractFromSolid3d(Solid3d solid, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            try
            {
                using var brep = new Brep(solid);
                ExtractFromBrep(brep, triangles);
            }
            catch
            {
                // Fallback to bounding box solid if B-Rep fails
                try
                {
                    AddBoxFallback(solid.GeometricExtents, triangles);
                }
                catch { }
            }
        }

        private static void ExtractFromBrep(Brep brep, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            try
            {
                using var control = new Mesh2dControl();
                using var filter = new Mesh2dFilter();
                filter.Insert(brep, control);

                using var mesh2d = new Mesh2d(filter);
                foreach (Element2d element in mesh2d.Element2ds)
                {
                    using (element)
                    {
                        var pts = new List<Point3d>();
                        foreach (Node node in element.Nodes)
                        {
                            using (node)
                            {
                                pts.Add(node.Point);
                            }
                        }

                        if (pts.Count == 3)
                        {
                            triangles.Add((pts[0], pts[1], pts[2]));
                        }
                        else if (pts.Count == 4)
                        {
                            // Quad to two triangles
                            triangles.Add((pts[0], pts[1], pts[2]));
                            triangles.Add((pts[0], pts[2], pts[3]));
                        }
                        else if (pts.Count > 4)
                        {
                            // Polygon fan triangulation
                            for (int i = 1; i < pts.Count - 1; i++)
                            {
                                triangles.Add((pts[0], pts[i], pts[i + 1]));
                            }
                        }
                    }
                }
            }
            catch
            {
                // Graceful fallback if mesh generation encounters invalid geometry
            }
        }

        private static void ExtractFromPolyFaceMesh(PolyFaceMesh polyface, Transaction tr, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            var vertices = new List<Point3d>();
            var faces = new List<int[]>();

            foreach (ObjectId id in polyface)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is PolyFaceMeshVertex v)
                {
                    vertices.Add(v.Position);
                }
                else if (obj is FaceRecord f)
                {
                    var idx = new List<int>();
                    for (short i = 0; i < 4; i++)
                    {
                        short vIdx = f.GetVertexAt(i);
                        if (vIdx != 0)
                        {
                            idx.Add(Math.Abs(vIdx) - 1); // 1-based signed index to 0-based
                        }
                    }
                    if (idx.Count >= 3)
                    {
                        faces.Add(idx.ToArray());
                    }
                }
            }

            foreach (var face in faces)
            {
                if (face.Length == 3 && face[0] < vertices.Count && face[1] < vertices.Count && face[2] < vertices.Count)
                {
                    triangles.Add((vertices[face[0]], vertices[face[1]], vertices[face[2]]));
                }
                else if (face.Length >= 4 && face[0] < vertices.Count && face[1] < vertices.Count && face[2] < vertices.Count && face[3] < vertices.Count)
                {
                    triangles.Add((vertices[face[0]], vertices[face[1]], vertices[face[2]]));
                    triangles.Add((vertices[face[0]], vertices[face[2]], vertices[face[3]]));
                }
            }
        }

        private static void ExtractFromSubDMesh(SubDMesh subD, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            var verts = subD.Vertices;
            var faceArray = subD.FaceArray;

            int ptr = 0;
            while (ptr < faceArray.Count)
            {
                int vertCount = faceArray[ptr++];
                var faceIndices = new List<int>();
                for (int i = 0; i < vertCount && ptr < faceArray.Count; i++)
                {
                    faceIndices.Add(faceArray[ptr++]);
                }

                if (faceIndices.Count == 3)
                {
                    triangles.Add((verts[faceIndices[0]], verts[faceIndices[1]], verts[faceIndices[2]]));
                }
                else if (faceIndices.Count == 4)
                {
                    triangles.Add((verts[faceIndices[0]], verts[faceIndices[1]], verts[faceIndices[2]]));
                    triangles.Add((verts[faceIndices[0]], verts[faceIndices[2]], verts[faceIndices[3]]));
                }
                else if (faceIndices.Count > 4)
                {
                    for (int i = 1; i < faceIndices.Count - 1; i++)
                    {
                        triangles.Add((verts[faceIndices[0]], verts[faceIndices[i]], verts[faceIndices[i + 1]]));
                    }
                }
            }
        }

        private static void ExtractFromBlockReference(BlockReference blockRef, Transaction tr, ExportConfig config, List<(Point3d A, Point3d B, Point3d C)> triangles, int depth = 0)
        {
            if (depth > 8) return;
            var btr = tr.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            if (btr == null) return;

            var xform = blockRef.BlockTransform;

            foreach (ObjectId id in btr)
            {
                var subEnt = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (subEnt == null || !subEnt.Visible) continue;

                var subTriangles = new List<(Point3d A, Point3d B, Point3d C)>();
                if (subEnt is Solid3d s)
                {
                    ExtractFromSolid3d(s, subTriangles);
                }
                else if (subEnt is PolyFaceMesh p)
                {
                    ExtractFromPolyFaceMesh(p, tr, subTriangles);
                }
                else if (subEnt is SubDMesh d)
                {
                    ExtractFromSubDMesh(d, subTriangles);
                }
                else if (subEnt is Body body)
                {
                    try { using var brep = new Brep(body); ExtractFromBrep(brep, subTriangles); } catch { }
                }
                else if (subEnt is Region region)
                {
                    try { using var brep = new Brep(region); ExtractFromBrep(brep, subTriangles); } catch { }
                }
                else if (subEnt is BlockReference nested)
                {
                    ExtractFromBlockReference(nested, tr, config, subTriangles, depth + 1);
                }
                else if (subEnt is Autodesk.AutoCAD.DatabaseServices.Face f)
                {
                    ExtractFromFace(f, subTriangles);
                }

                // Transform sub-entity vertices by block insertion matrix
                foreach (var t in subTriangles)
                {
                    triangles.Add((
                        t.A.TransformBy(xform),
                        t.B.TransformBy(xform),
                        t.C.TransformBy(xform)
                    ));
                }
            }
        }

        private static void ExtractFromFace(Autodesk.AutoCAD.DatabaseServices.Face face, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            try
            {
                var p0 = face.GetVertexAt(0);
                var p1 = face.GetVertexAt(1);
                var p2 = face.GetVertexAt(2);
                var p3 = face.GetVertexAt(3);

                triangles.Add((p0, p1, p2));
                if (p2 != p3 && p0 != p3)
                {
                    triangles.Add((p0, p2, p3));
                }
            }
            catch { }
        }

        private static void AddBoxFallback(Extents3d extents, List<(Point3d A, Point3d B, Point3d C)> triangles)
        {
            var min = extents.MinPoint;
            var max = extents.MaxPoint;

            var p0 = new Point3d(min.X, min.Y, min.Z);
            var p1 = new Point3d(max.X, min.Y, min.Z);
            var p2 = new Point3d(max.X, max.Y, min.Z);
            var p3 = new Point3d(min.X, max.Y, min.Z);

            var p4 = new Point3d(min.X, min.Y, max.Z);
            var p5 = new Point3d(max.X, min.Y, max.Z);
            var p6 = new Point3d(max.X, max.Y, max.Z);
            var p7 = new Point3d(min.X, max.Y, max.Z);

            // Bottom
            triangles.Add((p0, p2, p1)); triangles.Add((p0, p3, p2));
            // Top
            triangles.Add((p4, p5, p6)); triangles.Add((p4, p6, p7));
            // Front
            triangles.Add((p0, p1, p5)); triangles.Add((p0, p5, p4));
            // Back
            triangles.Add((p2, p3, p7)); triangles.Add((p2, p7, p6));
            // Left
            triangles.Add((p3, p0, p4)); triangles.Add((p3, p4, p7));
            // Right
            triangles.Add((p1, p2, p6)); triangles.Add((p1, p6, p5));
        }

        private static void WeldVertices(List<(Point3d A, Point3d B, Point3d C)> source, double toleranceMm, ExtractedMesh output)
        {
            var lookup = new Dictionary<Vertex3D, int>();
            var points = new List<Vertex3D>();
            var faces = new List<TriangleFace>();

            int GetOrAddVertex(Point3d p)
            {
                var v = new Vertex3D(p.X, p.Y, p.Z);
                if (lookup.TryGetValue(v, out int idx))
                {
                    return idx;
                }
                idx = points.Count;
                points.Add(v);
                lookup[v] = idx;
                return idx;
            }

            foreach (var t in source)
            {
                int a = GetOrAddVertex(t.A);
                int b = GetOrAddVertex(t.B);
                int c = GetOrAddVertex(t.C);

                // Skip degenerate triangles where 2 or more vertices coincide
                if (a != b && b != c && a != c)
                {
                    faces.Add(new TriangleFace(a, b, c));
                }
            }

            output.Vertices = points;
            output.Faces = faces;
        }
    }
}
