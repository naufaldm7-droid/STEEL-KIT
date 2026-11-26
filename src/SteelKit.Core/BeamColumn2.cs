using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    public enum ColumnOrient { TinggiSearahBalok, TinggiMelintang }
    public enum HollowPlate { PelatTempel, PelatTembus }
    public enum ConcreteFix { StudTanam, AngkurKimia }

    public class ProfilesTube : ProfileListBase { protected override Family[] Families => new[] { Family.HOLLOW, Family.CHS }; }

    /// <summary>Pembantu bersama untuk pelat sirip (shear tab) dan pengaku kolom.</summary>
    static class Shear
    {
        public static int Count(Profile beam, double pitch, double edge)
            => Math.Max(2, Math.Min(8, (int)Math.Floor((Support.ClearDepth(beam) - 2 * edge) / pitch) + 1));

        /// <summary>Pelat sirip di sisi +Y badan balok, mulai di x0, baut satu garis di x0 + a.</summary>
        public static double Tab(ConnResult r, Profile beam, double x0, double a, double e, double tp, int n, double p, BoltData b, out double hp)
        {
            double twb = Sec.Tw(beam);
            hp = (n - 1) * p + 2 * e;
            var pl = r.Box(PartKind.Pelat, "pelat sirip", x0, x0 + a + e, twb / 2, twb / 2 + tp, -hp / 2, hp / 2);
            r.AddBoq("Pelat", "PL " + Fmt.N(a + e) + "x" + Fmt.N(hp) + "x" + Fmt.N(tp) + " (pelat sirip)", 1, pl.MassKg());
            double len = 0;
            for (int i = 0; i < n; i++) len = r.Bolt(new Vec3(x0 + a, twb / 2 + tp, (i - (n - 1) / 2.0) * p), -Vec3.UnitY, tp + twb, b);
            return len;
        }

        /// <summary>Pengaku kolom (continuity plate) di level z, kedua sisi badan, di antara sayap kolom (tinggi profil searah X).</summary>
        public static void ColumnStiffeners(ConnResult r, Profile col, IEnumerable<double> levels, double ts)
        {
            double Hc = Sec.H(col), Bc = Sec.B(col), tfc = Sec.Tf(col), twc = Sec.Tw(col);
            foreach (var zl in levels)
                foreach (int s in new[] { 1, -1 })
                {
                    var st = r.SnipedStiffener(-Hc / 2 + tfc, Hc / 2 - tfc, s > 0 ? twc / 2 : -Bc / 2, s > 0 ? Bc / 2 : -twc / 2, zl - ts / 2, zl + ts / 2, s > 0, Sec.R(col) + 5);
                    r.AddBoq("Pelat", "PL " + Fmt.N(Hc - 2 * tfc) + "x" + Fmt.N((Bc - twc) / 2) + "x" + Fmt.N(ts) + " (pengaku kolom)", 1, st.MassKg());
                }
        }

        /// <summary>Pengaku balok tegak lurus as balok (sumbu X) pada x, kedua sisi badan.</summary>
        public static void BeamStiffeners(ConnResult r, Profile beam, double x, double zBot, double ts)
        {
            double H = Sec.H(beam), B = Sec.B(beam), tf = Sec.Tf(beam), tw = Sec.Tw(beam);
            // bidang (y, z), tebal arah x; dua sudut di sisi badan disnipe supaya bebas dari radius sudut profil
            double c = Math.Max(beam["r"], 0) + 5, zb = zBot + tf, zt = zBot + H - tf;
            foreach (int s in new[] { 1, -1 })
            {
                double y0 = tw / 2, y1 = B / 2;
                var pts = new List<Vec2> { new Vec2(y0 + c, zb), new Vec2(y1, zb), new Vec2(y1, zt), new Vec2(y0 + c, zt), new Vec2(y0, zt - c), new Vec2(y0, zb + c) };
                if (s < 0) pts = pts.Select(q => new Vec2(-q.X, q.Y)).Reverse().ToList();
                var st = r.Prism(PartKind.Pengaku, "", new Frame(new Vec3(x, 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), Shapes.Filleted(pts, null), -ts / 2, ts / 2);
                r.AddBoq("Pelat", "PL " + Fmt.N((B - tw) / 2) + "x" + Fmt.N(H - 2 * tf) + "x" + Fmt.N(ts) + " (pengaku balok)", 1, st.MassKg());
            }
        }
    }

    // =========================================================================================
    /// <summary>Balok duduk di atas kolom: pelat tutup kolom + 4 baut ke sayap bawah balok. Frame: asal = muka bawah balok di as kolom.</summary>
    public class CapPlate : Connection
    {
        public override ConnType Type => ConnType.PelatTutup;
        public override string Title => "Pelat tutup kolom (balok di atas kolom)";

        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesColumn))] public string Kolom { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Orientasi kolom")] public ColumnOrient Orientasi { get; set; } = ColumnOrient.TinggiSearahBalok;
        [Category("2. Pelat"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Panjang searah balok (mm)")] public double PanjangPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Lebar melintang (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Las kolom ke pelat a (mm)")] public double Las { get; set; }
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Gauge melintang (mm)")] public double Gauge { get; set; }
        [Category("3. Baut"), DisplayName("Jarak searah balok (mm)")] public double Pitch { get; set; }
        [Category("4. Pengaku"), DisplayName("Pengaku badan balok di atas sayap kolom")] public bool PengakuBalok { get; set; } = true;
        [Category("1. Profil"), DisplayName("Kemiringan balok (°)"), Description("0 = balok datar. Balok miring (mis. kanopi T): pelat sejajar sayap bawah balok, puncak kolom dipotong sejajar pelat.")]
        public double Sudut { get; set; }
        [Category("4. Pengaku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; }

        double Cs => Math.Cos(Sudut * Math.PI / 180);
        /// <summary>Panjang tapak kolom searah balok, diukur di bidang pelat (miring: dibagi cos sudut).</summary>
        double Along(Profile c) => (Orientasi == ColumnOrient.TinggiSearahBalok ? c.Extent(false) : c.Extent(true)) / Cs;
        double Across(Profile c) => Orientasi == ColumnOrient.TinggiSearahBalok ? c.Extent(true) : c.Extent(false);

        public override void Recommend()
        {
            var c = Prof(Kolom, "Kolom"); var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            TebalPelat = Fmt.StdPlate(Math.Max(12, Math.Max(Sec.Tf(c), 0.75 * b.D)));
            Gauge = Fmt.Round10(Math.Max(Sec.Tw(bm) + b.WasherOD + 20, 0.55 * Sec.B(bm)));
            Gauge = Math.Min(Gauge, Sec.B(bm) - 2 * b.MinEdge);
            Las = Math.Max(6, Fmt.MinFillet(Math.Min(TebalPelat, Sec.Tf(c))));
            // baut di luar penampang kolom: mur/kepala di bawah pelat tidak boleh duduk di atas sayap kolom atau lasnya
            Pitch = Fmt.Round10(Math.Max(3 * b.D, Along(c) + b.Across + 2 * Las + 10));
            PanjangPelat = Fmt.Round10(Math.Max(Along(c) + 50, Pitch + 2 * e));
            LebarPelat = Fmt.Round10(Math.Max(Across(c) + 50, Gauge + 2 * e));
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Sec.Tf(c)));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var c = Prof(Kolom, "Kolom"); var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            double tp = TebalPelat, Lp = PanjangPelat, Wp = LebarPelat, tfb = Sec.Tf(bm), Hb = Sec.H(bm);
            string lab = Pl(Lp, Wp, tp);
            var pl = r.Box(PartKind.Pelat, lab, -Lp / 2, Lp / 2, -Wp / 2, Wp / 2, -tp, 0);
            r.AddBoq("Pelat", lab + " (pelat tutup kolom)", 1, pl.MassKg());
            double len = 0;
            foreach (var x in new[] { -Pitch / 2, Pitch / 2 })
                foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                {
                    pl.Holes.Add(Shapes.Circle(x, y, b.Hole / 2));
                    len = r.Bolt(new Vec3(x, y, tfb), -Vec3.UnitZ, tfb + tp, b);
                }
            BoltBoq(r, Baut, Mutu, len, 4);
            WeldBoq(r, Las, 2 * (Along(c) + Across(c)), "kolom ke pelat tutup, keliling");
            if (PengakuBalok && (c.Family == Family.WF || c.Family == Family.H))
                foreach (var x in new[] { -Along(c) / 2 + Sec.Tf(c) / 2, Along(c) / 2 - Sec.Tf(c) / 2 })
                    Shear.BeamStiffeners(r, bm, x, 0, TebalPengaku);
            bool along = Orientasi == ColumnOrient.TinggiSearahBalok;
            // kolom tegak dunia; di frame sambungan (X searah balok, Z tegak lurus sayap bawah) arahnya miring bila balok miring
            double a = Sudut * Math.PI / 180;
            var up = new Vec3(Math.Sin(a), 0, Math.Cos(a));          // arah atas dunia di frame lokal
            var hz = new Vec3(Math.Cos(a), 0, -Math.Sin(a));          // mendatar searah balok di frame lokal
            var axisAtPlate = up * (-tp / Math.Cos(a));               // as kolom di muka bawah pelat
            double sl = 2 * Sec.H(c);
            var stub = Stub2(r, c, along ? new Frame(axisAtPlate - up * sl, -Vec3.UnitY, hz, up) : new Frame(axisAtPlate - up * sl, hz, Vec3.UnitY, up), sl + 2 * Sec.H(c));
            stub.Clip(new Vec3(0, 0, -tp), -Vec3.UnitZ);              // rintisan kolom rata di bawah pelat
            double L = Math.Max(2 * Hb, 800);
            Stub(r, bm, new Frame(new Vec3(-L, 0, Hb / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * L);
            if (Math.Abs(Sudut) < 0.01) r.TrimSupported = new Vec3(0, 0, -tp);
            else
            {
                // puncak kolom dipotong sejajar pelat miring (rapat, tidak menembus)
                r.CutSupported.Add(FlushEnd(c, axisAtPlate, up * -1, Vec3.UnitZ * -1, out Vec3 trim, out double _));
                r.TrimSupported = trim;
            }

            EdgeRule(r, "Baut ke tepi pelat (searah balok)", Lp / 2 - Pitch / 2, b);
            EdgeRule(r, "Baut ke tepi pelat (melintang)", Wp / 2 - Gauge / 2, b);
            EdgeRule(r, "Baut ke tepi sayap balok", Sec.B(bm) / 2 - Gauge / 2, b);
            PitchRule(r, "Baut", Math.Min(Pitch, Gauge), b);
            r.Rule(Gauge >= Sec.Tw(bm) + b.WasherOD - 1e-6, "Gauge cukup untuk badan balok + ring", "ruang kunci, praktik umum");
            r.Rule(Lp >= Along(c) - 1e-6 && Wp >= Across(c) - 1e-6, "Pelat menutup penampang kolom", "geometri");
            double outside = Pitch / 2 - b.Across / 2 - (Along(c) / 2 + Las);
            r.Rule(outside >= 5 - 1e-6, "Mur baut di luar kolom + las (" + Fmt.N(outside) + " ≥ 5 mm)", "ruang kunci, praktik umum");
            WeldRule(r, "Kolom ke pelat tutup", Las, Sec.Tf(c), tp);

            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH PELAT TUTUP", Vec3.UnitX, Vec3.UnitY));
            double off = 0.15 * Lp + 20;
            r.Dim(1, new Vec3(-Lp / 2, -Wp / 2, 0), new Vec3(Lp / 2, -Wp / 2, 0), -Vec3.UnitY, off);
            r.Dim(1, new Vec3(-Pitch / 2, Wp / 2, 0), new Vec3(Pitch / 2, Wp / 2, 0), Vec3.UnitY, off);
            r.Dim(1, new Vec3(Lp / 2, -Gauge / 2, 0), new Vec3(Lp / 2, Gauge / 2, 0), Vec3.UnitX, off);
            r.Note(0, new Vec3(Lp / 2, 0, -tp), lab + ", 4 baut " + Baut + " " + Grades.Label(Mutu) + "\\PLas a=" + Fmt.N(Las) + " keliling", 2, -2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Dudukan siku (seat) + siku atas, opsional pengaku. Frame seperti pelat ujung (X dari kolom ke balok, Z atas).</summary>
    public class SeatAngle : Connection
    {
        public override ConnType Type => ConnType.Dudukan;
        public override string Title => "Dudukan siku (seat) + siku atas";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF300x150x6.5x9";
        [Category("1. Profil"), DisplayName("Kolom / penopang"), TypeConverter(typeof(ProfilesIH))] public string Penopang { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.SayapKolom;
        [Category("2. Siku"), DisplayName("Siku dudukan"), TypeConverter(typeof(ProfilesAngle))] public string SikuDudukan { get; set; } = "L100x100x10";
        [Category("2. Siku"), DisplayName("Siku atas"), TypeConverter(typeof(ProfilesAngle))] public string SikuAtas { get; set; } = "L75x75x8";
        [Category("2. Siku"), DisplayName("Panjang siku (mm)")] public double PanjangSiku { get; set; }
        [Category("2. Siku"), DisplayName("Pengaku di bawah dudukan")] public bool Berpengaku { get; set; }
        [Category("2. Siku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; } = 10;
        [Category("2. Siku"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 10;
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            PanjangSiku = Fmt.Round10(Math.Max(Sec.B(bm), 3 * b.D + 2 * b.MinEdge));
            Gauge = Fmt.Round10(Math.Max(3 * b.D, Math.Min(0.6 * Sec.B(bm), PanjangSiku - 2 * b.MinEdge)));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var sup = Prof(Penopang, "Penopang"); var b = BoltData.Of(Baut);
            var Ls = Prof(SikuDudukan, "Siku dudukan"); var La = Prof(SikuAtas, "Siku atas");
            Sec.Face(sup, Muka == SupportFace.BadanBalok ? SupportFace.BadanKolom : Muka, out double f, out double clamp);
            double Hb = Sec.H(bm), tfb = Sec.Tf(bm), Lg = PanjangSiku;
            var fxz = new Frame(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY);
            double lenC = 0, lenB = 0;
            foreach (var (L, top) in new[] { (Ls, false), (La, true) })
            {
                double A = L["B"], Bv = L["H"], t = L["t"], z0 = top ? Hb / 2 : -Hb / 2, sg = top ? 1 : -1;
                var pts = new List<Vec2> { new Vec2(f, z0), new Vec2(f + A, z0), new Vec2(f + A, z0 + sg * t), new Vec2(f + t, z0 + sg * t), new Vec2(f + t, z0 + sg * Bv), new Vec2(f, z0 + sg * Bv) };
                r.Prism(PartKind.Siku, L.Name, fxz, Shapes.Filleted(pts, null), -Lg / 2, Lg / 2);
                r.AddBoq("Siku", L.Name + " L=" + Fmt.N(Lg) + (top ? " (siku atas)" : " (siku dudukan)"), 1, L.Properties().KgPerM(Env.Density) * Lg / 1000);
                double gv = Fmt.Round5(Math.Max(t + b.WasherOD / 2 + 3, 0.55 * Bv)), gh = Fmt.Round5(Math.Max(t + b.WasherOD / 2 + 3, 0.55 * A));
                foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                {
                    lenC = r.Bolt(new Vec3(f + t, y, z0 + sg * gv), -Vec3.UnitX, t + clamp, b);
                    lenB = r.Bolt(new Vec3(f + gh, y, top ? z0 + t : z0 + tfb), -Vec3.UnitZ, t + tfb, b);
                }
                EdgeRule(r, (top ? "Siku atas" : "Siku dudukan") + ": baut ke ujung kaki tegak", Bv - gv, b);
                EdgeRule(r, (top ? "Siku atas" : "Siku dudukan") + ": baut ke ujung kaki datar", A - gh, b);
                r.Rule(gh - Celah >= b.WasherOD / 2 + 2 - 1e-6 || top, "Baut kaki datar mengenai sayap balok (lewat celah ujung)", "geometri");
            }
            BoltBoq(r, Baut, Mutu, lenC, 4, "siku ke penopang");
            BoltBoq(r, Baut, Mutu, lenB, 4, "siku ke sayap balok");
            if (Berpengaku)
            {
                double A = Ls["B"], t = Ls["t"], hs = Ls["H"];
                var pts = new List<Vec2> { new Vec2(f + t, -Hb / 2 - t), new Vec2(f + A - 5, -Hb / 2 - t), new Vec2(f + t, -Hb / 2 - t - 1.5 * A) };
                var st = r.Prism(PartKind.Pengaku, "", fxz, Shapes.Filleted(pts, null), -TebalPengaku / 2, TebalPengaku / 2);
                r.AddBoq("Pelat", "PL t=" + Fmt.N(TebalPengaku) + " (pengaku dudukan)", 1, st.MassKg());
            }
            Support.Stub(r, sup, Muka, Hb, Stub);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Hb, 500));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            EdgeRule(r, "Baut ke ujung siku (memanjang)", (Lg - Gauge) / 2, b);
            PitchRule(r, "Baut sebaris", Gauge, b);
            r.Rule(Gauge / 2 + b.MinEdge <= Sec.B(bm) / 2 + 1e-6, "Baut di sayap balok dengan jarak tepi cukup", "jarak tepi");
            r.Rule(Ls["B"] - Celah >= 1.5 * tfb, "Panjang tumpuan balok di dudukan cukup", "praktik umum");

            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK MUKA", Vec3.UnitY, Vec3.UnitZ));
            r.Note(0, new Vec3(f + Ls["B"], 0, -Hb / 2), Ls.Name + " dudukan" + (Berpengaku ? " + pengaku" : "") + ", " + La.Name + " atas\\P8 baut " + Baut + " " + Grades.Label(Mutu), 2, -2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Sambungan momen dilas langsung: sayap balok las tumpul penetrasi penuh ke sayap kolom, badan dengan pelat sirip.</summary>
    public class WeldedMoment : Connection
    {
        public override ConnType Type => ConnType.MomenLas;
        public override string Title => "Momen las langsung (sayap CJP + pelat sirip)";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.SayapKolom;
        [Category("2. Las"), DisplayName("Celah akar (mm)")] public double Celah { get; set; } = 6;
        [Category("2. Las"), DisplayName("Las pelat sirip a (mm)")] public double Las { get; set; }
        [Category("3. Pelat sirip"), DisplayName("Tebal (mm)")] public double TebalPelat { get; set; }
        [Category("3. Pelat sirip"), DisplayName("Baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Pelat sirip"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Pelat sirip"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 3;
        [Category("3. Pelat sirip"), DisplayName("Jarak baut (mm)")] public double JarakBaut { get; set; }
        [Category("4. Pengaku"), DisplayName("Pengaku kolom")] public bool PengakuKolom { get; set; } = true;
        [Category("4. Pengaku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 70));
            JumlahBaut = Shear.Count(bm, JarakBaut, Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)) + 30);
            TebalPelat = Fmt.StdPlate(Math.Max(10, Sec.Tw(bm) + 2));
            Las = Math.Max(5, Fmt.MinFillet(Math.Min(TebalPelat, Sec.Tf(Prof(Kolom, "Kolom")))));
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Sec.Tf(bm)));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var col = Prof(Kolom, "Kolom"); var b = BoltData.Of(Baut);
            Sec.Face(col, Muka, out double f, out double _);
            double Hb = Sec.H(bm), Bb = Sec.B(bm), tfb = Sec.Tf(bm), e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)), a = Fmt.Round5(Celah + e + 10);
            double len = Shear.Tab(r, bm, f, a, e, TebalPelat, JumlahBaut, JarakBaut, b, out double hp);
            BoltBoq(r, Baut, Mutu, len, JumlahBaut, "badan");
            foreach (var z in new[] { Hb / 2 - tfb - 10, -Hb / 2 - 10 })
                r.Box(PartKind.Pelat, "pelat penahan las", f, f + 30, -Bb / 2 - 10, Bb / 2 + 10, z, z + 10);
            r.AddBoq("Pelat", "Pelat penahan las (backing bar) 30x10 L=" + Fmt.N(Bb + 20), 2, 30 * 10 * (Bb + 20) * 7.85e-6);
            r.AddBoq("Las", "Las tumpul penetrasi penuh (CJP) sayap balok, panjang " + Fmt.N(2 * Bb) + " mm", 1, 0);
            WeldBoq(r, Las, 2 * hp, "pelat sirip ke kolom, dua sisi");
            if (PengakuKolom && Muka == SupportFace.SayapKolom) Shear.ColumnStiffeners(r, col, new[] { Hb / 2 - tfb / 2, -Hb / 2 + tfb / 2 }, TebalPengaku);
            Stub(r, col, Sec.ColumnFrame(Muka, -Hb), 0, 2.2 * Hb);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Hb, 500));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            EdgeRule(r, "Baut ke tepi pelat sirip", e, b);
            EdgeRule(r, "Baut ke ujung balok", a - Celah, b);
            if (JumlahBaut > 1) PitchRule(r, "Baut", JarakBaut, b);
            r.Rule(hp <= Support.ClearDepth(bm) - 40 + 1e-6, "Pelat sirip bebas dari lubang akses las", "geometri, praktik umum AISC 360 J1.6");
            r.Rule(Celah >= 3 && Celah <= 10, "Celah akar " + Fmt.N(Celah) + " mm (3-10)", "praktik umum las CJP");
            WeldRule(r, "Pelat sirip ke kolom", Las, TebalPelat, Sec.Tf(col));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Note(0, new Vec3(f, 0, Hb / 2), "Las CJP sayap atas & bawah, lubang akses las\\PPelat sirip t=" + Fmt.N(TebalPelat) + ", " + JumlahBaut + " baut " + Baut, 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Sambungan momen pelat sayap: pelat atas/bawah dilas ke kolom, dibaut ke sayap balok; badan pelat sirip.</summary>
    public class FlangePlateMoment : Connection
    {
        public override ConnType Type => ConnType.MomenPelatSayap;
        public override string Title => "Momen pelat sayap berbaut";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.SayapKolom;
        [Category("2. Pelat sayap"), DisplayName("Tebal (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat sayap"), DisplayName("Lebar (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat sayap"), DisplayName("Baris baut")] public int Baris { get; set; } = 3;
        [Category("2. Pelat sayap"), DisplayName("Jarak baris (mm)")] public double JarakBaut { get; set; }
        [Category("2. Pelat sayap"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("2. Pelat sayap"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 15;
        [Category("2. Pelat sayap"), DisplayName("Las pelat ke kolom a (mm)")] public double Las { get; set; }
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Baut badan")] public int JumlahBautBadan { get; set; } = 3;
        [Category("4. Pengaku"), DisplayName("Pengaku kolom")] public bool PengakuKolom { get; set; } = true;

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            TebalPelat = Fmt.StdPlate(Math.Max(12, Sec.Tf(bm) + 4));
            LebarPelat = Fmt.Round10(Sec.B(bm));
            Gauge = Fmt.Round10(Math.Max(Sec.Tw(bm) + b.WasherOD + 30, 0.55 * Sec.B(bm)));
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 70));
            Las = Math.Max(6, Fmt.MinFillet(Math.Min(TebalPelat, Sec.Tf(Prof(Kolom, "Kolom")))));
            JumlahBautBadan = Math.Min(4, Shear.Count(bm, JarakBaut, 40));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var col = Prof(Kolom, "Kolom"); var b = BoltData.Of(Baut);
            Sec.Face(col, Muka, out double f, out double _);
            double Hb = Sec.H(bm), tfb = Sec.Tf(bm), tp = TebalPelat, bp = LebarPelat, e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            double Lp = Celah + e + (Baris - 1) * JarakBaut + e + 30;
            string lab = Pl(Lp, bp, tp);
            double len = 0;
            foreach (int s in new[] { 1, -1 })
            {
                double z0 = s > 0 ? Hb / 2 : -Hb / 2 - tp;
                var pl = r.Box(PartKind.Pelat, lab, f, f + Lp, -bp / 2, bp / 2, z0, z0 + tp);
                r.AddBoq("Pelat", lab + " (pelat sayap)", 1, pl.MassKg());
                for (int k = 0; k < Baris; k++)
                    foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                        len = r.Bolt(new Vec3(f + Celah + 30 + e + k * JarakBaut, y, s > 0 ? Hb / 2 + tp : -Hb / 2 - tp), s > 0 ? -Vec3.UnitZ : Vec3.UnitZ, tp + tfb, b);
            }
            BoltBoq(r, Baut, Mutu, len, 4 * Baris, "pelat sayap");
            double lenW = Shear.Tab(r, bm, f, Fmt.Round5(Celah + e + 10), e, Fmt.StdPlate(Math.Max(10, Sec.Tw(bm) + 2)), JumlahBautBadan, JarakBaut, b, out double hp);
            BoltBoq(r, Baut, Mutu, lenW, JumlahBautBadan, "badan");
            WeldBoq(r, Las, 2 * 2 * bp, "pelat sayap ke kolom");
            if (PengakuKolom && Muka == SupportFace.SayapKolom) Shear.ColumnStiffeners(r, col, new[] { Hb / 2 + tp / 2, -Hb / 2 - tp / 2 }, Fmt.StdPlate(Math.Max(10, tp)));
            Stub(r, col, Sec.ColumnFrame(Muka, -Hb), 0, 2.2 * Hb);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Hb, 600));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            EdgeRule(r, "Baut ke ujung pelat sayap", e + 0, b);
            EdgeRule(r, "Baut ke tepi samping pelat", bp / 2 - Gauge / 2, b);
            EdgeRule(r, "Baut ke tepi sayap balok", Sec.B(bm) / 2 - Gauge / 2, b);
            if (Baris > 1) PitchRule(r, "Baris baut", JarakBaut, b);
            PitchRule(r, "Gauge", Gauge, b);
            if (Muka == SupportFace.SayapKolom) r.Rule(bp <= Sec.B(col) + 1e-6, "Pelat sayap " + Fmt.N(bp) + " ≤ lebar sayap kolom", "geometri (las ke sayap kolom)");
            WeldRule(r, "Pelat sayap ke kolom", Las, tp, Sec.Tf(col));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Dim(1, new Vec3(f, -bp / 2, 0), new Vec3(f + Lp, -bp / 2, 0), -Vec3.UnitY, 40);
            r.Note(0, new Vec3(f + Lp, 0, Hb / 2 + tp), "2 x " + lab + ", " + (4 * Baris) + " baut " + Baut + "\\PPelat sirip badan " + JumlahBautBadan + " baut", 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Balok WF ke kolom hollow/pipa: pelat sirip dilas di muka kolom atau menembus kolom.</summary>
    public class HollowColumnBeam : Connection
    {
        public override ConnType Type => ConnType.KolomHollow;
        public override string Title => "Balok ke kolom hollow / pipa";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF250x125x6x9";
        [Category("1. Profil"), DisplayName("Kolom hollow / pipa"), TypeConverter(typeof(ProfilesTube))] public string Kolom { get; set; } = "SHS200x200x9";
        [Category("2. Pelat"), DisplayName("Metode")] public HollowPlate Metode { get; set; } = HollowPlate.PelatTembus;
        [Category("2. Pelat"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 15;
        [Category("2. Pelat"), DisplayName("Las a (mm)")] public double Las { get; set; }
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 3;
        [Category("3. Baut"), DisplayName("Jarak baut (mm)")] public double JarakBaut { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var c = Prof(Kolom, "Kolom");
            if (Sec.H(bm) <= 200 && Baut > BoltSize.M16) Baut = BoltSize.M16;
            var b = BoltData.Of(Baut);
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 70));
            JumlahBaut = Shear.Count(bm, JarakBaut, Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)));
            TebalPelat = Fmt.Plates.Where(t => t <= b.D / 2 + 2 + 1e-9 && t >= 6).DefaultIfEmpty(6).Max();
            Las = Math.Max(5, Fmt.MinFillet(Math.Min(TebalPelat, c["t"])));
            Metode = c.Extent(true) / Math.Max(c["t"], 1) > 25 ? HollowPlate.PelatTembus : HollowPlate.PelatTempel;
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var c = Prof(Kolom, "Kolom"); var b = BoltData.Of(Baut);
            double f = c.Extent(false) / 2, e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)), a = Fmt.Round5(Math.Max(Celah + e, 50));
            double x0 = Metode == HollowPlate.PelatTembus ? -f : f;
            double len = Shear.Tab(r, bm, x0, a + (f - x0), e, TebalPelat, JumlahBaut, JarakBaut, b, out double hp);
            BoltBoq(r, Baut, Mutu, len, JumlahBaut);
            WeldBoq(r, Las, (Metode == HollowPlate.PelatTembus ? 4 : 2) * hp, Metode == HollowPlate.PelatTembus ? "pelat tembus ke dinding kolom, kedua muka" : "pelat ke muka kolom, dua sisi");
            var s = c.BuildShape();
            r.Prism(PartKind.Batang, c.Name, new Frame(new Vec3(0, 0, -1.2 * Sec.H(bm)), -Vec3.UnitY, Vec3.UnitX, Vec3.UnitZ), s.Outer, 0, 2.4 * Sec.H(bm), s.Holes);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Sec.H(bm), 500));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            EdgeRule(r, "Baut ke tepi pelat", e, b);
            EdgeRule(r, "Baut ke ujung balok", a - Celah, b);
            if (JumlahBaut > 1) PitchRule(r, "Baut", JarakBaut, b);
            r.Rule(TebalPelat <= b.D / 2 + 2 + 1e-6, "Tebal pelat ≤ d/2 + 2", "daktilitas, praktik umum");
            r.Rule(Metode == HollowPlate.PelatTembus || c.Extent(true) / Math.Max(c["t"], 1) <= 25 + 1e-6, "Dinding kolom cukup tebal untuk pelat tempel (b/t ≤ 25), atau pakai pelat tembus", "praktik umum");
            WeldRule(r, "Pelat ke dinding kolom", Las, TebalPelat, c["t"]);
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Note(0, new Vec3(f + a, 0, hp / 2), (Metode == HollowPlate.PelatTembus ? "Pelat tembus" : "Pelat tempel") + " t=" + Fmt.N(TebalPelat) + ", " + JumlahBaut + " baut " + Baut, 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Balok di atas balok (tumpuk): 4 baut menembus kedua sayap, pengaku opsional. Frame: asal = bidang kontak, X = balok atas.</summary>
    public class StackedBeams : Connection
    {
        public override ConnType Type => ConnType.BalokTumpuk;
        public override string Title => "Balok tumpuk (balok di atas balok)";

        [Category("1. Profil"), DisplayName("Balok atas"), TypeConverter(typeof(ProfilesIH))] public string BalokAtas { get; set; } = "WF250x125x6x9";
        [Category("1. Profil"), DisplayName("Balok bawah"), TypeConverter(typeof(ProfilesIH))] public string BalokBawah { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Sudut silang (°)")] public double Sudut { get; set; } = 90;
        [Category("2. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("2. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("2. Baut"), DisplayName("Gauge di balok atas (mm)")] public double GaugeAtas { get; set; }
        [Category("2. Baut"), DisplayName("Gauge di balok bawah (mm)")] public double GaugeBawah { get; set; }
        [Category("3. Pengaku"), DisplayName("Pengaku di kedua balok")] public bool Pengaku { get; set; } = true;
        [Category("3. Pengaku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; } = 10;

        public override void Recommend()
        {
            var a = Prof(BalokAtas, "Atas"); var bb = Prof(BalokBawah, "Bawah"); var b = BoltData.Of(Baut);
            GaugeAtas = Fmt.Round10(Math.Min(Math.Max(Sec.Tw(a) + b.WasherOD + 20, 3 * b.D), Sec.B(a) - 2 * b.MinEdge));
            GaugeBawah = Fmt.Round10(Math.Min(Math.Max(Sec.Tw(bb) + b.WasherOD + 20, 3 * b.D), Sec.B(bb) - 2 * b.MinEdge));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var up = Prof(BalokAtas, "Atas"); var lo = Prof(BalokBawah, "Bawah"); var b = BoltData.Of(Baut);
            var dl = Geo.Dir2(Sudut); var nl = Geo.Perp(dl);
            double len = 0; var pts = new List<Vec3>();
            foreach (var ya in new[] { -GaugeAtas / 2, GaugeAtas / 2 })
                foreach (var yb in new[] { -GaugeBawah / 2, GaugeBawah / 2 })
                {
                    // titik pada garis y = ya (balok atas) dan n_l·p = yb (balok bawah)
                    double nx = nl.X, ny = nl.Y;
                    if (Math.Abs(nx) < 1e-6) continue;
                    var p = new Vec3((yb - ny * ya) / nx, ya, 0);
                    pts.Add(p);
                    len = r.Bolt(new Vec3(p.X, p.Y, Sec.Tf(up)), -Vec3.UnitZ, Sec.Tf(up) + Sec.Tf(lo), b);
                }
            BoltBoq(r, Baut, Mutu, len, pts.Count);
            if (Pengaku)
            {
                Shear.BeamStiffeners(r, up, 0, 0, TebalPengaku);
                var st = new ConnResult();
                Shear.BeamStiffeners(st, lo, 0, -Sec.H(lo), TebalPengaku);
                var rot = new Frame(Vec3.Zero, dl, nl, Vec3.UnitZ);
                foreach (var p in st.Parts) { p.F = rot.Compose(p.F); r.Parts.Add(p); }
                foreach (var bl in st.Boq) r.AddBoq(bl.Item, bl.Spec, bl.Count, bl.UnitKg);
            }
            double L = Math.Max(2 * Sec.H(lo), 800);
            Stub(r, up, new Frame(new Vec3(-L, 0, Sec.H(up) / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * L);
            Stub(r, lo, new Frame(dl * -L + new Vec3(0, 0, -Sec.H(lo) / 2), nl, Vec3.UnitZ, dl), 0, 2 * L);
            EdgeRule(r, "Baut ke tepi sayap balok atas", Sec.B(up) / 2 - GaugeAtas / 2, b);
            EdgeRule(r, "Baut ke tepi sayap balok bawah", Sec.B(lo) / 2 - GaugeBawah / 2, b);
            double minSp = pts.SelectMany((p, i) => pts.Skip(i + 1).Select(q => (p - q).Length)).DefaultIfEmpty(0).Min();
            PitchRule(r, "Baut", minSp, b);
            r.Rule(Sudut >= 30 && Sudut <= 150, "Sudut silang " + Fmt.N(Sudut) + "° (30°-150°)", "geometri");
            r.Rule(pts.Count == 4, "4 baut menembus kedua sayap", "praktik umum");
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("TAMPAK", Vec3.UnitX, Vec3.UnitZ));
            r.Note(0, pts.Count > 0 ? pts[0] : Vec3.Zero, "4 baut " + Baut + " " + Grades.Label(Mutu) + " menembus kedua sayap" + (Pengaku ? "\\PPengaku t=" + Fmt.N(TebalPengaku) + " di kedua balok" : ""), 2, -2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Kolom angin (dinding ujung PEB) ke sayap bawah rafter: pelat tutup sejajar sayap rafter, 4 baut, lubang slot tegak.
    /// Frame: asal = muka bawah sayap bawah rafter di as kolom, X = arah rafter (miring), Z = normal sayap.
    /// </summary>
    public class WindColumn : Connection
    {
        public override ConnType Type => ConnType.KolomAngin;
        public override string Title => "Kolom angin ke rafter (dinding ujung)";

        [Category("1. Profil"), DisplayName("Kolom angin"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "WF250x125x6x9";
        [Category("1. Profil"), DisplayName("Rafter"), TypeConverter(typeof(ProfilesIH))] public string Rafter { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Kemiringan rafter (°)")] public double Sudut { get; set; } = 10;
        [Category("2. Pelat"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Panjang searah rafter (mm)")] public double PanjangPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Lebar (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Panjang slot (mm)")] public double Slot { get; set; } = 40;
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("3. Baut"), DisplayName("Jarak searah rafter (mm)")] public double Pitch { get; set; }
        [Category("4. Pengaku"), DisplayName("Pengaku rafter")] public bool Pengaku { get; set; } = true;

        public override void Recommend()
        {
            var c = Prof(Kolom, "Kolom"); var raf = Prof(Rafter, "Rafter"); var b = BoltData.Of(Baut);
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            TebalPelat = Fmt.StdPlate(Math.Max(12, Sec.Tf(c)));
            Gauge = Fmt.Round10(Math.Min(Math.Max(Sec.Tw(raf) + b.WasherOD + 20, 0.55 * Sec.B(raf)), Sec.B(raf) - 2 * b.MinEdge));
            PanjangPelat = Fmt.Round10(Math.Max(c.Extent(false) / Math.Cos(Sudut * Math.PI / 180) + 40, 3 * b.D + 2 * e + Slot));
            LebarPelat = Fmt.Round10(Math.Max(c.Extent(true) + 40, Gauge + 2 * e));
            Pitch = Fmt.Round10(Math.Max(3 * b.D, PanjangPelat - 2 * e - Slot));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var c = Prof(Kolom, "Kolom"); var raf = Prof(Rafter, "Rafter"); var b = BoltData.Of(Baut);
            double a = Sudut * Math.PI / 180, tp = TebalPelat, Lp = PanjangPelat, Wp = LebarPelat, tfr = Sec.Tf(raf), Hr = Sec.H(raf);
            string lab = Pl(Lp, Wp, tp);
            var pl = r.Box(PartKind.Pelat, lab, -Lp / 2, Lp / 2, -Wp / 2, Wp / 2, -tp, 0);
            r.AddBoq("Pelat", lab + " (pelat kepala kolom angin, lubang slot " + Fmt.N(Slot) + ")", 1, pl.MassKg());
            double len = 0;
            foreach (var x in new[] { -Pitch / 2, Pitch / 2 }) foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                    len = r.Bolt(new Vec3(x, y, tfr), -Vec3.UnitZ, tfr + tp, b);
            BoltBoq(r, Baut, Mutu, len, 4);
            WeldBoq(r, Math.Max(5, Fmt.MinFillet(Math.Min(tp, Sec.Tf(c)))), 2 * (c.Extent(false) + c.Extent(true)), "kolom ke pelat kepala");
            if (Pengaku) foreach (var x in new[] { -Pitch / 2, Pitch / 2 }) Shear.BeamStiffeners(r, raf, x, 0, 10);
            // kolom tegak (dunia) dalam frame rafter
            var up = new Vec3(Math.Sin(a), 0, Math.Cos(a));
            var colX = new Vec3(Math.Cos(a), 0, -Math.Sin(a));
            double hc = c.Extent(false);
            double sTop = -tp / Math.Cos(a) + hc / 2 * Math.Tan(Math.Abs(a)) + 5;
            var cs = c.BuildShape();
            // ujung kolom dipotong miring sejajar pelat kepala (rapat ke pelat, tidak menembus)
            r.Prism(PartKind.Batang, c.Name, new Frame(up * (sTop - 3 * hc), Vec3.UnitY * -1, colX, up), cs.Outer, 0, 3 * hc, cs.Holes)
                .Clip(new Vec3(0, 0, -tp), -Vec3.UnitZ);
            r.TrimSupported = up * sTop;
            r.CutSupported.Add(Cutter.Box(new Frame(), -3 * hc, 3 * hc, -3 * hc, 3 * hc, -tp, Hr + 3 * hc));
            double L = Math.Max(2 * Hr, 800);
            Stub(r, raf, new Frame(new Vec3(-L, 0, Hr / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * L);
            EdgeRule(r, "Baut ke tepi pelat (searah rafter, ujung slot)", (Lp - Pitch - Slot) / 2, b);
            EdgeRule(r, "Baut ke tepi pelat (melintang)", Wp / 2 - Gauge / 2, b);
            EdgeRule(r, "Baut ke tepi sayap rafter", Sec.B(raf) / 2 - Gauge / 2, b);
            PitchRule(r, "Baut", Math.Min(Pitch, Gauge), b);
            r.Rule(Slot >= 20, "Lubang slot " + Fmt.N(Slot) + " mm memberi ruang lendutan rafter", "praktik umum PEB");
            r.Rule(Math.Abs(Sudut) <= 30, "Kemiringan rafter ≤ 30°", "geometri");
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH PELAT", Vec3.UnitX, Vec3.UnitY));
            r.Note(0, new Vec3(Lp / 2, 0, -tp), lab + ", 4 baut " + Baut + ", lubang slot tegak " + Fmt.N(Slot), 2, -2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Konsol crane / bracket: potongan WF dilas ke sayap kolom, pengaku, pelat dudukan rel. Frame: asal = as kolom di muka atas konsol, X keluar.</summary>
    public class CraneBracket : Connection
    {
        public override ConnType Type => ConnType.KonsolCrane;
        public override string Title => "Konsol crane / bracket";

        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "H400x400x13x21";
        [Category("1. Profil"), DisplayName("Profil konsol"), TypeConverter(typeof(ProfilesIH))] public string Konsol { get; set; } = "WF350x175x7x11";
        [Category("2. Konsol"), DisplayName("Panjang konsol dari muka kolom (mm)")] public double Panjang { get; set; } = 500;
        [Category("2. Konsol"), DisplayName("Tebal pelat dudukan (mm)")] public double TebalDudukan { get; set; } = 20;
        [Category("2. Konsol"), DisplayName("Panjang pelat dudukan (mm)")] public double PanjangDudukan { get; set; } = 250;
        [Category("2. Konsol"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; } = 12;
        [Category("2. Konsol"), DisplayName("Las badan a (mm)")] public double Las { get; set; } = 8;
        [Category("3. Baut dudukan"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut dudukan"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut dudukan"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("4. Pengaku kolom"), DisplayName("Pengaku kolom")] public bool PengakuKolom { get; set; } = true;

        public override void Recommend()
        {
            var k = Prof(Konsol, "Konsol"); var b = BoltData.Of(Baut);
            Gauge = Fmt.Round10(Math.Min(Math.Max(Sec.Tw(k) + b.WasherOD + 30, 0.55 * Sec.B(k)), Sec.B(k) - 2 * b.MinEdge));
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Sec.Tf(k)));
            Las = Math.Max(6, Fmt.MinFillet(Math.Min(Sec.Tw(k), Sec.Tf(Prof(Kolom, "Kolom")))));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var col = Prof(Kolom, "Kolom"); var k = Prof(Konsol, "Konsol"); var b = BoltData.Of(Baut);
            double f = Sec.H(col) / 2, Hk = Sec.H(k), Bk = Sec.B(k), tfk = Sec.Tf(k), L = Panjang, Ld = PanjangDudukan, td = TebalDudukan;
            Piece(r, PartKind.Profil, k, new Frame(new Vec3(f, 0, -Hk / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), L, "konsol");
            var seat = r.Box(PartKind.Pelat, "pelat dudukan", f + L - Ld, f + L, -Bk / 2, Bk / 2, 0, td);
            r.AddBoq("Pelat", Pl(Ld, Bk, td) + " (pelat dudukan rel/balok crane)", 1, seat.MassKg());
            double len = 0;
            foreach (var x in new[] { f + L - Ld + Ld / 4, f + L - Ld / 4 }) foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                    len = r.Bolt(new Vec3(x, y, -tfk), Vec3.UnitZ, tfk + td, b);
            BoltBoq(r, Baut, Mutu, len, 4, "dudukan");
            Shear.BeamStiffeners(r, k, f + L - Ld / 2, -Hk, TebalPengaku);
            Shear.BeamStiffeners(r, k, f + L - TebalPengaku / 2, -Hk, TebalPengaku);
            if (PengakuKolom) Shear.ColumnStiffeners(r, col, new[] { -tfk / 2, -Hk + tfk / 2 }, Fmt.StdPlate(Math.Max(10, tfk)));
            r.AddBoq("Las", "Las CJP sayap konsol ke kolom, panjang " + Fmt.N(2 * Bk) + " mm", 1, 0);
            WeldBoq(r, Las, 2 * (Hk - 2 * tfk), "badan konsol ke kolom, dua sisi");
            Stub(r, col, Sec.ColumnFrame(SupportFace.SayapKolom, -2 * Hk), 0, 3 * Hk);
            EdgeRule(r, "Baut ke tepi pelat dudukan", Ld / 4, b);
            EdgeRule(r, "Baut ke tepi sayap konsol", Bk / 2 - Gauge / 2, b);
            PitchRule(r, "Baut", Math.Min(Gauge, Ld / 2), b);
            r.Rule(L <= 2.5 * Hk + 1e-6, "Panjang konsol ≤ 2,5 x tinggi konsol (pendek, kaku)", "praktik umum");
            r.Rule(Bk <= Sec.B(col) + 1e-6, "Lebar konsol ≤ lebar sayap kolom", "geometri (las ke sayap)");
            WeldRule(r, "Badan konsol ke kolom", Las, Sec.Tw(k), Sec.Tf(col));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Dim(0, new Vec3(f, 0, td), new Vec3(f + L, 0, td), Vec3.UnitZ, 60);
            r.Note(0, new Vec3(f + L, 0, td), "Konsol " + k.Name + " L=" + Fmt.N(L) + ", pengaku t=" + Fmt.N(TebalPengaku) + "\\PPelat dudukan " + Fmt.N(Ld) + "x" + Fmt.N(Bk) + "x" + Fmt.N(td) + ", 4 baut " + Baut, 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Balok baja ke dinding/kolom beton: pelat tanam dengan stud, atau pelat + angkur kimia; pelat sirip untuk balok.</summary>
    public class ConcretePlate : Connection
    {
        public override ConnType Type => ConnType.PelatBeton;
        public override string Title => "Balok ke beton (pelat tanam / angkur kimia)";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF300x150x6.5x9";
        [Category("2. Pelat ke beton"), DisplayName("Metode")] public ConcreteFix Metode { get; set; } = ConcreteFix.AngkurKimia;
        [Category("2. Pelat ke beton"), DisplayName("Lebar pelat (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat ke beton"), DisplayName("Tinggi pelat (mm)")] public double TinggiPelat { get; set; }
        [Category("2. Pelat ke beton"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat ke beton"), DisplayName("Diameter stud / angkur")] public BoltSize Angkur { get; set; } = BoltSize.M16;
        [Category("2. Pelat ke beton"), DisplayName("Baris angkur")] public int Baris { get; set; } = 2;
        [Category("2. Pelat ke beton"), DisplayName("Kedalaman tanam (mm)")] public double Tanam { get; set; }
        [Category("3. Pelat sirip"), DisplayName("Tebal (mm)")] public double TebalSirip { get; set; }
        [Category("3. Pelat sirip"), DisplayName("Baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Pelat sirip"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Pelat sirip"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 3;
        [Category("3. Pelat sirip"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 15;

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var a = BoltData.Of(Angkur); var b = BoltData.Of(Baut);
            double p = Fmt.Round5(Math.Max(3 * b.D, 70));
            JumlahBaut = Shear.Count(bm, p, Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)));
            double hp = (JumlahBaut - 1) * p + 2 * Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            TinggiPelat = Fmt.Round10(Math.Max(hp + 100, 4 * a.D * (Baris - 1) + 2 * 50));
            LebarPelat = Fmt.Round10(Math.Max(200, 6 * a.D + 2 * 50));
            TebalPelat = Fmt.StdPlate(Math.Max(12, 0.75 * a.D));
            Tanam = Metode == ConcreteFix.AngkurKimia ? Fmt.Round5(10 * a.D) : Fmt.Round5(8 * a.D);
            TebalSirip = Fmt.Plates.Where(t => t <= b.D / 2 + 2 + 1e-9 && t >= 6).DefaultIfEmpty(6).Max();
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var a = BoltData.Of(Angkur); var b = BoltData.Of(Baut);
            double tp = TebalPelat, W = LebarPelat, H = TinggiPelat, d = a.D;
            bool stud = Metode == ConcreteFix.StudTanam;
            double xp0 = stud ? -tp : 0, xp1 = stud ? 0 : tp;
            var pl = r.Box(PartKind.Pelat, "pelat dinding", xp0, xp1, -W / 2, W / 2, -H / 2, H / 2);
            r.AddBoq("Pelat", Pl(W, H, tp) + (stud ? " (pelat tanam)" : " (pelat dinding)"), 1, pl.MassKg());
            double ey = 50, ez = 50;
            var ys = new[] { -W / 2 + ey, W / 2 - ey };
            var zs = Enumerable.Range(0, Baris).Select(i => -H / 2 + ez + i * (H - 2 * ez) / Math.Max(Baris - 1, 1)).ToList();
            foreach (var y in ys) foreach (var z in zs)
                {
                    if (stud)
                    {
                        r.Cylinder(PartKind.Angkur, "stud", new Vec3(-tp, y, z), -Vec3.UnitX, d / 2, Tanam - d / 2);
                        r.Cylinder(PartKind.Angkur, "", new Vec3(-tp - Tanam + d / 2, y, z), -Vec3.UnitX, 0.8 * d, d / 2);
                    }
                    else
                    {
                        r.Cylinder(PartKind.Angkur, "", new Vec3(-Tanam, y, z), Vec3.UnitX, d / 2, Tanam + tp + a.WasherT + a.Nut + 3 * a.Pitch);
                        r.Cylinder(PartKind.Ring, "", new Vec3(tp, y, z), Vec3.UnitX, a.WasherOD / 2, a.WasherT);
                        r.Hex(PartKind.Mur, "", new Vec3(tp + a.WasherT, y, z), Vec3.UnitX, a.Across, a.Nut);
                        pl.Holes.Add(Shapes.Rect(y - a.Hole / 2, z - a.Hole / 2, y + a.Hole / 2, z + a.Hole / 2));
                    }
                }
            int na = ys.Length * zs.Count;
            r.AddBoq("Angkur", stud ? "Stud berkepala Ø" + Fmt.N(d) + " L=" + Fmt.N(Tanam) + " (dilas ke pelat)" : "Angkur kimia " + Angkur + " hef=" + Fmt.N(Tanam) + " + ring + mur", na, 0);
            r.Box(PartKind.Beton, "beton", -Tanam - 150, Math.Min(0, xp0), -W / 2 - 200, W / 2 + 200, -H / 2 - 200, H / 2 + 200);
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D)), p = Fmt.Round5(Math.Max(3 * b.D, 70)), aa = Fmt.Round5(Math.Max(Celah + e, 50));
            double len = Shear.Tab(r, bm, xp1, aa, e, TebalSirip, JumlahBaut, p, b, out double hp);
            BoltBoq(r, Baut, Mutu, len, JumlahBaut);
            WeldBoq(r, Math.Max(5, Fmt.MinFillet(Math.Min(TebalSirip, tp))), 2 * hp, "pelat sirip ke pelat dinding");
            Stub(r, bm, Sec.BeamFrame(xp1 + Celah), 0, Math.Max(1.5 * Sec.H(bm), 500));
            r.TrimSupported = new Vec3(xp1 + Celah, 0, 0);
            double sy = ys[1] - ys[0], sz = zs.Count > 1 ? zs[1] - zs[0] : double.MaxValue;
            r.Rule(Math.Min(sy, sz) >= 4 * d - 1e-6, "Jarak antar angkur ≥ 4d = " + Fmt.N(4 * d), "praktik umum ACI 318 §17.9");
            r.Rule(Math.Min(ey, ez) >= 1.5 * d, "Angkur ke tepi pelat ≥ 1,5d", "praktik umum");
            r.Rule(hp <= H - 20 + 1e-6, "Pelat sirip muat di pelat dinding", "geometri");
            r.Rule(Tanam >= (stud ? 4 : 8) * d - 1e-6, "Kedalaman tanam " + Fmt.N(Tanam) + " ≥ " + (stud ? 4 : 8) + "d (minimum praktik; tetap dihitung)", stud ? "praktik umum stud" : "data pabrik angkur kimia");
            EdgeRule(r, "Baut ke tepi pelat sirip", e, b);
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK MUKA PELAT", Vec3.UnitY, Vec3.UnitZ));
            r.Note(0, new Vec3(xp0, 0, -H / 2), (stud ? "Pelat tanam + " : "Pelat + ") + na + (stud ? " stud Ø" + Fmt.N(d) : " angkur kimia " + Angkur) + ", hef=" + Fmt.N(Tanam), -6, -2);
            return r;
        }
    }
}
