using System;
using System.Collections.Generic;

namespace SteelKit.Core
{
    /// <summary>Properti penampang, dihitung langsung dari geometri (bukan dari tabel). Satuan mm.</summary>
    public class SectionProperties
    {
        public double Area;            // mm²
        public double Cx, Cy;          // titik berat (koordinat penampang)
        public double Ix, Iy, Ixy;     // mm⁴ terhadap sumbu titik berat
        public double Iu, Iv, Alpha;   // sumbu utama (mm⁴), Alpha = sudut sumbu u terhadap x (rad)
        public double Sx, Sy;          // modulus elastis minimum, mm³
        public double Zx, Zy;          // modulus plastis, mm³
        public double Rx, Ry, Rv;      // jari-jari girasi, mm (Rv = minimum, penting untuk siku)
        public double J;               // konstanta torsi (pendekatan), mm⁴
        public double Perimeter;       // keliling luar, mm (untuk luas cat)
        public double Width, Height;   // ukuran kotak batas
        public double KgPerM(double density = 7850) => Area * 1e-6 * density;
        /// <summary>Luas permukaan cat per meter (m²/m), luar + dalam untuk penampang terbuka.</summary>
        public double PaintAreaPerM;
    }

    public static class SectionCalc
    {
        public static SectionProperties Compute(SectionShape s)
        {
            var loops = new List<List<Vec2>>();
            foreach (var l in s.AllLoops) loops.Add(l.Discretize());

            // Arah: luar CCW (+), lubang CW (-) supaya luas lubang terkurangi.
            for (int i = 0; i < loops.Count; i++)
            {
                double a = SignedArea(loops[i]);
                bool wantPositive = i == 0;
                if ((a > 0) != wantPositive) loops[i].Reverse();
            }

            double A = 0, Sx1 = 0, Sy1 = 0, Ixx = 0, Iyy = 0, Ixy0 = 0;
            foreach (var p in loops)
            {
                int n = p.Count;
                for (int i = 0; i < n; i++)
                {
                    Vec2 a = p[i], b = p[(i + 1) % n];
                    double c = a.X * b.Y - b.X * a.Y;
                    A += c / 2;
                    Sx1 += (a.X + b.X) * c / 6;   // ∫x dA
                    Sy1 += (a.Y + b.Y) * c / 6;   // ∫y dA
                    Ixx += (a.Y * a.Y + a.Y * b.Y + b.Y * b.Y) * c / 12;   // ∫y² dA
                    Iyy += (a.X * a.X + a.X * b.X + b.X * b.X) * c / 12;   // ∫x² dA
                    Ixy0 += (a.X * b.Y + 2 * a.X * a.Y + 2 * b.X * b.Y + b.X * a.Y) * c / 24;
                }
            }
            var r = new SectionProperties { Area = A };
            if (A <= 0) return r;
            r.Cx = Sx1 / A; r.Cy = Sy1 / A;
            r.Ix = Ixx - A * r.Cy * r.Cy;
            r.Iy = Iyy - A * r.Cx * r.Cx;
            r.Ixy = Ixy0 - A * r.Cx * r.Cy;
            double avg = (r.Ix + r.Iy) / 2, dif = (r.Ix - r.Iy) / 2;
            double rad = Math.Sqrt(dif * dif + r.Ixy * r.Ixy);
            r.Iu = avg + rad; r.Iv = avg - rad;
            r.Alpha = 0.5 * Math.Atan2(-2 * r.Ixy, r.Ix - r.Iy);
            if (Math.Abs(r.Ixy) < 1e-6 * Math.Max(1, r.Ix)) { r.Alpha = 0; r.Iu = Math.Max(r.Ix, r.Iy); r.Iv = Math.Min(r.Ix, r.Iy); }

            var bb = s.Bounds();
            r.Width = bb[2] - bb[0]; r.Height = bb[3] - bb[1];
            double cyTop = bb[3] - r.Cy, cyBot = r.Cy - bb[1];
            double cxR = bb[2] - r.Cx, cxL = r.Cx - bb[0];
            r.Sx = r.Ix / Math.Max(cyTop, cyBot);
            r.Sy = r.Iy / Math.Max(cxR, cxL);
            r.Rx = Math.Sqrt(r.Ix / A); r.Ry = Math.Sqrt(r.Iy / A); r.Rv = Math.Sqrt(Math.Max(r.Iv, 0) / A);
            r.Zx = Plastic(loops, A, bb[1], bb[3], false);
            r.Zy = Plastic(loops, A, bb[0], bb[2], true);
            r.Perimeter = Perim(loops[0]);
            // Penampang terbuka: loop luar sudah mengelilingi semua permukaan. Penampang tertutup: hanya sisi luar dicat.
            r.PaintAreaPerM = r.Perimeter / 1000.0;
            return r;
        }

        static double Perim(List<Vec2> p)
        {
            double s = 0; for (int i = 0; i < p.Count; i++) s += (p[(i + 1) % p.Count] - p[i]).Length; return s;
        }

        public static double SignedArea(List<Vec2> p)
        {
            double a = 0; int n = p.Count;
            for (int i = 0; i < n; i++) { var u = p[i]; var v = p[(i + 1) % n]; a += u.X * v.Y - v.X * u.Y; }
            return a / 2;
        }

        // Modulus plastis: cari garis netral plastis (luas atas = luas bawah), lalu jumlahkan |jarak| x dA.
        static double Plastic(List<List<Vec2>> loops, double A, double lo, double hi, bool aboutY)
        {
            double a = lo, b = hi;
            for (int it = 0; it < 80; it++)
            {
                double m = (a + b) / 2;
                double above = Clip(loops, m, aboutY, out _);
                if (above > A / 2) a = m; else b = m;
            }
            double pna = (a + b) / 2;
            Clip(loops, pna, aboutY, out double qAbove);   // ∫(t - pna) dA, bagian atas (positif)
            double total = 0;                                // ∫(t - pna) dA seluruh penampang ≈ 0 di PNA
            foreach (var p in loops) total += FirstMoment(p, pna, aboutY);
            double qBelow = qAbove - total;                  // = -∫bawah, positif
            return qAbove + qBelow;
        }

        // Luas dan momen pertama bagian dengan koordinat >= garis potong (y atau x).
        static double Clip(List<List<Vec2>> loops, double cut, bool aboutY, out double moment)
        {
            double area = 0; moment = 0;
            foreach (var p in loops)
            {
                var c = ClipHalf(p, cut, aboutY);
                if (c.Count < 3) continue;
                area += SignedArea(c);
                moment += FirstMoment(c, cut, aboutY);
            }
            return area;
        }

        static double FirstMoment(List<Vec2> p, double origin, bool aboutY)
        {
            double s = 0; int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                double c = a.X * b.Y - b.X * a.Y;
                s += aboutY ? (a.X + b.X) * c / 6 : (a.Y + b.Y) * c / 6;
            }
            return s - origin * SignedArea(p);
        }

        // Sutherland–Hodgman terhadap satu setengah bidang (koordinat >= cut). Integral tetap benar untuk poligon cekung.
        static List<Vec2> ClipHalf(List<Vec2> p, double cut, bool aboutY)
        {
            var o = new List<Vec2>();
            int n = p.Count;
            Func<Vec2, double> f = v => aboutY ? v.X - cut : v.Y - cut;
            for (int i = 0; i < n; i++)
            {
                Vec2 a = p[i], b = p[(i + 1) % n];
                double fa = f(a), fb = f(b);
                if (fa >= 0) o.Add(a);
                if ((fa >= 0) != (fb >= 0))
                {
                    double t = fa / (fa - fb);
                    o.Add(new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                }
            }
            return o;
        }
    }
}
