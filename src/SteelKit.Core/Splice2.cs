using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace SteelKit.Core
{
    public enum PurlinJoin { Tumpang, Selongsong }

    public class ProfilesUNP : ProfileListBase { protected override Family[] Families => new[] { Family.UNP, Family.CNP }; }
    public class ProfilesTread : ProfileListBase { protected override Family[] Families => new[] { Family.PL }; }

    // =========================================================================================
    /// <summary>
    /// Splice pelat sambung (cover plate) balok / kolom: pelat sayap luar + pelat badan dua sisi, baut di kedua batang;
    /// pelat pengisi bila tinggi profil berbeda. Frame: asal = tengah celah di as, X = arah batang B, Z = tinggi profil.
    /// </summary>
    public class CoverPlateSplice : Connection
    {
        public override ConnType Type => ConnType.SplicePelatSambung;
        public override string Title => "Splice pelat sambung (sayap + badan)";

        [Category("1. Profil"), DisplayName("Batang A"), TypeConverter(typeof(ProfilesIH))] public string ProfilA { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Batang B"), TypeConverter(typeof(ProfilesIH))] public string ProfilB { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Celah antar ujung (mm)")] public double Celah { get; set; } = 10;
        [Category("2. Sayap"), DisplayName("Tebal pelat sayap (mm)")] public double TebalSayap { get; set; }
        [Category("2. Sayap"), DisplayName("Lebar pelat sayap (mm)")] public double LebarSayap { get; set; }
        [Category("2. Sayap"), DisplayName("Baris baut per sisi")] public int BarisSayap { get; set; } = 2;
        [Category("2. Sayap"), DisplayName("Gauge (mm)")] public double Gauge { get; set; }
        [Category("3. Badan"), DisplayName("Tebal pelat badan (mm)")] public double TebalBadan { get; set; }
        [Category("3. Badan"), DisplayName("Tinggi pelat badan (mm)")] public double TinggiBadan { get; set; }
        [Category("3. Badan"), DisplayName("Baut tegak per kolom")] public int BarisBadan { get; set; } = 3;
        [Category("3. Badan"), DisplayName("Kolom baut per sisi")] public int KolomBadan { get; set; } = 1;
        [Category("4. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M20;
        [Category("4. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("4. Baut"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }

        public override void Recommend()
        {
            var a = Prof(ProfilA, "A"); var b = Prof(ProfilB, "B"); var bd = BoltData.Of(Baut);
            double H = Math.Min(Sec.H(a), Sec.H(b)), B = Math.Min(Sec.B(a), Sec.B(b));
            TebalSayap = Fmt.StdPlate(Math.Max(10, Math.Max(Sec.Tf(a), Sec.Tf(b))));
            LebarSayap = Fmt.Round10(B);
            Gauge = Fmt.Round10(Math.Min(Math.Max(Math.Max(Sec.Tw(a), Sec.Tw(b)) + bd.WasherOD + 30, 0.55 * B), B - 2 * bd.MinEdge));
            TebalBadan = Fmt.StdPlate(Math.Max(8, 0.75 * Math.Max(Sec.Tw(a), Sec.Tw(b))));
            double clear = Math.Min(Support.ClearDepth(a), Support.ClearDepth(b));
            TinggiBadan = Math.Floor((clear - 10) / 10) * 10;
            JarakBaut = Fmt.Round5(Math.Max(3 * bd.D, 70));
            BarisBadan = Math.Max(2, Math.Min(6, (int)Math.Floor((TinggiBadan - 2 * Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D))) / JarakBaut) + 1));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var pa = Prof(ProfilA, "A"); var pb = Prof(ProfilB, "B"); var bd = BoltData.Of(Baut);
            double Hmax = Math.Max(Sec.H(pa), Sec.H(pb)), tf = Math.Max(Sec.Tf(pa), Sec.Tf(pb)), tw = Math.Max(Sec.Tw(pa), Sec.Tw(pb));
            double e = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D)), p = JarakBaut, g2 = Celah / 2;
            double Lf = 2 * (e + (BarisSayap - 1) * p + e) + Celah, Lw = 2 * (e + (KolomBadan - 1) * p + e) + Celah;
            double tpf = TebalSayap, tpw = TebalBadan, bpf = LebarSayap, hw = TinggiBadan;
            int nf = 0, nw = 0; double lenF = 0, lenW = 0;
            foreach (int s in new[] { 1, -1 })
            {
                double z0 = s > 0 ? Hmax / 2 : -Hmax / 2 - tpf;
                var fp = r.Box(PartKind.Pelat, "", -Lf / 2, Lf / 2, -bpf / 2, bpf / 2, z0, z0 + tpf);
                r.AddBoq("Pelat", Pl(Lf, bpf, tpf) + " (pelat sambung sayap)", 1, fp.MassKg());
                foreach (int side in new[] { -1, 1 })
                {
                    var prof = side < 0 ? pa : pb;
                    double fill = (Hmax - Sec.H(prof)) / 2;
                    if (fill > 0.5)
                    {
                        double zf = s > 0 ? Sec.H(prof) / 2 : -Hmax / 2;
                        var fl = r.Box(PartKind.Pelat, "pengisi", side < 0 ? -Lf / 2 : g2, side < 0 ? -g2 : Lf / 2, -bpf / 2, bpf / 2, zf, zf + fill);
                        r.AddBoq("Pelat", Pl(Lf / 2 - g2, bpf, fill) + " (pengisi)", 1, fl.MassKg());
                    }
                    for (int k = 0; k < BarisSayap; k++)
                        foreach (var y in new[] { -Gauge / 2, Gauge / 2 })
                        {
                            double x = side * (g2 + e + k * p);
                            double grip = tpf + Sec.Tf(prof) + fill;
                            lenF = Math.Max(lenF, r.Bolt(new Vec3(x, y, s > 0 ? Hmax / 2 + tpf : -Hmax / 2 - tpf), s > 0 ? -Vec3.UnitZ : Vec3.UnitZ, grip, bd));
                            nf++;
                        }
                }
            }
            foreach (int s in new[] { 1, -1 })
            {
                var wp = r.Box(PartKind.Pelat, "", -Lw / 2, Lw / 2, s > 0 ? tw / 2 : -tw / 2 - tpw, s > 0 ? tw / 2 + tpw : -tw / 2, -hw / 2, hw / 2);
                r.AddBoq("Pelat", Pl(Lw, hw, tpw) + " (pelat sambung badan)", 1, wp.MassKg());
            }
            foreach (int side in new[] { -1, 1 })
                for (int c = 0; c < KolomBadan; c++)
                    for (int i = 0; i < BarisBadan; i++)
                    {
                        lenW = r.Bolt(new Vec3(side * (g2 + e + c * p), tw / 2 + tpw, (i - (BarisBadan - 1) / 2.0) * p), -Vec3.UnitY, 2 * tpw + tw, bd);
                        nw++;
                    }
            BoltBoq(r, Baut, Mutu, lenF, nf, "sayap");
            BoltBoq(r, Baut, Mutu, lenW, nw, "badan");
            double L = Math.Max(1.5 * Hmax, Lf);
            Stub(r, pa, new Frame(new Vec3(-g2, 0, 0), Vec3.UnitY * -1, Vec3.UnitZ, Vec3.UnitX * -1), 0, L);
            Stub(r, pb, new Frame(new Vec3(g2, 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, L);
            r.TrimSupport = new Vec3(-g2, 0, 0);
            r.TrimSupported = new Vec3(g2, 0, 0);

            EdgeRule(r, "Baut sayap ke ujung pelat", e, bd);
            EdgeRule(r, "Baut sayap ke tepi pelat", bpf / 2 - Gauge / 2, bd);
            EdgeRule(r, "Baut sayap ke tepi sayap profil", Math.Min(Sec.B(pa), Sec.B(pb)) / 2 - Gauge / 2, bd);
            EdgeRule(r, "Baut badan ke tepi pelat badan", hw / 2 - (BarisBadan - 1) * p / 2, bd);
            PitchRule(r, "Baut", p, bd);
            PitchRule(r, "Gauge sayap", Gauge, bd);
            r.Rule(hw <= Math.Min(Support.ClearDepth(pa), Support.ClearDepth(pb)) + 1e-6, "Pelat badan muat di tinggi bersih badan", "geometri");
            r.Rule(Math.Abs(Sec.B(pa) - Sec.B(pb)) <= 0.25 * Math.Max(Sec.B(pa), Sec.B(pb)), "Lebar sayap kedua profil tidak jauh berbeda", "praktik umum");
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH SAYAP", Vec3.UnitX, Vec3.UnitY));
            r.Dim(0, new Vec3(-Lf / 2, 0, Hmax / 2 + tpf), new Vec3(Lf / 2, 0, Hmax / 2 + tpf), Vec3.UnitZ, 50);
            r.Dim(1, new Vec3(-Lf / 2, -bpf / 2, 0), new Vec3(Lf / 2, -bpf / 2, 0), -Vec3.UnitY, 40);
            r.Note(0, new Vec3(Lf / 2, 0, Hmax / 2 + tpf), "2 " + Pl(Lf, bpf, tpf) + " sayap, 2 " + Pl(Lw, hw, tpw) + " badan\\P" + (nf + nw) + " baut " + Baut + " " + Grades.Label(Mutu), 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Sambungan memanjang gording: tumpang (Z, bersarang) atau selongsong (C, punggung ke punggung).</summary>
    public class PurlinSplice : Connection
    {
        public override ConnType Type => ConnType.GordingSambung;
        public override string Title => "Sambungan gording (tumpang / selongsong)";

        [Category("1. Profil"), DisplayName("Gording A"), TypeConverter(typeof(ProfilesPurlin))] public string ProfilA { get; set; } = "Z200x75x20x2.3";
        [Category("1. Profil"), DisplayName("Gording B"), TypeConverter(typeof(ProfilesPurlin))] public string ProfilB { get; set; } = "Z200x75x20x2.3";
        [Category("2. Sambungan"), DisplayName("Metode")] public PurlinJoin Metode { get; set; } = PurlinJoin.Tumpang;
        [Category("2. Sambungan"), DisplayName("Panjang tumpang / selongsong (mm)")] public double Panjang { get; set; }
        [Category("2. Sambungan"), DisplayName("Celah (selongsong) (mm)")] public double Celah { get; set; } = 10;
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M12;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas4_6;
        [Category("3. Baut"), DisplayName("Jarak tegak baut (mm)")] public double JarakTegak { get; set; }

        public override void Recommend()
        {
            var a = Prof(ProfilA, "A"); var bd = BoltData.Of(Baut);
            Metode = a.Family == Family.Z ? PurlinJoin.Tumpang : PurlinJoin.Selongsong;
            Panjang = Metode == PurlinJoin.Tumpang ? Fmt.Round10(Math.Max(600, 4 * Sec.H(a))) : Fmt.Round10(Math.Max(300, 2 * Sec.H(a)));
            JarakTegak = Fmt.Round5(Math.Max(3 * bd.D, Math.Min(0.5 * Sec.H(a), Sec.H(a) - 2 * (a["t"] + a["r"] + bd.WasherOD))));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var pa = Prof(ProfilA, "A"); var pb = Prof(ProfilB, "B"); var bd = BoltData.Of(Baut);
            double t = pa["t"], H = Sec.H(pa), e = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D)), pv = JarakTegak, L = Panjang;
            double web = pa.Family == Family.CNP ? -Sec.B(pa) / 2 : -t / 2;      // muka luar badan (y lokal)
            int nb = 0; double len = 0;
            if (Metode == PurlinJoin.Tumpang)
            {
                foreach (var x in new[] { -L / 2 + e, L / 2 - e })
                    foreach (var z in new[] { -pv / 2, pv / 2 })
                    { len = r.Bolt(new Vec3(x, web + 2 * t, z), -Vec3.UnitY, 2 * t, bd); nb++; }
                r.TrimSupport = new Vec3(L / 2, 0, 0);
                r.TrimSupported = new Vec3(-L / 2, 0, 0);
                r.Rule(pa.Family == Family.Z && pb.Family == Family.Z, "Tumpang dipakai untuk gording Z (bersarang)", "praktik umum; gording C pakai selongsong");
                r.Rule(L >= 3 * H - 1e-6, "Panjang tumpang " + Fmt.N(L) + " ≥ 3 x tinggi gording", "praktik umum (±10% bentang)");
            }
            else
            {
                // selongsong: potongan profil sama, punggung badan menempel badan gording (sisi luar)
                Piece(r, PartKind.Profil, pa, new Frame(new Vec3(L / 2, 2 * web, 0), -Vec3.UnitY, Vec3.UnitZ, -Vec3.UnitX), L, "selongsong");
                foreach (int side in new[] { -1, 1 })
                    foreach (var x in new[] { Celah / 2 + e, L / 2 - e })
                        foreach (var z in new[] { -pv / 2, pv / 2 })
                        { len = r.Bolt(new Vec3(side * x, web - t, z), Vec3.UnitY, 2 * t, bd); nb++; }
                r.TrimSupport = new Vec3(-Celah / 2, 0, 0);
                r.TrimSupported = new Vec3(Celah / 2, 0, 0);
                r.Rule(L / 2 - Celah / 2 - 2 * e >= 3 * bd.D - 1e-6, "Jarak baut per sisi selongsong ≥ 3d", "jarak baut minimum");
                r.Rule(true, "Selongsong punggung ke punggung dengan badan gording", "praktik umum gording C");
            }
            BoltBoq(r, Baut, Mutu, len, nb);
            double Ls = Math.Max(L, 600);
            Stub(r, pa, new Frame(new Vec3(-Ls, 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, Ls + (Metode == PurlinJoin.Tumpang ? L / 2 : -Celah / 2));
            Stub(r, pb, new Frame(new Vec3(Metode == PurlinJoin.Tumpang ? -L / 2 : Celah / 2, Metode == PurlinJoin.Tumpang ? t : 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, Ls);
            EdgeRule(r, "Baut ke ujung", e, bd);
            PitchRule(r, "Baut tegak", pv, bd);
            r.Rule(pv / 2 + bd.WasherOD / 2 <= H / 2 - t - pa["r"] + 1e-6, "Baut bebas dari sayap gording", "geometri");
            r.Views.Add(new ViewSpec("TAMPAK BADAN", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Dim(0, new Vec3(-L / 2, 0, -H / 2), new Vec3(L / 2, 0, -H / 2), -Vec3.UnitZ, 40);
            r.Note(0, new Vec3(L / 2, 0, H / 2), (Metode == PurlinJoin.Tumpang ? "Tumpang L=" : "Selongsong L=") + Fmt.N(L) + ", " + nb + " baut " + Baut, 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Buhul truss: satu pelat buhul untuk 3 batang atau lebih (siku, plat, CNP, hollow, pipa). Batang pertama = batang
    /// tepi (chord), boleh menerus. Frame seperti pelat buhul: bidang pelat XY, muka atas pelat z=0, batang tepi ke +X.
    /// Daftar batang: "profil@sudut@muka@heel@arah" dipisah '|'.
    /// </summary>
    public class TrussNode : Connection
    {
        public override ConnType Type => ConnType.BuhulTruss;
        public override string Title => "Buhul truss (3+ batang)";

        [Category("1. Batang"), DisplayName("Batang tepi (chord)"), TypeConverter(typeof(ProfilesLight))] public string Chord { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Batang tepi menerus")] public bool ChordMenerus { get; set; } = true;
        [Category("1. Batang"), DisplayName("Batang lain (profil@sudut|...)"), Description("Contoh: L50x50x5@45|L50x50x5@90|L50x50x5@135. Sudut terhadap batang tepi.")]
        public string Batang { get; set; } = "L50x50x5@45|L50x50x5@90|L50x50x5@135";

        [Category("2. Pelat buhul"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat buhul"), DisplayName("Celah antar ujung batang (mm)")] public double Celah { get; set; } = 15;
        [Category("2. Pelat buhul"), DisplayName("Tepi pelat dari baut (mm)")] public double TepiPelat { get; set; }
        [Category("3. Sambung"), DisplayName("Metode")] public GussetMethod Metode { get; set; } = GussetMethod.Baut;
        [Category("3. Sambung"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("3. Sambung"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Sambung"), DisplayName("Jumlah baut per batang")] public int JumlahBaut { get; set; } = 2;
        [Category("3. Sambung"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }
        [Category("3. Sambung"), DisplayName("Jarak baut ke ujung (mm)")] public double JarakUjung { get; set; }
        [Category("4. Las"), DisplayName("Las a (mm)")] public double Las { get; set; }
        [Category("4. Las"), DisplayName("Panjang las per batang (mm)")] public double PanjangLas { get; set; }

        [Browsable(false)] public int MukaChord { get; set; }
        [Browsable(false)] public double HeelChord { get; set; } = double.NaN;
        [Browsable(false)] public double ArahChord { get; set; } = 1;
        [Browsable(false)] public double SelisihMuka { get; set; }

        public class Member
        {
            public string Profil; public double Sudut; public int Muka; public double Heel = double.NaN; public double Arah = 1;
            /// <summary>+1 = di atas pelat (sisi batang tepi), -1 = di sisi seberang pelat.</summary>
            public int Sisi = 1;
            /// <summary>Tinggi as batang yang digambar dari muka atas pelat (NaN = sudah menempel).</summary>
            public double Zc = double.NaN;
        }

        public List<Member> Members()
        {
            var ci = CultureInfo.InvariantCulture;
            var list = new List<Member>();
            foreach (var item in (Batang ?? "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = item.Split('@');
                if (p.Length < 2) continue;
                var m = new Member { Profil = p[0].Trim() };
                double.TryParse(p[1], NumberStyles.Float, ci, out m.Sudut);
                if (p.Length > 2) int.TryParse(p[2], out m.Muka);
                if (p.Length > 3 && double.TryParse(p[3], NumberStyles.Float, ci, out double h)) m.Heel = h;
                if (p.Length > 4 && double.TryParse(p[4], NumberStyles.Float, ci, out double a)) m.Arah = a;
                if (p.Length > 5 && int.TryParse(p[5], out int sd)) m.Sisi = sd >= 0 ? 1 : -1;
                if (p.Length > 6 && double.TryParse(p[6], NumberStyles.Float, ci, out double zc)) m.Zc = zc;
                list.Add(m);
            }
            return list;
        }

        public static string Pack(IEnumerable<Member> ms)
        {
            var ci = CultureInfo.InvariantCulture;
            return string.Join("|", ms.Select(m => m.Profil + "@" + m.Sudut.ToString("0.###", ci) + "@" + m.Muka + "@" +
                (double.IsNaN(m.Heel) ? "" : m.Heel.ToString("0.###", ci)) + "@" + m.Arah.ToString("0", ci) + "@" + m.Sisi + "@" +
                (double.IsNaN(m.Zc) ? "" : m.Zc.ToString("0.###", ci))));
        }

        static LegFace Leg(Profile p, int idx)
        {
            var l = LegFace.Of(p);
            if (l.Count == 0) throw new ArgumentException(p.Name + ": belum didukung buhul (pakai siku, plat strip, CNP, hollow, pipa).");
            return l[Math.Max(0, Math.Min(idx, l.Count - 1))];
        }

        public override void Recommend()
        {
            var all = new List<Profile> { Prof(Chord, "Batang tepi") };
            all.AddRange(Members().Select(m => Prof(m.Profil, "Batang")));
            var legs = all.Select(p => Leg(p, 0)).ToList();
            Metode = legs.All(l => l.Boltable) ? GussetMethod.Baut : GussetMethod.Las;
            double w = legs.Min(l => l.Width);
            Baut = w < 60 ? BoltSize.M12 : w < 90 ? BoltSize.M16 : BoltSize.M20;
            var bd = BoltData.Of(Baut);
            JarakUjung = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            JarakBaut = Fmt.Round5(Math.Max(3 * bd.D, 40));
            TebalPelat = Fmt.StdPlate(Math.Max(8, legs.Max(l => l.Thick) + 2));
            TepiPelat = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            Las = Math.Max(4, Fmt.MinFillet(Math.Min(TebalPelat, legs.Min(l => l.Thick))));
            PanjangLas = Fmt.Round10(Math.Max(2 * w, 100));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var bd = BoltData.Of(Baut);
            bool bolt = Metode == GussetMethod.Baut;
            var items = new List<(Profile p, LegFace lf, double ang, double heel, double dir, bool chord)>();
            var sides = new List<int>(); var zcs = new List<double>();
            var pc = Prof(Chord, "Batang tepi"); var lc = Leg(pc, MukaChord);
            items.Add((pc, lc, 0, double.IsNaN(HeelChord) ? lc.Heel : HeelChord, ArahChord >= 0 ? 1 : -1, true));
            sides.Add(1); zcs.Add(lc.Offset);
            foreach (var m in Members())
            {
                var p = Prof(m.Profil, "Batang"); var lf = Leg(p, m.Muka);
                items.Add((p, lf, m.Sudut, double.IsNaN(m.Heel) ? lf.Heel : m.Heel, m.Arah >= 0 ? 1 : -1, false));
                sides.Add(m.Sisi); zcs.Add(m.Zc);
            }
            if (items.Count < 3) r.Rule(false, "Buhul truss butuh minimal 3 batang", "geometri");
            Func<int, double> half = i => Math.Max(Math.Abs(items[i].heel), Math.Abs(items[i].heel + items[i].dir * items[i].lf.Width));
            var dist = new double[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].chord && ChordMenerus) { dist[i] = double.NaN; continue; }
                double d = Celah / 2;
                for (int j = 0; j < items.Count; j++)
                {
                    if (i == j) continue;
                    var dirs = new List<double> { items[j].ang };
                    if (items[j].chord && ChordMenerus) dirs.Add(items[j].ang + 180);
                    foreach (var aj in dirs)
                    {
                        double th = Math.Abs(((items[i].ang - aj) % 360 + 540) % 360 - 180) * Math.PI / 180;
                        if (th < 1e-3) continue;
                        double need = items[j].chord && ChordMenerus
                            ? (half(j) + Celah) / Math.Max(Math.Sin(th), 0.1) + (Math.Abs(Math.Tan(th)) > 1e-6 ? half(i) / Math.Abs(Math.Tan(th)) : 0)
                            : (Math.Max(half(i), half(j)) + Celah / 2) / Math.Tan(Math.Min(th, Math.PI - 1e-3) / 2);
                        if (th > Math.PI / 2 && items[j].chord && ChordMenerus) need = (half(j) + Celah) / Math.Max(Math.Sin(th), 0.1);
                        d = Math.Max(d, need);
                    }
                }
                dist[i] = Fmt.Round5(d);
            }
            var hull = new List<Vec2> { new Vec2(0, 0) };
            double len = 0; int nb = 0;
            int idx = 0;
            foreach (var it in items)
            {
                var u = Geo.Dir2(it.ang); var pp = Geo.Perp(u);
                double gauge = it.p.Family == Family.L ? Math.Min(it.lf.GaugeDefault, it.lf.Width - bd.MinEdge) : it.lf.GaugeDefault;
                double line = it.heel + it.dir * gauge;
                double lo = Math.Min(it.heel, it.heel + it.dir * it.lf.Width), hi = Math.Max(it.heel, it.heel + it.dir * it.lf.Width);
                bool cont = double.IsNaN(dist[idx]);
                var st = new List<double>();
                if (bolt)
                {
                    if (cont) for (int k = 0; k < JumlahBaut; k++) st.Add((k - (JumlahBaut - 1) / 2.0) * JarakBaut);
                    else for (int k = 0; k < JumlahBaut; k++) st.Add(dist[idx] + JarakUjung + k * JarakBaut);
                }
                double s0 = cont ? (bolt ? st.Min() - JarakUjung : -PanjangLas / 2) : dist[idx];
                double s1 = bolt ? st.Max() + JarakUjung : (cont ? PanjangLas / 2 : dist[idx] + PanjangLas);
                if (cont)
                {
                    // gusset chord menerus mencakup semua proyeksi batang lain
                    s0 = Math.Min(s0, -items.Max(x => x.lf.Width)); s1 = Math.Max(s1, items.Max(x => x.lf.Width));
                }
                double t0 = bolt ? Math.Min(lo, line - TepiPelat) : lo, t1 = bolt ? Math.Max(hi, line + TepiPelat) : hi;
                foreach (var s in new[] { s0, s1 }) foreach (var tt in new[] { t0, t1 }) { var q = u * s + pp * tt; hull.Add(new Vec2(q.X, q.Y)); }
                int side = sides[idx];
                double faceZ = side > 0 ? 0 : -TebalPelat;                  // muka pelat tempat kaki menempel
                double zWant = side > 0 ? it.lf.Offset : -TebalPelat - it.lf.Offset;
                foreach (var s in st)
                {
                    var q = u * s + pp * line;
                    len = side > 0 ? r.Bolt(new Vec3(q.X, q.Y, it.lf.Thick), -Vec3.UnitZ, it.lf.Thick + TebalPelat, bd)
                                   : r.Bolt(new Vec3(q.X, q.Y, -TebalPelat - it.lf.Thick), Vec3.UnitZ, it.lf.Thick + TebalPelat, bd);
                    nb++;
                }
                var fm = side > 0 ? new Frame(Vec3.Zero, u, pp, Vec3.UnitZ) : new Frame(new Vec3(0, 0, faceZ), u, pp * -1, -Vec3.UnitZ);
                double hs = side, hl = side > 0 ? lo : -hi, hh = side > 0 ? hi : -lo;
                double heelL = side > 0 ? it.heel : -it.heel, dirL = side > 0 ? it.dir : -it.dir;
                double a0 = cont ? s0 - 300 : dist[idx], a1 = cont ? s1 + 300 : Math.Max(dist[idx] + 4 * it.lf.Width, s1 + 100);
                r.Prism(PartKind.Batang, it.p.Name, fm, Shapes.Rect(a0, hl, a1, hh), 0, it.lf.Thick);
                if (it.lf.Upstand > 0) r.Prism(PartKind.Batang, it.p.Name, fm, Shapes.Rect(a0, Math.Min(heelL, heelL + dirL * it.lf.Thick), a1, Math.Max(heelL, heelL + dirL * it.lf.Thick)), 0, it.lf.Upstand);
                if (!double.IsNaN(zcs[idx]) && Math.Abs(zcs[idx] - zWant) > 0.05) r.ShiftMembers[idx] = Vec3.UnitZ * (zWant - zcs[idx]);
                if (!cont) r.TrimMembers[idx] = u * dist[idx] + Vec3.UnitZ * zWant;
                if (bolt)
                {
                    EdgeRule(r, it.p.Name + ": baut ke tepi kaki", it.lf.Width - gauge, bd);
                    if (it.lf.Upstand > 0) r.Rule(gauge >= it.lf.Thick + bd.WasherOD / 2 - 1e-6, it.p.Name + ": ring bebas dari kaki tegak", "ruang kunci, praktik umum");
                }
                idx++;
            }
            if (r.TrimMembers.TryGetValue(0, out Vec3 t0p)) r.TrimSupport = t0p;
            if (r.TrimMembers.TryGetValue(1, out Vec3 t1p)) r.TrimSupported = t1p;
            var g = r.Prism(PartKind.Pelat, "pelat buhul", new Frame(), Shapes.Filleted(Projection.Hull(hull), null), -TebalPelat, 0);
            r.AddBoq("Pelat", "PL t=" + Fmt.N(TebalPelat) + " (pelat buhul truss)", 1, g.MassKg());
            if (bolt) BoltBoq(r, Baut, Mutu, len, nb);
            else WeldBoq(r, Las, 2 * PanjangLas * items.Count, "batang ke pelat buhul");
            if (bolt)
            {
                EdgeRule(r, "Baut ke ujung batang", JarakUjung, bd);
                if (JumlahBaut > 1) PitchRule(r, "Baut", JarakBaut, bd);
                r.Rule(items.All(x => x.lf.Boltable), "Semua batang terbuka (bisa dibaut)", "hollow/pipa: metode Las");
                r.Rule(JumlahBaut >= 2, "Minimal 2 baut per batang", "praktik umum");
            }
            double maxShift = r.ShiftMembers.Values.Select(v => Math.Abs(v.Z)).DefaultIfEmpty(0).Max();
            r.Rule(maxShift <= 100, "Batang digeser sejajar maks " + Fmt.N(Math.Round(maxShift, 1)) + " mm supaya punggung kaki menempel pelat", "geometri");
            if (sides.Any(x => x < 0)) r.Rule(true, sides.Count(x => x < 0) + " batang dipasang di sisi seberang pelat buhul", "praktik umum");
            var angs = items.Select(x => x.ang).ToList();
            double minAng = double.MaxValue;
            for (int i = 0; i < angs.Count; i++) for (int j = i + 1; j < angs.Count; j++) minAng = Math.Min(minAng, Math.Abs(((angs[i] - angs[j]) % 360 + 540) % 360 - 180));
            r.Rule(minAng >= 15, "Sudut terkecil antar batang " + Fmt.N(minAng) + "° ≥ 15°", "geometri");
            WeldRule(r, "Las batang ke pelat buhul", Las, items.Min(x => x.lf.Thick), TebalPelat);
            r.Views.Add(new ViewSpec("DENAH BUHUL TRUSS", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitX, Vec3.UnitZ));
            r.Note(0, Vec3.Zero, "Pelat buhul t=" + Fmt.N(TebalPelat) + ", " + items.Count + " batang" + (bolt ? ", " + nb + " baut " + Baut : ", las a=" + Fmt.N(Las)), -4, -3);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Las langsung hollow/pipa (T/Y): batang cabang dipotong mengikuti permukaan batang tepi (mulut ikan untuk pipa)
    /// lalu dilas keliling. Frame: asal = as batang tepi di titik temu, X = batang tepi, Y = ke sisi cabang.
    /// </summary>
    public class DirectWeld : Connection
    {
        public override ConnType Type => ConnType.LasLangsung;
        public override string Title => "Las langsung hollow / pipa (T, Y)";

        [Category("1. Batang"), DisplayName("Batang tepi"), TypeConverter(typeof(ProfilesTube))] public string Chord { get; set; } = "SHS100x100x4.5";
        [Category("1. Batang"), DisplayName("Batang cabang"), TypeConverter(typeof(ProfilesTube))] public string Cabang { get; set; } = "SHS60x60x3.2";
        [Category("1. Batang"), DisplayName("Sudut cabang (°)")] public double Sudut { get; set; } = 90;
        [Category("2. Las"), DisplayName("Las keliling a (mm)")] public double Las { get; set; }
        /// <summary>Ukuran batang tepi searah Y (setengahnya = jarak muka dari as).</summary>
        [Browsable(false)] public double TepiArahY { get; set; } = double.NaN;

        public override void Recommend()
        {
            var b = Prof(Cabang, "Cabang");
            Las = Math.Max(3, Math.Ceiling(1.2 * b["t"]));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var c = Prof(Chord, "Batang tepi"); var b = Prof(Cabang, "Cabang");
            double th = Sudut * Math.PI / 180, sn = Math.Max(Math.Sin(th), 0.1);
            bool pipe = c.Family == Family.CHS;
            double fy = (double.IsNaN(TepiArahY) ? c.Extent(false) : TepiArahY) / 2, fz = pipe ? fy : c.Extent(true) / 2;
            var u = new Vec3(Math.Cos(th), Math.Sin(th), 0);
            double hb = b.Extent(false) / 2, wb = b.Extent(true) / 2;
            double sFace = fy / sn;
            double extra = hb * Math.Abs(Math.Cos(th)) / sn + (pipe ? fy * 0.6 : 0) + 5;
            r.TrimSupported = u * (sFace - extra);
            if (pipe) r.CutSupported.Add(Cutter.Cyl(new Frame(Vec3.Zero, Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), fy, -10 * fy - 2000, 10 * fy + 2000));
            else r.CutSupported.Add(Cutter.Box(new Frame(), -10 * fy - 2000, 10 * fy + 2000, -fy, fy, -fz, fz));
            // las keliling: cincin tipis di sekitar ujung cabang
            var bs = b.BuildShape();
            Axes.Member(u, 0, out Vec3 bx, out Vec3 by, out Vec3 bz);
            var wf = new Frame(u * sFace, bx, by, bz);
            var weld = r.Prism(PartKind.Las, "las", wf, bs.Outer, -Las, Las * 0.6, new[] { Shapes.Rect(-wb + 0.01, -hb + 0.01, wb - 0.01, hb - 0.01) });
            double perim = SectionCalc.Compute(new SectionShape { Outer = bs.Outer }).Perimeter / sn;
            WeldBoq(r, Las, perim, "keliling cabang ke batang tepi");
            var cs = c.BuildShape();
            r.Prism(PartKind.Batang, c.Name, new Frame(new Vec3(4 * fy + 300, 0, 0), Vec3.UnitZ, Vec3.UnitY, -Vec3.UnitX), cs.Outer, 0, 8 * fy + 600, cs.Holes);
            // cabang dipotong rapat ke muka batang tepi (hollow: bidang muka; pipa: lembah mulut ikan)
            double yCut = pipe ? Math.Sqrt(Math.Max(fy * fy - wb * wb, 0)) : fy;
            r.Prism(PartKind.Batang, b.Name, new Frame(u * (sFace - extra), bx, by, bz), bs.Outer, 0, Math.Max(6 * hb, 400) + extra, bs.Holes)
                .Clip(new Vec3(0, yCut, 0), Vec3.UnitY);
            double beta = (pipe ? b.Extent(true) : b.Extent(true)) / (2 * fz);
            r.Rule(beta <= 1 + 1e-6, "Lebar cabang ≤ lebar batang tepi (β = " + Fmt.N(Math.Round(beta, 2)) + ")", "praktik umum sambungan pipa/hollow");
            r.Rule(Sudut >= 30 && Sudut <= 150, "Sudut cabang " + Fmt.N(Sudut) + "° (30°-150°)", "praktik umum las langsung");
            r.Rule(b["t"] <= c["t"] + 1e-6, "Tebal cabang ≤ tebal batang tepi", "praktik umum");
            r.Rule(Las >= b["t"] - 1e-6, "Las keliling a=" + Fmt.N(Las) + " ≥ tebal dinding cabang " + Fmt.N(b["t"]), "praktik umum");
            r.Views.Add(new ViewSpec("TAMPAK BIDANG", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            r.Note(0, u * sFace, b.Name + " dipotong " + (pipe ? "mulut ikan" : "miring") + " ke " + c.Name + "\\PLas keliling a=" + Fmt.N(Las), 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Anak tangga: pelat/bordes di atas dua siku yang dibaut ke badan ibu tangga (UNP/CNP).</summary>
    public class StairTread : Connection
    {
        public override ConnType Type => ConnType.AnakTangga;
        public override string Title => "Anak tangga ke ibu tangga";

        [Category("1. Profil"), DisplayName("Ibu tangga"), TypeConverter(typeof(ProfilesUNP))] public string IbuTangga { get; set; } = "UNP200x80x7.5x11";
        [Category("1. Profil"), DisplayName("Siku dudukan"), TypeConverter(typeof(ProfilesAngle))] public string Siku { get; set; } = "L50x50x5";
        [Category("2. Anak tangga"), DisplayName("Lebar bersih tangga (mm)")] public double Lebar { get; set; } = 1000;
        [Category("2. Anak tangga"), DisplayName("Antrede / kedalaman (mm)")] public double Antrede { get; set; } = 250;
        [Category("2. Anak tangga"), DisplayName("Tebal pelat bordes (mm)")] public double TebalPelat { get; set; } = 4.5;
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M12;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas4_6;

        public override void Recommend() { }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var s = Prof(IbuTangga, "Ibu tangga"); var L = Prof(Siku, "Siku"); var bd = BoltData.Of(Baut);
            double W = Lebar, A = Antrede, tp = TebalPelat, t = L["t"], leg = L["B"], tw = Sec.Tw(s), Hs = Sec.H(s), Bs = Sec.B(s);
            var tread = r.Box(PartKind.Pelat, "bordes", -W / 2 + 5, W / 2 - 5, -A / 2, A / 2, -tp, 0);
            r.AddBoq("Pelat", "Pelat bordes " + Fmt.N(W - 10) + "x" + Fmt.N(A) + "x" + Fmt.N(tp) + " (anak tangga)", 1, tread.MassKg());
            double Lc = A - 20, p = Fmt.Round5(Math.Max(3 * bd.D, Math.Min(Lc - 2 * bd.MinEdge, 150)));
            double g = Fmt.Round5(Math.Max(t + bd.WasherOD / 2 + 2, Math.Min(0.55 * leg, leg - bd.MinEdge)));
            double lenW = 0, lenT = 0;
            var fyz = new Frame(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY);
            foreach (int sd in new[] { 1, -1 })
            {
                double xw = sd * W / 2;
                var pts = new List<Vec2> { new Vec2(xw, -tp), new Vec2(xw - sd * leg, -tp), new Vec2(xw - sd * leg, -tp - t), new Vec2(xw - sd * t, -tp - t), new Vec2(xw - sd * t, -tp - leg), new Vec2(xw, -tp - leg) };
                r.Prism(PartKind.Siku, L.Name, fyz, Shapes.Filleted(pts, null), -Lc / 2, Lc / 2);
                r.AddBoq("Siku", L.Name + " L=" + Fmt.N(Lc) + " (dudukan anak tangga)", 1, L.Properties().KgPerM(Env.Density) * Lc / 1000);
                foreach (var y in new[] { -p / 2, p / 2 })
                {
                    lenW = r.Bolt(new Vec3(xw - sd * t, y, -tp - g), sd * Vec3.UnitX, t + tw, bd);
                    lenT = r.Bolt(new Vec3(xw - sd * g, y, 0), -Vec3.UnitZ, tp + t, bd);
                }
                var ss = s.BuildShape();
                double zc = 20 - Hs / 2;
                r.Prism(PartKind.Batang, s.Name, sd > 0 ? new Frame(new Vec3(xw + Bs / 2, A, zc), Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY) : new Frame(new Vec3(xw - Bs / 2, -A, zc), -Vec3.UnitX, Vec3.UnitZ, Vec3.UnitY), ss.Outer, 0, 2 * A, ss.Holes);
            }
            BoltBoq(r, Baut, Mutu, lenW, 4, "siku ke ibu tangga");
            BoltBoq(r, Baut, Mutu, lenT, 4, "bordes ke siku (kepala benam)");
            EdgeRule(r, "Baut ke tepi kaki siku", leg - g, bd);
            EdgeRule(r, "Baut ke ujung siku", (Lc - p) / 2, bd);
            PitchRule(r, "Baut", p, bd);
            r.Rule(g + bd.WasherOD / 2 <= Hs / 2, "Baut siku di badan ibu tangga", "geometri");
            r.Rule(tp >= 4.5 - 1e-9, "Pelat bordes ≥ 4,5 mm", "praktik umum tangga");
            r.Rule(A >= 220 && A <= 320, "Antrede " + Fmt.N(A) + " (220-320)", "praktik umum");
            r.Views.Add(new ViewSpec("POTONGAN MELINTANG", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("DENAH", Vec3.UnitX, Vec3.UnitY));
            r.Dim(0, new Vec3(-W / 2, 0, 0), new Vec3(W / 2, 0, 0), Vec3.UnitZ, 60);
            r.Note(0, new Vec3(W / 2, 0, -tp), "2 " + L.Name + " L=" + Fmt.N(Lc) + ", 8 baut " + Baut + "\\PPelat bordes t=" + Fmt.N(tp), 2, -2);
            return r;
        }
    }
}
