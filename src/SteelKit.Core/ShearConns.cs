using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    static class Support
    {
        /// <summary>Potongan penopang untuk gambar detail: kolom tegak atau balok induk mendatar (searah Y).</summary>
        public static void Stub(ConnResult r, Profile sup, SupportFace face, double hb, Action<ConnResult, Profile, Frame, double, double> stub, double zSup = 0)
        {
            double Hs = Sec.H(sup);
            if (face == SupportFace.BadanBalok)
            {
                double L = Math.Max(1.5 * Hs, 500);
                stub(r, sup, new Frame(new Vec3(0, -L, zSup), -Vec3.UnitX, Vec3.UnitZ, Vec3.UnitY), 0, 2 * L);
            }
            else stub(r, sup, Sec.ColumnFrame(face, -1.2 * hb), 0, 2.4 * hb);
        }

        public static double ClearDepth(Profile beam) => Sec.H(beam) - 2 * Sec.Tf(beam) - 2 * Sec.R(beam);

        /// <summary>Coping balok anak bila sayapnya bertabrakan dengan sayap balok induk (elevasi as induk zg terhadap as balok).</summary>
        public static void CopeDefaults(Profile beam, Profile girder, double zg, double f, double gap, out double top, out double bot, out double len)
        {
            double Hb = Sec.H(beam), Hg = Sec.H(girder), tfg = Sec.Tf(girder), rg = Sec.R(girder);
            double under = zg + Hg / 2 - tfg, over = zg - Hg / 2 + tfg;
            top = Hb / 2 > under - 1 ? Fmt.Round5(Hb / 2 - under + rg + 10) : 0;
            bot = -Hb / 2 < over + 1 ? Fmt.Round5(over - (-Hb / 2) + rg + 10) : 0;
            len = Fmt.Round5(Math.Max(Sec.B(girder) / 2 - f - gap + 15, 30));
        }

        public static void ApplyCope(ConnResult r, Profile beam, double x0, double top, double bot, double len, double hp)
        {
            double Hb = Sec.H(beam), B = Sec.B(beam);
            if (top > 0) r.CutSupported.Add(Cutter.Box(new Frame(), x0 - 50, x0 + len, -B, B, Hb / 2 - top, Hb / 2 + 50));
            if (bot > 0) r.CutSupported.Add(Cutter.Box(new Frame(), x0 - 50, x0 + len, -B, B, -Hb / 2 - 50, -Hb / 2 + bot));
            if (top > 0 || bot > 0)
            {
                // rintisan balok di gambar ikut di-coping: bagian ujung sepanjang coping dipotong atas/bawah
                var stub = r.Parts.LastOrDefault(p => p.Kind == PartKind.Batang && p.Label == beam.Name && (p.F.O - new Vec3(x0, 0, 0)).Length < 1e-6 && p.Z0 == 0);
                if (stub != null && stub.Z1 > len)
                {
                    var end = new Part { Kind = PartKind.Batang, Label = stub.Label, F = stub.F, Outline = stub.Outline, Z0 = 0, Z1 = len };
                    end.Holes.AddRange(stub.Holes);
                    if (top > 0) end.Clip(new Vec3(0, 0, Hb / 2 - top), -Vec3.UnitZ);
                    if (bot > 0) end.Clip(new Vec3(0, 0, -Hb / 2 + bot), Vec3.UnitZ);
                    stub.Z0 = len;
                    r.Parts.Add(end);
                }
                r.Rule(hp / 2 <= Hb / 2 - Math.Max(top, bot) - 5 + 1e-6, "Pelat/siku muat di badan setelah coping (atas " + Fmt.N(top) + ", bawah " + Fmt.N(bot) + ")", "geometri");
                r.Rule(Math.Max(top, bot) <= 0.5 * Hb + 1e-6, "Dalam coping ≤ 0,5 x tinggi balok", "praktik umum");
                r.Note(0, new Vec3(x0 + len, 0, top > 0 ? Hb / 2 - top : -Hb / 2 + bot), "Coping " + (top > 0 ? "atas " + Fmt.N(top) : "") + (top > 0 && bot > 0 ? ", " : "") + (bot > 0 ? "bawah " + Fmt.N(bot) : "") + " x " + Fmt.N(len) + ", R=10", 2, top > 0 ? 2 : -2);
            }
        }
    }

    // =========================================================================================
    /// <summary>Fin plate (pelat sirip) di satu sisi badan balok. Frame lokal seperti pelat ujung.</summary>
    public class FinPlate : Connection
    {
        public override ConnType Type => ConnType.FinPlate;
        public override string Title => "Fin plate (pelat sirip)";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF300x150x6.5x9";
        [Category("1. Profil"), DisplayName("Penopang"), TypeConverter(typeof(ProfilesIH))] public string Penopang { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.SayapKolom;

        [Category("2. Pelat"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Garis baut dari muka penopang a (mm)")] public double JarakGaris { get; set; }
        [Category("2. Pelat"), DisplayName("Tepi ujung pelat (mm)")] public double TepiUjung { get; set; }
        [Category("2. Pelat"), DisplayName("Tepi atas/bawah (mm)")] public double TepiTegak { get; set; }
        [Category("2. Pelat"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 15;
        [Category("2. Pelat"), DisplayName("Las pelat ke penopang a (mm)")] public double Las { get; set; }

        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 3;
        [Category("3. Baut"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Elevasi as balok induk thd as balok (mm)")] public double ElevasiPenopang { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Coping atas (mm)")] public double CopingAtas { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Coping bawah (mm)")] public double CopingBawah { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Panjang coping (mm)")] public double PanjangCoping { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok");
            if (Sec.H(bm) <= 200 && Baut > BoltSize.M16) Baut = BoltSize.M16;
            var b = BoltData.Of(Baut);
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 70));
            TepiTegak = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            TepiUjung = TepiTegak;
            JarakGaris = Fmt.Round5(Math.Max(Celah + TepiTegak, 50));
            double clear = Support.ClearDepth(bm);
            CopingAtas = CopingBawah = PanjangCoping = 0;
            if (Muka == SupportFace.BadanBalok)
            {
                var sup = Prof(Penopang, "Penopang");
                Sec.Face(sup, SupportFace.BadanKolom, out double f0, out double _);
                Support.CopeDefaults(bm, sup, ElevasiPenopang, f0, Celah, out double ct, out double cb, out double cl);
                CopingAtas = ct; CopingBawah = cb; PanjangCoping = cl;
                clear = Math.Min(clear, Sec.H(bm) - 2 * Math.Max(ct, cb) - 20);
            }
            JumlahBaut = Math.Max(2, Math.Min(8, (int)Math.Floor((clear - 2 * TepiTegak) / JarakBaut) + 1));
            TebalPelat = Fmt.Plates.Where(t => t <= b.D / 2 + 2 + 1e-9 && t >= 6).DefaultIfEmpty(6).Max();
            Las = Math.Max(Fmt.MinFillet(TebalPelat), Math.Ceiling(0.625 * TebalPelat));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var sup = Prof(Penopang, "Penopang"); var b = BoltData.Of(Baut);
            Sec.Face(sup, Muka == SupportFace.BadanBalok ? SupportFace.BadanKolom : Muka, out double f, out double _);
            double twb = Sec.Tw(bm), tp = TebalPelat, n = JumlahBaut, p = JarakBaut;
            double hp = (n - 1) * p + 2 * TepiTegak, lp = JarakGaris + TepiUjung;
            string lab = Pl(lp, hp, tp);
            var plate = r.Box(PartKind.Pelat, lab, f, f + lp, twb / 2, twb / 2 + tp, -hp / 2, hp / 2);
            r.AddBoq("Pelat", lab + " (fin plate)", 1, plate.MassKg());
            double len = 0;
            var zs = Enumerable.Range(0, JumlahBaut).Select(i => (i - (n - 1) / 2) * p).ToList();
            foreach (var z in zs) len = r.Bolt(new Vec3(f + JarakGaris, twb / 2 + tp, z), -Vec3.UnitY, tp + twb, b);
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", JumlahBaut, BoltKg(b, len));
            Support.Stub(r, sup, Muka, Sec.H(bm), Stub, ElevasiPenopang);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Sec.H(bm), 500));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            if (Muka == SupportFace.BadanBalok) Support.ApplyCope(r, bm, f + Celah, CopingAtas, CopingBawah, PanjangCoping, hp);

            EdgeRule(r, "Baut ke tepi atas/bawah pelat", TepiTegak, b);
            EdgeRule(r, "Baut ke tepi ujung pelat", TepiUjung, b);
            EdgeRule(r, "Baut ke ujung balok", JarakGaris - Celah, b);
            if (JumlahBaut > 1) PitchRule(r, "Baut", p, b);
            r.Rule(hp <= Support.ClearDepth(bm) + 1e-6, "Tinggi pelat " + Fmt.N(hp) + " ≤ tinggi bersih badan " + Fmt.N(Support.ClearDepth(bm)), "geometri");
            r.Rule(tp <= b.D / 2 + 2 + 1e-6, "Tebal fin plate " + Fmt.N(tp) + " ≤ d/2 + 2 = " + Fmt.N(b.D / 2 + 2) + " mm", "daktilitas, praktik umum AISC (konfigurasi konvensional)");
            r.Rule(Celah >= 10 - 1e-6, "Celah ujung balok " + Fmt.N(Celah) + " ≥ 10 mm", "toleransi pemasangan, praktik umum");
            WeldRule(r, "Fin plate ke penopang (dua sisi)", Las, tp, Muka == SupportFace.SayapKolom ? Sec.Tf(sup) : Sec.Tw(sup));
            if (Muka == SupportFace.BadanBalok) r.Rule(true, "Periksa coping: bila sayap atas balok setinggi sayap balok induk, sayap balok anak perlu dicoak", "catatan");

            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            double off = 0.15 * hp + 20;
            r.Dim(0, new Vec3(f, 0, -hp / 2), new Vec3(f + JarakGaris, 0, -hp / 2), -Vec3.UnitZ, off);
            r.Dim(0, new Vec3(f, 0, -hp / 2), new Vec3(f + lp, 0, -hp / 2), -Vec3.UnitZ, off * 2);
            var chain = new List<double> { hp / 2 }; chain.AddRange(zs.OrderByDescending(z => z)); chain.Add(-hp / 2);
            for (int i = 1; i < chain.Count; i++) r.Dim(0, new Vec3(f + lp, 0, chain[i]), new Vec3(f + lp, 0, chain[i - 1]), Vec3.UnitX, off);
            r.Note(0, new Vec3(f + lp, 0, hp / 2), lab + ", " + JumlahBaut + " baut " + Baut + " " + Grades.Label(Mutu) + "\\PLas a=" + Fmt.N(Las) + " dua sisi", 2, 2);
            r.Dim(1, new Vec3(0, twb / 2, hp / 2), new Vec3(0, twb / 2 + tp, hp / 2), Vec3.UnitZ, off);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Siku ganda (double cleat) di kedua sisi badan balok.</summary>
    public class DoubleCleat : Connection
    {
        public override ConnType Type => ConnType.SikuGanda;
        public override string Title => "Siku ganda (double cleat)";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF300x150x6.5x9";
        [Category("1. Profil"), DisplayName("Penopang"), TypeConverter(typeof(ProfilesIH))] public string Penopang { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.BadanBalok;
        [Category("1. Profil"), DisplayName("Profil siku"), TypeConverter(typeof(ProfilesAngle))] public string Siku { get; set; } = "L75x75x8";

        [Category("2. Siku"), DisplayName("Garis baut kaki badan dari punggung (mm)")] public double GarisBadan { get; set; }
        [Category("2. Siku"), DisplayName("Garis baut kaki penopang dari punggung (mm)")] public double GarisPenopang { get; set; }
        [Category("2. Siku"), DisplayName("Tepi atas/bawah (mm)")] public double TepiTegak { get; set; }
        [Category("2. Siku"), DisplayName("Celah ujung balok (mm)")] public double Celah { get; set; } = 15;

        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Jumlah baris baut")] public int JumlahBaut { get; set; } = 3;
        [Category("3. Baut"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Elevasi as balok induk thd as balok (mm)")] public double ElevasiPenopang { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Coping atas (mm)")] public double CopingAtas { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Coping bawah (mm)")] public double CopingBawah { get; set; }
        [Category("4. Coping (balok induk)"), DisplayName("Panjang coping (mm)")] public double PanjangCoping { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok");
            CopingAtas = CopingBawah = PanjangCoping = 0;
            if (Muka == SupportFace.BadanBalok)
            {
                var sup0 = Prof(Penopang, "Penopang");
                Support.CopeDefaults(bm, sup0, ElevasiPenopang, Sec.Tw(sup0) / 2, Celah, out double ct, out double cb, out double cl);
                CopingAtas = ct; CopingBawah = cb; PanjangCoping = cl;
            }
            if (Sec.H(bm) <= 200 && Baut > BoltSize.M16) Baut = BoltSize.M16;
            var b = BoltData.Of(Baut);
            string want = b.D <= 16 ? "L75x75x6" : b.D <= 20 ? "L75x75x8" : "L90x90x9";
            if (Env.Catalog.Find(want) != null) Siku = want;
            var L = Prof(Siku, "Siku");
            double t = L["t"];
            GarisBadan = Fmt.Round5(Math.Max(t + b.WasherOD / 2 + 5, L["H"] - Math.Max(b.MinEdge, 1.5 * b.D)));
            GarisBadan = Math.Min(GarisBadan, L["H"] - b.MinEdge);
            GarisPenopang = Fmt.Round5(Math.Max(t + b.WasherOD / 2 + 5, L["B"] - Math.Max(b.MinEdge, 1.5 * b.D)));
            GarisPenopang = Math.Min(GarisPenopang, L["B"] - b.MinEdge);
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 70));
            TepiTegak = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            double clear = Support.ClearDepth(bm);
            if (CopingAtas > 0 || CopingBawah > 0) clear = Math.Min(clear, Sec.H(bm) - 2 * Math.Max(CopingAtas, CopingBawah) - 20);
            JumlahBaut = Math.Max(2, Math.Min(8, (int)Math.Floor((clear - 2 * TepiTegak) / JarakBaut) + 1));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var sup = Prof(Penopang, "Penopang"); var L = Prof(Siku, "Siku"); var b = BoltData.Of(Baut);
            Sec.Face(sup, Muka == SupportFace.BadanBalok ? SupportFace.BadanKolom : Muka, out double f, out double clamp);
            double twb = Sec.Tw(bm), t = L["t"], A = L["H"], Bl = L["B"], w = twb / 2;
            double n = JumlahBaut, p = JarakBaut, lc = (n - 1) * p + 2 * TepiTegak;
            var zs = Enumerable.Range(0, JumlahBaut).Select(i => (i - (n - 1) / 2) * p).ToList();
            foreach (int s in new[] { 1, -1 })
            {
                var pts = new List<Vec2> { new Vec2(f, s * w), new Vec2(f + A, s * w), new Vec2(f + A, s * (w + t)), new Vec2(f + t, s * (w + t)), new Vec2(f + t, s * (w + Bl)), new Vec2(f, s * (w + Bl)) };
                r.Prism(PartKind.Siku, L.Name, new Frame(), Shapes.Filleted(pts, null), -lc / 2, lc / 2);
            }
            r.AddBoq("Siku", L.Name + " L=" + Fmt.N(lc), 2, L.Properties().KgPerM(Env.Density) * lc / 1000);
            double len1 = 0, len2 = 0;
            foreach (var z in zs)
            {
                len1 = r.Bolt(new Vec3(f + GarisBadan, w + t, z), -Vec3.UnitY, 2 * t + twb, b);
                foreach (int s in new[] { 1, -1 }) len2 = r.Bolt(new Vec3(f + t, s * (w + GarisPenopang), z), -Vec3.UnitX, t + clamp, b);
            }
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len1) + " " + Grades.Label(Mutu) + " + mur + 2 ring (badan)", JumlahBaut, BoltKg(b, len1));
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len2) + " " + Grades.Label(Mutu) + " + mur + 2 ring (penopang)", 2 * JumlahBaut, BoltKg(b, len2));
            Support.Stub(r, sup, Muka, Sec.H(bm), Stub, ElevasiPenopang);
            Stub(r, bm, Sec.BeamFrame(f + Celah), 0, Math.Max(1.5 * Sec.H(bm), 500));
            r.TrimSupported = new Vec3(f + Celah, 0, 0);
            if (Muka == SupportFace.BadanBalok) Support.ApplyCope(r, bm, f + Celah, CopingAtas, CopingBawah, PanjangCoping, lc);

            EdgeRule(r, "Kaki badan: baut ke ujung kaki", A - GarisBadan, b);
            EdgeRule(r, "Kaki penopang: baut ke ujung kaki", Bl - GarisPenopang, b);
            EdgeRule(r, "Baut ke tepi atas/bawah siku", TepiTegak, b);
            EdgeRule(r, "Baut ke ujung balok", GarisBadan - Celah, b);
            if (JumlahBaut > 1) PitchRule(r, "Baut", p, b);
            r.Rule(GarisBadan >= t + b.WasherOD / 2 - 1e-6 && GarisPenopang >= t + b.WasherOD / 2 - 1e-6, "Ruang ring terhadap kaki siku lain", "ruang kunci, praktik umum");
            r.Rule(lc <= Support.ClearDepth(bm) + 1e-6, "Panjang siku " + Fmt.N(lc) + " ≤ tinggi bersih badan " + Fmt.N(Support.ClearDepth(bm)), "geometri");
            if (Muka == SupportFace.SayapKolom)
                r.Rule(w + GarisPenopang + b.MinEdge <= Sec.B(sup) / 2 + 1e-6, "Baut penopang muat di sayap kolom", "jarak tepi di sayap kolom");
            if (Muka == SupportFace.BadanBalok) r.Rule(true, "Periksa coping: bila sayap atas balok setinggi sayap balok induk, sayap balok anak perlu dicoak", "catatan");

            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            double off = 0.15 * lc + 20;
            r.Dim(0, new Vec3(f, 0, -lc / 2), new Vec3(f + GarisBadan, 0, -lc / 2), -Vec3.UnitZ, off);
            var chain = new List<double> { lc / 2 }; chain.AddRange(zs.OrderByDescending(z => z)); chain.Add(-lc / 2);
            for (int i = 1; i < chain.Count; i++) r.Dim(0, new Vec3(f + A, 0, chain[i]), new Vec3(f + A, 0, chain[i - 1]), Vec3.UnitX, off);
            r.Note(0, new Vec3(f + A, 0, lc / 2), "2 " + L.Name + " L=" + Fmt.N(lc) + "\\P" + (3 * JumlahBaut) + " baut " + Baut + " " + Grades.Label(Mutu), 2, 2);
            r.Dim(1, new Vec3(0, -(w + GarisPenopang), -lc / 2), new Vec3(0, w + GarisPenopang, -lc / 2), -Vec3.UnitZ, off);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Cleat gording: pelat dilas di sayap atas rafter, gording dibaut di badannya.
    /// Frame lokal: asal = muka atas sayap rafter di bawah gording, X = arah gording, Z = tegak lurus sayap rafter.
    /// </summary>
    public class PurlinCleat : Connection
    {
        public override ConnType Type => ConnType.CleatGording;
        public override string Title => "Cleat gording";

        [Category("1. Profil"), DisplayName("Gording"), TypeConverter(typeof(ProfilesPurlin))] public string Gording { get; set; } = "C150x65x20x3.2";
        [Category("1. Profil"), DisplayName("Rafter"), TypeConverter(typeof(ProfilesIH))] public string Rafter { get; set; } = "WF400x200x8x13";

        [Category("2. Cleat"), DisplayName("Tebal cleat (mm)")] public double TebalCleat { get; set; }
        [Category("2. Cleat"), DisplayName("Lebar cleat searah gording (mm)")] public double LebarCleat { get; set; }
        [Category("2. Cleat"), DisplayName("Tinggi cleat (mm)")] public double TinggiCleat { get; set; }
        [Category("2. Cleat"), DisplayName("Tinggi as baut dari sayap rafter (mm)")] public double TinggiBaut { get; set; }
        [Category("2. Cleat"), DisplayName("Las cleat ke rafter a (mm)")] public double Las { get; set; }

        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M12;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas4_6;
        [Category("3. Baut"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 2;
        [Category("3. Baut"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }

        double WebFace(Profile g) => g.Family == Family.CNP ? -Sec.B(g) / 2 : -g["t"] / 2;

        public override void Recommend()
        {
            var g = Prof(Gording, "Gording");
            double Hp = Sec.H(g), tg = g["t"], rg = g["r"];
            if (Hp >= 250 && Baut < BoltSize.M16) Baut = BoltSize.M16;
            var b = BoltData.Of(Baut);
            TebalCleat = Hp >= 250 ? 8 : 6;
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            TinggiCleat = Fmt.Round5(Math.Min(0.75 * Hp, Hp - tg - rg - 5));
            TinggiBaut = Fmt.Round5(Math.Max(TinggiCleat - e, tg + rg + b.WasherOD / 2 + 3));
            JarakBaut = Fmt.Round5(Math.Max(3 * b.D, 50));
            LebarCleat = Fmt.Round10(Math.Max((JumlahBaut - 1) * JarakBaut + 2 * e, 80));
            Las = Math.Max(4, Fmt.MinFillet(TebalCleat));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var g = Prof(Gording, "Gording"); var raf = Prof(Rafter, "Rafter"); var b = BoltData.Of(Baut);
            double Hp = Sec.H(g), tg = g["t"], rg = g["r"], face = WebFace(g), tc = TebalCleat, wc = LebarCleat, hc = TinggiCleat;
            string lab = Pl(wc, hc, tc);
            var cl = r.Box(PartKind.Pelat, lab, -wc / 2, wc / 2, face - tc, face, 0, hc);
            r.AddBoq("Pelat", lab + " (cleat gording)", 1, cl.MassKg());
            var xs = Enumerable.Range(0, JumlahBaut).Select(i => (i - (JumlahBaut - 1) / 2.0) * JarakBaut).ToList();
            double len = 0;
            foreach (var x in xs) len = r.Bolt(new Vec3(x, face - tc, TinggiBaut), Vec3.UnitY, tc + tg, b);
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", JumlahBaut, BoltKg(b, len));
            double Lg = Math.Max(2 * Hp, 400), Hr = Sec.H(raf);
            Stub(r, g, new Frame(new Vec3(-Lg, 0, Hp / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * Lg);
            Stub(r, raf, new Frame(new Vec3(0, -Math.Max(Hr, 300), -Hr / 2), -Vec3.UnitX, Vec3.UnitZ, Vec3.UnitY), 0, 2 * Math.Max(Hr, 300));

            EdgeRule(r, "Baut ke tepi samping cleat", wc / 2 - (xs.Count > 0 ? xs.Max() : 0), b);
            EdgeRule(r, "Baut ke tepi atas cleat", hc - TinggiBaut, b);
            if (JumlahBaut > 1) PitchRule(r, "Baut", JarakBaut, b);
            r.Rule(TinggiBaut - b.WasherOD / 2 >= tg + rg - 1e-6, "Ring baut bebas dari sayap bawah gording", "ruang kunci, praktik umum");
            r.Rule(hc <= Hp - tg - rg + 1e-6, "Tinggi cleat " + Fmt.N(hc) + " ≤ tinggi bersih badan gording " + Fmt.N(Hp - tg - rg), "geometri");
            WeldRule(r, "Cleat ke sayap rafter", Las, tc, Sec.Tf(raf));

            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            double off = 0.2 * hc + 15;
            r.Dim(0, new Vec3(-wc / 2, 0, 0), new Vec3(wc / 2, 0, 0), -Vec3.UnitZ, off);
            r.Dim(0, new Vec3(wc / 2, 0, 0), new Vec3(wc / 2, 0, TinggiBaut), Vec3.UnitX, off);
            r.Dim(0, new Vec3(wc / 2, 0, TinggiBaut), new Vec3(wc / 2, 0, hc), Vec3.UnitX, off);
            r.Note(0, new Vec3(wc / 2, 0, hc), lab + ", " + JumlahBaut + " baut " + Baut + "\\PLas a=" + Fmt.N(Las), 2, 2);
            r.Dim(1, new Vec3(0, face - tc, hc), new Vec3(0, face, hc), Vec3.UnitZ, off);
            return r;
        }
    }
}
