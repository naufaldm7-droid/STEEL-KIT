using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SteelKit.Core
{
    public enum ConnType
    {
        PelatDasar, PelatUjung, FinPlate, SikuGanda, SambunganPelatUjung, LututHaunch, CleatGording, PelatBuhul,
        BracingRod, Turnbuckle, BracingBuhul, SilangBracing, FlangeBrace, SagRod,
        PelatTutup, Dudukan, MomenLas, MomenPelatSayap, KolomHollow, BalokTumpuk, KolomAngin, KonsolCrane, PelatBeton,
        SplicePelatSambung, GordingSambung, BuhulTruss, LasLangsung, AnakTangga,
    }

    /// <summary>Lingkungan bersama (diisi plugin AutoCAD atau uji): katalog profil dan berat jenis.</summary>
    public static class Env
    {
        static Catalog catalog;
        public static Catalog Catalog
        {
            get { return catalog ?? (catalog = Catalog.Load(Catalog.ReadEmbedded(), null)); }
            set { catalog = value; }
        }
        public static double Density = 7850;
    }

    /// <summary>Daftar pilihan nama profil untuk PropertyGrid (tetap boleh diketik bebas).</summary>
    public abstract class ProfileListBase : StringConverter
    {
        protected abstract Family[] Families { get; }
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
            => new StandardValuesCollection(Env.Catalog.All.Where(p => Families.Contains(p.Family)).Select(p => p.Name).ToList());
    }
    public class ProfilesIH : ProfileListBase { protected override Family[] Families => new[] { Family.WF, Family.H }; }
    public class ProfilesColumn : ProfileListBase { protected override Family[] Families => new[] { Family.WF, Family.H, Family.HOLLOW, Family.CHS }; }
    public class ProfilesPurlin : ProfileListBase { protected override Family[] Families => new[] { Family.CNP, Family.Z }; }
    public class ProfilesAngle : ProfileListBase { protected override Family[] Families => new[] { Family.L }; }

    /// <summary>
    /// Dasar semua sambungan. Properti publik = parameter yang bisa diatur pengguna (tampil di dialog),
    /// disimpan sebagai teks kunci=nilai di objek AutoCAD supaya bisa diedit ulang.
    /// Hanya aturan geometri (jarak/ukuran minimum) yang dicek; kekuatan tidak.
    /// </summary>
    public abstract class Connection
    {
        [Browsable(false)] public abstract ConnType Type { get; }
        [Browsable(false)] public abstract string Title { get; }

        /// <summary>Isi semua dimensi dengan nilai rekomendasi (praktik umum) dari profil dan ukuran baut saat ini.</summary>
        public abstract void Recommend();
        public abstract ConnResult Build();

        protected static Profile Prof(string name, string what)
        {
            var p = Env.Catalog.Find(name);
            if (p == null) throw new ArgumentException(what + " \"" + name + "\" tidak ada di katalog.");
            return p;
        }

        public static Connection Create(ConnType t)
        {
            switch (t)
            {
                case ConnType.PelatDasar: return new BasePlate();
                case ConnType.PelatUjung: return new EndPlate();
                case ConnType.FinPlate: return new FinPlate();
                case ConnType.SikuGanda: return new DoubleCleat();
                case ConnType.SambunganPelatUjung: return new SplicePlate();
                case ConnType.LututHaunch: return new KneeHaunch();
                case ConnType.PelatBuhul: return new GussetNode();
                case ConnType.BracingRod: return new BracingRod();
                case ConnType.Turnbuckle: return new Turnbuckle();
                case ConnType.BracingBuhul: return new BracingGusset();
                case ConnType.SilangBracing: return new CrossBracing();
                case ConnType.FlangeBrace: return new FlangeBrace();
                case ConnType.SagRod: return new SagRod();
                case ConnType.PelatTutup: return new CapPlate();
                case ConnType.Dudukan: return new SeatAngle();
                case ConnType.MomenLas: return new WeldedMoment();
                case ConnType.MomenPelatSayap: return new FlangePlateMoment();
                case ConnType.KolomHollow: return new HollowColumnBeam();
                case ConnType.BalokTumpuk: return new StackedBeams();
                case ConnType.KolomAngin: return new WindColumn();
                case ConnType.KonsolCrane: return new CraneBracket();
                case ConnType.PelatBeton: return new ConcretePlate();
                case ConnType.SplicePelatSambung: return new CoverPlateSplice();
                case ConnType.GordingSambung: return new PurlinSplice();
                case ConnType.BuhulTruss: return new TrussNode();
                case ConnType.LasLangsung: return new DirectWeld();
                case ConnType.AnakTangga: return new StairTread();
                default: return new PurlinCleat();
            }
        }

        public static string Label(ConnType t)
        {
            switch (t)
            {
                case ConnType.PelatDasar: return "Pelat dasar kolom + angkur";
                case ConnType.PelatUjung: return "Pelat ujung balok-kolom (end plate)";
                case ConnType.FinPlate: return "Fin plate (pelat sirip, geser)";
                case ConnType.SikuGanda: return "Siku ganda (double cleat, geser)";
                case ConnType.SambunganPelatUjung: return "Sambungan pelat ujung (puncak / splice)";
                case ConnType.LututHaunch: return "Lutut portal + haunch (PEB)";
                case ConnType.PelatBuhul: return "Pelat buhul siku / hollow / pipa (gusset)";
                case ConnType.BracingRod: return "Ujung rod bracing (ring miring / clevis)";
                case ConnType.Turnbuckle: return "Turnbuckle rod bracing";
                case ConnType.BracingBuhul: return "Pelat buhul bracing ke kolom / balok";
                case ConnType.SilangBracing: return "Persilangan bracing";
                case ConnType.FlangeBrace: return "Flange brace (PEB)";
                case ConnType.SagRod: return "Sag rod ke gording";
                case ConnType.PelatTutup: return "Pelat tutup kolom (balok di atas kolom)";
                case ConnType.Dudukan: return "Dudukan siku (seat)";
                case ConnType.MomenLas: return "Momen las langsung";
                case ConnType.MomenPelatSayap: return "Momen pelat sayap berbaut";
                case ConnType.KolomHollow: return "Balok ke kolom hollow / pipa";
                case ConnType.BalokTumpuk: return "Balok tumpuk";
                case ConnType.KolomAngin: return "Kolom angin ke rafter";
                case ConnType.KonsolCrane: return "Konsol crane / bracket";
                case ConnType.PelatBeton: return "Balok ke beton (pelat tanam / angkur kimia)";
                case ConnType.SplicePelatSambung: return "Splice pelat sambung (sayap + badan)";
                case ConnType.GordingSambung: return "Sambungan gording (tumpang / selongsong)";
                case ConnType.BuhulTruss: return "Buhul truss 3+ batang";
                case ConnType.LasLangsung: return "Las langsung hollow / pipa";
                case ConnType.AnakTangga: return "Anak tangga ke ibu tangga";
                default: return "Cleat gording";
            }
        }

        // ------------------------------------------------------------------ simpan / muat
        IEnumerable<PropertyInfo> Props() => GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite);

        public string Serialize()
        {
            var sb = new StringBuilder("tipe=" + Type);
            foreach (var p in Props())
            {
                var v = p.GetValue(this, null);
                string s = v is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(v, CultureInfo.InvariantCulture);
                sb.Append(';').Append(p.Name).Append('=').Append((s ?? "").Replace(";", ","));
            }
            return sb.ToString();
        }

        public static Connection Deserialize(string text)
        {
            var kv = (text ?? "").Split(';').Select(x => x.Split(new[] { '=' }, 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
            if (!kv.TryGetValue("tipe", out string t) || !Enum.TryParse(t, out ConnType type)) return null;
            var c = Create(type);
            foreach (var p in c.Props())
            {
                if (!kv.TryGetValue(p.Name, out string s)) continue;
                try
                {
                    object v;
                    if (p.PropertyType == typeof(double)) v = double.Parse(s, CultureInfo.InvariantCulture);
                    else if (p.PropertyType == typeof(int)) v = int.Parse(s, CultureInfo.InvariantCulture);
                    else if (p.PropertyType == typeof(bool)) v = bool.Parse(s);
                    else if (p.PropertyType.IsEnum) v = Enum.Parse(p.PropertyType, s);
                    else v = s;
                    p.SetValue(c, v, null);
                }
                catch { /* abaikan nilai rusak: tetap pakai bawaan */ }
            }
            return c;
        }

        public Connection Clone() => Deserialize(Serialize());

        // ------------------------------------------------------------------ pembantu bersama
        /// <summary>Baris baut pelat ujung (koordinat tegak): luar atas, dalam atas, tambahan, dalam bawah, luar bawah.</summary>
        protected static List<double> PlateRows(double zTop, double zBot, double tfTop, double tfBot, double pf, bool extTop, bool extBot, int extra, double pitch = 0)
        {
            var rows = new List<double>();
            if (extTop) rows.Add(zTop + pf);
            double a = zTop - tfTop - pf, b = zBot + tfBot + pf;
            rows.Add(a);
            // jarak baris tambahan: tetap (pitch > 0, dihitung dari baris dalam atas) atau dibagi rata, dibulatkan ke 5 mm
            double step = pitch > 0 ? pitch : Math.Floor(Math.Abs(a - b) / (extra + 1) / 5) * 5;
            for (int i = 1; i <= extra; i++) rows.Add(a - step * i);
            if (b < a - 1e-6) rows.Add(b);
            if (extBot) rows.Add(zBot - pf);
            return rows;
        }

        protected static double BoltKg(BoltData b, double len)
        {
            double hex = 3 * Math.Sqrt(3) / 2 * Math.Pow(b.Across / Math.Sqrt(3), 2);
            double vol = Math.PI * b.D * b.D / 4 * len + hex * (b.Head + b.Nut) + 2 * Math.PI / 4 * (b.WasherOD * b.WasherOD - b.Hole * b.Hole) * b.WasherT;
            return vol * 7.85e-6;
        }

        protected static string Pl(double a, double b, double t) => "PL " + Fmt.N(a) + "x" + Fmt.N(b) + "x" + Fmt.N(t);

        /// <summary>Potongan profil sebagai bahan sambungan (ikut 3D dan daftar bahan), mis. siku, konsol WF.</summary>
        protected static Part Piece(ConnResult r, PartKind kind, Profile p, Frame f, double len, string use)
        {
            var s = p.BuildShape();
            var part = r.Prism(kind, p.Name, f, s.Outer, 0, len, s.Holes);
            r.AddBoq(kind == PartKind.Siku ? "Siku" : "Profil", p.Name + " L=" + Fmt.N(len) + (string.IsNullOrEmpty(use) ? "" : " (" + use + ")"), 1, p.Properties().KgPerM(Env.Density) * len / 1000);
            return part;
        }

        /// <summary>Baris baut seragam di sepanjang garis: n buah mulai dari 'start' dengan jarak 'step'.</summary>
        protected static List<Vec3> Line(Vec3 start, Vec3 step, int n) => Enumerable.Range(0, Math.Max(n, 0)).Select(i => start + step * i).ToList();

        protected static void BoltBoq(ConnResult r, BoltSize s, BoltGrade g, double len, int n, string where = "")
        {
            if (n <= 0) return;
            r.AddBoq("Baut", "Baut " + s + "x" + Fmt.N(len) + " " + Grades.Label(g) + " + mur + 2 ring" + (string.IsNullOrEmpty(where) ? "" : " (" + where + ")"), n, BoltKg(BoltData.Of(s), len));
        }

        protected static void WeldBoq(ConnResult r, double a, double length, string where)
            => r.AddBoq("Las", "Las sudut a=" + Fmt.N(a) + " " + where + ", panjang " + Fmt.N(Math.Round(length)) + " mm", 1, 0);

        /// <summary>Potongan batang (untuk gambar detail; tidak ikut dibuat 3D bila batang asli ada).</summary>
        protected static void Stub(ConnResult r, Profile p, Frame f, double z0, double z1) => StubPart(r, p, f, z0, z1);

        protected static Part StubPart(ConnResult r, Profile p, Frame f, double z0, double z1)
        {
            var s = p.BuildShape();
            return r.Prism(PartKind.Batang, p.Name, f, s.Outer, z0, z1, s.Holes);
        }

        /// <summary>
        /// Ujung batang rapat ke sebuah bidang (mis. muka pelat ujung tegak) walau batang miring terhadap bidang itu:
        /// as batang diperpanjang sampai seluruh penampang melewati bidang, lalu dipotong tepat di bidang (potongan tegak / plumb cut).
        /// <paramref name="axisPoint"/> = titik as batang pada bidang; <paramref name="dir"/> = arah batang menjauhi sambungan;
        /// <paramref name="n"/> = normal bidang ke arah batang. Kembali: pemotong batang + titik ujung as baru (<paramref name="trim"/>).
        /// </summary>
        protected static Cutter FlushEnd(Profile p, Vec3 axisPoint, Vec3 dir, Vec3 n, out Vec3 trim, out double ext)
        {
            dir = dir.Unit; n = n.Unit;
            var pr = p.Properties();
            double R = 0.5 * Math.Sqrt(pr.Width * pr.Width + pr.Height * pr.Height);
            double c = Math.Abs(dir.Dot(n));
            ext = (c > 0.99999 ? 0 : R * Math.Sqrt(1 - c * c) / Math.Max(c, 0.05)) + 2;
            trim = axisPoint - dir * ext;
            double big = 2 * R + ext + 200;
            return Cutter.Box(ConnResult.AxisFrame(axisPoint, -n), -big, big, -big, big, 0, big);
        }

        /// <summary>Rintisan batang (gambar detail) yang mengembalikan Part-nya.</summary>
        protected static Part Stub2(ConnResult r, Profile p, Frame f, double len) => StubPart(r, p, f, 0, len);

        /// <summary>Potongan batang ke bidang + potongan rintisan (stub) gambar yang sama, sepanjang <paramref name="len"/> dari bidang.</summary>
        protected static Cutter FlushStub(ConnResult r, Profile p, Vec3 axisPoint, Vec3 dir, Vec3 n, double roll, double len, out Vec3 trim)
        {
            var cut = FlushEnd(p, axisPoint, dir, n, out trim, out double ext);
            Axes.Member(dir, roll, out Vec3 ax, out Vec3 ay, out Vec3 az);
            StubPart(r, p, new Frame(trim, ax, ay, az), 0, len + ext).Clip(axisPoint, n);
            return cut;
        }

        /// <summary>Las sudut pada sambungan T: hanya ukuran minimum (batas maksimum t-2 AISC J2.2b berlaku untuk las di tepi pelat, bukan sambungan T).</summary>
        protected static void WeldRule(ConnResult r, string where, double a, double t1, double t2)
        {
            double thin = Math.Min(t1, t2);
            double min = Fmt.MinFillet(thin);
            r.Rule(a >= min - 1e-9, where + ": las sudut a=" + Fmt.N(a) + " ≥ minimum " + Fmt.N(min) + " mm", "ukuran las minimum, praktik umum AISC 360 Tabel J2.4");
            r.Rule(a <= Math.Max(t1, t2) + 1e-9, where + ": las sudut a=" + Fmt.N(a) + " ≤ tebal bagian tertebal " + Fmt.N(Math.Max(t1, t2)) + " mm", "geometri");
        }

        protected static void EdgeRule(ConnResult r, string where, double e, BoltData b)
            => r.Rule(e >= b.MinEdge - 1e-6, where + ": jarak tepi " + Fmt.N(e) + " ≥ " + Fmt.N(b.MinEdge) + " mm", "jarak tepi minimum " + b.Size + ", praktik umum AISC 360 Tabel J3.4M");

        protected static void PitchRule(ConnResult r, string where, double p, BoltData b)
            => r.Rule(p >= b.MinSpacing - 1e-6, where + ": jarak antar baut " + Fmt.N(p) + " ≥ 3d = " + Fmt.N(b.MinSpacing) + " mm", "jarak baut minimum, praktik umum AISC 360 J3.3");

        protected static void MaxEdgeRule(ConnResult r, string where, double e, double t)
        {
            double max = Math.Min(12 * t, 150);
            r.Rule(e <= max + 1e-6, where + ": jarak tepi " + Fmt.N(e) + " ≤ 12t (maks 150) = " + Fmt.N(max) + " mm", "jarak tepi maksimum, praktik umum AISC 360 J3.5");
        }
    }

    /// <summary>Rekomendasi pemakaian sambungan menurut kondisi (praktik umum, bukan hasil desain).</summary>
    public static class Recommendations
    {
        public class Rec
        {
            public string Condition; public ConnType Type; public string Note;
            public Rec(string c, ConnType t, string n) { Condition = c; Type = t; Note = n; }
        }

        public static readonly List<Rec> All = new List<Rec>
        {
            new Rec("Kaki kolom portal PEB (tumpuan sendi)", ConnType.PelatDasar,
                "Pola angkur Dalam4 (4 angkur di antara sayap, dekat badan), tanpa pengaku. Momen kaki dianggap nol."),
            new Rec("Kaki kolom jepit / kolom bangunan bertingkat", ConnType.PelatDasar,
                "Pola angkur Luar4/Luar6/Luar8 di luar sayap, dengan pengaku di as badan bila pelat menonjol jauh."),
            new Rec("Kolom kanopi pipa / hollow ke beton", ConnType.PelatDasar,
                "Pelat persegi + 4 angkur di sudut, las keliling. Tutup ujung atas pipa supaya air tidak masuk."),
            new Rec("Balok ke kolom, hanya meneruskan geser (balok lantai sederhana)", ConnType.FinPlate,
                "Fin plate paling cepat dipasang. Alternatif: siku ganda bila balok perlu dipasang dari samping."),
            new Rec("Balok ke kolom pada portal (meneruskan momen)", ConnType.PelatUjung,
                "Pelat ujung diperpanjang + pengaku kolom di level sayap balok."),
            new Rec("Balok anak ke balok induk", ConnType.SikuGanda,
                "Siku ganda atau fin plate pada badan balok induk. Bila sayap atas sejajar, sayap balok anak perlu dicoak (coping)."),
            new Rec("Lutut portal PEB (kolom-rafter)", ConnType.LututHaunch,
                "Haunch di bawah rafter (panjang ±10% bentang), pelat ujung tegak, pengaku kolom di level sayap."),
            new Rec("Puncak portal (rafter-rafter)", ConnType.SambunganPelatUjung,
                "Pelat ujung miring berpasangan, baut di luar dan di dalam sayap."),
            new Rec("Sambungan memanjang rafter / balok / kolom (splice)", ConnType.SambunganPelatUjung,
                "Pelat ujung berpasangan; letakkan di daerah momen kecil."),
            new Rec("Rangka siku / plat strip / CNP: sudut, siku-T, atau sambungan lurus", ConnType.PelatBuhul,
                "Pelat buhul di bawah kaki yang sebidang, minimal 2 baut per batang pada garis gauge siku. Ujung batang diberi celah supaya tidak bertumpuk."),
            new Rec("Rangka hollow / pipa ringan (railing, kanopi, rangka atap ringan)", ConnType.PelatBuhul,
                "Metode Las: batang dilas ke pelat buhul sepanjang kaki. Untuk sambungan langsung pipa-ke-pipa tanpa pelat, potong dan las di lapangan."),
            new Rec("Bracing rod X atap / dinding PEB", ConnType.BracingRod,
                "Rod menembus badan kolom/rafter dengan ring miring (hillside) + mur ganda; clevis + pelat mata bila sudut rod kecil atau badan tipis."),
            new Rec("Pengencang rod bracing", ConnType.Turnbuckle,
                "Satu turnbuckle per rod (di tengah) supaya rod bisa dikencangkan sampai lurus."),
            new Rec("Bracing siku / hollow ke kolom atau balok", ConnType.BracingBuhul,
                "Pelat buhul dilas ke sayap (bidang bracing = bidang badan) atau ke badan; di pertemuan balok-kolom pelat dilas ke keduanya."),
            new Rec("Dua bracing siku bersilangan", ConnType.SilangBracing,
                "Satu baut + pelat pengisi setebal kaki di titik silang, mengurangi panjang tekuk."),
            new Rec("Sayap bawah rafter tertekan (dekat lutut / di bawah gording)", ConnType.FlangeBrace,
                "Siku 45° dari sayap bawah rafter ke badan gording, dua sisi, pengaku di rafter."),
            new Rec("Gording panjang (penahan arah lemah)", ConnType.SagRod,
                "Rod Ø10-16 menembus badan gording di 1/3 atau 1/2 bentang, mur + ring di kedua sisi."),
            new Rec("Balok menerus duduk di atas kolom", ConnType.PelatTutup,
                "Pelat tutup kolom + 4 baut ke sayap bawah balok, pengaku badan balok di atas sayap kolom."),
            new Rec("Balok sederhana yang mudah dipasang (dudukan)", ConnType.Dudukan,
                "Siku dudukan menahan reaksi, siku atas menjaga stabilitas; pengaku bila reaksi besar."),
            new Rec("Momen balok-kolom dilas di bengkel/lapangan", ConnType.MomenLas,
                "Sayap dilas CJP ke kolom, badan pelat sirip berbaut, pengaku kolom di level sayap."),
            new Rec("Momen balok-kolom berbaut di lapangan", ConnType.MomenPelatSayap,
                "Pelat sayap dilas ke kolom di bengkel, dibaut ke sayap balok di lapangan."),
            new Rec("Balok ke kolom hollow / pipa", ConnType.KolomHollow,
                "Pelat tembus bila dinding kolom tipis (b/t > 25), pelat tempel bila tebal."),
            new Rec("Balok anak di atas balok induk", ConnType.BalokTumpuk,
                "4 baut menembus kedua sayap, pengaku di kedua balok pada titik tumpu."),
            new Rec("Kolom angin dinding ujung PEB", ConnType.KolomAngin,
                "Pelat kepala sejajar sayap rafter dengan lubang slot tegak supaya rafter bebas melendut."),
            new Rec("Balok crane / beban terpusat pada kolom", ConnType.KonsolCrane,
                "Konsol WF pendek dilas ke sayap kolom, pengaku di titik beban dan di kolom."),
            new Rec("Balok baja ke dinding / kolom beton", ConnType.PelatBeton,
                "Pelat tanam berstud bila dicor bersamaan; pelat + angkur kimia bila beton sudah jadi."),
            new Rec("Sambungan memanjang balok/kolom yang dibaut di lapangan", ConnType.SplicePelatSambung,
                "Pelat sambung sayap + badan; pengisi bila tinggi profil berbeda."),
            new Rec("Gording menerus beberapa bentang", ConnType.GordingSambung,
                "Gording Z ditumpang (bersarang) di atas rafter; gording C pakai selongsong."),
            new Rec("Titik buhul rangka batang (truss) siku / hollow", ConnType.BuhulTruss,
                "Satu pelat buhul untuk semua batang yang bertemu; batang tepi menerus."),
            new Rec("Rangka hollow / pipa tanpa pelat", ConnType.LasLangsung,
                "Cabang dipotong mengikuti batang tepi (mulut ikan untuk pipa) dan dilas keliling."),
            new Rec("Anak tangga baja", ConnType.AnakTangga,
                "Pelat bordes di atas dua siku yang dibaut ke badan ibu tangga UNP."),
            new Rec("Gording atau girt ke rafter / kolom", ConnType.CleatGording,
                "Cleat pelat dilas di sayap rafter, 2 baut M12 (4 baut pada sambungan tumpang gording Z)."),
        };

        public const string Disclaimer = "Rekomendasi ini praktik umum. Jenis, ukuran, dan kapasitas sambungan tetap ditentukan dan dihitung engineer struktur (SNI 1729; cross-ref AISC 360).";
    }
}
