using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SteelKit.Core
{
    /// <summary>Satu garis as (grid): label (A, B, ... atau 1, 2, ...) dan posisinya (mm) di sumbu dunia.</summary>
    public class GridLine
    {
        public string Label;
        public double Pos;
    }

    /// <summary>
    /// Sistem as dari titik dasar kolom: posisi X diberi huruf (A, B, ...; tanpa I dan O),
    /// posisi Y diberi angka (1, 2, ...). Kolom = batang yang hampir tegak.
    /// </summary>
    public class GridSystem
    {
        public List<GridLine> X = new List<GridLine>();
        public List<GridLine> Y = new List<GridLine>();
        public bool Any => X.Count + Y.Count > 0;

        public static GridSystem FromMembers(IEnumerable<(Vec3 S, Vec3 E)> members, double tolMm = 50)
        {
            var g = new GridSystem();
            var bases = new List<Vec3>();
            foreach (var (s, e) in members)
            {
                var d = e - s;
                if (d.Length < 1e-6 || Math.Abs(d.Unit.Z) < 0.9) continue;
                bases.Add(s.Z <= e.Z ? s : e);
            }
            if (bases.Count == 0) return g;
            var xs = Cluster(bases.Select(b => b.X), tolMm);
            var ys = Cluster(bases.Select(b => b.Y), tolMm);
            for (int i = 0; i < xs.Count; i++) g.X.Add(new GridLine { Label = Letter(i), Pos = xs[i] });
            for (int i = 0; i < ys.Count; i++) g.Y.Add(new GridLine { Label = (i + 1).ToString(CultureInfo.InvariantCulture), Pos = ys[i] });
            return g;
        }

        /// <summary>Kelompokkan nilai yang berdekatan (dalam toleransi), kembalikan rata-rata tiap kelompok, terurut naik.</summary>
        public static List<double> Cluster(IEnumerable<double> values, double tol)
        {
            var v = values.OrderBy(x => x).ToList();
            var outp = new List<double>();
            int i = 0;
            while (i < v.Count)
            {
                int j = i;
                while (j + 1 < v.Count && v[j + 1] - v[j] <= tol) j++;
                outp.Add(Math.Round(v.Skip(i).Take(j - i + 1).Average(), 1));
                i = j + 1;
            }
            return outp;
        }

        /// <summary>Huruf as: A..Z tanpa I dan O (mudah tertukar dengan 1 dan 0), lalu AA, AB, ...</summary>
        public static string Letter(int i)
        {
            const string a = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            return i < a.Length ? a[i].ToString() : Letter(i / a.Length - 1) + a[i % a.Length];
        }
    }

    /// <summary>Penanda elevasi pada tampak: nama dan tinggi (mm) relatif terhadap dasar kolom.</summary>
    public class LevelMark
    {
        public string Name;
        public double Z;            // mm dunia
        public double Rel;          // mm relatif terhadap dasar (±0.000)
        public string Text => Elevation(Rel);

        public static string Elevation(double relMm)
        {
            double m = Math.Round(relMm / 1000.0, 3);
            if (Math.Abs(m) < 0.0005) return "±0.000";
            return (m > 0 ? "+" : "−") + Math.Abs(m).ToString("0.000", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Elevasi dari batang: dasar kolom terendah (±0.000), puncak kolom (bisa lebih dari satu), puncak rangka,
        /// ditambah level dari tabel level pengguna yang berada di dalam rentang. Nilai berdekatan (toleransi) digabung.
        /// </summary>
        public static List<LevelMark> FromMembers(IEnumerable<(Vec3 S, Vec3 E)> members, IEnumerable<(string name, double z)> userLevels = null, double tolMm = 50)
        {
            var list = members.ToList();
            var outp = new List<LevelMark>();
            if (list.Count == 0) return outp;
            var cols = list.Where(m => (m.E - m.S).Length > 1e-6 && Math.Abs((m.E - m.S).Unit.Z) >= 0.9).ToList();
            double zMin = list.Min(m => Math.Min(m.S.Z, m.E.Z)), zMax = list.Max(m => Math.Max(m.S.Z, m.E.Z));
            double zBase = cols.Count > 0 ? cols.Min(m => Math.Min(m.S.Z, m.E.Z)) : zMin;
            void Add(string n, double z)
            {
                if (outp.Any(o => Math.Abs(o.Z - z) <= tolMm)) return;
                outp.Add(new LevelMark { Name = n, Z = z, Rel = z - zBase });
            }
            Add(cols.Count > 0 ? "DASAR KOLOM" : "DASAR", zBase);
            if (cols.Count > 0)
            {
                var tops = GridSystem.Cluster(cols.Select(m => Math.Max(m.S.Z, m.E.Z)), tolMm);
                for (int i = 0; i < tops.Count; i++) Add(tops.Count == 1 ? "PUNCAK KOLOM" : "PUNCAK KOLOM " + (i + 1), tops[i]);
            }
            if (userLevels != null)
                foreach (var (n, z) in userLevels)
                    if (z > zMin + tolMm && z < zMax - tolMm) Add((n ?? "").ToUpperInvariant(), z);
            Add("PUNCAK RANGKA", zMax);
            return outp.OrderBy(o => o.Z).ToList();
        }
    }

    /// <summary>Data simbol las yang dibaca dari catatan detail sambungan ("Las sudut a=6 keliling", "Las CJP ...").</summary>
    public class WeldMark
    {
        public List<double> Sizes = new List<double>();     // tebal leher a (mm), urut kemunculan
        public bool Cjp, AllAround, BothSides;
        public double Length;                               // panjang las bila disebut ("sepanjang 150"), 0 = menerus
        public string Tail = "";                            // keterangan ekor simbol
        public string Rest = "";                            // sisa catatan (bukan las), baris dipisah \P

        public string SizeText => Sizes.Count == 0 ? "" : string.Join("/", Sizes.Distinct().Select(s => "a" + Fmt.N(s)));

        static readonly Regex ASize = new Regex(@"\ba\s*=\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
        static readonly Regex Len = new Regex(@"sepanjang\s+(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);

        /// <summary>Pisahkan bagian las dari teks catatan. Null bila catatan tidak menyebut las berukuran / CJP.</summary>
        public static WeldMark Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var w = new WeldMark();
            var rest = new List<string>();
            var tails = new List<string>();
            var segs = new List<(string desc, string size)>();
            bool found = false;
            foreach (var line in text.Split(new[] { "\\P" }, StringSplitOptions.None))
            {
                var keep = new List<string>();
                bool prevWeld = false;
                foreach (var seg0 in line.Split(new[] { ", " }, StringSplitOptions.None))
                {
                    string seg = seg0.Trim();
                    bool las = Regex.IsMatch(seg, @"\blas\b", RegexOptions.IgnoreCase);
                    bool cjp = Regex.IsMatch(seg, @"\bCJP\b");
                    bool hasA = ASize.IsMatch(seg);
                    // "Las sayap a=8, badan a=6": segmen kedua tanpa kata "las" tetap bagian las
                    bool weld = (las && (hasA || cjp)) || (prevWeld && hasA && Regex.IsMatch(seg, @"^\w+\s+a\s*="));
                    if (!weld) { keep.Add(seg0); prevWeld = false; continue; }
                    found = true; prevWeld = true;
                    foreach (Match m in ASize.Matches(seg)) w.Sizes.Add(double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture));
                    if (cjp) w.Cjp = true;
                    if (Regex.IsMatch(seg, @"\bkeliling\b", RegexOptions.IgnoreCase)) w.AllAround = true;
                    if (Regex.IsMatch(seg, @"\b(dua|kedua)\s+sisi\b", RegexOptions.IgnoreCase)) w.BothSides = true;
                    var lm = Len.Match(seg);
                    if (lm.Success) w.Length = double.Parse(lm.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                    // ekor: kata yang tersisa setelah kata kunci las dibuang ("sayap atas & bawah", "ke host", "kaki")
                    string t = seg;
                    t = Regex.Replace(t, @"\blas\b|\bsudut\b|\bkeliling\b|\b(dua|kedua)\s+sisi\b|\bCJP\b", "", RegexOptions.IgnoreCase);
                    t = Len.Replace(t, "");
                    var am = ASize.Match(seg);
                    t = ASize.Replace(t, "");
                    t = Regex.Replace(t, @"\s+", " ").Trim(' ', ',', '-');
                    segs.Add((t, am.Success ? "a" + am.Groups[1].Value.Replace(',', '.') : ""));
                }
                if (keep.Count > 0) rest.Add(string.Join(", ", keep).Trim());
            }
            if (!found) return null;
            // ukuran berbeda per bagian: "a8/a6" di simbol, bagiannya berurutan sama di ekor ("sayap/badan")
            bool multi = w.Sizes.Distinct().Count() > 1;
            if (multi) tails.Add(string.Join("/", segs.Where(q => q.size.Length > 0).Select(q => q.desc.Length > 0 ? q.desc : "?")));
            foreach (var (desc, size) in segs)
                if (!(multi && size.Length > 0) && desc.Length > 0) tails.Add(desc);
            var tailParts = new List<string>();
            if (w.Cjp) tailParts.Add("CJP");
            tailParts.AddRange(tails);
            w.Tail = string.Join(", ", tailParts);
            w.Rest = string.Join("\\P", rest.Where(r => r.Length > 0));
            return w;
        }
    }

    /// <summary>
    /// Penempatan dimensi berantai: dimensi yang teksnya bertumpuk dengan tetangga di garis yang sama
    /// dipindah ke lapis luar berikutnya. Masukan per dimensi: awal, akhir (sepanjang garis dimensi) dan lebar teks.
    /// </summary>
    public static class DimTiers
    {
        public static int[] Assign(IList<(double s0, double s1, double textW)> dims, double pad)
        {
            var tiers = new int[dims.Count];
            var occupied = new List<List<(double a, double b)>>();
            var order = Enumerable.Range(0, dims.Count).OrderBy(i => Math.Min(dims[i].s0, dims[i].s1)).ToList();
            foreach (int i in order)
            {
                double a = Math.Min(dims[i].s0, dims[i].s1), b = Math.Max(dims[i].s0, dims[i].s1);
                double mid = (a + b) / 2, half = dims[i].textW / 2 + pad;
                // teks di tengah; bila lebih lebar dari dimensinya, rentang teks yang dipakai
                double ta = Math.Min(a, mid - half), tb = Math.Max(b, mid + half);
                // dimensi yang muat tidak bertabrakan walau berbagi garis ekstensi: pakai rentang teks saja
                if (dims[i].textW + 2 * pad <= b - a) { ta = mid - half; tb = mid + half; }
                int t = 0;
                while (true)
                {
                    if (t == occupied.Count) occupied.Add(new List<(double, double)>());
                    if (!occupied[t].Any(o => ta < o.b && tb > o.a)) break;
                    t++;
                }
                occupied[t].Add((ta, tb));
                tiers[i] = t;
            }
            return tiers;
        }

        /// <summary>Perkiraan lebar teks dimensi (font teknik: ±0,75 tinggi per karakter).</summary>
        public static double TextWidth(string text, double h) => (string.IsNullOrEmpty(text) ? 4 : text.Length) * h * 0.75;
    }

    /// <summary>
    /// Tanda tangan bagian untuk marking: daftar lubang yang benar-benar mengenai batang (badan, sayap, lonjong) dan
    /// pemotong ujung, dalam koordinat lokal batang (x lebar, y tinggi, z panjang dari ujung awal), dibulatkan 2 mm.
    /// Batang yang sama tetapi dipasang terbalik (ujung ditukar) menghasilkan tanda tangan yang sama.
    /// </summary>
    public static class PartSignature
    {
        public static string Of(Profile p, Anchor a, double lengthMm, IEnumerable<Cutter> localCuts)
        {
            var sh = p.AnchorShift(a);
            double hw = p.Extent(true) / 2 + 1, hh = p.Extent(false) / 2 + 1, L = lengthMm;
            var items = new List<(string kind, double u, double v, double z, double d)>();
            foreach (var c in localCuts ?? Enumerable.Empty<Cutter>())
            {
                var o = c.F.O - new Vec3(sh.X, sh.Y, 0); var w = c.F.W;
                if (c.Cylinder)
                {
                    if (Math.Abs(w.X) > 0.9) { if (Math.Abs(o.Y) <= hh && o.Z >= -1 && o.Z <= L + 1) items.Add(("W", 0, o.Y, o.Z, 2 * c.Radius)); }
                    else if (Math.Abs(w.Y) > 0.9) { if (Math.Abs(o.X) <= hw && o.Z >= -1 && o.Z <= L + 1) items.Add(("F", o.X, Math.Sign(o.Y), o.Z, 2 * c.Radius)); }
                    else if (Math.Abs(w.X) > 0.05)
                    {
                        var q = o + w * (-o.X / w.X);
                        if (Math.Abs(q.Y) <= hh && q.Z >= -1 && q.Z <= L + 1) items.Add(("S", 0, q.Y, q.Z, 2 * c.Radius));
                    }
                }
                else
                {
                    // pemotong kotak: titik tengah + arah sumbu, cukup untuk membedakan bentuk ujung
                    var m = c.F.O + c.F.U * ((c.X0 + c.X1) / 2) + c.F.V * ((c.Y0 + c.Y1) / 2) + c.F.W * ((c.Z0 + c.Z1) / 2) - new Vec3(sh.X, sh.Y, 0);
                    items.Add(("B" + R(Math.Abs(c.F.W.Z) * 100), m.X, m.Y, m.Z, 0));
                }
            }
            string Key(bool mirror) => string.Join(";", items
                .Select(t => t.kind + ":" + R(t.u) + "," + R(t.v) + "," + R(mirror ? L - t.z : t.z) + "," + R(t.d))
                .OrderBy(x => x, StringComparer.Ordinal));
            string k0 = Key(false), k1 = Key(true);
            return string.CompareOrdinal(k0, k1) <= 0 ? k0 : k1;
        }

        static string R(double v) => (Math.Round(v / 2) * 2).ToString("0", CultureInfo.InvariantCulture);
    }

    /// <summary>Panjang batang skala grafis yang enak dibaca: ±targetPaperMm di kertas.</summary>
    public static class ScaleBar
    {
        static readonly double[] Nice = { 100, 200, 250, 500, 1000, 2000, 2500, 5000, 10000, 20000, 25000, 50000, 100000 };

        public static double ModelLength(double scale, double targetPaperMm = 25)
        {
            double want = scale * targetPaperMm;
            return Nice.OrderBy(n => Math.Abs(Math.Log(n / want))).First();
        }

        public static string Label(double mm) => mm >= 1000 ? Fmt.N(mm / 1000) + " m" : Fmt.N(mm) + " mm";
    }
}
