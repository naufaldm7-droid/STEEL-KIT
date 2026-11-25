using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelKit.Core
{
    public enum PartKind { Pelat, Pengaku, Baut, Mur, Ring, Angkur, RingPlat, Grout, Beton, Siku, Las, Batang, Profil }

    /// <summary>
    /// Benda pemotong batang (koordinat lokal sambungan): kotak (coping, potongan miring) atau silinder (mulut ikan pipa).
    /// Disimpan di batang dan dikurangkan dari solidnya setiap kali batang dibangun ulang.
    /// </summary>
    public class Cutter
    {
        public bool Cylinder;
        public Frame F = new Frame();
        public double X0, X1, Y0, Y1, Z0, Z1;   // kotak: rentang di F; silinder: Z0..Z1 sepanjang F.W
        public double Radius;

        public static Cutter Box(Frame f, double x0, double x1, double y0, double y1, double z0, double z1)
            => new Cutter { F = f, X0 = x0, X1 = x1, Y0 = y0, Y1 = y1, Z0 = z0, Z1 = z1 };
        public static Cutter Cyl(Frame f, double r, double z0, double z1)
            => new Cutter { Cylinder = true, F = f, Radius = r, Z0 = z0, Z1 = z1 };

        /// <summary>Titik sudut + tiga vektor rusuk (untuk kotak) atau pusat dasar + sumbu (silinder), dalam frame induk.</summary>
        public Cutter In(Frame parent) => new Cutter { Cylinder = Cylinder, F = parent.Compose(F), X0 = X0, X1 = X1, Y0 = Y0, Y1 = Y1, Z0 = Z0, Z1 = Z1, Radius = Radius };

        public string Serialize()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var v = new[] { F.O.X, F.O.Y, F.O.Z, F.U.X, F.U.Y, F.U.Z, F.V.X, F.V.Y, F.V.Z, F.W.X, F.W.Y, F.W.Z, X0, X1, Y0, Y1, Z0, Z1, Radius };
            return (Cylinder ? "C" : "B") + ":" + string.Join(",", v.Select(x => x.ToString("R", ci)));
        }

        public static Cutter Parse(string s)
        {
            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var p = s.Split(':');
                var v = p[1].Split(',').Select(x => double.Parse(x, ci)).ToArray();
                return new Cutter
                {
                    Cylinder = p[0] == "C",
                    F = new Frame(new Vec3(v[0], v[1], v[2]), new Vec3(v[3], v[4], v[5]), new Vec3(v[6], v[7], v[8]), new Vec3(v[9], v[10], v[11])),
                    X0 = v[12], X1 = v[13], Y0 = v[14], Y1 = v[15], Z0 = v[16], Z1 = v[17], Radius = v[18],
                };
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Satu benda padat: loop 2D di bidang (U,V) frame F, diekstrusi sepanjang W dari Z0 ke Z1.
    /// Koordinat F dinyatakan dalam koordinat lokal sambungan.
    /// </summary>
    public class Part
    {
        public PartKind Kind;
        public string Label = "";
        public Frame F = new Frame();
        public Loop Outline;
        public List<Loop> Holes = new List<Loop>();
        public double Z0, Z1;
        public double Thickness => Z1 - Z0;
        /// <summary>
        /// Bidang potong (koordinat lokal sambungan): hanya bagian dengan (titik - P)·N ≥ 0 yang dipertahankan.
        /// Dipakai untuk potongan tegak (plumb cut) ujung batang miring dan sayap haunch supaya rapat, tidak menembus.
        /// </summary>
        public List<ClipPlane> Keep = new List<ClipPlane>();
        public Part Clip(Vec3 point, Vec3 keepNormal) { Keep.Add(new ClipPlane(point, keepNormal)); return this; }

        public double MassKg(double density = 7850)
        {
            var s = new SectionShape { Outer = Outline };
            s.Holes.AddRange(Holes);
            return SectionCalc.Compute(s).Area * Thickness * 1e-9 * density;
        }

        /// <summary>Titik-titik 3D (lokal sambungan) dari kedua tutup, untuk proyeksi dan kotak batas.</summary>
        public IEnumerable<Vec3> Corners()
        {
            var loop = Outline.Discretize(Math.PI / 24);
            if (Keep.Count == 0)
            {
                foreach (var p in loop)
                {
                    yield return F.Point(p.X, p.Y, Z0);
                    yield return F.Point(p.X, p.Y, Z1);
                }
                yield break;
            }
            // terpotong bidang: titik sudut yang tersisa + titik potong rusuk dengan setiap bidang
            var a = loop.Select(p => F.Point(p.X, p.Y, Z0)).ToList();
            var b = loop.Select(p => F.Point(p.X, p.Y, Z1)).ToList();
            var pts = new List<Vec3>(a.Concat(b));
            var edges = new List<(Vec3, Vec3)>();
            for (int i = 0; i < a.Count; i++)
            {
                int j = (i + 1) % a.Count;
                edges.Add((a[i], a[j])); edges.Add((b[i], b[j])); edges.Add((a[i], b[i]));
            }
            foreach (var pl in Keep)
                foreach (var (p, q) in edges)
                {
                    double dp = pl.Dist(p), dq = pl.Dist(q);
                    if ((dp < 0) != (dq < 0)) pts.Add(p + (q - p) * (dp / (dp - dq)));
                }
            foreach (var p in pts)
                if (Keep.All(pl => pl.Dist(p) >= -1e-6)) yield return p;
        }
    }

    /// <summary>Bidang potong: dipertahankan sisi (titik - P)·N ≥ 0.</summary>
    public class ClipPlane
    {
        public Vec3 P, N;
        public ClipPlane(Vec3 p, Vec3 n) { P = p; N = n.Unit; }
        public double Dist(Vec3 x) => (x - P).Dot(N);
    }

    public class Check
    {
        public bool Ok;
        public string Item;
        public string Rule;
        public Check(bool ok, string item, string rule) { Ok = ok; Item = item; Rule = rule; }
        public override string ToString() => (Ok ? "OK    " : "GAGAL ") + Item + (string.IsNullOrEmpty(Rule) ? "" : "  [" + Rule + "]");
    }

    public class BoqLine
    {
        public string Item;     // kategori: Pelat, Baut, Angkur, ...
        public string Spec;     // uraian
        public int Count;
        public double UnitKg;
        public double TotalKg => Count * UnitKg;
    }

    public class ViewSpec
    {
        public string Title;
        public Vec3 R, Up;   // sumbu kanan dan atas gambar, dalam koordinat lokal sambungan
        public ViewSpec(string t, Vec3 r, Vec3 up) { Title = t; R = r; Up = up; }
        public Vec2 P(Vec3 p) => new Vec2(p.Dot(R), p.Dot(Up));
        public Vec3 Toward => R.Cross(Up);
    }

    public class DimSpec
    {
        public int View;
        public Vec3 A, B;
        public Vec3 OffsetDir;
        public double Offset;
        public string Text;
    }

    public class NoteSpec
    {
        public int View;
        public Vec3 At;
        public string Text;
        public Vec2 Shift;     // geser teks dari titik tunjuk (dalam satuan tinggi teks)
    }

    /// <summary>Hasil bangun sambungan: benda 3D, cek aturan, daftar bahan, dan data gambar detail 2D.</summary>
    public class ConnResult
    {
        public List<Part> Parts = new List<Part>();
        public List<Check> Checks = new List<Check>();
        public List<BoqLine> Boq = new List<BoqLine>();
        public List<ViewSpec> Views = new List<ViewSpec>();
        public List<DimSpec> Dims = new List<DimSpec>();
        public List<NoteSpec> Notes = new List<NoteSpec>();
        /// <summary>Penyesuaian ujung batang (koordinat lokal sambungan): batang yang ditopang berakhir di titik ini.</summary>
        public Vec3? TrimSupported;
        /// <summary>Penyesuaian ujung batang penopang (mis. kolom diperpanjang sampai atas pelat ujung lutut).</summary>
        public Vec3? TrimSupport;
        /// <summary>Pemotong untuk batang yang ditopang / penopang (coping, potongan miring, mulut ikan).</summary>
        public List<Cutter> CutSupported = new List<Cutter>();
        public List<Cutter> CutSupport = new List<Cutter>();
        /// <summary>Penyesuaian ujung untuk sambungan banyak batang (indeks = urutan batang di sambungan; 0 = penopang, 1 = ditopang).</summary>
        public Dictionary<int, Vec3> TrimMembers = new Dictionary<int, Vec3>();
        /// <summary>Geser seluruh batang (kedua ujung) sejajar, mis. supaya punggung kaki menempel pelat buhul. Indeks seperti TrimMembers.</summary>
        public Dictionary<int, Vec3> ShiftMembers = new Dictionary<int, Vec3>();
        public bool AllOk => Checks.All(c => c.Ok);

        public void Rule(bool ok, string item, string rule) => Checks.Add(new Check(ok, item, rule));

        public void AddBoq(string item, string spec, int count, double unitKg)
        {
            var l = Boq.FirstOrDefault(b => b.Item == item && b.Spec == spec);
            if (l != null) { l.Count += count; return; }
            Boq.Add(new BoqLine { Item = item, Spec = spec, Count = count, UnitKg = unitKg });
        }

        public void Dim(int view, Vec3 a, Vec3 b, Vec3 offDir, double off, string text = null)
            => Dims.Add(new DimSpec { View = view, A = a, B = b, OffsetDir = offDir, Offset = off, Text = text });

        public void Note(int view, Vec3 at, string text, double sx = 1.5, double sy = 1.5)
            => Notes.Add(new NoteSpec { View = view, At = at, Text = text, Shift = new Vec2(sx, sy) });

        // ---------------------------------------------------------------- pembangun benda
        public Part Box(PartKind k, string label, double x0, double x1, double y0, double y1, double z0, double z1)
        {
            var p = new Part { Kind = k, Label = label, Outline = Shapes.Rect(x0, y0, x1, y1), Z0 = z0, Z1 = z1 };
            Parts.Add(p);
            return p;
        }

        /// <summary>Prisma: loop di bidang (u,v) frame f, tebal dari z0 ke z1 sepanjang w.</summary>
        public Part Prism(PartKind k, string label, Frame f, Loop loop, double z0, double z1, IEnumerable<Loop> holes = null)
        {
            var p = new Part { Kind = k, Label = label, F = f, Outline = loop, Z0 = z0, Z1 = z1 };
            if (holes != null) p.Holes.AddRange(holes);
            Parts.Add(p);
            return p;
        }

        public static Frame AxisFrame(Vec3 origin, Vec3 axis)
        {
            var w = axis.Unit;
            var helper = Math.Abs(w.Dot(Vec3.UnitZ)) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
            var u = helper.Cross(w).Unit;
            var v = w.Cross(u).Unit;
            return new Frame(origin, u, v, w);
        }

        public Part Cylinder(PartKind k, string label, Vec3 baseCenter, Vec3 axis, double r, double len)
            => Prism(k, label, AxisFrame(baseCenter, axis), Shapes.Circle(0, 0, r), 0, len);

        public Part Hex(PartKind k, string label, Vec3 baseCenter, Vec3 axis, double across, double h)
        {
            double rr = across / Math.Sqrt(3);
            var pts = Enumerable.Range(0, 6).Select(i => new Vec2(rr * Math.Cos(Math.PI / 3 * i), rr * Math.Sin(Math.PI / 3 * i))).ToList();
            return Prism(k, label, AxisFrame(baseCenter, axis), Shapes.Filleted(pts, null), 0, h);
        }

        /// <summary>
        /// Baut lengkap: kepala di sisi 'face' (permukaan tempat kepala menekan), batang masuk searah 'dir'
        /// sejauh jepitan 'grip', lalu ring dan mur di sisi lain. Mengembalikan panjang baut.
        /// </summary>
        public double Bolt(Vec3 face, Vec3 dir, double grip, BoltData b, bool washerUnderHead = true)
        {
            var d = dir.Unit;
            double len = b.Length(grip, washerUnderHead ? 2 : 1);
            double wHead = washerUnderHead ? b.WasherT : 0;
            if (washerUnderHead) Cylinder(PartKind.Ring, "", face - d * b.WasherT, d, b.WasherOD / 2, b.WasherT);
            Hex(PartKind.Baut, "", face - d * (wHead + b.Head), d, b.Across, b.Head);
            Cylinder(PartKind.Baut, "", face - d * wHead, d, b.D / 2, len);
            Cylinder(PartKind.Ring, "", face + d * grip, d, b.WasherOD / 2, b.WasherT);
            Hex(PartKind.Mur, "", face + d * (grip + b.WasherT), d, b.Across, b.Nut);
            return len;
        }

        public double[] Bounds()
        {
            double[] bb = { double.MaxValue, double.MaxValue, double.MaxValue, double.MinValue, double.MinValue, double.MinValue };
            foreach (var p in Parts)
                foreach (var c in p.Corners())
                {
                    bb[0] = Math.Min(bb[0], c.X); bb[1] = Math.Min(bb[1], c.Y); bb[2] = Math.Min(bb[2], c.Z);
                    bb[3] = Math.Max(bb[3], c.X); bb[4] = Math.Max(bb[4], c.Y); bb[5] = Math.Max(bb[5], c.Z);
                }
            return bb;
        }
    }

    /// <summary>Garis 2D hasil proyeksi untuk gambar detail.</summary>
    public class Shape2D
    {
        public List<Vec2> Pts;
        public bool IsCircle;
        public Vec2 Center;
        public double Radius;
        public PartKind Kind;
    }

    public static class Projection
    {
        /// <summary>Proyeksi ortografis semua benda ke satu tampak (tanpa penghapusan garis tersembunyi).</summary>
        public static List<Shape2D> Project(ConnResult r, ViewSpec v)
        {
            var list = new List<Shape2D>();
            var toward = v.Toward;
            foreach (var p in r.Parts)
            {
                double along = Math.Abs(p.F.W.Dot(toward));
                if (along > 0.999 && p.Keep.Count == 0)
                {
                    // dilihat searah tebal: bentuk loop asli (lingkaran tetap lingkaran)
                    foreach (var loop in new[] { p.Outline }.Concat(p.Holes))
                    {
                        if (loop.Vertices.Count == 2 && Math.Abs(Math.Abs(loop.Vertices[0].Bulge) - 1) < 1e-9)
                        {
                            var a = p.F.Point(loop.Vertices[0].X, loop.Vertices[0].Y, p.Z1);
                            var b = p.F.Point(loop.Vertices[1].X, loop.Vertices[1].Y, p.Z1);
                            var c = (v.P(a) + v.P(b)) * 0.5;
                            list.Add(new Shape2D { IsCircle = true, Center = c, Radius = (v.P(a) - v.P(b)).Length / 2, Kind = p.Kind });
                        }
                        else
                        {
                            var pts = loop.Discretize(Math.PI / 24).Select(q => v.P(p.F.Point(q.X, q.Y, p.Z1))).ToList();
                            list.Add(new Shape2D { Pts = pts, Kind = p.Kind });
                        }
                    }
                }
                else
                {
                    var hull = Hull(p.Corners().Select(v.P).ToList());
                    if (hull.Count >= 3) list.Add(new Shape2D { Pts = hull, Kind = p.Kind });
                }
            }
            return list;
        }

        /// <summary>Selubung cembung (monotone chain).</summary>
        public static List<Vec2> Hull(List<Vec2> pts)
        {
            var p = pts.OrderBy(a => Math.Round(a.X, 6)).ThenBy(a => Math.Round(a.Y, 6)).ToList();
            if (p.Count < 3) return p;
            var h = new List<Vec2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                foreach (var q in p)
                {
                    while (h.Count >= start + 2 && Vec2.Cross(h[h.Count - 1] - h[h.Count - 2], q - h[h.Count - 2]) <= 1e-9) h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
                p.Reverse();
            }
            return h;
        }
    }
}
