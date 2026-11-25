using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SteelKit.Core
{
    public enum Family { WF, H, UNP, CNP, Z, L, T, HOLLOW, CHS, PL, RD }

    public class ParamDef
    {
        public string Key, Label;
        public ParamDef(string key, string label) { Key = key; Label = label; }
    }

    /// <summary>Keterangan per keluarga profil: nama, parameter, dan kegunaan umum.</summary>
    public class FamilyInfo
    {
        public Family Family;
        public string Label;         // nama untuk pengguna
        public string Usage;         // pemakaian umum (praktik umum, bukan hasil desain)
        public ParamDef[] Params;

        public static readonly Dictionary<Family, FamilyInfo> All = new Dictionary<Family, FamilyInfo>
        {
            [Family.WF] = new FamilyInfo(Family.WF, "WF / IWF (sayap lebar)",
                "Balok lantai, rafter portal, balok crane ringan. Kuat terhadap lentur sumbu kuat; untuk kolom pilih H-beam bila tekuk sumbu lemah dominan.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("tw", "Tebal badan tw"), P("tf", "Tebal sayap tf"), P("r", "Jari-jari r")),
            [Family.H] = new FamilyInfo(Family.H, "H-beam (lebar = tinggi)",
                "Kolom bangunan dan portal, balok beban berat, tiang. Kekakuan kedua sumbu hampir seimbang.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("tw", "Tebal badan tw"), P("tf", "Tebal sayap tf"), P("r", "Jari-jari r")),
            [Family.UNP] = new FamilyInfo(Family.UNP, "UNP (kanal U canai panas)",
                "Balok tangga (ibu tangga), lintel, balok tepi, dudukan mesin, rangka platform. Pusat geser di luar badan: pasang berpasangan atau dikekang bila menerima beban besar.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("tw", "Tebal badan tw"), P("tf", "Tebal sayap tf"), P("r", "Jari-jari r")),
            [Family.CNP] = new FamilyInfo(Family.CNP, "CNP / C berbibir (canai dingin)",
                "Gording atap, girt dinding, rangka plafon dan partisi berat, rangka kanopi ringan. Pasang sag rod untuk bentang gording yang panjang.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("C", "Tinggi bibir C"), P("t", "Tebal t"), P("r", "Jari-jari dalam r")),
            [Family.Z] = new FamilyInfo(Family.Z, "Z berbibir (canai dingin)",
                "Gording dan girt menerus dengan sambungan tumpang (overlap) di atas rafter, umum pada bangunan PEB.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("C", "Tinggi bibir C"), P("t", "Tebal t"), P("r", "Jari-jari dalam r")),
            [Family.L] = new FamilyInfo(Family.L, "Siku (sama / tidak sama kaki)",
                "Bracing, batang rangka batang (truss), cleat sambungan, dudukan, lintel kecil. Cek jari-jari girasi minimum (rv) untuk batang tekan.",
                P("H", "Kaki tegak A"), P("B", "Kaki datar B"), P("t", "Tebal t"), P("r", "Jari-jari r")),
            [Family.T] = new FamilyInfo(Family.T, "T (potongan WF / H)",
                "Batang tepi rangka batang, lintel, pengaku. Biasanya dipotong dari WF atau H-beam.",
                P("H", "Tinggi H"), P("B", "Lebar sayap B"), P("tw", "Tebal badan tw"), P("tf", "Tebal sayap tf"), P("r", "Jari-jari r")),
            [Family.HOLLOW] = new FamilyInfo(Family.HOLLOW, "Hollow (kotak / persegi panjang)",
                "Kolom ringan, rangka kanopi, railing, rangka batang ringan, rangka mezanin. Rapi secara arsitektur dan kuat torsi.",
                P("H", "Tinggi H"), P("B", "Lebar B"), P("t", "Tebal t"), P("r", "Jari-jari luar r")),
            [Family.CHS] = new FamilyInfo(Family.CHS, "Pipa baja (CHS / SCH / medium)",
                "Kolom kanopi, railing dan pegangan tangga, struktur rangka ruang, bracing tekan, tiang. Kekakuan sama ke segala arah.",
                P("D", "Diameter luar D"), P("t", "Tebal t")),
            [Family.PL] = new FamilyInfo(Family.PL, "Plat strip",
                "Pelat sambungan, pengaku (stiffener), gusset, bracing tarik ringan, pelat tumpuan.",
                P("B", "Lebar B"), P("t", "Tebal t")),
            [Family.RD] = new FamilyInfo(Family.RD, "Besi bulat (rod)",
                "Bracing X tarik (atap dan dinding), sag rod gording, batang tarik, angkur. Hanya menahan tarik.",
                P("D", "Diameter D")),
        };

        static ParamDef P(string k, string l) => new ParamDef(k, l);

        FamilyInfo(Family f, string label, string usage, params ParamDef[] ps) { Family = f; Label = label; Usage = usage; Params = ps; }

        public static FamilyInfo Of(Family f) => All[f];

        public static bool TryParse(string s, out Family f)
        {
            return Enum.TryParse((s ?? "").Trim(), true, out f);
        }
    }

    public class Profile
    {
        public Family Family;
        public string Name;
        public Dictionary<string, double> Dims = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public string Note = "";
        public bool IsUser;          // dari katalog pengguna (bisa dihapus)

        public double this[string key]
        {
            get { return Dims.TryGetValue(key, out double v) ? v : 0; }
            set { Dims[key] = value; }
        }

        public FamilyInfo Info => FamilyInfo.Of(Family);

        public Profile Clone()
        {
            var p = new Profile { Family = Family, Name = Name, Note = Note, IsUser = IsUser };
            foreach (var kv in Dims) p.Dims[kv.Key] = kv.Value;
            return p;
        }

        static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Nama baku dari dimensi, mis. WF400x200x8x13, C150x65x20x3.2, CHS114.3x4.5.</summary>
        public string AutoName()
        {
            switch (Family)
            {
                case Family.WF: return "WF" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["tw"]) + "x" + F(this["tf"]);
                case Family.H: return "H" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["tw"]) + "x" + F(this["tf"]);
                case Family.UNP: return "UNP" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["tw"]) + "x" + F(this["tf"]);
                case Family.T: return "T" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["tw"]) + "x" + F(this["tf"]);
                case Family.CNP: return "C" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["C"]) + "x" + F(this["t"]);
                case Family.Z: return "Z" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["C"]) + "x" + F(this["t"]);
                case Family.L: return "L" + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["t"]);
                case Family.HOLLOW: return (Math.Abs(this["H"] - this["B"]) < 1e-9 ? "SHS" : "RHS") + F(this["H"]) + "x" + F(this["B"]) + "x" + F(this["t"]);
                case Family.CHS: return "CHS" + F(this["D"]) + "x" + F(this["t"]);
                case Family.PL: return "PL" + F(this["B"]) + "x" + F(this["t"]);
                case Family.RD: return "RD" + F(this["D"]);
            }
            return "?";
        }

        /// <summary>Isi nilai bawaan untuk parameter yang kosong (jari-jari).</summary>
        public void FillDefaults()
        {
            switch (Family)
            {
                case Family.CNP:
                case Family.Z:
                    if (!Dims.ContainsKey("r")) this["r"] = this["t"];
                    break;
                case Family.HOLLOW:
                    if (!Dims.ContainsKey("r")) this["r"] = 2 * this["t"];
                    break;
                case Family.WF: case Family.H: case Family.UNP: case Family.T: case Family.L:
                    if (!Dims.ContainsKey("r")) this["r"] = 0;
                    break;
            }
        }

        /// <summary>Daftar kesalahan dimensi (kosong = valid).</summary>
        public List<string> Validate()
        {
            var e = new List<string>();
            foreach (var pd in Info.Params)
            {
                double v = this[pd.Key];
                bool optional = pd.Key == "r";
                if (double.IsNaN(v) || v < 0 || (!optional && v <= 0)) e.Add(pd.Label + " harus lebih dari 0.");
            }
            if (e.Count > 0) return e;
            double H = this["H"], B = this["B"], tw = this["tw"], tf = this["tf"], t = this["t"], C = this["C"], D = this["D"], r = this["r"];
            switch (Family)
            {
                case Family.WF: case Family.H:
                    if (tw >= B) e.Add("Tebal badan harus lebih kecil dari lebar sayap.");
                    if (2 * tf >= H) e.Add("Dua kali tebal sayap harus lebih kecil dari tinggi.");
                    if (tw + 2 * r >= B) e.Add("Jari-jari terlalu besar untuk lebar sayap.");
                    if (2 * tf + 2 * r >= H) e.Add("Jari-jari terlalu besar untuk tinggi.");
                    break;
                case Family.UNP:
                    if (tw >= B) e.Add("Tebal badan harus lebih kecil dari lebar sayap.");
                    if (2 * tf >= H) e.Add("Dua kali tebal sayap harus lebih kecil dari tinggi.");
                    if (tw + r >= B || 2 * tf + 2 * r >= H) e.Add("Jari-jari terlalu besar.");
                    break;
                case Family.T:
                    if (tw >= B) e.Add("Tebal badan harus lebih kecil dari lebar sayap.");
                    if (tf + r >= H) e.Add("Tebal sayap + jari-jari harus lebih kecil dari tinggi.");
                    break;
                case Family.CNP: case Family.Z:
                    if (2 * t >= B) e.Add("Tebal terlalu besar untuk lebar sayap.");
                    if (C >= H / 2) e.Add("Bibir harus lebih pendek dari setengah tinggi.");
                    if (C <= t + r) e.Add("Bibir terlalu pendek untuk tebal dan jari-jari.");
                    if (r + t >= B / 2) e.Add("Jari-jari terlalu besar.");
                    break;
                case Family.L:
                    if (t >= H || t >= B) e.Add("Tebal harus lebih kecil dari panjang kaki.");
                    if (t + r >= H || t + r >= B) e.Add("Jari-jari terlalu besar.");
                    break;
                case Family.HOLLOW:
                    if (2 * t >= Math.Min(H, B)) e.Add("Tebal terlalu besar untuk ukuran hollow.");
                    if (r >= Math.Min(H, B) / 2) e.Add("Jari-jari sudut terlalu besar.");
                    break;
                case Family.CHS:
                    if (2 * t >= D) e.Add("Tebal terlalu besar untuk diameter.");
                    break;
            }
            return e;
        }

        /// <summary>Bentuk penampang, titik asal di tengah kotak batas. H searah sumbu Y lokal, B searah X.</summary>
        public SectionShape BuildShape()
        {
            double H = this["H"], B = this["B"], tw = this["tw"], tf = this["tf"], t = this["t"], C = this["C"], D = this["D"], r = this["r"];
            var s = new SectionShape();
            switch (Family)
            {
                case Family.WF:
                case Family.H:
                {
                    double x = B / 2, y = H / 2, w = tw / 2;
                    var pts = V(-x, -y, x, -y, x, -y + tf, w, -y + tf, w, y - tf, x, y - tf, x, y, -x, y, -x, y - tf, -w, y - tf, -w, -y + tf, -x, -y + tf);
                    s.Outer = Shapes.Filleted(pts, R(0, 0, 0, r, r, 0, 0, 0, 0, r, r, 0));
                    break;
                }
                case Family.UNP:
                {
                    var pts = V(0, 0, B, 0, B, tf, tw, tf, tw, H - tf, B, H - tf, B, H, 0, H);
                    s.Outer = Shapes.Filleted(pts, R(0, 0, 0, r, r, 0, 0, 0));
                    break;
                }
                case Family.T:
                {
                    double x = B / 2, w = tw / 2;
                    var pts = V(-w, 0, w, 0, w, H - tf, x, H - tf, x, H, -x, H, -x, H - tf, -w, H - tf);
                    s.Outer = Shapes.Filleted(pts, R(0, 0, r, 0, 0, 0, 0, r));
                    break;
                }
                case Family.CNP:
                {
                    double ro = r + t;
                    var pts = V(0, 0, B, 0, B, C, B - t, C, B - t, t, t, t, t, H - t, B - t, H - t, B - t, H - C, B, H - C, B, H, 0, H);
                    s.Outer = Shapes.Filleted(pts, R(ro, ro, 0, 0, r, r, r, r, 0, 0, ro, ro));
                    break;
                }
                case Family.Z:
                {
                    double ro = r + t;
                    var pts = V(0, 0, B, 0, B, C, B - t, C, B - t, t, t, t, t, H, t - B, H, t - B, H - C, 2 * t - B, H - C, 2 * t - B, H - t, 0, H - t);
                    s.Outer = Shapes.Filleted(pts, R(ro, ro, 0, 0, r, r, ro, ro, 0, 0, r, r));
                    break;
                }
                case Family.L:
                {
                    var pts = V(0, 0, B, 0, B, t, t, t, t, H, 0, H);
                    s.Outer = Shapes.Filleted(pts, R(0, 0, r / 2, r, r / 2, 0));
                    break;
                }
                case Family.HOLLOW:
                {
                    double ri = Math.Max(r - t, 0);
                    s.Outer = Shapes.Rect(-B / 2, -H / 2, B / 2, H / 2, r);
                    s.Holes.Add(Shapes.Rect(-B / 2 + t, -H / 2 + t, B / 2 - t, H / 2 - t, ri));
                    break;
                }
                case Family.CHS:
                    s.Outer = Shapes.Circle(0, 0, D / 2);
                    s.Holes.Add(Shapes.Circle(0, 0, D / 2 - t));
                    break;
                case Family.PL:
                    s.Outer = Shapes.Rect(-B / 2, -t / 2, B / 2, t / 2);
                    break;
                case Family.RD:
                    s.Outer = Shapes.Circle(0, 0, D / 2);
                    break;
            }
            var bb = s.Bounds();
            return s.Translated(-(bb[0] + bb[2]) / 2, -(bb[1] + bb[3]) / 2);
        }

        /// <summary>Konstanta torsi St. Venant, rumus pendekatan dinding tipis (mm⁴).</summary>
        public double TorsionJ()
        {
            double H = this["H"], B = this["B"], tw = this["tw"], tf = this["tf"], t = this["t"], C = this["C"], D = this["D"];
            switch (Family)
            {
                case Family.WF: case Family.H: case Family.UNP:
                    return (2 * B * Math.Pow(tf, 3) + (H - 2 * tf) * Math.Pow(tw, 3)) / 3;
                case Family.T:
                    return (B * Math.Pow(tf, 3) + (H - tf) * Math.Pow(tw, 3)) / 3;
                case Family.CNP: case Family.Z:
                    return ((H - t) + 2 * (B - t) + 2 * (C - t / 2)) * Math.Pow(t, 3) / 3;
                case Family.L:
                    return (H + B - t) * Math.Pow(t, 3) / 3;
                case Family.HOLLOW:
                {
                    double am = (B - t) * (H - t), pm = 2 * ((B - t) + (H - t));
                    return 4 * am * am * t / pm;
                }
                case Family.CHS:
                    return Math.PI * (Math.Pow(D, 4) - Math.Pow(D - 2 * t, 4)) / 32;
                case Family.RD:
                    return Math.PI * Math.Pow(D, 4) / 32;
                case Family.PL:
                {
                    double a = Math.Max(B, t), b = Math.Min(B, t);
                    return a * b * b * b / 3 * (1 - 0.63 * b / a);
                }
            }
            return 0;
        }

        /// <summary>Pergeseran penampang (mm, sumbu lokal x,y) supaya titik acuan berada di garis batang.</summary>
        public Vec2 AnchorShift(Anchor a)
        {
            var bb = BuildShape().Bounds();
            switch (a)
            {
                case Anchor.Atas: return new Vec2(0, -bb[3]);
                case Anchor.Bawah: return new Vec2(0, -bb[1]);
                case Anchor.TitikBerat:
                    var pr = Properties();
                    return new Vec2(-pr.Cx, -pr.Cy);
                default: return new Vec2(0, 0);
            }
        }

        public SectionProperties Properties()
        {
            var p = SectionCalc.Compute(BuildShape());
            p.J = TorsionJ();
            return p;
        }

        /// <summary>Garis tampak samping memanjang batang (offset dari as, mm) + apakah garis tersembunyi.
        /// view = "samping" (melihat badan, tinggi H) atau "atas" (melihat sayap, lebar B).</summary>
        public List<KeyValuePair<double, bool>> ElevationLines(bool topView)
        {
            double H = this["H"], B = this["B"], tw = this["tw"], tf = this["tf"], t = this["t"], D = this["D"], C = this["C"];
            var l = new List<KeyValuePair<double, bool>>();
            Action<double, bool> add = (o, hidden) => l.Add(new KeyValuePair<double, bool>(o, hidden));
            double half = Extent(topView) / 2;
            add(-half, false); add(half, false);
            switch (Family)
            {
                case Family.WF: case Family.H:
                    if (!topView) { add(-H / 2 + tf, false); add(H / 2 - tf, false); }
                    else { add(-tw / 2, true); add(tw / 2, true); }
                    break;
                case Family.UNP:
                    if (!topView) { add(-H / 2 + tf, false); add(H / 2 - tf, false); }
                    else add(-B / 2 + tw, true);
                    break;
                case Family.CNP: case Family.Z:
                    if (!topView) { add(-H / 2 + t, false); add(H / 2 - t, false); add(-H / 2 + C, false); add(H / 2 - C, false); }
                    else add(-B / 2 + t, true);
                    break;
                case Family.T:
                    if (!topView) add(H / 2 - tf, false); else { add(-tw / 2, true); add(tw / 2, true); }
                    break;
                case Family.L:
                    if (!topView) add(-H / 2 + t, false); else add(-B / 2 + t, true);
                    break;
                case Family.HOLLOW:
                    if (!topView) { add(-H / 2 + t, true); add(H / 2 - t, true); }
                    else { add(-B / 2 + t, true); add(B / 2 - t, true); }
                    break;
                case Family.CHS:
                    add(-D / 2 + t, true); add(D / 2 - t, true);
                    break;
            }
            return l;
        }

        /// <summary>Ukuran penampang tegak lurus arah pandang (mm).</summary>
        public double Extent(bool topView)
        {
            switch (Family)
            {
                case Family.CHS: case Family.RD: return this["D"];
                case Family.PL: return topView ? this["B"] : this["t"];
                case Family.Z: return topView ? 2 * this["B"] - this["t"] : this["H"];
                default: return topView ? this["B"] : this["H"];
            }
        }

        static List<Vec2> V(params double[] xy)
        {
            var l = new List<Vec2>();
            for (int i = 0; i < xy.Length; i += 2) l.Add(new Vec2(xy[i], xy[i + 1]));
            return l;
        }

        static double[] R(params double[] r) => r;

        public override string ToString() => Name;
    }
}
