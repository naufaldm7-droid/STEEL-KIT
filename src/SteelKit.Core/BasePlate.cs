using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    public enum AnchorPattern { Dalam4, Luar4, Luar6, Luar8 }
    public enum BaseSupport { Beton, Baja }
    /// <summary>Pola pengaku pelat dasar: segaris badan (2), segaris sayap (4), atau keduanya (6).</summary>
    public enum StiffenerLayout { Tidak, Badan2, Sayap4, BadanSayap6 }

    /// <summary>
    /// Pelat dasar kolom + baut angkur lengkap. Frame lokal: asal = as kolom di muka atas pelat dasar
    /// (= titik bawah kolom yang digambar), X = arah lebar sayap kolom, Y = arah tinggi profil, Z = ke atas.
    /// </summary>
    public class BasePlate : Connection
    {
        public override ConnType Type => ConnType.PelatDasar;
        public override string Title => "Pelat dasar kolom + angkur";

        [Category("1. Profil"), DisplayName("Kolom"), TypeConverter(typeof(ProfilesColumn)), Description("Profil kolom (WF, H, hollow, pipa).")]
        public string Kolom { get; set; } = "H300x300x10x15";

        [Category("2. Angkur"), DisplayName("Pola angkur"), Description("Dalam4 = 4 angkur di antara sayap (sendi). Luar4/6/8 = di luar sayap (jepit). Hollow/pipa: Luar4 di sudut.")]
        public AnchorPattern Pola { get; set; } = AnchorPattern.Luar4;
        [Category("2. Angkur"), DisplayName("Diameter angkur")]
        public BoltSize Angkur { get; set; } = BoltSize.M24;
        [Category("2. Angkur"), DisplayName("Tipe angkur")]
        public AnchorType TipeAngkur { get; set; } = AnchorType.L_Kait;
        [Category("2. Angkur"), DisplayName("Mutu angkur"), Description("Teks bebas untuk label, mis. ASTM F1554 Gr.36 atau Gr.55.")]
        public string MutuAngkur { get; set; } = "ASTM F1554 Gr.36";
        [Category("2. Angkur"), DisplayName("Jarak angkur arah X (mm)"), Description("As ke as angkur terluar searah lebar sayap.")]
        public double JarakX { get; set; }
        [Category("2. Angkur"), DisplayName("Jarak angkur arah Y (mm)"), Description("As ke as baris angkur searah tinggi profil.")]
        public double JarakY { get; set; }
        [Category("2. Angkur"), DisplayName("Kedalaman tanam hef (mm)"), Description("Dari muka beton. Nilai bawaan praktik umum; tetapkan dari hitungan ACI 318 Bab 17 / data pabrik.")]
        public double Tanam { get; set; }
        [Category("2. Angkur"), DisplayName("Panjang kait (mm)")]
        public double Kait { get; set; }
        [Category("2. Angkur"), DisplayName("Mur perata di bawah pelat")]
        public bool MurPerata { get; set; } = true;
        [Category("2. Angkur"), DisplayName("Mur ganda di atas")]
        public bool MurGanda { get; set; } = true;

        [Category("3. Pelat dasar"), DisplayName("Lebar pelat X (mm)")]
        public double LebarPelat { get; set; }
        [Category("3. Pelat dasar"), DisplayName("Panjang pelat Y (mm)")]
        public double PanjangPelat { get; set; }
        [Category("3. Pelat dasar"), DisplayName("Tebal pelat (mm)")]
        public double TebalPelat { get; set; }
        [Category("3. Pelat dasar"), DisplayName("Las kolom ke pelat a (mm)")]
        public double Las { get; set; }

        [Category("4. Grout & beton"), DisplayName("Dudukan"), Description("Beton = angkur + grout. Baja = dibaut ke sayap balok baja (tiang mezanin, railing, kolom di atas balok).")]
        public BaseSupport Dudukan { get; set; } = BaseSupport.Beton;
        [Category("4. Grout & beton"), DisplayName("Tebal sayap balok dudukan (mm)")]
        public double TebalSayapDudukan { get; set; } = 13;
        [Category("4. Grout & beton"), DisplayName("Lebar sayap balok dudukan (mm)")]
        public double LebarSayapDudukan { get; set; } = 200;
        [Category("4. Grout & beton"), DisplayName("Balok dudukan searah X pelat")]
        public bool DudukanSearahX { get; set; } = true;
        [Category("4. Grout & beton"), DisplayName("Tebal grout (mm)")]
        public double Grout { get; set; } = 50;
        [Category("4. Grout & beton"), DisplayName("Gambar pedestal beton")]
        public bool Pedestal { get; set; } = true;
        [Category("4. Grout & beton"), DisplayName("Tepi pedestal dari pelat (mm)")]
        public double TepiPedestal { get; set; }
        [Category("4. Grout & beton"), DisplayName("Tinggi pedestal digambar (mm)")]
        public double TinggiPedestal { get; set; } = 400;

        [Category("5. Tambahan"), DisplayName("Pola pengaku"),
         Description("Badan2 = 2 pelat segaris badan di luar sayap. Sayap4 = 4 pelat segaris sayap di luar ujung sayap. BadanSayap6 = keduanya. " +
                     "Dipakai bila pelat dasar lebih lebar dari kolom (angkur di luar sayap).")]
        public StiffenerLayout PolaPengaku { get; set; } = StiffenerLayout.Tidak;
        /// <summary>Pengaturan lama (versi awal): true = Badan2. Disimpan supaya sambungan lama tetap terbaca.</summary>
        [Browsable(false)] public bool Pengaku { get; set; }
        [Category("5. Tambahan"), DisplayName("Tebal pengaku (mm)")]
        public double TebalPengaku { get; set; }
        [Category("5. Tambahan"), DisplayName("Tinggi pengaku (mm)")]
        public double TinggiPengaku { get; set; }
        [Category("6. Pengaku badan kolom"), DisplayName("Pengaku tiap (mm)"),
         Description("Pasangan pelat pengaku di kedua sisi badan kolom, setiap jarak ini dari pelat dasar sampai ujung kolom. 0 = tidak ada. Sudut disnipe ke radius profil.")]
        public double JarakPengakuKolom { get; set; }
        [Category("6. Pengaku badan kolom"), DisplayName("Tebal pengaku badan (mm)")] public double TebalPengakuKolom { get; set; }
        [Category("6. Pengaku badan kolom"), DisplayName("Panjang kolom (mm)"), Description("Diisi otomatis dari batang kolom saat sambung otomatis.")]
        public double PanjangKolom { get; set; } = 6000;

        [Category("5. Tambahan"), DisplayName("Shear key")]
        public bool ShearKey { get; set; }
        [Category("5. Tambahan"), DisplayName("Shear key: tinggi x panjang x tebal")]
        public string ShearKeyUkuran { get; set; } = "100x200x20";

        Profile Col => Prof(Kolom, "Kolom");
        bool Tube => Col.Family == Family.HOLLOW || Col.Family == Family.CHS;
        double Tf => Col.Family == Family.HOLLOW || Col.Family == Family.CHS ? Col["t"] : Col["tf"] > 0 ? Col["tf"] : Col["t"];
        double Tw => Col["tw"] > 0 ? Col["tw"] : Col["t"];

        double ClearC(BoltData b) => Fmt.Round5(b.PlateWasherSize / 2 + Las + 10);
        double EdgeE(BoltData b) => Fmt.Round5(Math.Max(1.5 * b.D, b.MinEdge));

        int PerRow => Pola == AnchorPattern.Luar6 ? 3 : Pola == AnchorPattern.Luar8 ? 4 : 2;

        public override void Recommend()
        {
            var c = Col; var b = BoltData.Of(Angkur);
            double H = c.Extent(false), B = c.Extent(true);
            if (Tube && Pola == AnchorPattern.Dalam4) Pola = AnchorPattern.Luar4;
            TebalPelat = Fmt.StdPlate(Math.Max(Math.Max(16, 1.5 * Tf), b.D));
            Las = Math.Max(6, Fmt.MinFillet(Math.Min(TebalPelat, Tf)));
            double cc = ClearC(b), e = EdgeE(b), sMin = 4 * b.D;
            if (c.Family == Family.CHS)
            {
                JarakX = JarakY = Fmt.Round5(Math.Sqrt(2) * (B / 2 + cc));
            }
            else if (Pola == AnchorPattern.Dalam4)
            {
                JarakY = Math.Floor((H - 2 * Tf - b.PlateWasherSize - 20) / 5) * 5;
                JarakX = Fmt.Round5(Math.Max(Tw + 2 * (b.PlateWasherSize / 2 + Las + 5), 0.5 * B));
            }
            else
            {
                JarakY = Fmt.Round5(H + 2 * cc);
                JarakX = Fmt.Round5(Math.Max(B + 50 - 2 * e, sMin * (PerRow - 1)));
                if (Tube) JarakX = Fmt.Round5(Math.Max(B + 2 * cc, JarakX));
            }
            if (Dudukan == BaseSupport.Baja)
            {
                // baut harus masuk lebar sayap balok dudukan (arah melintang balok)
                double gMax = Math.Floor((LebarSayapDudukan - 2 * b.MinEdge) / 5) * 5;
                if (DudukanSearahX) JarakY = Math.Min(JarakY, gMax); else JarakX = Math.Min(JarakX, gMax);
                Tanam = 0; Kait = 0;
            }
            LebarPelat = Fmt.Round10(Math.Max(JarakX + 2 * e, B + 50));
            PanjangPelat = Fmt.Round10(Math.Max(JarakY + 2 * e, H + 50));
            Tanam = Grades.DefaultEmbed(TipeAngkur, b.D);
            Kait = Grades.DefaultHook(TipeAngkur, b.D);
            TepiPedestal = Fmt.Round10(Math.Max(100, 6 * b.D - e));
            TebalPengaku = Fmt.StdPlate(Math.Max(10, Tw));
            TebalPengakuKolom = Fmt.StdPlate(Math.Max(8, Tw));
            TinggiPengaku = Fmt.Round10(Math.Max(150, 1.5 * (PanjangPelat - H) / 2));
            ShearKeyUkuran = "100x" + Fmt.N(Fmt.Round10(0.6 * B)) + "x20";
        }

        public List<Vec2> AnchorPositions()
        {
            double gx = JarakX, gy = JarakY;
            var xs = new List<double>();
            switch (Pola)
            {
                case AnchorPattern.Luar6: xs.AddRange(new[] { -gx / 2, 0, gx / 2 }); break;
                case AnchorPattern.Luar8: xs.AddRange(new[] { -gx / 2, -gx / 6, gx / 6, gx / 2 }); break;
                default: xs.AddRange(new[] { -gx / 2, gx / 2 }); break;
            }
            return xs.SelectMany(x => new[] { new Vec2(x, -gy / 2), new Vec2(x, gy / 2) }).ToList();
        }

        ConnResult BuildOnSteel(ConnResult r, Profile c, BoltData b, List<Vec2> pos, string plateLabel)
        {
            double tp = TebalPelat, tf = TebalSayapDudukan, len = 0;
            r.TrimSupport = Vec3.Zero;          // kaki tiang dipotong rata di muka atas pelat dasar
            foreach (var p in pos) len = r.Bolt(new Vec3(p.X, p.Y, 0), -Vec3.UnitZ, tp + tf, b);
            BoltBoq(r, Angkur, BoltGrade.Kelas8_8, len, pos.Count, "ke sayap balok");
            double far = 2 * Math.Max(LebarPelat, PanjangPelat);
            if (DudukanSearahX) r.Box(PartKind.Batang, "sayap balok", -far, far, -LebarSayapDudukan / 2, LebarSayapDudukan / 2, -tp - tf, -tp);
            else r.Box(PartKind.Batang, "sayap balok", -LebarSayapDudukan / 2, LebarSayapDudukan / 2, -far, far, -tp - tf, -tp);
            double e = Math.Min((LebarPelat - JarakX) / 2, (PanjangPelat - JarakY) / 2);
            EdgeRule(r, "Baut ke tepi pelat", e, b);
            double trans = DudukanSearahX ? pos.Max(p => Math.Abs(p.Y)) : pos.Max(p => Math.Abs(p.X));
            EdgeRule(r, "Baut ke tepi sayap balok dudukan", LebarSayapDudukan / 2 - trans, b);
            PitchRule(r, "Baut", Math.Min(JarakX / Math.Max(PerRow - 1, 1), JarakY), b);
            WeldRule(r, "Kolom ke pelat dasar", Las, Tf, tp);
            r.Views.Add(new ViewSpec("DENAH PELAT DASAR", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("TAMPAK PELAT DASAR", Vec3.UnitX, Vec3.UnitZ));
            r.Note(0, new Vec3(LebarPelat / 2, PanjangPelat / 2, 0), plateLabel + "\\P" + pos.Count + " baut " + Angkur + " ke sayap balok", 1, 1);
            return r;
        }

        double[] KeySize()
        {
            var p = (ShearKeyUkuran ?? "").ToLowerInvariant().Split('x').Select(s => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0).ToArray();
            return p.Length == 3 && p.All(v => v > 0) ? p : new double[] { 100, 200, 20 };
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var c = Col; var b = BoltData.Of(Angkur);
            double H = c.Extent(false), B = c.Extent(true), d = b.D;
            double Bp = LebarPelat, Lp = PanjangPelat, tp = TebalPelat, g = Grout;
            double zc = -tp - g, zb = zc - Tanam;
            double wpS = b.PlateWasherSize, wpT = b.PlateWasherT;
            int nutsTop = MurGanda ? 2 : 1;
            double ztop = wpT + nutsTop * b.Nut + 3 * b.Pitch;
            var pos = AnchorPositions();
            string plateLabel = Pl(Bp, Lp, tp);

            // pelat dasar dengan lubang angkur
            var plate = r.Box(PartKind.Pelat, plateLabel, -Bp / 2, Bp / 2, -Lp / 2, Lp / 2, -tp, 0);
            foreach (var p in pos) plate.Holes.Add(Shapes.Circle(p.X, p.Y, b.AnchorHole / 2));
            r.AddBoq("Pelat", plateLabel + " (pelat dasar)", 1, plate.MassKg());

            // potongan kolom untuk gambar detail
            Stub(r, c, new Frame(), 0, Math.Max(1.2 * H, 300));

            if (Dudukan == BaseSupport.Baja) return BuildOnSteel(r, c, b, pos, plateLabel);
            // grout dan pedestal
            r.Box(PartKind.Grout, "grout", -Bp / 2, Bp / 2, -Lp / 2, Lp / 2, zc, -tp);
            double pe = TepiPedestal;
            if (Pedestal) r.Box(PartKind.Beton, "pedestal", -Bp / 2 - pe, Bp / 2 + pe, -Lp / 2 - pe, Lp / 2 + pe, zc - TinggiPedestal, zc);

            // angkur
            double hook = TipeAngkur == AnchorType.L_Kait || TipeAngkur == AnchorType.J_Kait ? Kait : 0;
            int nutsPerAnchor = nutsTop + (MurPerata ? 1 : 0) + (TipeAngkur == AnchorType.Berkepala ? 1 : TipeAngkur == AnchorType.UlirPelatBawah ? 2 : 0);
            foreach (var p in pos)
            {
                r.Cylinder(PartKind.Angkur, "", new Vec3(p.X, p.Y, zb), Vec3.UnitZ, d / 2, ztop - zb);
                var inward = new Vec3(-p.X, -p.Y, 0);
                inward = inward.Length < 1e-6 ? Vec3.UnitX : inward.Unit;
                switch (TipeAngkur)
                {
                    case AnchorType.L_Kait:
                        r.Cylinder(PartKind.Angkur, "", new Vec3(p.X, p.Y, zb + d / 2), inward, d / 2, hook);
                        break;
                    case AnchorType.J_Kait:
                        double run = Fmt.Round5(3 * d);
                        r.Cylinder(PartKind.Angkur, "", new Vec3(p.X, p.Y, zb + d / 2), inward, d / 2, run);
                        r.Cylinder(PartKind.Angkur, "", new Vec3(p.X, p.Y, zb) + inward * run, Vec3.UnitZ, d / 2, hook);
                        break;
                    case AnchorType.Berkepala:
                        r.Hex(PartKind.Mur, "", new Vec3(p.X, p.Y, zb - b.Head), Vec3.UnitZ, b.Across, b.Head);
                        break;
                    case AnchorType.UlirPelatBawah:
                        double s2 = Fmt.Round5(2.5 * d) / 2;
                        r.Hex(PartKind.Mur, "", new Vec3(p.X, p.Y, zb - b.Nut), Vec3.UnitZ, b.Across, b.Nut);
                        r.Box(PartKind.RingPlat, "", p.X - s2, p.X + s2, p.Y - s2, p.Y + s2, zb, zb + 10);
                        r.Hex(PartKind.Mur, "", new Vec3(p.X, p.Y, zb + 10), Vec3.UnitZ, b.Across, b.Nut);
                        break;
                }
                if (MurPerata) r.Hex(PartKind.Mur, "", new Vec3(p.X, p.Y, -tp - b.Nut), Vec3.UnitZ, b.Across, b.Nut);
                var wp = r.Box(PartKind.RingPlat, "", p.X - wpS / 2, p.X + wpS / 2, p.Y - wpS / 2, p.Y + wpS / 2, 0, wpT);
                wp.Holes.Add(Shapes.Circle(p.X, p.Y, (d + 2) / 2));
                for (int i = 0; i < nutsTop; i++) r.Hex(PartKind.Mur, "", new Vec3(p.X, p.Y, wpT + i * b.Nut), Vec3.UnitZ, b.Across, b.Nut);
            }
            double anchorLen = Fmt.Round10(ztop - zb + hook + (TipeAngkur == AnchorType.J_Kait ? Fmt.Round5(3 * d) : 0));
            double ulir = Fmt.Round10(ztop + tp + g + 25);
            string aSpec = "Angkur " + Angkur + " L=" + Fmt.N(anchorLen) + " (" + Grades.Label(TipeAngkur) + ", " + MutuAngkur + ", ulir " + Fmt.N(ulir) + ")";
            r.AddBoq("Angkur", aSpec, pos.Count, Math.PI * d * d / 4 * anchorLen * 7.85e-6);
            r.AddBoq("Mur", "Mur " + Angkur, pos.Count * nutsPerAnchor, 0);
            r.AddBoq("Ring plat", Pl(wpS, wpS, wpT) + " lubang Ø" + Fmt.N(d + 2), pos.Count, wpS * wpS * wpT * 7.85e-6);
            if (TipeAngkur == AnchorType.UlirPelatBawah) r.AddBoq("Pelat", Pl(Fmt.Round5(2.5 * d), Fmt.Round5(2.5 * d), 10) + " (pelat bawah angkur)", pos.Count, Math.Pow(Fmt.Round5(2.5 * d), 2) * 10 * 7.85e-6);

            // pengaku pelat dasar: bentuk trapesium (tinggi penuh di muka kolom, 35% di tepi pelat), sudut bawah dalam disnipe 15 mm untuk las
            var layout = PolaPengaku == StiffenerLayout.Tidak && Pengaku ? StiffenerLayout.Badan2 : PolaPengaku;
            if (layout != StiffenerLayout.Tidak && !Tube)
            {
                double ts = TebalPengaku, hs = TinggiPengaku, sn = 15;
                List<Vec2> Trap(double a, double b2, int s) => new List<Vec2>
                    { new Vec2(s * a, sn), new Vec2(s * (a + sn), 0), new Vec2(s * b2, 0), new Vec2(s * b2, 0.35 * hs), new Vec2(s * a, hs) };
                if ((layout == StiffenerLayout.Badan2 || layout == StiffenerLayout.BadanSayap6) && Lp > H + 1)
                {
                    var fr = new Frame(Vec3.Zero, Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX);   // bidang (y, z), tebal arah x, di as badan
                    foreach (int s in new[] { 1, -1 })
                    {
                        var part = r.Prism(PartKind.Pengaku, "", fr, Shapes.Filleted(Trap(H / 2, Lp / 2, s), null), -ts / 2, ts / 2);
                        r.AddBoq("Pelat", Pl((Lp - H) / 2, hs, ts) + " (pengaku pelat dasar)", 1, part.MassKg());
                    }
                }
                if ((layout == StiffenerLayout.Sayap4 || layout == StiffenerLayout.BadanSayap6) && Bp > B + 1)
                {
                    var fr = new Frame(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY);  // bidang (x, z), tebal arah y, segaris sayap
                    foreach (int sy in new[] { 1, -1 })
                        foreach (int s in new[] { 1, -1 })
                        {
                            double yc = sy * (H / 2 - Tf / 2);
                            var part = r.Prism(PartKind.Pengaku, "", fr, Shapes.Filleted(Trap(B / 2, Bp / 2, s), null), -yc - ts / 2, -yc + ts / 2);
                            r.AddBoq("Pelat", Pl((Bp - B) / 2, hs, ts) + " (pengaku pelat dasar)", 1, part.MassKg());
                        }
                    // ring plat & kunci angkur tidak boleh menabrak pengaku segaris sayap
                    double clear = pos.Count == 0 ? double.MaxValue : pos.Min(q => Math.Abs(Math.Abs(q.Y) - (H / 2 - Tf / 2))) - ts / 2;
                    r.Rule(clear >= wpS / 2 + 5 - 1e-6, "Ring plat angkur bebas dari pengaku sayap (" + Fmt.N(clear) + " ≥ " + Fmt.N(wpS / 2 + 5) + " mm)", "ruang kunci, praktik umum");
                }
                if (layout == StiffenerLayout.Badan2 || layout == StiffenerLayout.BadanSayap6)
                {
                    double clear = pos.Count == 0 ? double.MaxValue : pos.Where(q => Math.Abs(q.Y) > H / 2).Select(q => Math.Abs(q.X) - ts / 2).DefaultIfEmpty(double.MaxValue).Min();
                    r.Rule(clear >= wpS / 2 + 5 - 1e-6, "Ring plat angkur bebas dari pengaku badan (" + Fmt.N(clear) + " ≥ " + Fmt.N(wpS / 2 + 5) + " mm)", "ruang kunci, praktik umum");
                }
                r.Rule(hs >= (layout == StiffenerLayout.Sayap4 ? Bp - B : Lp - H) / 2 * 0.8 - 1e-6 || hs >= 150, "Tinggi pengaku " + Fmt.N(hs) + " mm cukup terhadap tonjolan pelat", "praktik umum");
            }
            // pengaku badan kolom berjarak (sepanjang kolom), kedua sisi badan, sudut disnipe ke radius profil
            if (JarakPengakuKolom > 0 && !Tube && (c.Family == Family.WF || c.Family == Family.H))
            {
                double ts = TebalPengakuKolom > 0 ? TebalPengakuKolom : Tw, yi = H / 2 - Tf, xo = B / 2 - 1, sn = Math.Max(c["r"], 0) + 5;
                int nLev = 0;
                for (double z = JarakPengakuKolom; z <= PanjangKolom - 0.5 * JarakPengakuKolom + 1e-6 && nLev < 40; z += JarakPengakuKolom, nLev++)
                    foreach (int sd in new[] { 1, -1 })
                    {
                        double xi = Tw / 2;
                        var pts = new List<Vec2> { new Vec2(xi, -yi + sn), new Vec2(xi + sn, -yi), new Vec2(xo, -yi), new Vec2(xo, yi), new Vec2(xi + sn, yi), new Vec2(xi, yi - sn) };
                        if (sd < 0) pts = pts.Select(q => new Vec2(-q.X, q.Y)).Reverse().ToList();
                        var st = r.Prism(PartKind.Pengaku, "", new Frame(), Shapes.Filleted(pts, null), z - ts / 2, z + ts / 2);
                        r.AddBoq("Pelat", Pl(xo - Tw / 2, 2 * yi, ts) + " (pengaku badan kolom)", 1, st.MassKg());
                    }
                if (nLev > 0)
                {
                    WeldBoq(r, Math.Max(5, Fmt.MinFillet(ts)), nLev * 2 * 2 * (2 * yi + (xo - Tw / 2)), "pengaku badan kolom, dua sisi");
                    r.Rule(JarakPengakuKolom >= 300 - 1e-6, "Jarak pengaku badan kolom " + Fmt.N(JarakPengakuKolom) + " ≥ 300 mm (ruang las)", "praktik umum");
                }
            }
            if (ShearKey)
            {
                var k = KeySize();
                var sk = r.Box(PartKind.Pelat, "", -k[2] / 2, k[2] / 2, -k[1] / 2, k[1] / 2, -tp - k[0], -tp);
                r.AddBoq("Pelat", Pl(k[1], k[0], k[2]) + " (shear key)", 1, sk.MassKg());
            }
            r.AddBoq("Grout", "Grout non-susut t=" + Fmt.N(g) + " (" + (Bp * Lp * g * 1e-9).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m³)", 1, 0);

            // ---------------- cek aturan geometri
            double e = Math.Min((Bp - JarakX) / 2, (Lp - JarakY) / 2);
            if (Tube && Pola == AnchorPattern.Dalam4) r.Rule(false, "Pola Dalam4 tidak bisa untuk hollow/pipa", "pilih Luar4");
            var xs = pos.Select(p => p.X).Distinct().OrderBy(v => v).ToList();
            double sx = xs.Count > 1 ? xs.Zip(xs.Skip(1), (a1, a2) => a2 - a1).Min() : double.MaxValue;
            double sMin = 4 * d;
            r.Rule(sx >= sMin - 1e-6 && JarakY >= sMin - 1e-6, "Jarak antar angkur " + Fmt.N(Math.Min(sx, JarakY)) + " ≥ 4d = " + Fmt.N(sMin) + " mm",
                "jarak angkur minimum, praktik umum ACI 318 §17.9.2 (6d bila angkur dikencangkan)");
            r.Rule(e >= 1.5 * d - 1e-6, "Angkur ke tepi pelat " + Fmt.N(e) + " ≥ 1,5d = " + Fmt.N(1.5 * d) + " mm", "praktik umum (AISC Design Guide 1)");
            if (c.Family == Family.CHS)
            {
                double dist = Math.Sqrt(JarakX * JarakX + JarakY * JarakY) / 2 - B / 2;
                r.Rule(dist >= wpS / 2 + Las + 5 - 1e-6, "Ruang ring plat & kunci ke dinding pipa " + Fmt.N(dist) + " ≥ " + Fmt.N(wpS / 2 + Las + 5) + " mm", "praktik umum");
            }
            else if (Pola == AnchorPattern.Dalam4)
            {
                r.Rule(JarakY / 2 <= H / 2 - Tf - wpS / 2 - 5 + 1e-6, "Angkur muat di antara sayap (Y/2=" + Fmt.N(JarakY / 2) + " ≤ " + Fmt.N(H / 2 - Tf - wpS / 2 - 5) + ")", "ruang ring plat & kunci, praktik umum");
                r.Rule(JarakX / 2 >= Tw / 2 + Las + wpS / 2 + 5 - 1e-6, "Angkur bebas dari badan (X/2=" + Fmt.N(JarakX / 2) + " ≥ " + Fmt.N(Tw / 2 + Las + wpS / 2 + 5) + ")", "ruang ring plat & kunci, praktik umum");
            }
            else
            {
                double clr = Math.Min(JarakY / 2 - H / 2, Tube ? JarakX / 2 - B / 2 : double.MaxValue);
                r.Rule(clr >= wpS / 2 + Las + 5 - 1e-6, "Ruang ring plat & kunci ke muka kolom " + Fmt.N(clr) + " ≥ " + Fmt.N(wpS / 2 + Las + 5) + " mm", "praktik umum");
            }
            r.Rule(Bp >= B + 2 * Las - 1e-6 && Lp >= H + 2 * Las - 1e-6, "Pelat dasar menutup kolom + ruang las", "geometri");
            r.Rule(g >= 25 - 1e-6, "Tebal grout " + Fmt.N(g) + " ≥ 25 mm", "praktik umum grout non-susut");
            if (MurPerata) r.Rule(g >= b.Nut + 10 - 1e-6, "Mur perata muat di grout (" + Fmt.N(g) + " ≥ " + Fmt.N(b.Nut + 10) + ")", "praktik umum");
            r.Rule(Tanam >= 8 * d - 1e-6, "Kedalaman tanam " + Fmt.N(Tanam) + " ≥ 8d = " + Fmt.N(8 * d) + " (minimum praktik; tetap dihitung)", "praktik umum, wajib cek ACI 318 Bab 17");
            if (Pedestal) r.Rule(e + TepiPedestal >= 6 * d - 1e-6, "Angkur ke tepi beton " + Fmt.N(e + TepiPedestal) + " ≥ 6d = " + Fmt.N(6 * d) + " mm", "praktik umum ACI 318 §17.9.2");
            if (Pengaku && Pola == AnchorPattern.Luar6) r.Rule(false, "Angkur tengah pola Luar6 bertabrakan dengan pengaku di as badan", "geometri");
            if (Pengaku && Tube) r.Rule(false, "Pengaku as badan hanya untuk WF/H", "geometri");
            WeldRule(r, "Kolom ke pelat dasar", Las, Tf, tp);

            // ---------------- gambar detail
            r.Views.Add(new ViewSpec("DENAH PELAT DASAR", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("TAMPAK PELAT DASAR", Vec3.UnitX, Vec3.UnitZ));
            double off = 0.12 * Math.Max(Bp, Lp);
            r.Dim(0, new Vec3(-Bp / 2, -Lp / 2, 0), new Vec3(Bp / 2, -Lp / 2, 0), -Vec3.UnitY, off * 2);
            r.Dim(0, new Vec3(-JarakX / 2, -Lp / 2, 0), new Vec3(JarakX / 2, -Lp / 2, 0), -Vec3.UnitY, off);
            r.Dim(0, new Vec3(-Bp / 2, -Lp / 2, 0), new Vec3(-Bp / 2, Lp / 2, 0), -Vec3.UnitX, off * 2);
            r.Dim(0, new Vec3(-Bp / 2, -JarakY / 2, 0), new Vec3(-Bp / 2, JarakY / 2, 0), -Vec3.UnitX, off);
            r.Note(0, new Vec3(Bp / 2, Lp / 2, 0), plateLabel + "\\P" + pos.Count + " lubang Ø" + Fmt.N(b.AnchorHole), 1, 1);
            r.Note(0, new Vec3(pos[pos.Count - 1].X, pos[pos.Count - 1].Y, 0), pos.Count + " angkur " + Angkur + " - " + Grades.Label(TipeAngkur), 1.5, -2.5);
            double xr = Bp / 2;
            r.Dim(1, new Vec3(xr, 0, -tp), new Vec3(xr, 0, 0), Vec3.UnitX, off);
            r.Dim(1, new Vec3(xr, 0, zc), new Vec3(xr, 0, -tp), Vec3.UnitX, off);
            r.Dim(1, new Vec3(xr, 0, zb), new Vec3(xr, 0, zc), Vec3.UnitX, off * 2);
            r.Dim(1, new Vec3(JarakX / 2, 0, 0), new Vec3(JarakX / 2, 0, ztop), Vec3.UnitX, off * 2 + (Bp - JarakX) / 2);
            r.Note(1, new Vec3(-Bp / 2, 0, zc), "Grout non-susut t=" + Fmt.N(g), -6, -1.5);
            r.Note(1, new Vec3(-JarakX / 2, 0, zb), aSpec + "\\Phef=" + Fmt.N(Tanam) + (MurPerata ? ", mur perata" : "") + (MurGanda ? ", mur ganda" : ""), -6, -2);
            r.Note(1, new Vec3(-B / 2, 0, 0), "Las sudut a=" + Fmt.N(Las) + " keliling", -6, 3);
            return r;
        }
    }
}
