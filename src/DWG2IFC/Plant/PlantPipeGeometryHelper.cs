using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Plant3D.IfcExporter.Geometry;
using Plant3D.IfcExporter.Settings;

namespace Plant3D.IfcExporter.Plant
{
    public static class PlantPipeGeometryHelper
    {
        private static bool _initialized;
        private static readonly List<Type> _pipeTypes = new();
        private static readonly Dictionary<Type, PipeReflection> _cache = new();

        private sealed class PipeReflection
        {
            public PropertyInfo? StartPoint;
            public PropertyInfo? EndPoint;
            public PropertyInfo? OutsideDiameter;
            public PropertyInfo? NominalDiameter;
            public PropertyInfo? WallThickness;
            public PropertyInfo? InnerDiameter;
            public PropertyInfo? PathCurve;
        }

        public static bool IsPipeClassName(string rxClassName)
        {
            if (string.IsNullOrEmpty(rxClassName)) return false;
            if (rxClassName.Equals("AcPpDb3dPipe", StringComparison.OrdinalIgnoreCase)) return true;
            if (rxClassName.StartsWith("AcPpDb3dPipeInline", StringComparison.OrdinalIgnoreCase)) return true;
            if (rxClassName.StartsWith("PnP3dPipe", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            string[] candidateTypeNames =
            {
                "Autodesk.ProcessPower.PnP3dObjects.Pipe",
                "Autodesk.ProcessPower.PnP3dObjects.PipeInlineAsset",
                "Autodesk.ProcessPower.PnP3dObjects.PipeConnector"
            };

            foreach (var fqn in candidateTypeNames)
            {
                try
                {
                    // Try multiple potential assembly names used across Plant 3D versions
                    Type? t = Type.GetType(fqn + ", PnP3dObjectsMgd")
                              ?? Type.GetType(fqn + ", PnP3dObjects")
                              ?? ResolveFromLoadedAssemblies(fqn);
                    if (t != null) _pipeTypes.Add(t);
                }
                catch { }
            }
        }

        private static Type? ResolveFromLoadedAssemblies(string fqn)
        {
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType(fqn, throwOnError: false, ignoreCase: false);
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }

        private static PipeReflection? GetReflection(Entity entity)
        {
            Initialize();
            var runtimeType = entity.GetType();
            if (_cache.TryGetValue(runtimeType, out var cached)) return cached;

            // Only cache if runtime type derives from one of our known pipe types
            bool matches = _pipeTypes.Any(pt => pt.IsAssignableFrom(runtimeType));
            if (!matches && !IsPipeClassName(entity.GetRXClass().Name)) return null;

            var r = new PipeReflection
            {
                StartPoint = runtimeType.GetProperty("StartPoint", BindingFlags.Public | BindingFlags.Instance)
                             ?? runtimeType.GetProperty("Start", BindingFlags.Public | BindingFlags.Instance),
                EndPoint = runtimeType.GetProperty("EndPoint", BindingFlags.Public | BindingFlags.Instance)
                           ?? runtimeType.GetProperty("End", BindingFlags.Public | BindingFlags.Instance),
                OutsideDiameter = runtimeType.GetProperty("OutsideDiameter", BindingFlags.Public | BindingFlags.Instance)
                                  ?? runtimeType.GetProperty("OuterDiameter", BindingFlags.Public | BindingFlags.Instance)
                                  ?? runtimeType.GetProperty("OD", BindingFlags.Public | BindingFlags.Instance),
                NominalDiameter = runtimeType.GetProperty("NominalDiameter", BindingFlags.Public | BindingFlags.Instance),
                WallThickness = runtimeType.GetProperty("WallThickness", BindingFlags.Public | BindingFlags.Instance),
                InnerDiameter = runtimeType.GetProperty("InnerDiameter", BindingFlags.Public | BindingFlags.Instance)
                                ?? runtimeType.GetProperty("InsideDiameter", BindingFlags.Public | BindingFlags.Instance),
                PathCurve = runtimeType.GetProperty("PathCurve", BindingFlags.Public | BindingFlags.Instance)
                            ?? runtimeType.GetProperty("Path", BindingFlags.Public | BindingFlags.Instance)
            };

            _cache[runtimeType] = r;
            return r;
        }

        public static bool TryBuildPipeGeometry(
            Entity entity,
            PlantComponentData data,
            ExportConfig config,
            out SweptDiskGeometry? geometry,
            out FailureReason failure)
        {
            geometry = null;
            failure = FailureReason.None;

            try
            {
                var refl = GetReflection(entity);
                Point3d? start = null, end = null;
                double? outsideDiameter = null;
                double? wallThickness = null;
                object? pathCurveObj = null;

                if (refl != null)
                {
                    start = TryReadPoint(refl.StartPoint, entity);
                    end = TryReadPoint(refl.EndPoint, entity);
                    outsideDiameter = TryReadDouble(refl.OutsideDiameter, entity)
                                      ?? TryReadDouble(refl.NominalDiameter, entity);
                    wallThickness = TryReadDouble(refl.WallThickness, entity);
                    pathCurveObj = refl.PathCurve?.GetValue(entity);
                }

                // Secondary diameter source — Plant DataLinks row via PlantComponentData
                if (!outsideDiameter.HasValue && data.OutsideDiameterMm.HasValue)
                {
                    outsideDiameter = data.OutsideDiameterMm;
                }

                // Build axis
                List<Point3d> axis;
                if (pathCurveObj is Curve3d pathCurve)
                {
                    axis = SampleCurve3d(pathCurve, Math.Max(2, config.CurvedPipeSamples));
                }
                else if (start.HasValue && end.HasValue)
                {
                    axis = new List<Point3d> { start.Value, end.Value };
                }
                else
                {
                    // Last resort — try reading the entity's own extents as an axis seed
                    if (!TryAxisFromExtents(entity, out axis))
                    {
                        failure = refl == null ? FailureReason.PlantReflectionUnavailable : FailureReason.EmptyGeometry;
                        return false;
                    }
                }

                if (axis.Count < 2 || axis.First().DistanceTo(axis.Last()) < 1e-6)
                {
                    failure = FailureReason.ZeroLengthPipe;
                    return false;
                }

                if (!outsideDiameter.HasValue || outsideDiameter.Value <= 0)
                {
                    failure = FailureReason.MissingDiameter;
                    return false;
                }

                double outerR = outsideDiameter.Value * 0.5;
                double? innerR = null;
                if (wallThickness.HasValue && wallThickness.Value > 0 && wallThickness.Value < outerR)
                {
                    innerR = outerR - wallThickness.Value;
                }

                var g = new SweptDiskGeometry
                {
                    OuterRadius = outerR,
                    InnerRadius = innerR,
                    AxisPolyline = axis.Select(p => new Vertex3D(p.X, p.Y, p.Z)).ToList()
                };
                ApplyColor(entity, g);
                geometry = g;
                return true;
            }
            catch
            {
                failure = FailureReason.EmptyGeometry;
                return false;
            }
        }

        public static List<(Point3d A, Point3d B, Point3d C)> TessellateCylinderMesh(
            List<Point3d> axis, double outerRadius, int sides)
        {
            var tris = new List<(Point3d, Point3d, Point3d)>();
            if (axis == null || axis.Count < 2 || outerRadius <= 0) return tris;
            if (sides < 3) sides = 3;

            // Compute an initial perpendicular reference frame
            var seg0Dir = (axis[1] - axis[0]).GetNormal();
            var refU = GetPerpendicular(seg0Dir);

            // Previous ring points
            var prevRing = BuildRing(axis[0], seg0Dir, refU, outerRadius, sides);
            // Cap at start
            CapRing(prevRing, axis[0], tris, flip: true);

            for (int i = 1; i < axis.Count; i++)
            {
                var dir = (axis[i] - axis[i - 1]).GetNormal();
                // Rotate refU to stay perpendicular to new direction (minimum-rotation transport)
                var prevDir = i == 1 ? seg0Dir : (axis[i - 1] - axis[i - 2]).GetNormal();
                refU = TransportPerpendicular(refU, prevDir, dir);
                var ring = BuildRing(axis[i], dir, refU, outerRadius, sides);
                for (int s = 0; s < sides; s++)
                {
                    int next = (s + 1) % sides;
                    tris.Add((prevRing[s], prevRing[next], ring[next]));
                    tris.Add((prevRing[s], ring[next], ring[s]));
                }
                prevRing = ring;
            }

            // Cap at end
            CapRing(prevRing, axis[axis.Count - 1], tris, flip: false);
            return tris;
        }

        private static List<Point3d> BuildRing(Point3d center, Vector3d dir, Vector3d refU, double radius, int sides)
        {
            var refV = dir.CrossProduct(refU).GetNormal();
            var ring = new List<Point3d>(sides);
            for (int s = 0; s < sides; s++)
            {
                double a = (2.0 * Math.PI * s) / sides;
                double cx = Math.Cos(a) * radius;
                double cy = Math.Sin(a) * radius;
                ring.Add(center + refU * cx + refV * cy);
            }
            return ring;
        }

        private static void CapRing(List<Point3d> ring, Point3d center, List<(Point3d, Point3d, Point3d)> tris, bool flip)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                int next = (i + 1) % ring.Count;
                if (flip) tris.Add((center, ring[next], ring[i]));
                else tris.Add((center, ring[i], ring[next]));
            }
        }

        private static Vector3d GetPerpendicular(Vector3d dir)
        {
            // Pick axis most orthogonal to dir
            Vector3d candidate = Math.Abs(dir.Z) < 0.9 ? Vector3d.ZAxis : Vector3d.XAxis;
            var perp = dir.CrossProduct(candidate);
            if (perp.Length < 1e-9)
            {
                perp = dir.CrossProduct(Vector3d.YAxis);
            }
            return perp.GetNormal();
        }

        private static Vector3d TransportPerpendicular(Vector3d prevU, Vector3d prevDir, Vector3d newDir)
        {
            // Minimum-rotation transport of prevU from prevDir to newDir
            var axis = prevDir.CrossProduct(newDir);
            double sinA = axis.Length;
            if (sinA < 1e-9) return prevU; // Collinear
            double cosA = prevDir.DotProduct(newDir);
            double angle = Math.Atan2(sinA, cosA);
            var rot = Matrix3d.Rotation(angle, axis.GetNormal(), Point3d.Origin);
            var transformed = prevU.TransformBy(rot);
            // Re-orthogonalise against newDir
            transformed = (transformed - newDir * transformed.DotProduct(newDir)).GetNormal();
            return transformed;
        }

        private static Point3d? TryReadPoint(PropertyInfo? pi, object instance)
        {
            if (pi == null) return null;
            try
            {
                var v = pi.GetValue(instance);
                if (v is Point3d p) return p;
            }
            catch { }
            return null;
        }

        private static double? TryReadDouble(PropertyInfo? pi, object instance)
        {
            if (pi == null) return null;
            try
            {
                var v = pi.GetValue(instance);
                if (v is double d && !double.IsNaN(d)) return d;
                if (v is float f) return (double)f;
                if (v is int i) return (double)i;
            }
            catch { }
            return null;
        }

        private static List<Point3d> SampleCurve3d(Curve3d curve, int samples)
        {
            var pts = new List<Point3d>();
            try
            {
                var interval = curve.GetInterval();
                double t0 = interval.LowerBound;
                double t1 = interval.UpperBound;
                for (int i = 0; i <= samples; i++)
                {
                    double t = t0 + (t1 - t0) * ((double)i / samples);
                    pts.Add(curve.EvaluatePoint(t));
                }
            }
            catch { }
            return pts;
        }

        private static bool TryAxisFromExtents(Entity entity, out List<Point3d> axis)
        {
            axis = new List<Point3d>();
            try
            {
                var ext = entity.GeometricExtents;
                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                double dx = max.X - min.X, dy = max.Y - min.Y, dz = max.Z - min.Z;
                // Longest axis of the AABB becomes the pipe axis
                if (dx >= dy && dx >= dz)
                {
                    axis.Add(new Point3d(min.X, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5));
                    axis.Add(new Point3d(max.X, (min.Y + max.Y) * 0.5, (min.Z + max.Z) * 0.5));
                }
                else if (dy >= dz)
                {
                    axis.Add(new Point3d((min.X + max.X) * 0.5, min.Y, (min.Z + max.Z) * 0.5));
                    axis.Add(new Point3d((min.X + max.X) * 0.5, max.Y, (min.Z + max.Z) * 0.5));
                }
                else
                {
                    axis.Add(new Point3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, min.Z));
                    axis.Add(new Point3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, max.Z));
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static double? ParseDiameterMm(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            // Match leading numeric segment, handles "DN50", "2\"", "50 mm", "2in", "50.8"
            var m = Regex.Match(raw, @"[-+]?\d+(\.\d+)?");
            if (!m.Success) return null;
            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return null;
            string lower = raw.ToLowerInvariant();
            if (lower.Contains("\"") || lower.Contains("in") || lower.Contains("inch")) return v * 25.4;
            if (lower.Contains("mm") || lower.StartsWith("dn")) return v;
            // Nominal size like "50" — treat as mm (DN convention)
            return v;
        }

        private static void ApplyColor(Entity entity, SweptDiskGeometry geom)
        {
            try
            {
                if (entity.Color.IsByLayer)
                {
                    // Caller usually re-applies via layer; keep defaults otherwise
                    return;
                }
                var cv = entity.Color.ColorValue;
                geom.Red = cv.R;
                geom.Green = cv.G;
                geom.Blue = cv.B;
            }
            catch { }
        }
    }
}
