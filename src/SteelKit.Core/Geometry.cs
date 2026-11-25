using System;
using System.Collections.Generic;

namespace SteelKit.Core
{
    /// <summary>Titik 2D sederhana (mm).</summary>
    public struct Vec2
    {
        public double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public double Length => Math.Sqrt(X * X + Y * Y);
        public Vec2 Unit { get { double l = Length; return l > 0 ? new Vec2(X / l, Y / l) : this; } }
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
    }

    /// <summary>Simpul polyline: titik + bulge ke simpul berikutnya (konvensi AutoCAD: tan(sudut/4), positif = berlawanan jarum jam).</summary>
    public struct Vertex
    {
        public double X, Y, Bulge;
        public Vertex(double x, double y, double bulge = 0) { X = x; Y = y; Bulge = bulge; }
        public Vec2 P => new Vec2(X, Y);
    }

    /// <summary>Satu kurva tertutup (garis + busur).</summary>
    public class Loop
    {
        public List<Vertex> Vertices = new List<Vertex>();
        public Loop() { }
        public Loop(IEnumerable<Vertex> v) { Vertices.AddRange(v); }

        /// <summary>Titik-titik rapat (busur dipecah) untuk hitungan properti dan pratinjau.</summary>
        public List<Vec2> Discretize(double maxStepRad = Math.PI / 96)
        {
            var pts = new List<Vec2>();
            int n = Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                var a = Vertices[i];
                var b = Vertices[(i + 1) % n];
                pts.Add(a.P);
                if (Math.Abs(a.Bulge) < 1e-12) continue;
                double theta = 4 * Math.Atan(a.Bulge);           // sudut sapuan bertanda
                Vec2 chord = b.P - a.P;
                double c = chord.Length;
                if (c < 1e-12) continue;
                double r = c / (2 * Math.Sin(Math.Abs(theta) / 2));
                // pusat: tengah tali busur + normal * jarak
                Vec2 mid = (a.P + b.P) * 0.5;
                Vec2 nrm = new Vec2(-chord.Y, chord.X).Unit; // kiri dari arah tali
                double h = r * Math.Cos(Math.Abs(theta) / 2);
                Vec2 center = mid + nrm * (theta > 0 ? h : -h); // h < 0 otomatis untuk busur > 180°
                double a0 = Math.Atan2(a.Y - center.Y, a.X - center.X);
                int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / maxStepRad));
                for (int k = 1; k < steps; k++)
                {
                    double ang = a0 + theta * k / steps;
                    pts.Add(new Vec2(center.X + r * Math.Cos(ang), center.Y + r * Math.Sin(ang)));
                }
            }
            return pts;
        }

        public Loop Transformed(Func<Vec2, Vec2> f, bool mirror = false)
        {
            var l = new Loop();
            foreach (var v in Vertices) { var p = f(v.P); l.Vertices.Add(new Vertex(p.X, p.Y, mirror ? -v.Bulge : v.Bulge)); }
            return l;
        }
    }

    /// <summary>Penampang = satu loop luar + nol atau lebih lubang. Luas lubang dikurangkan.</summary>
    public class SectionShape
    {
        public Loop Outer;
        public List<Loop> Holes = new List<Loop>();

        public IEnumerable<Loop> AllLoops { get { yield return Outer; foreach (var h in Holes) yield return h; } }

        public SectionShape Translated(double dx, double dy)
        {
            var s = new SectionShape { Outer = Outer.Transformed(p => new Vec2(p.X + dx, p.Y + dy)) };
            foreach (var h in Holes) s.Holes.Add(h.Transformed(p => new Vec2(p.X + dx, p.Y + dy)));
            return s;
        }

        public SectionShape Scaled(double k)
        {
            var s = new SectionShape { Outer = Outer.Transformed(p => p * k) };
            foreach (var h in Holes) s.Holes.Add(h.Transformed(p => p * k));
            return s;
        }

        /// <summary>Kotak batas (min x, min y, max x, max y) dari titik rapat.</summary>
        public double[] Bounds()
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var l in AllLoops)
                foreach (var p in l.Discretize())
                {
                    if (p.X < x0) x0 = p.X; if (p.Y < y0) y0 = p.Y;
                    if (p.X > x1) x1 = p.X; if (p.Y > y1) y1 = p.Y;
                }
            return new[] { x0, y0, x1, y1 };
        }
    }

    public static class Shapes
    {
        /// <summary>
        /// Poligon bersudut tajam + jari-jari per sudut -> loop dengan busur fillet (cembung atau cekung otomatis).
        /// radii[i] = 0 berarti sudut tajam.
        /// </summary>
        public static Loop Filleted(IList<Vec2> pts, IList<double> radii)
        {
            int n = pts.Count;
            var loop = new Loop();
            for (int i = 0; i < n; i++)
            {
                Vec2 A = pts[(i - 1 + n) % n], P = pts[i], C = pts[(i + 1) % n];
                double r = radii == null ? 0 : radii[i];
                if (r <= 1e-9) { loop.Vertices.Add(new Vertex(P.X, P.Y)); continue; }
                Vec2 u = (A - P).Unit, v = (C - P).Unit;
                double cosAng = Math.Max(-1, Math.Min(1, Vec2.Dot(u, v)));
                double ang = Math.Acos(cosAng);               // sudut dalam di P
                if (ang < 1e-6 || Math.PI - ang < 1e-6) { loop.Vertices.Add(new Vertex(P.X, P.Y)); continue; }
                double d = r / Math.Tan(ang / 2);
                // batasi supaya fillet tidak melebihi panjang sisi
                double maxD = Math.Min((A - P).Length, (C - P).Length) * 0.999;
                if (d > maxD) { d = maxD; }
                Vec2 t1 = P + u * d, t2 = P + v * d;
                double sweep = Math.PI - ang;
                double turn = Vec2.Cross(P - A, C - P);        // >0 belok kiri -> busur CCW
                double bulge = Math.Tan(sweep / 4) * (turn > 0 ? 1 : -1);
                loop.Vertices.Add(new Vertex(t1.X, t1.Y, bulge));
                loop.Vertices.Add(new Vertex(t2.X, t2.Y));
            }
            return loop;
        }

        public static Loop Circle(double cx, double cy, double r)
        {
            return new Loop(new[] { new Vertex(cx - r, cy, 1), new Vertex(cx + r, cy, 1) });
        }

        public static Loop Rect(double x0, double y0, double x1, double y1, double r = 0)
        {
            var pts = new[] { new Vec2(x0, y0), new Vec2(x1, y0), new Vec2(x1, y1), new Vec2(x0, y1) };
            return Filleted(pts, new[] { r, r, r, r });
        }
    }
}
