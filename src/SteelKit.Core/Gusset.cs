using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    public enum GussetMethod { Baut, Las }

    /// <summary>Satu muka "kaki" profil yang bisa ditempel pelat buhul (koordinat lokal penampang, terhadap as tengah).</summary>
    public class LegFace
    {
        public Vec2 Normal;        // normal keluar (lokal x,y)
        public double Offset;      // jarak muka dari as tengah
        public Vec2 Axis;          // arah melintang kaki di bidang pelat (lokal)
        public double Heel;        // posisi pangkal kaki (sepanjang Axis)
        public double Width;       // lebar kaki dari pangkal
        public double Thick;       // tebal kaki
        public double Upstand;     // tinggi kaki tegak di pangkal (siku), 0 bila tidak ada
        public bool Boltable;      // bisa dibaut (penampang terbuka)
        public double GaugeDefault;

        /// <summary>Muka-muka kaki menurut jenis profil. Urutan = prioritas.</summary>
        public static List<LegFace> Of(Profile p)
        {
            double H = p.Extent(false), B = p.Extent(true), t = p["t"];
            var l = new List<LegFace>();
            switch (p.Family)
            {
                case Family.L:
                    l.Add(new LegFace { Normal = new Vec2(0, -1), Offset = H / 2, Axis = new Vec2(1, 0), Heel = -B / 2, Width = B, Thick = t, Upstand = H, Boltable = true, GaugeDefault = Gauge(B, t) });
                    l.Add(new LegFace { Normal = new Vec2(-1, 0), Offset = B / 2, Axis = new Vec2(0, 1), Heel = -H / 2, Width = H, Thick = t, Upstand = B, Boltable = true, GaugeDefault = Gauge(H, t) });
                    break;
                case Family.PL:
                    l.Add(new LegFace { Normal = new Vec2(0, -1), Offset = t / 2, Axis = new Vec2(1, 0), Heel = -B / 2, Width = B, Thick = t, Boltable = true, GaugeDefault = B / 2 });
                    l.Add(new LegFace { Normal = new Vec2(0, 1), Offset = t / 2, Axis = new Vec2(1, 0), Heel = -B / 2, Width = B, Thick = t, Boltable = true, GaugeDefault = B / 2 });
                    break;
                case Family.CNP:
                    l.Add(new LegFace { Normal = new Vec2(-1, 0), Offset = B / 2, Axis = new Vec2(0, 1), Heel = -H / 2, Width = H, Thick = t, Upstand = 0, Boltable = true, GaugeDefault = H / 2 });
                    break;
                case Family.HOLLOW:
                    foreach (var s in new[] { -1, 1 })
                    {
                        l.Add(new LegFace { Normal = new Vec2(0, s), Offset = H / 2, Axis = new Vec2(1, 0), Heel = -B / 2, Width = B, Thick = t, Boltable = false, GaugeDefault = B / 2 });
                        l.Add(new LegFace { Normal = new Vec2(s, 0), Offset = B / 2, Axis = new Vec2(0, 1), Heel = -H / 2, Width = H, Thick = t, Boltable = false, GaugeDefault = H / 2 });
                    }
                    break;
                case Family.CHS:
                    double D = p["D"];
                    foreach (var n in new[] { new Vec2(0, -1), new Vec2(0, 1), new Vec2(-1, 0), new Vec2(1, 0) })
                        l.Add(new LegFace { Normal = n, Offset = D / 2, Axis = new Vec2(-n.Y, n.X), Heel = -D / 2, Width = D, Thick = t, Boltable = false, GaugeDefault = D / 2 });
                    break;
            }
            return l;
        }

        static double Gauge(double leg, double t) => Fmt.Round5(Math.Max(0.55 * leg, t + 12));

        public static bool Supported(Family f) => f == Family.L || f == Family.PL || f == Family.CNP || f == Family.HOLLOW || f == Family.CHS;
    }

    /// <summary>
    /// Pelat buhul (gusset) untuk dua batang ringan (siku, plat strip, CNP, hollow, pipa) yang bertemu di satu bidang:
    /// sudut rangka, siku-T (batang A menerus), atau lurus. Frame lokal: bidang pelat = XY dengan muka atas pelat di z=0,
    /// batang di sisi +Z, batang A ke +X, batang B pada SudutB dari A.
    /// </summary>
    public class GussetNode : Connection
    {
        public override ConnType Type => ConnType.PelatBuhul;
        public override string Title => "Pelat buhul (gusset) batang ringan";

        [Category("1. Batang"), DisplayName("Profil batang A"), TypeConverter(typeof(ProfilesLight))] public string ProfilA { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Profil batang B"), TypeConverter(typeof(ProfilesLight))] public string ProfilB { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Sudut batang B terhadap A (°)")] public double SudutB { get; set; } = 90;
        [Category("1. Batang"), DisplayName("Batang A menerus (siku-T)")] public bool MenerusA { get; set; }

        [Category("2. Pelat buhul"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat buhul"), DisplayName("Celah antar ujung batang (mm)")] public double Celah { get; set; } = 15;
        [Category("2. Pelat buhul"), DisplayName("Tepi pelat dari baut (mm)")] public double TepiPelat { get; set; }

        [Category("3. Sambung"), DisplayName("Metode")] public GussetMethod Metode { get; set; } = GussetMethod.Baut;
        [Category("3. Sambung"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("3. Sambung"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Sambung"), DisplayName("Jumlah baut per batang")] public int JumlahBaut { get; set; } = 2;
        [Category("3. Sambung"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }
        [Category("3. Sambung"), DisplayName("Jarak baut ke ujung batang (mm)")] public double JarakUjung { get; set; }
        [Category("3. Sambung"), DisplayName("Gauge batang A dari pangkal kaki (mm)")] public double GaugeA { get; set; }
        [Category("3. Sambung"), DisplayName("Gauge batang B dari pangkal kaki (mm)")] public double GaugeB { get; set; }

        [Category("4. Las"), DisplayName("Las sudut a (mm)")] public double Las { get; set; }
        [Category("4. Las"), DisplayName("Panjang las per batang (mm)")] public double PanjangLas { get; set; }

        // geometri kaki di bidang pelat (diisi pendeteksi otomatis; bawaan = kaki utama profil)
        [Browsable(false)] public double HeelA { get; set; } = double.NaN;
        [Browsable(false)] public double ArahA { get; set; } = 1;
        [Browsable(false)] public double HeelB { get; set; } = double.NaN;
        [Browsable(false)] public double ArahB { get; set; } = 1;
        [Browsable(false)] public double SelisihMuka { get; set; }
        [Browsable(false)] public int MukaA { get; set; }
        [Browsable(false)] public int MukaB { get; set; }

        static LegFace Leg(Profile p, int idx)
        {
            var l = LegFace.Of(p);
            if (l.Count == 0) throw new ArgumentException(p.Name + ": jenis profil ini belum didukung pelat buhul (pakai siku, plat strip, CNP, hollow, atau pipa).");
            return l[Math.Max(0, Math.Min(idx, l.Count - 1))];
        }

        public override void Recommend()
        {
            var a = Prof(ProfilA, "Batang A"); var b = Prof(ProfilB, "Batang B");
            var la = Leg(a, MukaA); var lb = Leg(b, MukaB);
            bool bolt = la.Boltable && lb.Boltable;
            Metode = bolt ? GussetMethod.Baut : GussetMethod.Las;
            double w = Math.Min(la.Width, lb.Width);
            Baut = w < 60 ? BoltSize.M12 : w < 90 ? BoltSize.M16 : w < 125 ? BoltSize.M20 : BoltSize.M22;
            var bd = BoltData.Of(Baut);
            JarakUjung = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            JarakBaut = Fmt.Round5(Math.Max(3 * bd.D, 40));
            JumlahBaut = 2;
            GaugeA = la.GaugeDefault; GaugeB = lb.GaugeDefault;
            if (a.Family == Family.L) GaugeA = Math.Min(GaugeA, la.Width - bd.MinEdge);
            if (b.Family == Family.L) GaugeB = Math.Min(GaugeB, lb.Width - bd.MinEdge);
            TebalPelat = Fmt.StdPlate(Math.Max(8, Math.Max(la.Thick, lb.Thick)));
            TepiPelat = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            Las = Math.Max(4, Fmt.MinFillet(Math.Min(TebalPelat, Math.Min(la.Thick, lb.Thick))));
            Las = Math.Min(Las, Math.Max(la.Thick, TebalPelat));
            PanjangLas = Fmt.Round10(Math.Max(2 * w, 100));
        }

        static Vec3 Perp(Vec3 u) => new Vec3(-u.Y, u.X, 0);

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var pa = Prof(ProfilA, "Batang A"); var pb = Prof(ProfilB, "Batang B");
            var la = Leg(pa, MukaA); var lb = Leg(pb, MukaB);
            var bd = BoltData.Of(Baut);
            double ang = SudutB * Math.PI / 180;
            var uA = Vec3.UnitX; var uB = new Vec3(Math.Cos(ang), Math.Sin(ang), 0);
            double heelA = double.IsNaN(HeelA) ? la.Heel : HeelA, heelB = double.IsNaN(HeelB) ? lb.Heel : HeelB;
            double dirA = ArahA >= 0 ? 1 : -1, dirB = ArahB >= 0 ? 1 : -1;
            double a0 = Math.Min(heelA, heelA + dirA * la.Width), a1 = Math.Max(heelA, heelA + dirA * la.Width);
            double b0 = Math.Min(heelB, heelB + dirB * lb.Width), b1 = Math.Max(heelB, heelB + dirB * lb.Width);
            double mA = Math.Max(Math.Abs(a0), Math.Abs(a1)), mB = Math.Max(Math.Abs(b0), Math.Abs(b1));
            double between = Math.Acos(Math.Max(-1, Math.Min(1, uA.Dot(uB))));
            double dA, dB;
            if (MenerusA)
            {
                dA = double.NaN;
                double s = Math.Sin(between), t = Math.Tan(between);
                dB = (mA + Celah) / Math.Max(s, 0.1) + (Math.Abs(t) > 1e-6 && Math.Abs(t) < 1e6 ? mB / Math.Abs(t) : 0);
            }
            else
            {
                double half = between / 2;
                dA = dB = between > Math.PI - 1e-3 ? Celah / 2 : (Math.Max(mA, mB) + Celah / 2) / Math.Tan(half);
            }
            dB = Fmt.Round5(dB);
            if (!double.IsNaN(dA)) dA = Fmt.Round5(dA);
            bool bolt = Metode == GussetMethod.Baut;
            double tg = TebalPelat, eT = TepiPelat;
            var hull = new List<Vec2> { new Vec2(0, 0) };
            double len = 0; int nb = 0;
            var members = new List<(Profile p, LegFace lf, Vec3 u, double d, double heel, double dir, double s0, double s1, double gauge, string name)>();
            if (MenerusA)
            {
                double reach = Math.Max(mB / Math.Max(Math.Sin(between), 0.2) + (bolt ? (JumlahBaut - 1) * JarakBaut / 2 + JarakUjung : PanjangLas / 2) + eT, 150);
                members.Add((pa, la, uA, double.NaN, heelA, dirA, -reach, reach, GaugeA, "A"));
            }
            else members.Add((pa, la, uA, dA, heelA, dirA, dA, 0, GaugeA, "A"));
            members.Add((pb, lb, uB, dB, heelB, dirB, dB, 0, GaugeB, "B"));

            foreach (var m in members)
            {
                var perp = Perp(m.u);
                double line = m.heel + m.dir * m.gauge;
                double lo = Math.Min(m.heel, m.heel + m.dir * m.lf.Width), hi = Math.Max(m.heel, m.heel + m.dir * m.lf.Width);
                double sStart, sEnd;
                var stations = new List<double>();
                if (double.IsNaN(m.d))
                {
                    if (bolt) for (int k = 0; k < JumlahBaut; k++) stations.Add((k - (JumlahBaut - 1) / 2.0) * JarakBaut);
                    sStart = bolt ? stations.Min() - JarakUjung : -PanjangLas / 2;
                    sEnd = bolt ? stations.Max() + JarakUjung : PanjangLas / 2;
                }
                else
                {
                    if (bolt) for (int k = 0; k < JumlahBaut; k++) stations.Add(m.d + JarakUjung + k * JarakBaut);
                    sStart = m.d;
                    sEnd = bolt ? stations.Max() + JarakUjung : m.d + PanjangLas;
                }
                double t0 = bolt ? Math.Min(lo, line - eT) : lo, t1 = bolt ? Math.Max(hi, line + eT) : hi;
                foreach (var s in new[] { sStart, sEnd })
                    foreach (var tt in new[] { t0, t1 })
                    {
                        var q = m.u * s + perp * tt;
                        hull.Add(new Vec2(q.X, q.Y));
                    }
                if (bolt)
                    foreach (var s in stations)
                    {
                        var q = m.u * s + perp * line;
                        len = r.Bolt(new Vec3(q.X, q.Y, m.lf.Thick), -Vec3.UnitZ, m.lf.Thick + tg, bd);
                        nb++;
                    }
                // potongan batang: kaki di bidang pelat + kaki tegak (siku)
                double stubS0 = double.IsNaN(m.d) ? m.s0 : m.d, stubS1 = double.IsNaN(m.d) ? m.s1 : Math.Max(m.d + 4 * m.lf.Width, sEnd + 100);
                var fm = new Frame(Vec3.Zero, m.u, perp, Vec3.UnitZ);
                r.Prism(PartKind.Batang, m.p.Name, fm, Shapes.Rect(stubS0, lo, stubS1, hi), 0, m.lf.Thick);
                if (m.lf.Upstand > 0)
                {
                    double h0 = m.heel, h1 = m.heel + m.dir * m.lf.Thick;
                    r.Prism(PartKind.Batang, m.p.Name, fm, Shapes.Rect(stubS0, Math.Min(h0, h1), stubS1, Math.Max(h0, h1)), 0, m.lf.Upstand);
                }

                // ---- cek per batang
                if (bolt)
                {
                    EdgeRule(r, "Batang " + m.name + ": baut ke ujung batang", JarakUjung, bd);
                    if (JumlahBaut > 1) PitchRule(r, "Batang " + m.name, JarakBaut, bd);
                    double toe = m.lf.Width - m.gauge;
                    EdgeRule(r, "Batang " + m.name + ": baut ke tepi kaki", toe, bd);
                    if (m.lf.Upstand > 0)
                        r.Rule(m.gauge >= m.lf.Thick + bd.WasherOD / 2 - 1e-6, "Batang " + m.name + ": ring baut bebas dari kaki tegak (gauge " + Fmt.N(m.gauge) + " ≥ " + Fmt.N(m.lf.Thick + bd.WasherOD / 2) + ")", "ruang kunci, praktik umum");
                }
            }
            var gpts = Projection.Hull(hull);
            var gusset = r.Prism(PartKind.Pelat, "pelat buhul", new Frame(), Shapes.Filleted(gpts, null), -tg, 0);
            var gp = SectionCalc.Compute(new SectionShape { Outer = gusset.Outline });
            string glab = "PL t=" + Fmt.N(tg) + " (pelat buhul, ±" + Fmt.N(gp.Width) + "x" + Fmt.N(gp.Height) + ")";
            r.AddBoq("Pelat", glab, 1, gusset.MassKg());
            if (bolt) r.AddBoq("Baut", "Baut " + Baut + "x" + Fmt.N(len) + " " + Grades.Label(Mutu) + " + mur + 2 ring", nb, BoltKg(bd, len));

            if (!double.IsNaN(dA)) r.TrimSupport = uA * dA + Vec3.UnitZ * la.Offset;
            r.TrimSupported = uB * dB + Vec3.UnitZ * lb.Offset;
            if (Math.Abs(SelisihMuka) > 0.05 && Math.Abs(SelisihMuka) < 900) r.ShiftMembers[1] = Vec3.UnitZ * SelisihMuka;

            // ---- cek umum
            r.Rule(between >= 15 * Math.PI / 180 - 1e-9, "Sudut antar batang " + Fmt.N(between * 180 / Math.PI) + "° ≥ 15°", "geometri");
            r.Rule(Math.Abs(SelisihMuka) <= 100, Math.Abs(SelisihMuka) <= 0.05 ? "Muka kaki kedua batang sebidang" : "Batang B digeser sejajar " + Fmt.N(SelisihMuka) + " mm supaya kakinya menempel pelat",
                Math.Abs(SelisihMuka) > 900 ? "kaki B menghadap sisi lain: putar batang B" : "geometri");
            if (bolt)
            {
                r.Rule(la.Boltable && lb.Boltable, "Batang terbuka (bisa dibaut)", "hollow/pipa: pakai metode Las");
                r.Rule(JumlahBaut >= 2, "Minimal 2 baut per batang (" + JumlahBaut + ")", "praktik umum");
            }
            WeldRule(r, Metode == GussetMethod.Las ? "Las batang ke pelat buhul" : "Las (bila ada) batang ke pelat buhul", Las, Math.Min(la.Thick, lb.Thick), tg);
            if (Metode == GussetMethod.Las) r.Rule(PanjangLas >= Math.Max(4 * Las, 40) - 1e-6, "Panjang las " + Fmt.N(PanjangLas) + " ≥ maks(4a, 40)", "panjang las minimum, praktik umum AISC 360 J2.2b");

            // ---- gambar detail
            r.Views.Add(new ViewSpec("DENAH PELAT BUHUL", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitX, Vec3.UnitZ));
            double off = 0.12 * Math.Max(gp.Width, gp.Height) + 15;
            foreach (var m in members)
            {
                if (!bolt) continue;
                var perp = Perp(m.u);
                double line = m.heel + m.dir * m.gauge;
                double side = Math.Max(m.heel, m.heel + m.dir * m.lf.Width) + 5;
                var chain = new List<double>();
                if (!double.IsNaN(m.d)) chain.Add(m.d);
                for (int k = 0; k < JumlahBaut; k++) chain.Add(double.IsNaN(m.d) ? (k - (JumlahBaut - 1) / 2.0) * JarakBaut : m.d + JarakUjung + k * JarakBaut);
                for (int i = 1; i < chain.Count; i++)
                    r.Dim(0, m.u * chain[i - 1] + perp * side, m.u * chain[i] + perp * side, perp, off);
                r.Note(0, m.u * chain.Last() + perp * line, "Batang " + m.name + ": " + m.p.Name + ", " + JumlahBaut + " baut " + Baut + ", gauge " + Fmt.N(m.gauge), 2, 2);
            }
            r.Note(0, new Vec3(0, 0, 0), glab + (bolt ? "" : "\\PLas sudut a=" + Fmt.N(Las) + " sepanjang " + Fmt.N(PanjangLas) + " kedua sisi kaki"), -4, -3);
            r.Dim(1, new Vec3(0, 0, -tg), new Vec3(0, 0, 0), -Vec3.UnitX, off);
            return r;
        }
    }

    public class ProfilesLight : ProfileListBase { protected override Family[] Families => new[] { Family.L, Family.PL, Family.CNP, Family.HOLLOW, Family.CHS }; }
}
