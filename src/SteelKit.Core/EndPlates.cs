using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    public enum SupportFace { SayapKolom, BadanKolom, BadanBalok }
    public enum EndPlateKind { Rata, DiperpanjangAtas, DiperpanjangKeduanya }

    /// <summary>Data penampang yang dipakai bersama pelat ujung.</summary>
    static class Sec
    {
        public static double H(Profile p) => p.Extent(false);
        public static double B(Profile p) => p.Extent(true);
        public static double Tf(Profile p) => p["tf"] > 0 ? p["tf"] : p["t"];
        public static double Tw(Profile p) => p["tw"] > 0 ? p["tw"] : p["t"];
        public static double R(Profile p) => p["r"];

        /// <summary>Jarak muka penopang dari as penopang dan tebal yang dijepit baut.</summary>
        public static void Face(Profile sup, SupportFace f, out double face, out double clamp)
        {
            if (f == SupportFace.SayapKolom) { face = H(sup) / 2; clamp = Tf(sup); }
            else { face = Tw(sup) / 2; clamp = Tw(sup); }
        }

        /// <summary>Frame penampang batang tegak (kolom) dengan tinggi profil searah X lokal (sayap menghadap +X) atau badan menghadap +X.</summary>
        public static Frame ColumnFrame(SupportFace f, double z0)
            => f == SupportFace.SayapKolom
                ? new Frame(new Vec3(0, 0, z0), -Vec3.UnitY, Vec3.UnitX, Vec3.UnitZ)
                : new Frame(new Vec3(0, 0, z0), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

        /// <summary>Frame penampang balok mendatar searah +X lokal (tinggi profil ke +Z).</summary>
        public static Frame BeamFrame(double x0) => new Frame(new Vec3(x0, 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX);
    }

    // =========================================================================================
    /// <summary>
    /// Pelat ujung balok ke kolom (rata / diperpanjang). Frame lokal: asal = as balok di as kolom,
    /// X = dari kolom ke arah balok (mendatar), Z = ke atas, Y = Z x X.
    /// </summary>
    public class EndPlate : Connection
    {
        public override ConnType Type => ConnType.PelatUjung;
        public override string Title => "Pelat ujung balok-kolom";

        [Category("1. Profil"), DisplayName("Balok"), TypeConverter(typeof(ProfilesIH))] public string Balok { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Kolom / penopang"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "H300x300x10x15";
        [Category("1. Profil"), DisplayName("Muka penopang")] public SupportFace Muka { get; set; } = SupportFace.SayapKolom;

        [Category("2. Pelat ujung"), DisplayName("Jenis pelat ujung")] public EndPlateKind Jenis { get; set; } = EndPlateKind.DiperpanjangAtas;
        [Category("2. Pelat ujung"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat ujung"), DisplayName("Lebar pelat (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat ujung"), DisplayName("Perpanjangan dari sayap (mm)"), Description("Pelat menonjol di luar sayap balok (rata: cukup untuk las).")] public double Perpanjangan { get; set; }

        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Baut per baris"), Description("2 atau 4.")] public int BautPerBaris { get; set; } = 2;
        [Category("3. Baut"), DisplayName("Gauge (mm)"), Description("Jarak as baut kiri-kanan.")] public double Gauge { get; set; }
        [Category("3. Baut"), DisplayName("Jarak baut ke sayap pf (mm)")] public double Pf { get; set; }
        [Category("3. Baut"), DisplayName("Baris tambahan di badan")] public int BarisTambahan { get; set; }

        [Category("4. Las & pengaku"), DisplayName("Las sayap a (mm)")] public double LasSayap { get; set; }
        [Category("4. Las & pengaku"), DisplayName("Las badan a (mm)")] public double LasBadan { get; set; }
        [Category("4. Las & pengaku"), DisplayName("Pengaku kolom di level sayap")] public bool PengakuKolom { get; set; } = true;
        [Category("4. Las & pengaku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; }

        public override void Recommend()
        {
            var bm = Prof(Balok, "Balok"); var b = BoltData.Of(Baut);
            if (Sec.H(bm) > 450 && Baut < BoltSize.M24) { Baut = BoltSize.M24; b = BoltData.Of(Baut); }
            TebalPelat = Fmt.StdPlate(Math.Max(16, b.D));
            LasSayap = Math.Max(Fmt.MinFillet(Math.Min(Sec.Tf(bm), TebalPelat)), Math.Ceiling(0.7 * Sec.Tf(bm)));
            LasSayap = Math.Min(LasSayap, Math.Min(Sec.Tf(bm), TebalPelat) - 2 < 3 ? Math.Min(Sec.Tf(bm), TebalPelat) : Math.Min(Sec.Tf(bm), TebalPelat) - 2);
            LasBadan = Math.Max(Fmt.MinFillet(Math.Min(Sec.Tw(bm), TebalPelat)), 5);
            Pf = Fmt.Round5(Math.Max(b.D + 12, b.WasherOD / 2 + LasSayap + 5));
            Gauge = Fmt.Round10(Math.Max(Sec.Tw(bm) + 2 * LasBadan + b.WasherOD + 10, 0.55 * Sec.B(bm)));
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            double width = BautPerBaris == 4 ? Gauge + 2 * b.MinSpacing : Gauge;
            LebarPelat = Fmt.Round10(Math.Max(Sec.B(bm) + 20, width + 2 * e));
            Perpanjangan = Jenis == EndPlateKind.Rata ? 15 : Fmt.Round5(Pf + e);
            double inner = Sec.H(bm) - 2 * Sec.Tf(bm) - 2 * Pf;
            BarisTambahan = Math.Max(0, (int)Math.Floor(inner / Math.Max(3 * b.D, 90)) - 1);
            BarisTambahan = Math.Min(BarisTambahan, 3);
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Sec.Tf(bm)));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bm = Prof(Balok, "Balok"); var col = Prof(Kolom, "Kolom"); var b = BoltData.Of(Baut);
            Sec.Face(col, Muka, out double f, out double clamp);
            double Hb = Sec.H(bm), Bb = Sec.B(bm), tfb = Sec.Tf(bm), twb = Sec.Tw(bm), tp = TebalPelat, bp = LebarPelat;
            bool extT = Jenis != EndPlateKind.Rata, extB = Jenis == EndPlateKind.DiperpanjangKeduanya;
            double zt = Hb / 2 + (extT ? Perpanjangan : 15), zbot = -Hb / 2 - (extB ? Perpanjangan : 15);
            var rows = PlateRows(Hb / 2, -Hb / 2, tfb, tfb, Pf, extT, extB, BarisTambahan);
            var ys = BautPerBaris == 4 ? new[] { -Gauge / 2 - b.MinSpacing, -Gauge / 2, Gauge / 2, Gauge / 2 + b.MinSpacing } : new[] { -Gauge / 2, Gauge / 2 };

            string plateLabel = Pl(bp, zt - zbot, tp);
            var plate = r.Box(PartKind.Pelat, plateLabel, f, f + tp, -bp / 2, bp / 2, zbot, zt);
            r.AddBoq("Pelat", plateLabel + " (pelat ujung)", 1, plate.MassKg());
            double len = 0;
            foreach (var z in rows) foreach (var y in ys) len = r.Bolt(new Vec3(f + tp, y, z), -Vec3.UnitX, tp + clamp, b);
            int nb = rows.Count * ys.Length;
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", nb, BoltKg(b, len));

            if (Muka == SupportFace.BadanBalok) Support.Stub(r, col, Muka, Hb, Stub);
            else Stub(r, col, Sec.ColumnFrame(Muka, -Hb), 0, Hb * 2.2);
            Stub(r, bm, Sec.BeamFrame(f + tp), 0, Math.Max(1.5 * Hb, 500));
            if (PengakuKolom && Muka == SupportFace.SayapKolom && col.Family != Family.HOLLOW)
            {
                double Hc = Sec.H(col), Bc = Sec.B(col), tfc = Sec.Tf(col), twc = Sec.Tw(col), ts = TebalPengaku;
                foreach (var zl in new[] { Hb / 2 - tfb / 2, -Hb / 2 + tfb / 2 })
                    foreach (int s in new[] { 1, -1 })
                    {
                        double y0 = s > 0 ? twc / 2 : -Bc / 2, y1 = s > 0 ? Bc / 2 : -twc / 2;
                        var st = r.Box(PartKind.Pengaku, "", -Hc / 2 + tfc, Hc / 2 - tfc, y0, y1, zl - ts / 2, zl + ts / 2);
                        r.AddBoq("Pelat", Pl(Hc - 2 * tfc, (Bc - twc) / 2, ts) + " (pengaku kolom)", 1, st.MassKg());
                    }
            }
            r.TrimSupported = new Vec3(f + tp, 0, 0);

            // ---------------- cek
            double eV = Math.Min(zt - rows.Max(), rows.Min() - zbot);
            EdgeRule(r, "Baut ke tepi atas/bawah pelat", eV, b);
            EdgeRule(r, "Baut ke tepi samping pelat", bp / 2 - ys.Max(), b);
            MaxEdgeRule(r, "Tepi samping pelat", bp / 2 - ys.Max(), tp);
            var sorted = rows.OrderBy(z => z).ToList();
            for (int i = 1; i < sorted.Count; i++) PitchRule(r, "Baris " + i, sorted[i] - sorted[i - 1], b);
            if (BautPerBaris == 4) PitchRule(r, "Baut sebaris", b.MinSpacing, b);
            r.Rule(Pf >= b.WasherOD / 2 + LasSayap + 3 - 1e-6, "pf=" + Fmt.N(Pf) + " cukup untuk ring + las sayap (≥ " + Fmt.N(b.WasherOD / 2 + LasSayap + 3) + ")", "ruang kunci, praktik umum");
            r.Rule(Gauge >= twb + 2 * LasBadan + b.WasherOD - 1e-6, "Gauge " + Fmt.N(Gauge) + " cukup untuk badan + las + ring (≥ " + Fmt.N(twb + 2 * LasBadan + b.WasherOD) + ")", "ruang kunci, praktik umum");
            r.Rule(bp >= Bb - 1e-6, "Lebar pelat " + Fmt.N(bp) + " ≥ lebar sayap balok " + Fmt.N(Bb), "geometri");
            if (Muka == SupportFace.SayapKolom)
                r.Rule(ys.Max() + b.MinEdge <= Sec.B(col) / 2 + 1e-6, "Baut muat di sayap kolom (" + Fmt.N(ys.Max() + b.MinEdge) + " ≤ " + Fmt.N(Sec.B(col) / 2) + ")", "jarak tepi di sayap kolom");
            else
                r.Rule(bp <= Sec.H(col) - 2 * Sec.Tf(col) - 2 * Sec.R(col) + 1e-6, "Pelat muat di antara sayap kolom (" + Fmt.N(bp) + " ≤ " + Fmt.N(Sec.H(col) - 2 * Sec.Tf(col) - 2 * Sec.R(col)) + ")", "geometri");
            WeldRule(r, "Sayap balok ke pelat", LasSayap, tfb, tp);
            WeldRule(r, "Badan balok ke pelat", LasBadan, twb, tp);

            // ---------------- gambar detail
            r.Views.Add(new ViewSpec("TAMPAK MUKA PELAT UJUNG", Vec3.UnitY, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            double off = 0.1 * (zt - zbot);
            r.Dim(0, new Vec3(f, -bp / 2, zbot), new Vec3(f, bp / 2, zbot), -Vec3.UnitZ, off * 2);
            r.Dim(0, new Vec3(f, -Gauge / 2, zbot), new Vec3(f, Gauge / 2, zbot), -Vec3.UnitZ, off);
            var chain = new List<double> { zt }; chain.AddRange(rows.OrderByDescending(z => z)); chain.Add(zbot);
            for (int i = 1; i < chain.Count; i++) r.Dim(0, new Vec3(f, bp / 2, chain[i]), new Vec3(f, bp / 2, chain[i - 1]), Vec3.UnitY, off);
            r.Note(0, new Vec3(f, -bp / 2, zt), plateLabel + "\\P" + nb + " baut " + Baut + " " + Grades.Label(Mutu), -4, 1.5);
            r.Dim(1, new Vec3(f, 0, zt), new Vec3(f + tp, 0, zt), Vec3.UnitZ, off);
            r.Note(1, new Vec3(f + tp, 0, Hb / 2), "Las sayap a=" + Fmt.N(LasSayap) + ", badan a=" + Fmt.N(LasBadan), 2, 2);
            if (PengakuKolom && Muka == SupportFace.SayapKolom) r.Note(1, new Vec3(0, 0, -Hb / 2), "Pengaku kolom t=" + Fmt.N(TebalPengaku), -8, -2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Sambungan pelat ujung berpasangan (puncak / splice). Frame lokal: bidang pelat = YZ di x=0,
    /// batang A ke -X, batang B ke +X, keduanya miring sebesar Sudut ke bawah (puncak) terhadap bidang datar.
    /// </summary>
    public class SplicePlate : Connection
    {
        public override ConnType Type => ConnType.SambunganPelatUjung;
        public override string Title => "Sambungan pelat ujung (puncak / splice)";

        [Category("1. Profil"), DisplayName("Profil batang"), TypeConverter(typeof(ProfilesIH))] public string Profil { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Sudut miring batang (°)"), Description("0 = splice lurus; positif = puncak (batang turun menjauhi sambungan).")] public double Sudut { get; set; }

        [Category("2. Pelat ujung"), DisplayName("Tebal tiap pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat ujung"), DisplayName("Lebar pelat (mm)")] public double LebarPelat { get; set; }
        [Category("2. Pelat ujung"), DisplayName("Perpanjangan atas (mm)")] public double PerpanjanganAtas { get; set; }
        [Category("2. Pelat ujung"), DisplayName("Perpanjangan bawah (mm)")] public double PerpanjanganBawah { get; set; }

        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Baut"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("3. Baut"), DisplayName("Jarak baut ke sayap pf (mm)")] public double Pf { get; set; }
        [Category("3. Baut"), DisplayName("Baris tambahan di badan")] public int BarisTambahan { get; set; }

        [Category("4. Las"), DisplayName("Las sayap a (mm)")] public double LasSayap { get; set; }
        [Category("4. Las"), DisplayName("Las badan a (mm)")] public double LasBadan { get; set; }

        double Cos => Math.Cos(Sudut * Math.PI / 180);

        public override void Recommend()
        {
            var m = Prof(Profil, "Profil"); var b = BoltData.Of(Baut);
            if (Sec.H(m) > 450 && Baut < BoltSize.M24) { Baut = BoltSize.M24; b = BoltData.Of(Baut); }
            TebalPelat = Fmt.StdPlate(Math.Max(16, b.D));
            double thin = Math.Min(Sec.Tf(m), TebalPelat);
            LasSayap = Math.Max(Fmt.MinFillet(thin), Math.Min(Math.Ceiling(0.7 * Sec.Tf(m)), thin - 2));
            LasBadan = Math.Max(Fmt.MinFillet(Math.Min(Sec.Tw(m), TebalPelat)), 5);
            Pf = Fmt.Round5(Math.Max(b.D + 12, b.WasherOD / 2 + LasSayap + 5));
            Gauge = Fmt.Round10(Math.Max(Sec.Tw(m) + 2 * LasBadan + b.WasherOD + 10, 0.55 * Sec.B(m)));
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            LebarPelat = Fmt.Round10(Math.Max(Sec.B(m) + 20, Gauge + 2 * e));
            PerpanjanganAtas = Sudut > 0.5 ? Fmt.Round5(Pf + e) : 15;
            PerpanjanganBawah = 15;
            double inner = Sec.H(m) / Cos - 2 * Sec.Tf(m) / Cos - 2 * Pf;
            BarisTambahan = Math.Min(3, Math.Max(0, (int)Math.Floor(inner / Math.Max(3 * b.D, 90)) - 1));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var m = Prof(Profil, "Profil"); var b = BoltData.Of(Baut);
            double H = Sec.H(m), tf = Sec.Tf(m), tp = TebalPelat, bp = LebarPelat, cs = Cos;
            double hc = H / cs, tfc = tf / cs;
            bool extT = PerpanjanganAtas > Pf + b.MinEdge - 1e-6, extB = PerpanjanganBawah > Pf + b.MinEdge - 1e-6;
            double zt = hc / 2 + PerpanjanganAtas, zb = -hc / 2 - PerpanjanganBawah;
            var rows = PlateRows(hc / 2, -hc / 2, tfc, tfc, Pf, extT, extB, BarisTambahan);
            var ys = new[] { -Gauge / 2, Gauge / 2 };
            string lab = Pl(bp, zt - zb, tp);
            foreach (int s in new[] { -1, 1 })
            {
                var pl = r.Box(PartKind.Pelat, lab, s < 0 ? -tp : 0, s < 0 ? 0 : tp, -bp / 2, bp / 2, zb, zt);
                r.AddBoq("Pelat", lab + " (pelat ujung)", 1, pl.MassKg());
            }
            double len = 0;
            foreach (var z in rows) foreach (var y in ys) len = r.Bolt(new Vec3(-tp, y, z), Vec3.UnitX, 2 * tp, b);
            int nb = rows.Count * ys.Length;
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", nb, BoltKg(b, len));

            double a = Sudut * Math.PI / 180;
            var dA = new Vec3(-Math.Cos(a), 0, -Math.Sin(a)); var dB = new Vec3(Math.Cos(a), 0, -Math.Sin(a));
            // kedua batang dipotong tegak rapat ke pelat masing-masing (puncak: potongan sejajar pelat, bukan tegak lurus batang)
            double stubLen = Math.Max(1.5 * H, 500);
            r.CutSupport.Add(FlushStub(r, m, dA * (tp / cs), dA, -Vec3.UnitX, 0, stubLen, out Vec3 trimA));
            r.CutSupported.Add(FlushStub(r, m, dB * (tp / cs), dB, Vec3.UnitX, 0, stubLen, out Vec3 trimB));
            r.TrimSupport = trimA;
            r.TrimSupported = trimB;

            double eV = Math.Min(zt - rows.Max(), rows.Min() - zb);
            EdgeRule(r, "Baut ke tepi atas/bawah pelat", eV, b);
            EdgeRule(r, "Baut ke tepi samping pelat", bp / 2 - Gauge / 2, b);
            MaxEdgeRule(r, "Tepi samping pelat", bp / 2 - Gauge / 2, tp);
            var sorted = rows.OrderBy(z => z).ToList();
            for (int i = 1; i < sorted.Count; i++) PitchRule(r, "Baris " + i, sorted[i] - sorted[i - 1], b);
            r.Rule(Gauge >= Sec.Tw(m) + 2 * LasBadan + b.WasherOD - 1e-6, "Gauge " + Fmt.N(Gauge) + " cukup untuk badan + las + ring", "ruang kunci, praktik umum");
            r.Rule(Math.Abs(Sudut) < 45, "Sudut batang " + Fmt.N(Sudut) + "° < 45°", "geometri");
            WeldRule(r, "Sayap ke pelat", LasSayap, tf, tp);
            WeldRule(r, "Badan ke pelat", LasBadan, Sec.Tw(m), tp);

            r.Views.Add(new ViewSpec("TAMPAK MUKA PELAT", Vec3.UnitY, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            double off = 0.1 * (zt - zb);
            r.Dim(0, new Vec3(0, -bp / 2, zb), new Vec3(0, bp / 2, zb), -Vec3.UnitZ, off * 2);
            r.Dim(0, new Vec3(0, -Gauge / 2, zb), new Vec3(0, Gauge / 2, zb), -Vec3.UnitZ, off);
            var chain = new List<double> { zt }; chain.AddRange(rows.OrderByDescending(z => z)); chain.Add(zb);
            for (int i = 1; i < chain.Count; i++) r.Dim(0, new Vec3(0, bp / 2, chain[i]), new Vec3(0, bp / 2, chain[i - 1]), Vec3.UnitY, off);
            r.Note(0, new Vec3(0, -bp / 2, zt), "2 x " + lab + "\\P" + nb + " baut " + Baut + " " + Grades.Label(Mutu), -4, 1.5);
            r.Dim(1, new Vec3(-tp, 0, zt), new Vec3(tp, 0, zt), Vec3.UnitZ, off);
            r.Note(1, new Vec3(tp, 0, hc / 2), "Las sayap a=" + Fmt.N(LasSayap) + ", badan a=" + Fmt.N(LasBadan), 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Lutut portal PEB: kolom tegak, rafter miring dari puncak kolom, haunch di bawah rafter, pelat ujung tegak
    /// dibaut ke sayap kolom. Frame lokal: asal = titik temu as kolom dan as rafter, X = arah rafter (mendatar), Z = atas.
    /// </summary>
    public class KneeHaunch : Connection
    {
        public override ConnType Type => ConnType.LututHaunch;
        public override string Title => "Lutut portal + haunch";

        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesIH))] public string Kolom { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Rafter"), TypeConverter(typeof(ProfilesIH))] public string Rafter { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Kemiringan rafter (°)")] public double Sudut { get; set; } = 10;

        [Category("2. Haunch"), DisplayName("Panjang haunch (mm)"), Description("Praktik umum ±10% bentang.")] public double PanjangHaunch { get; set; }
        [Category("2. Haunch"), DisplayName("Tinggi tambahan di muka kolom (mm)")] public double TinggiHaunch { get; set; }
        [Category("2. Haunch"), DisplayName("Tebal badan haunch (mm)")] public double TebalBadanHaunch { get; set; }

        [Category("3. Pelat ujung"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("3. Pelat ujung"), DisplayName("Lebar pelat (mm)")] public double LebarPelat { get; set; }
        [Category("3. Pelat ujung"), DisplayName("Perpanjangan atas (mm)")] public double PerpanjanganAtas { get; set; }

        [Category("4. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("4. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("4. Baut"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("4. Baut"), DisplayName("Jarak baut ke sayap pf (mm)")] public double Pf { get; set; }
        [Category("4. Baut"), DisplayName("Baris tambahan")] public int BarisTambahan { get; set; }

        [Category("5. Las & pengaku"), DisplayName("Las sayap a (mm)")] public double LasSayap { get; set; }
        [Category("5. Las & pengaku"), DisplayName("Las badan a (mm)")] public double LasBadan { get; set; }
        [Category("5. Las & pengaku"), DisplayName("Pengaku kolom")] public bool PengakuKolom { get; set; } = true;
        [Category("5. Las & pengaku"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; }
        [Category("5. Las & pengaku"), DisplayName("Pelat tutup kolom")] public bool TutupKolom { get; set; } = true;

        /// <summary>Panjang rafter dari as kolom (dipakai untuk rekomendasi panjang haunch).</summary>
        [Browsable(false)] public double PanjangRafter { get; set; } = 10000;

        double Tan => Math.Tan(Sudut * Math.PI / 180);
        double Cos => Math.Cos(Sudut * Math.PI / 180);

        public override void Recommend()
        {
            var raf = Prof(Rafter, "Rafter"); var b = BoltData.Of(Baut);
            if (Sec.H(raf) > 450 && Baut < BoltSize.M24) { Baut = BoltSize.M24; b = BoltData.Of(Baut); }
            PanjangHaunch = Fmt.Round10(Math.Max(0.2 * PanjangRafter, 2.5 * Sec.H(raf)));
            TinggiHaunch = Fmt.Round10(0.8 * Sec.H(raf));
            TebalBadanHaunch = Fmt.StdPlate(Math.Max(6, Sec.Tw(raf)));
            TebalPelat = Fmt.StdPlate(Math.Max(16, b.D));
            double thin = Math.Min(Sec.Tf(raf), TebalPelat);
            LasSayap = Math.Max(Fmt.MinFillet(thin), Math.Min(Math.Ceiling(0.7 * Sec.Tf(raf)), thin - 2));
            LasBadan = Math.Max(Fmt.MinFillet(Math.Min(Sec.Tw(raf), TebalPelat)), 5);
            Pf = Fmt.Round5(Math.Max(b.D + 12, b.WasherOD / 2 + LasSayap + 5));
            Gauge = Fmt.Round10(Math.Max(Sec.Tw(raf) + 2 * LasBadan + b.WasherOD + 10, 0.55 * Sec.B(raf)));
            double e = Fmt.Round5(Math.Max(b.MinEdge, 1.5 * b.D));
            LebarPelat = Fmt.Round10(Math.Max(Sec.B(raf) + 20, Gauge + 2 * e));
            PerpanjanganAtas = Fmt.Round5(Pf + e);
            double depth = Sec.H(raf) / Cos + TinggiHaunch;
            BarisTambahan = Math.Min(4, Math.Max(0, (int)Math.Floor((depth - 2 * Sec.Tf(raf) - 2 * Pf) / Math.Max(3.5 * b.D, 100)) - 1));
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Sec.Tf(raf)));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var col = Prof(Kolom, "Kolom"); var raf = Prof(Rafter, "Rafter"); var b = BoltData.Of(Baut);
            double Hc = Sec.H(col), Bc = Sec.B(col), tfc = Sec.Tf(col), twc = Sec.Tw(col);
            double Hr = Sec.H(raf), Br = Sec.B(raf), tfr = Sec.Tf(raf);
            double f = Hc / 2, tp = TebalPelat, x1 = f + tp, tan = Tan, cs = Cos;
            Func<double, double> zTop = x => x * tan + Hr / (2 * cs);
            Func<double, double> zBot = x => x * tan - Hr / (2 * cs);
            double Lh = PanjangHaunch, Dh = TinggiHaunch;
            double zHaunchFace = zBot(x1) - Dh;                     // sisi bawah badan haunch di muka pelat
            // sayap haunch: garis miring dari muka pelat ke sisi bawah rafter di ujung haunch
            var p1 = new Vec3(x1, 0, zHaunchFace); var p2 = new Vec3(x1 + Lh, 0, zBot(x1 + Lh));
            var u = (p2 - p1).Unit; double fl = (p2 - p1).Length;
            double plateTop = zTop(x1) + PerpanjanganAtas, plateBot = zHaunchFace - tfr / Math.Max(u.X, 0.2) - 15;
            var rows = PlateRows(zTop(x1), zHaunchFace - tfr, tfr / cs, tfr, Pf, true, false, BarisTambahan);
            var ys = new[] { -Gauge / 2, Gauge / 2 };

            string lab = Pl(LebarPelat, plateTop - plateBot, tp);
            var plate = r.Box(PartKind.Pelat, lab, f, x1, -LebarPelat / 2, LebarPelat / 2, plateBot, plateTop);
            r.AddBoq("Pelat", lab + " (pelat ujung lutut)", 1, plate.MassKg());
            double len = 0;
            foreach (var z in rows) foreach (var y in ys) len = r.Bolt(new Vec3(x1, y, z), -Vec3.UnitX, tp + tfc, b);
            int nb = rows.Count * ys.Length;
            r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", nb, BoltKg(b, len));

            // badan haunch: segitiga tepat di bawah sisi bawah rafter (menempel, tanpa celah) sampai sayap haunch
            var fxz = new Frame(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY);   // loop di bidang (x, z)
            var web = new List<Vec2> { new Vec2(x1, zBot(x1)), new Vec2(x1 + Lh, zBot(x1 + Lh)), new Vec2(x1, zHaunchFace) };
            var wp = r.Prism(PartKind.Pelat, "", fxz, Shapes.Filleted(web, null), -TebalBadanHaunch / 2, TebalBadanHaunch / 2);
            r.AddBoq("Pelat", "PL t=" + Fmt.N(TebalBadanHaunch) + " (badan haunch)", 1, wp.MassKg());
            // sayap haunch di sisi luar badan; kedua ujung dipotong rapat: tegak di muka pelat, sejajar sisi bawah rafter di ujung
            var ff = new Frame(p1, u, Vec3.UnitY, u.Cross(Vec3.UnitY));
            double fe = 3 * tfr + 20;
            double sn = Math.Sin(Sudut * Math.PI / 180);
            var flange = r.Prism(PartKind.Pelat, "", ff, Shapes.Rect(-fe, -Br / 2, fl + fe, Br / 2), -tfr, 0)
                .Clip(new Vec3(x1, 0, 0), Vec3.UnitX)
                .Clip(new Vec3(0, 0, zBot(0)), new Vec3(sn, 0, -cs));
            r.AddBoq("Pelat", Pl(Br, fl, tfr) + " (sayap haunch)", 1, Br * fl * tfr * 7.85e-6);

            if (PengakuKolom)
            {
                double ts = TebalPengaku;
                foreach (var zl in new[] { zTop(f) - tfr / 2, zHaunchFace - tfr / 2 })
                    foreach (int s in new[] { 1, -1 })
                    {
                        double y0 = s > 0 ? twc / 2 : -Bc / 2, y1 = s > 0 ? Bc / 2 : -twc / 2;
                        var st = r.Box(PartKind.Pengaku, "", -Hc / 2 + tfc, Hc / 2 - tfc, y0, y1, zl - ts / 2, zl + ts / 2);
                        r.AddBoq("Pelat", Pl(Hc - 2 * tfc, (Bc - twc) / 2, ts) + " (pengaku kolom)", 1, st.MassKg());
                    }
            }
            double colTop = plateTop;
            if (TutupKolom)
            {
                double tcap = Fmt.StdPlate(Math.Max(10, tfc));
                var cap = r.Box(PartKind.Pelat, "", -Hc / 2, Hc / 2, -Bc / 2, Bc / 2, plateTop - tcap, plateTop);
                r.AddBoq("Pelat", Pl(Hc, Bc, tcap) + " (tutup kolom)", 1, cap.MassKg());
                colTop = plateTop - tcap;
            }
            Stub(r, col, Sec.ColumnFrame(SupportFace.SayapKolom, plateBot - Hc), 0, colTop - plateBot + Hc);
            // rafter dipotong tegak (plumb cut) rapat ke muka pelat ujung: tidak menembus pelat, tidak bercelah
            var dR = new Vec3(cs, 0, Math.Sin(Sudut * Math.PI / 180));
            r.CutSupported.Add(FlushStub(r, raf, dR * (x1 / cs), dR, Vec3.UnitX, 0, Lh + Hr, out Vec3 trimR));
            r.TrimSupported = trimR;
            r.TrimSupport = new Vec3(0, 0, colTop);

            double eV = Math.Min(plateTop - rows.Max(), rows.Min() - plateBot);
            EdgeRule(r, "Baut ke tepi atas/bawah pelat", eV, b);
            EdgeRule(r, "Baut ke tepi samping pelat", LebarPelat / 2 - Gauge / 2, b);
            var sorted = rows.OrderBy(z => z).ToList();
            for (int i = 1; i < sorted.Count; i++) PitchRule(r, "Baris " + i, sorted[i] - sorted[i - 1], b);
            r.Rule(Gauge / 2 + b.MinEdge <= Bc / 2 + 1e-6, "Baut muat di sayap kolom (" + Fmt.N(Gauge / 2 + b.MinEdge) + " ≤ " + Fmt.N(Bc / 2) + ")", "jarak tepi di sayap kolom");
            r.Rule(Lh > 0 && Dh > 0, "Haunch punya panjang dan tinggi", "geometri");
            r.Rule(Dh <= 1.5 * Hr + 1e-6, "Tinggi haunch " + Fmt.N(Dh) + " ≤ 1,5 x tinggi rafter", "praktik umum PEB");
            WeldRule(r, "Sayap rafter ke pelat", LasSayap, tfr, tp);
            WeldRule(r, "Badan rafter ke pelat", LasBadan, Sec.Tw(raf), tp);

            r.Views.Add(new ViewSpec("TAMPAK SAMPING LUTUT", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK MUKA PELAT UJUNG", Vec3.UnitY, Vec3.UnitZ));
            double off = 0.08 * (plateTop - plateBot);
            r.Dim(0, new Vec3(x1, 0, zHaunchFace), new Vec3(x1 + Lh, 0, zHaunchFace), -Vec3.UnitZ, off * 2, "haunch " + Fmt.N(Lh));
            r.Dim(0, new Vec3(x1, 0, zHaunchFace), new Vec3(x1, 0, zBot(x1)), Vec3.UnitX, -off, null);
            r.Note(0, new Vec3(x1 + Lh / 2, 0, (zBot(x1 + Lh / 2) + zHaunchFace) / 2), "Badan haunch t=" + Fmt.N(TebalBadanHaunch) + "\\PSayap haunch " + Fmt.N(Br) + "x" + Fmt.N(tfr), 2, -3);
            r.Note(0, new Vec3(x1, 0, plateTop), lab + ", " + nb + " baut " + Baut + " " + Grades.Label(Mutu), 2, 2);
            var chain = new List<double> { plateTop }; chain.AddRange(rows.OrderByDescending(z => z)); chain.Add(plateBot);
            for (int i = 1; i < chain.Count; i++) r.Dim(1, new Vec3(x1, LebarPelat / 2, chain[i]), new Vec3(x1, LebarPelat / 2, chain[i - 1]), Vec3.UnitY, off);
            r.Dim(1, new Vec3(x1, -Gauge / 2, plateBot), new Vec3(x1, Gauge / 2, plateBot), -Vec3.UnitZ, off);
            return r;
        }
    }
}
