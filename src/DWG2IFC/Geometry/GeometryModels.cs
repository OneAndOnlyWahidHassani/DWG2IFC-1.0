using System;
using System.Collections.Generic;

namespace Plant3D.IfcExporter.Geometry
{
    public readonly struct Vertex3D : IEquatable<Vertex3D>
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        private const double Precision = 10000.0; // 0.1 mm precision
        private readonly long _qx;
        private readonly long _qy;
        private readonly long _qz;

        public Vertex3D(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
            _qx = (long)Math.Round(x * Precision);
            _qy = (long)Math.Round(y * Precision);
            _qz = (long)Math.Round(z * Precision);
        }

        public bool Equals(Vertex3D other) =>
            _qx == other._qx && _qy == other._qy && _qz == other._qz;

        public override bool Equals(object? obj) =>
            obj is Vertex3D other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_qx, _qy, _qz);

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    public readonly struct TriangleFace
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }

        public TriangleFace(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    public class ExtractedMesh
    {
        public List<Vertex3D> Vertices { get; set; } = new();
        public List<TriangleFace> Faces { get; set; } = new();
        public byte Red { get; set; } = 180;
        public byte Green { get; set; } = 180;
        public byte Blue { get; set; } = 180;

        public bool IsValid => Vertices.Count >= 3 && Faces.Count >= 1;
    }
}
