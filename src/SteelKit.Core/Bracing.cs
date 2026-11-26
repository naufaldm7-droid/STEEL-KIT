using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SteelKit.Core
{
    public enum RodEnd { RingMiring, Clevis }
    public enum HostFace { Sayap, Badan }

    public class ProfilesRod : ProfileListBase { protected override Family[] Families => new[] { Family.RD }; }

    static class Geo
    {
        public static Vec3 Dir2(double deg) => new Vec3(Math.Cos(deg * Math.PI / 180), Math.Sin(deg * Math.PI / 180), 0);
        public static Vec3 Perp(Vec3 u) => new Vec3(-u.Y, u.X, 0);

        /// <summary>Potongan profil sepanjang sumbu X frame, tinggi profil (y lokal) searah Y frame (sayap tegak lurus Y).</summary>
        public static Frame AlongX(Vec3 o, bool depthAlongY)
            => depthAlongY ? new Frame(o, Vec3.UnitZ, Vec3.UnitY, -Vec3.UnitX) : new Frame(o, Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX);

        public static List<Vec2> Circle(Vec2 c, double r, int n = 12)
            => Enumerable.Range(0, n).Select(i => new Vec2(c.X + r * Math.Cos(2 * Math.PI * i / n), c.Y + r * Math.Sin(2 * Math.PI * i / n))).ToList();
    }

    // =========================================================================================
    /// <summary>
    /// Ujung rod bracing di badan kolom/rafter. RingMiring: rod menembus badan, ring miring (hillside) + mur di sisi
    /// belakang (umum di PEB). Clevis: pelat mata dilas di badan + clevis + pin.
    /// Frame lokal: asal = as rod menembus bidang tengah badan, Z = normal badan ke sisi rod, rod = (cos φ, 0, sin φ).
    /// </summary>
    public class BracingRod : Connection
    {
        public override ConnType Type => ConnType.BracingRod;
        public override string Title => "Ujung rod bracing (ring miring / clevis)";

        [Category("1. Batang"), DisplayName("Kolom / rafter"), TypeConverter(typeof(ProfilesIH))] public string Host { get; set; } = "WF400x200x8x13";
        [Category("1. Batang"), DisplayName("Rod"), TypeConverter(typeof(ProfilesRod))] public string Rod { get; set; } = "RD16";
        [Category("1. Batang"), DisplayName("Sudut rod terhadap bidang badan (°)")] public double SudutRod { get; set; } = 45;
        [Category("1. Batang"), DisplayName("Sudut as kolom/rafter di bidang badan (°)")] public double SudutHost { get; set; } = 90;
        [Category("2. Ujung rod"), DisplayName("Metode")] public RodEnd Metode { get; set; } = RodEnd.RingMiring;
        [Category("2. Ujung rod"), DisplayName("Mutu rod")] public string MutuRod { get; set; } = "BJ 41 / ASTM A36";
        [Category("2. Ujung rod"), DisplayName("Mur ganda")] public bool MurGanda { get; set; } = true;
        [Category("3. Clevis"), DisplayName("Diameter pin (mm)")] public double Pin { get; set; }
        [Category("3. Clevis"), DisplayName("Tebal pelat mata (mm)")] public double TebalPelat { get; set; }
        [Category("3. Clevis"), DisplayName("Jari-jari pelat mata (mm)")] public double JariMata { get; set; }
        [Category("3. Clevis"), DisplayName("Las pelat mata a (mm)")] public double Las { get; set; }

        public override void Recommend()
        {
            var rd = Prof(Rod, "Rod"); double d = rd["D"];
            Pin = Math.Ceiling(d + 4);
            TebalPelat = Fmt.StdPlate(Math.Max(10, 0.8 * d));
            JariMata = Fmt.Round5(Pin / 2 + 1 + Math.Max(Pin, 20));
            Las = Math.Max(5, Fmt.MinFillet(Math.Min(TebalPelat, Sec.Tw(Prof(Host, "Host")))));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var host = Prof(Host, "Kolom/rafter"); var rd = Prof(Rod, "Rod");
            double d = rd["D"], tw = Sec.Tw(host), phi = SudutRod * Math.PI / 180;
            var b = BoltData.Of(NearestBolt(d));
            var uR = new Vec3(Math.Cos(phi), 0, Math.Sin(phi));
            double sinp = Math.Max(Math.Sin(phi), 0.05);
            // potongan host: badan di bidang XY (z = ±tw/2), sumbu host pada SudutHost di bidang badan
            var hd = Geo.Dir2(SudutHost);
            double L = Math.Max(2 * Sec.H(host), 600);
            Stub(r, host, new Frame(hd * -L, Vec3.UnitZ, hd.Cross(Vec3.UnitZ), hd), 0, 2 * L);
            int nNuts = MurGanda ? 2 : 1;
            Vec3 end;
            string spec;
            if (Metode == RodEnd.RingMiring)
            {
                var pb = uR * (-(tw / 2) / sinp);                 // titik as rod di muka belakang badan
                // tebal ring miring: cukup supaya ring datar & mur (tegak lurus rod) tidak menyentuh badan pada sudut landai
                double rOut = Math.Max(b.WasherOD / 2, b.Across / Math.Sqrt(3));
                double hw = Fmt.Round5(Math.Max(Math.Max(0.9 * d, 12), rOut * Math.Cos(phi) / sinp + 2));
                // ring miring: muka miringnya rata dengan muka belakang badan (dipotong bidang badan), tidak menembus
                double reach = 1.25 * d * Math.Cos(phi) / sinp + 1;
                r.Cylinder(PartKind.Ring, "ring miring", pb - uR * hw, uR, 1.25 * d, hw + reach).Clip(new Vec3(0, 0, -tw / 2), -Vec3.UnitZ);
                // lubang lonjong di badan host: silinder searah rod menembus badan (bentuknya lonjong karena miring)
                // tutup silinder tegak lurus rod, bukan sejajar badan: diperpanjang R·cot(φ) di kedua sisi supaya tembus penuh
                double rh = (d + 3) / 2, ext = 2 + rh * Math.Cos(phi) / sinp;
                r.CutSupport.Add(Cutter.Cyl(ConnResult.AxisFrame(pb - uR * ext, uR), rh, 0, tw / sinp + 2 * ext));
                r.Cylinder(PartKind.Ring, "", pb - uR * (hw + b.WasherT), uR, b.WasherOD / 2, b.WasherT);
                for (int i = 0; i < nNuts; i++) r.Hex(PartKind.Mur, "", pb - uR * (hw + b.WasherT + (i + 1) * b.Nut), uR, b.Across, b.Nut);
                end = pb - uR * (hw + b.WasherT + nNuts * b.Nut + 3 * b.Pitch);
                spec = "Ring miring (hillside) + ring + " + nNuts + " mur " + b.Size;
                r.AddBoq("Ring", "Ring miring (hillside washer) " + b.Size, 1, 0);
                r.AddBoq("Mur", "Mur " + b.Size + " + ring", nNuts, 0);
                r.Rule(SudutRod >= 25 - 1e-9, "Sudut rod terhadap badan " + Fmt.N(SudutRod) + "° ≥ 25° (ring miring)", "praktik umum PEB");
                r.Rule(SudutRod <= 90 + 1e-9, "Sudut rod ≤ 90°", "geometri");
                double hole = (d + 3) / sinp;
                r.Rule(hole <= Sec.H(host) - 2 * Sec.Tf(host) - 2 * Sec.R(host), "Lubang lonjong di badan " + Fmt.N(hole) + " mm muat di tinggi bersih badan", "geometri");
            }
            else
            {
                double R = JariMata, tp = TebalPelat, pinHole = Pin + 1, hC = R + 10;
                double sC = (tw / 2 + hC) / sinp;
                var C = uR * sC;
                // pelat mata di bidang XZ (tebal arah Y), dilas di muka badan z = tw/2
                var fe = new Frame(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, -Vec3.UnitY);
                var pts = Geo.Circle(new Vec2(C.X, C.Z), R, 16);
                double bx = R + 15;
                pts.Add(new Vec2(C.X - bx, tw / 2)); pts.Add(new Vec2(C.X + bx, tw / 2));
                var eye = r.Prism(PartKind.Pelat, "pelat mata", fe, Shapes.Filleted(Projection.Hull(pts), null), -tp / 2, tp / 2, new[] { Shapes.Circle(C.X, C.Z, pinHole / 2) });
                r.AddBoq("Pelat", "PL t=" + Fmt.N(tp) + " (pelat mata clevis)", 1, eye.MassKg());
                var nr = new Vec3(-Math.Sin(phi), 0, Math.Cos(phi));
                double tf = Fmt.StdPlate(0.6 * d), Lf = R + 1.5 * d;
                foreach (int s in new[] { 1, -1 })
                {
                    double z0 = s > 0 ? tp / 2 + 1 : -(tp / 2 + 1 + tf), z1 = s > 0 ? tp / 2 + 1 + tf : -(tp / 2 + 1);
                    r.Prism(PartKind.Mur, "clevis", new Frame(C, uR, nr, -Vec3.UnitY), Shapes.Rect(-R * 0.8, -R * 0.8, Lf, R * 0.8), z0, z1);
                }
                double body = 1.6 * d;
                r.Cylinder(PartKind.Mur, "clevis", C + uR * Lf, uR, 0.9 * d, body);
                double pinLen = tp + 2 * (1 + tf) + 12;
                r.Cylinder(PartKind.Baut, "pin", C + Vec3.UnitY * (pinLen / 2), -Vec3.UnitY, Pin / 2, pinLen);
                end = C + uR * (Lf + body * 0.5);
                spec = "Clevis " + b.Size + " + pin Ø" + Fmt.N(Pin);
                r.AddBoq("Clevis", spec, 1, 0);
                r.Rule(R - pinHole / 2 >= Pin - 1e-6, "Tepi pelat mata " + Fmt.N(R - pinHole / 2) + " ≥ diameter pin " + Fmt.N(Pin), "praktik umum sambungan pin");
                r.Rule(Pin >= d - 1e-6, "Pin Ø" + Fmt.N(Pin) + " ≥ rod Ø" + Fmt.N(d), "praktik umum");
                r.Rule(SudutRod >= 15 - 1e-9, "Sudut rod terhadap badan " + Fmt.N(SudutRod) + "° ≥ 15°", "geometri");
                WeldRule(r, "Pelat mata ke badan", Las, tp, tw);
                WeldBoq(r, Las, 2 * 2 * bx, "pelat mata ke badan, dua sisi");
            }
            r.TrimSupported = end;
            r.Cylinder(PartKind.Batang, rd.Name, end, uR, d / 2, 600);
            r.Rule(d >= 10 - 1e-9, "Rod Ø" + Fmt.N(d) + " ≥ 10 mm", "praktik umum bracing");
            r.Rule(tw >= 4 - 1e-9, "Badan host tebal " + Fmt.N(tw) + " ≥ 4 mm", "praktik umum");

            r.Views.Add(new ViewSpec("TAMPAK (BIDANG ROD)", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK MUKA BADAN", Vec3.UnitX, Vec3.UnitY));
            r.Note(0, end, rd.Name + " " + MutuRod + "\\P" + spec, 2, -2);
            r.Note(1, Vec3.Zero, Metode == RodEnd.RingMiring ? "Lubang lonjong di badan, ring miring di sisi belakang" : "Pelat mata t=" + Fmt.N(TebalPelat) + ", las a=" + Fmt.N(Las) + " dua sisi", 2, 2);
            return r;
        }

        public static BoltSize NearestBolt(double d)
        {
            var all = Enum.GetValues(typeof(BoltSize)).Cast<BoltSize>().ToList();
            return all.OrderBy(s => Math.Abs(BoltData.Of(s).D - d)).First();
        }
    }

    // =========================================================================================
    /// <summary>Turnbuckle (pengencang) di tengah rod bracing. Frame: asal = tengah rod, X = arah rod.</summary>
    public class Turnbuckle : Connection
    {
        public override ConnType Type => ConnType.Turnbuckle;
        public override string Title => "Turnbuckle rod bracing";

        [Category("1. Rod"), DisplayName("Rod"), TypeConverter(typeof(ProfilesRod))] public string Rod { get; set; } = "RD16";
        [Category("2. Turnbuckle"), DisplayName("Panjang badan (mm)")] public double Panjang { get; set; }
        [Category("2. Turnbuckle"), DisplayName("Mur kunci")] public bool MurKunci { get; set; } = true;

        public override void Recommend() { Panjang = Fmt.Round10(10 * Prof(Rod, "Rod")["D"]); }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var rd = Prof(Rod, "Rod"); double d = rd["D"], L = Panjang;
            var b = BoltData.Of(BracingRod.NearestBolt(d));
            double boss = 1.6 * d, acr = 1.8 * d;
            foreach (int s in new[] { -1, 1 })
            {
                r.Hex(PartKind.Mur, "turnbuckle", new Vec3(s > 0 ? L / 2 - boss : -L / 2, 0, 0), Vec3.UnitX, acr, boss);
                if (MurKunci) r.Hex(PartKind.Mur, "", new Vec3(s > 0 ? L / 2 : -L / 2 - b.Nut, 0, 0), Vec3.UnitX, b.Across, b.Nut);
            }
            foreach (int s in new[] { -1, 1 })
                r.Box(PartKind.Mur, "turnbuckle", -L / 2 + boss, L / 2 - boss, s * 0.75 * d - 0.2 * d, s * 0.75 * d + 0.2 * d, -0.45 * d, 0.45 * d);
            r.Cylinder(PartKind.Batang, rd.Name, new Vec3(-L, 0, 0), Vec3.UnitX, d / 2, 2 * L);
            r.AddBoq("Turnbuckle", "Turnbuckle " + b.Size + " badan terbuka L=" + Fmt.N(L) + (MurKunci ? " + 2 mur kunci" : ""), 1, 0);
            r.Rule(L >= 8 * d - 1e-6, "Panjang badan " + Fmt.N(L) + " ≥ 8d", "praktik umum (DIN 1480)");
            r.Rule(d >= 10, "Rod Ø" + Fmt.N(d) + " ≥ 10 mm", "praktik umum bracing");
            r.Rule(MurKunci, "Mur kunci terpasang di kedua sisi", "praktik umum: cegah longgar");
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK ATAS", Vec3.UnitX, Vec3.UnitY));
            r.Dim(0, new Vec3(-L / 2, 0, -0.5 * d), new Vec3(L / 2, 0, -0.5 * d), -Vec3.UnitZ, 2 * d);
            r.Note(0, new Vec3(L / 2, 0, 0.5 * d), "Turnbuckle " + b.Size + ", kencangkan sampai rod lurus", 1, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Pelat buhul bracing (siku / plat / hollow / pipa) dilas ke sayap atau badan kolom/balok, opsional juga ke batang
    /// kedua (buhul sudut balok-kolom). Frame lokal: asal = as host di titik temu, X = as host, Y = ke sisi bracing, Z = normal pelat.
    /// </summary>
    public enum GussetShape { Trapesium, Otomatis }

    public class BracingGusset : Connection
    {
        public override ConnType Type => ConnType.BracingBuhul;
        public override string Title => "Pelat buhul bracing ke kolom / balok";

        [Category("1. Batang"), DisplayName("Kolom / balok"), TypeConverter(typeof(ProfilesIH))] public string Host { get; set; } = "WF400x200x8x13";
        [Category("1. Batang"), DisplayName("Muka yang dilas pelat")] public HostFace Muka { get; set; } = HostFace.Sayap;
        [Category("1. Batang"), DisplayName("Batang kedua (buhul sudut, kosong = tidak ada)"), TypeConverter(typeof(ProfilesIH))] public string Host2 { get; set; } = "";
        [Category("1. Batang"), DisplayName("Sudut batang kedua (°)")] public double SudutHost2 { get; set; } = 90;
        [Category("1. Batang"), DisplayName("Bracing"), TypeConverter(typeof(ProfilesLight))] public string Brace { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Sudut bracing terhadap host (°)")] public double SudutBrace { get; set; } = 45;

        [Category("2. Pelat"), DisplayName("Tebal pelat (mm)")] public double TebalPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Celah ujung bracing (mm)")] public double Celah { get; set; } = 20;
        [Category("2. Pelat"), DisplayName("Tepi pelat dari baut (mm)")] public double TepiPelat { get; set; }
        [Category("2. Pelat"), DisplayName("Las pelat ke host a (mm)")] public double Las { get; set; }
        [Category("2. Pelat"), DisplayName("Bentuk pelat"), Description("Trapesium = alas di muka host, sisi atas sejajar alas (mudah dipotong & dilas). Otomatis = mengikuti bracing sekecil mungkin. Buhul sudut (batang kedua) selalu otomatis.")]
        public GussetShape Bentuk { get; set; } = GussetShape.Trapesium;

        [Category("3. Sambung bracing"), DisplayName("Metode")] public GussetMethod Metode { get; set; } = GussetMethod.Baut;
        [Category("3. Sambung bracing"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("3. Sambung bracing"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("3. Sambung bracing"), DisplayName("Jumlah baut")] public int JumlahBaut { get; set; } = 2;
        [Category("3. Sambung bracing"), DisplayName("Jarak antar baut (mm)")] public double JarakBaut { get; set; }
        [Category("3. Sambung bracing"), DisplayName("Jarak baut ke ujung bracing (mm)")] public double JarakUjung { get; set; }
        [Category("3. Sambung bracing"), DisplayName("Gauge dari pangkal kaki (mm)")] public double Gauge { get; set; }
        [Category("3. Sambung bracing"), DisplayName("Panjang las bracing (mm)")] public double PanjangLas { get; set; }

        [Browsable(false)] public int MukaBrace { get; set; }
        [Browsable(false)] public double HeelBrace { get; set; } = double.NaN;
        [Browsable(false)] public double ArahBrace { get; set; } = 1;
        /// <summary>Geser bidang tengah pelat dari as host searah Z (supaya kaki bracing menempel pelat).</summary>
        [Browsable(false)] public double GeserPelat { get; set; } = double.NaN;

        LegFace Leg(Profile p)
        {
            var l = LegFace.Of(p);
            if (l.Count == 0) throw new ArgumentException(p.Name + ": bracing harus siku, plat strip, CNP, hollow, atau pipa.");
            return l[Math.Max(0, Math.Min(MukaBrace, l.Count - 1))];
        }

        public override void Recommend()
        {
            var br = Prof(Brace, "Bracing"); var lf = Leg(br);
            Metode = lf.Boltable ? GussetMethod.Baut : GussetMethod.Las;
            Baut = lf.Width < 60 ? BoltSize.M12 : lf.Width < 90 ? BoltSize.M16 : lf.Width < 125 ? BoltSize.M20 : BoltSize.M22;
            var bd = BoltData.Of(Baut);
            JarakUjung = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            JarakBaut = Fmt.Round5(Math.Max(3 * bd.D, 40));
            Gauge = br.Family == Family.L ? Math.Min(lf.GaugeDefault, lf.Width - bd.MinEdge) : lf.GaugeDefault;
            TebalPelat = Fmt.StdPlate(Math.Max(10, lf.Thick + 2));
            TepiPelat = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            Las = Math.Max(5, Fmt.MinFillet(Math.Min(TebalPelat, Sec.Tf(Prof(Host, "Host")))));
            PanjangLas = Fmt.Round10(Math.Max(2 * lf.Width, 100));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var host = Prof(Host, "Host"); var br = Prof(Brace, "Bracing"); var lf = Leg(br);
            var bd = BoltData.Of(Baut);
            double tg = TebalPelat, f = Muka == HostFace.Sayap ? Sec.H(host) / 2 : Sec.Tw(host) / 2;
            double zMid = double.IsNaN(GeserPelat) ? 0 : GeserPelat;
            var uB = Geo.Dir2(SudutBrace); var pB = Geo.Perp(uB);
            double heel = double.IsNaN(HeelBrace) ? lf.Heel : HeelBrace, dir = ArahBrace >= 0 ? 1 : -1;
            double lo = Math.Min(heel, heel + dir * lf.Width), hi = Math.Max(heel, heel + dir * lf.Width);
            bool bolt = Metode == GussetMethod.Baut;
            // jarak mundur ujung bracing dari as host supaya bebas dari muka host (dan muka batang kedua)
            Func<Vec3, double, double> clear = (n, face) =>
            {
                double un = uB.Dot(n);
                if (un <= 0.05) return 0;
                double tn = Math.Min(lo * pB.Dot(n), hi * pB.Dot(n));
                return (face + Celah - tn) / un;
            };
            double s0 = clear(Vec3.UnitY, f);
            Profile h2 = string.IsNullOrWhiteSpace(Host2) ? null : Prof(Host2, "Batang kedua");
            Vec3 u2 = Geo.Dir2(SudutHost2), n2 = Geo.Perp(u2);
            if (n2.Dot(uB) < 0) n2 = n2 * -1;
            double f2 = h2 == null ? 0 : Sec.H(h2) / 2;
            if (h2 != null) s0 = Math.Max(s0, clear(n2, f2));
            s0 = Fmt.Round5(s0);
            double line = heel + dir * Gauge;
            var stations = bolt ? Enumerable.Range(0, JumlahBaut).Select(k => s0 + JarakUjung + k * JarakBaut).ToList() : new List<double>();
            double sEnd = bolt ? stations.Last() + JarakUjung : s0 + PanjangLas;
            double t0 = bolt ? Math.Min(lo, line - TepiPelat) : lo, t1 = bolt ? Math.Max(hi, line + TepiPelat) : hi;
            var pts = new List<Vec2>();
            foreach (var s in new[] { s0, sEnd }) foreach (var t in new[] { t0, t1 }) { var q = uB * s + pB * t; pts.Add(new Vec2(q.X, q.Y)); }
            double xs0 = pts.Min(p => p.X) - 20, xs1 = pts.Max(p => p.X) + 20;
            pts.Add(new Vec2(xs0, f)); pts.Add(new Vec2(xs1, f));
            double weld = 2 * (xs1 - xs0);
            if (h2 != null)
            {
                // titik pada muka batang kedua (garis n2·p = f2) di sekitar daerah bracing
                foreach (var s in new[] { s0, sEnd })
                {
                    var q = uB * s; double k = f2 - q.Dot(n2);
                    var onFace = q + n2 * k;
                    pts.Add(new Vec2(onFace.X, onFace.Y));
                }
                var corner = Corner(f, u2, n2, f2);
                if (corner.HasValue) pts.Add(corner.Value);
                weld += 2 * 150;
            }
            var hull = Projection.Hull(pts.Where(p => p.Y >= f - 1e-6).ToList());
            if (Bentuk == GussetShape.Trapesium && h2 == null)
            {
                // trapesium: alas di muka host (y = f), sisi atas sejajar alas di titik tertinggi, sisi miring memuat seluruh daerah bracing
                var up = pts.Where(q => q.Y > f + 1).ToList();
                double xa = Math.Floor(xs0 / 5) * 5, xb = Math.Ceiling(xs1 / 5) * 5, yTop = Math.Ceiling(up.Max(q => q.Y) / 5) * 5;
                double xt0 = up.Min(q => xa + (q.X - xa) * (yTop - f) / (q.Y - f)), xt1 = up.Max(q => xb + (q.X - xb) * (yTop - f) / (q.Y - f));
                xt0 = Math.Floor(Math.Min(xt0, xb - 30) / 5) * 5; xt1 = Math.Ceiling(Math.Max(xt1, xa + 30) / 5) * 5;
                hull = new List<Vec2> { new Vec2(xa, f), new Vec2(xb, f), new Vec2(xt1, yTop), new Vec2(xt0, yTop) };
            }
            var g = r.Prism(PartKind.Pelat, "pelat buhul", new Frame(new Vec3(0, 0, zMid), Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ), Shapes.Filleted(hull, null), -tg / 2, tg / 2);
            r.AddBoq("Pelat", "PL t=" + Fmt.N(tg) + " (pelat buhul bracing)", 1, g.MassKg());
            WeldBoq(r, Las, weld, "pelat buhul ke host, dua sisi");
            double zFace = zMid + tg / 2;                 // muka pelat tempat kaki bracing
            double len = 0;
            if (bolt)
            {
                foreach (var s in stations)
                {
                    var q = uB * s + pB * line;
                    len = r.Bolt(new Vec3(q.X, q.Y, zFace + lf.Thick), -Vec3.UnitZ, lf.Thick + tg, bd);
                }
                BoltBoq(r, Baut, Mutu, len, stations.Count, "bracing");
            }
            else WeldBoq(r, Las, 2 * PanjangLas, "bracing ke pelat buhul");
            // potongan untuk gambar
            double L = Math.Max(2 * Sec.H(host), 700);
            Stub(r, host, Muka == HostFace.Sayap ? new Frame(new Vec3(L, 0, 0), Vec3.UnitZ, Vec3.UnitY, -Vec3.UnitX) : new Frame(new Vec3(-L, 0, 0), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * L);
            if (h2 != null)
            {
                var w2 = u2; var v2 = n2; var uu = v2.Cross(w2);
                Stub(r, h2, new Frame(Vec3.Zero, uu, v2, w2), 0, Math.Max(2 * Sec.H(h2), 700));
            }
            var fm = new Frame(new Vec3(0, 0, zFace), uB, pB, Vec3.UnitZ);
            double stubEnd = Math.Max(sEnd + 200, s0 + 4 * lf.Width);
            r.Prism(PartKind.Batang, br.Name, fm, Shapes.Rect(s0, lo, stubEnd, hi), 0, lf.Thick);
            if (lf.Upstand > 0) r.Prism(PartKind.Batang, br.Name, fm, Shapes.Rect(s0, Math.Min(heel, heel + dir * lf.Thick), stubEnd, Math.Max(heel, heel + dir * lf.Thick)), 0, lf.Upstand);
            r.TrimSupported = uB * s0 + Vec3.UnitZ * (zFace + lf.Offset);

            if (bolt)
            {
                EdgeRule(r, "Baut ke ujung bracing", JarakUjung, bd);
                if (JumlahBaut > 1) PitchRule(r, "Baut bracing", JarakBaut, bd);
                EdgeRule(r, "Baut ke tepi kaki bracing", lf.Width - Gauge, bd);
                r.Rule(JumlahBaut >= 2, "Minimal 2 baut (" + JumlahBaut + ")", "praktik umum");
                r.Rule(lf.Boltable, "Bracing terbuka (bisa dibaut)", "hollow/pipa: pakai metode Las");
                if (lf.Upstand > 0) r.Rule(Gauge >= lf.Thick + bd.WasherOD / 2 - 1e-6, "Ring bebas dari kaki tegak siku", "ruang kunci, praktik umum");
            }
            r.Rule(SudutBrace >= 20 && SudutBrace <= 160, "Sudut bracing " + Fmt.N(SudutBrace) + "° di antara 20°-160°", "geometri");
            if (Muka == HostFace.Sayap) r.Rule(Math.Abs(zMid) + tg / 2 <= Sec.B(host) / 2 + 1e-6, "Pelat buhul berada di dalam lebar sayap host", "geometri");
            WeldRule(r, "Pelat buhul ke host", Las, tg, Muka == HostFace.Sayap ? Sec.Tf(host) : Sec.Tw(host));

            r.Views.Add(new ViewSpec("TAMPAK BIDANG BRACING", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            if (bolt)
            {
                var chain = new List<double> { s0 }; chain.AddRange(stations);
                for (int i = 1; i < chain.Count; i++) r.Dim(0, uB * chain[i - 1] + pB * (hi + 5), uB * chain[i] + pB * (hi + 5), pB, 40);
            }
            r.Note(0, uB * sEnd + pB * line, br.Name + (bolt ? ", " + JumlahBaut + " baut " + Baut : ", las a=" + Fmt.N(Las)) + "\\PPelat buhul t=" + Fmt.N(tg) + ", las a=" + Fmt.N(Las) + " ke host", 2, 2);
            return r;
        }

        static Vec2? Corner(double f, Vec3 u2, Vec3 n2, double f2)
        {
            // perpotongan garis y = f dengan garis n2·p = f2
            double nx = n2.X, ny = n2.Y;
            if (Math.Abs(nx) < 1e-9) return null;
            double x = (f2 - ny * f) / nx;
            return new Vec2(x, f);
        }
    }

    // =========================================================================================
    /// <summary>Persilangan dua bracing siku/plat: satu baut + pelat pengisi. Frame: asal = titik silang, Z = normal bidang.</summary>
    public class CrossBracing : Connection
    {
        public override ConnType Type => ConnType.SilangBracing;
        public override string Title => "Persilangan bracing (pelat pengisi + baut)";

        [Category("1. Batang"), DisplayName("Bracing A"), TypeConverter(typeof(ProfilesLight))] public string ProfilA { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Bracing B"), TypeConverter(typeof(ProfilesLight))] public string ProfilB { get; set; } = "L75x75x8";
        [Category("1. Batang"), DisplayName("Sudut antar bracing (°)")] public double Sudut { get; set; } = 70;
        [Category("2. Sambung"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M16;
        [Category("2. Sambung"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas8_8;
        [Category("2. Sambung"), DisplayName("Tebal pelat pengisi (mm)")] public double TebalPengisi { get; set; }
        [Category("2. Sambung"), DisplayName("Sisi pelat pengisi (mm)")] public double SisiPengisi { get; set; }

        public override void Recommend()
        {
            var a = LegFace.Of(Prof(ProfilA, "A")).First(); var b = LegFace.Of(Prof(ProfilB, "B")).First();
            double w = Math.Min(a.Width, b.Width);
            Baut = w < 60 ? BoltSize.M12 : w < 90 ? BoltSize.M16 : BoltSize.M20;
            TebalPengisi = Fmt.StdPlate(Math.Max(a.Thick, b.Thick) + 2);
            SisiPengisi = Fmt.Round5(Math.Max(2.5 * BoltData.Of(Baut).D + 10, 60));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var pa = Prof(ProfilA, "A"); var pb = Prof(ProfilB, "B");
            var la = LegFace.Of(pa).FirstOrDefault() ?? throw new ArgumentException("Bracing A tidak didukung");
            var lb = LegFace.Of(pb).FirstOrDefault() ?? throw new ArgumentException("Bracing B tidak didukung");
            var bd = BoltData.Of(Baut);
            double tf = TebalPengisi, s = SisiPengisi;
            var uA = Vec3.UnitX; var uB = Geo.Dir2(Sudut);
            double lineA = la.Heel + la.GaugeDefault, lineB = lb.Heel + lb.GaugeDefault;
            // titik baut = perpotongan garis gauge kedua batang
            var pA = Geo.Perp(uA); var pB = Geo.Perp(uB);
            double den = uA.Dot(pB);
            double sa = Math.Abs(den) < 1e-6 ? 0 : (lineB - lineA * pA.Dot(pB)) / den;
            var bp = uA * sa + pA * lineA;
            var fill = r.Box(PartKind.Pelat, "pelat pengisi", bp.X - s / 2, bp.X + s / 2, bp.Y - s / 2, bp.Y + s / 2, -tf / 2, tf / 2);
            fill.Holes.Add(Shapes.Circle(bp.X, bp.Y, bd.Hole / 2));
            r.AddBoq("Pelat", Pl(s, s, tf) + " (pengisi silang)", 1, fill.MassKg());
            double len = r.Bolt(new Vec3(bp.X, bp.Y, tf / 2 + la.Thick), -Vec3.UnitZ, la.Thick + tf + lb.Thick, bd);
            BoltBoq(r, Baut, Mutu, len, 1, "silang");
            double L = 4 * Math.Max(la.Width, lb.Width) + 200;
            var fa = new Frame(new Vec3(0, 0, tf / 2), uA, pA, Vec3.UnitZ);
            r.Prism(PartKind.Batang, pa.Name, fa, Shapes.Rect(-L, Math.Min(la.Heel, la.Heel + la.Width), L, Math.Max(la.Heel, la.Heel + la.Width)), 0, la.Thick);
            if (la.Upstand > 0) r.Prism(PartKind.Batang, pa.Name, fa, Shapes.Rect(-L, la.Heel, L, la.Heel + la.Thick), 0, la.Upstand);
            var fb = new Frame(new Vec3(0, 0, -tf / 2), uB, pB * -1, -Vec3.UnitZ);
            r.Prism(PartKind.Batang, pb.Name, fb, Shapes.Rect(-L, -lb.Heel - lb.Width, L, -lb.Heel), 0, lb.Thick);
            if (lb.Upstand > 0) r.Prism(PartKind.Batang, pb.Name, fb, Shapes.Rect(-L, -lb.Heel - lb.Thick, L, -lb.Heel), 0, lb.Upstand);

            EdgeRule(r, "Baut ke tepi pelat pengisi", s / 2, bd);
            EdgeRule(r, "Baut ke tepi kaki A", la.Width - la.GaugeDefault, bd);
            EdgeRule(r, "Baut ke tepi kaki B", lb.Width - lb.GaugeDefault, bd);
            r.Rule(Sudut >= 20 && Sudut <= 160, "Sudut silang " + Fmt.N(Sudut) + "° di antara 20°-160°", "geometri");
            r.Rule(la.Boltable && lb.Boltable, "Kedua bracing bisa dibaut", "geometri");

            r.Views.Add(new ViewSpec("TAMPAK BIDANG BRACING", Vec3.UnitX, Vec3.UnitY));
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitY, Vec3.UnitZ));
            r.Note(0, bp, "1 baut " + Baut + " + pelat pengisi " + Fmt.N(s) + "x" + Fmt.N(s) + "x" + Fmt.N(tf) + "\\PBracing B di sisi lain pelat pengisi", 2, 2);
            r.Dim(1, new Vec3(0, bp.Y, -tf / 2), new Vec3(0, bp.Y, tf / 2), Vec3.UnitY, 40);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>
    /// Flange brace PEB: dua siku dari sayap bawah rafter ke badan gording, menahan tekuk sayap bawah.
    /// Frame sama dengan cleat gording: asal = muka atas sayap rafter di bawah gording, X = arah gording, Z = normal sayap.
    /// </summary>
    public class FlangeBrace : Connection
    {
        public override ConnType Type => ConnType.FlangeBrace;
        public override string Title => "Flange brace (sayap bawah rafter ke gording)";

        [Category("1. Profil"), DisplayName("Rafter"), TypeConverter(typeof(ProfilesIH))] public string Rafter { get; set; } = "WF400x200x8x13";
        [Category("1. Profil"), DisplayName("Gording"), TypeConverter(typeof(ProfilesPurlin))] public string Gording { get; set; } = "C150x65x20x3.2";
        [Category("1. Profil"), DisplayName("Siku flange brace"), TypeConverter(typeof(ProfilesAngle))] public string Siku { get; set; } = "L50x50x5";
        [Category("2. Pemasangan"), DisplayName("Dua sisi")] public bool DuaSisi { get; set; } = true;
        [Category("2. Pemasangan"), DisplayName("Tinggi pengaku di atas sayap bawah (mm)")] public double TinggiPengaku { get; set; } = 100;
        [Category("2. Pemasangan"), DisplayName("Tebal pengaku (mm)")] public double TebalPengaku { get; set; } = 8;
        [Category("3. Baut"), DisplayName("Diameter baut")] public BoltSize Baut { get; set; } = BoltSize.M12;
        [Category("3. Baut"), DisplayName("Mutu baut")] public BoltGrade Mutu { get; set; } = BoltGrade.Kelas4_6;
        [Category("3. Baut"), DisplayName("Jarak baut ke ujung siku (mm)")] public double JarakUjung { get; set; }
        [Category("3. Baut"), DisplayName("Las pengaku a (mm)")] public double Las { get; set; } = 5;

        public override void Recommend()
        {
            var bd = BoltData.Of(Baut);
            JarakUjung = Fmt.Round5(Math.Max(bd.MinEdge, 1.5 * bd.D));
            TebalPengaku = Fmt.StdPlate(Math.Max(8, Sec.Tw(Prof(Rafter, "Rafter"))));
            Las = Math.Max(4, Fmt.MinFillet(Math.Min(TebalPengaku, Sec.Tw(Prof(Rafter, "Rafter")))));
        }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var raf = Prof(Rafter, "Rafter"); var g = Prof(Gording, "Gording"); var L = Prof(Siku, "Siku");
            var bd = BoltData.Of(Baut);
            double Hr = Sec.H(raf), Br = Sec.B(raf), twr = Sec.Tw(raf), tfr = Sec.Tf(raf), Hp = Sec.H(g), tp = g["t"], rp = g["r"];
            double face = g.Family == Family.CNP ? -Sec.B(g) / 2 : -tp / 2;
            double t = L["t"], leg = L["B"], gauge = Fmt.Round5(Math.Max(t + bd.WasherOD / 2 + 2, Math.Min(0.55 * leg, leg - bd.MinEdge)));
            double ts = TebalPengaku, hs = TinggiPengaku, e = JarakUjung;
            double zB = -Hr + tfr + hs / 2, zT = Hp / 2;
            int sides = DuaSisi ? 2 : 1;
            double lenBrace = 0, lenBolt1 = 0, lenBolt2 = 0;
            foreach (int s in DuaSisi ? new[] { 1, -1 } : new[] { 1 })
            {
                double xB = s * (twr / 2 + (Br / 2 - twr / 2) / 2), xT = xB + s * (zT - zB);
                var pBot = new Vec3(xB, face, zB); var pTop = new Vec3(xT, face, zT);
                var dir = (pTop - pBot).Unit;
                var V = new Vec3(dir.Z, 0, -dir.X) * -s;                       // arah kaki dalam bidang XZ
                var U = -Vec3.UnitY;
                var W = U.Cross(V);
                if (W.Dot(dir) < 0) { V = V * -1; W = U.Cross(V); }
                double len = (pTop - pBot).Length + 2 * e;
                var o = pBot - dir * e - V * gauge;
                var lp = new List<Vec2> { new Vec2(0, 0), new Vec2(leg, 0), new Vec2(leg, t), new Vec2(t, t), new Vec2(t, leg), new Vec2(0, leg) };
                var sk = r.Prism(PartKind.Siku, L.Name, new Frame(o, U, V, W), Shapes.Filleted(lp, null), 0, len);
                lenBrace = len;
                // pengaku di rafter (bidang XZ, muka di y = face)
                var st = r.Box(PartKind.Pengaku, "", s > 0 ? twr / 2 : -Br / 2, s > 0 ? Br / 2 : -twr / 2, face, face + ts, -Hr + tfr, -Hr + tfr + hs);
                r.AddBoq("Pelat", Pl((Br - twr) / 2, hs, ts) + " (pengaku flange brace)", 1, st.MassKg());
                lenBolt1 = r.Bolt(new Vec3(pTop.X, face - t, pTop.Z), Vec3.UnitY, t + tp, bd);
                lenBolt2 = r.Bolt(new Vec3(pBot.X, face - t, pBot.Z), Vec3.UnitY, t + ts, bd);
                r.AddBoq("Siku", L.Name + " L=" + Fmt.N(Math.Round(len)) + " (flange brace)", 1, L.Properties().KgPerM(Env.Density) * len / 1000);
            }
            BoltBoq(r, Baut, Mutu, lenBolt1, sides, "ke gording");
            BoltBoq(r, Baut, Mutu, lenBolt2, sides, "ke pengaku rafter");
            WeldBoq(r, Las, sides * 2 * ((Br - twr) / 2 + hs), "pengaku ke badan & sayap rafter");
            double Lg = Math.Max(2 * Hr, 600);
            Stub(r, g, new Frame(new Vec3(-Lg, 0, Hp / 2), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 0, 2 * Lg);
            Stub(r, raf, new Frame(new Vec3(0, -Lg / 2, -Hr / 2), -Vec3.UnitX, Vec3.UnitZ, Vec3.UnitY), 0, Lg);

            r.Rule(Math.Abs(Math.Atan2(zT - zB, Math.Abs(zT - zB)) * 180 / Math.PI - 45) < 1, "Kemiringan flange brace 45°", "praktik umum PEB");
            EdgeRule(r, "Baut ke ujung siku", e, bd);
            EdgeRule(r, "Baut ke tepi kaki siku", leg - gauge, bd);
            r.Rule(gauge >= t + bd.WasherOD / 2 - 1e-6, "Ring bebas dari kaki tegak siku", "ruang kunci, praktik umum");
            r.Rule(zT - bd.WasherOD / 2 >= tp + rp - 1e-6, "Baut atas di badan gording, bebas dari sayap bawah", "geometri");
            EdgeRule(r, "Baut ke tepi pengaku", hs / 2, bd);
            WeldRule(r, "Pengaku ke rafter", Las, ts, twr);

            r.Views.Add(new ViewSpec("POTONGAN MELINTANG RAFTER", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK SAMPING", Vec3.UnitY, Vec3.UnitZ));
            r.Note(0, new Vec3(0, face, zT), sides + " " + L.Name + " L=" + Fmt.N(Math.Round(lenBrace)) + " @45°, baut " + Baut + "\\PPengaku t=" + Fmt.N(ts) + " di rafter", 2, 2);
            return r;
        }
    }

    // =========================================================================================
    /// <summary>Sag rod menembus badan gording: ring + mur di kedua sisi. Frame: asal = as rod di bidang tengah badan, Z = normal badan.</summary>
    public class SagRod : Connection
    {
        public override ConnType Type => ConnType.SagRod;
        public override string Title => "Sag rod ke gording";

        [Category("1. Batang"), DisplayName("Gording"), TypeConverter(typeof(ProfilesPurlin))] public string Gording { get; set; } = "C150x65x20x3.2";
        [Category("1. Batang"), DisplayName("Rod"), TypeConverter(typeof(ProfilesRod))] public string Rod { get; set; } = "RD12";
        [Category("1. Batang"), DisplayName("Sudut rod terhadap normal badan (°)")] public double Sudut { get; set; }
        [Category("2. Pengikat"), DisplayName("Mur di kedua sisi")] public bool DuaSisi { get; set; } = true;

        public override void Recommend() { }

        public override ConnResult Build()
        {
            var r = new ConnResult();
            var g = Prof(Gording, "Gording"); var rd = Prof(Rod, "Rod");
            double d = rd["D"], t = g["t"], a = Sudut * Math.PI / 180;
            var b = BoltData.Of(BracingRod.NearestBolt(d));
            var uR = new Vec3(Math.Sin(a), 0, Math.Cos(a));
            double c = Math.Max(Math.Cos(a), 0.3);
            var back = uR * (-(t / 2) / c); var front = uR * ((t / 2) / c);
            r.Cylinder(PartKind.Ring, "", back - uR * b.WasherT, uR, b.WasherOD / 2, b.WasherT);
            r.Hex(PartKind.Mur, "", back - uR * (b.WasherT + b.Nut), uR, b.Across, b.Nut);
            if (DuaSisi)
            {
                r.Cylinder(PartKind.Ring, "", front, uR, b.WasherOD / 2, b.WasherT);
                r.Hex(PartKind.Mur, "", front + uR * b.WasherT, uR, b.Across, b.Nut);
            }
            r.TrimSupported = back - uR * (b.WasherT + b.Nut + 3 * b.Pitch);
            r.Cylinder(PartKind.Batang, rd.Name, r.TrimSupported.Value, uR, d / 2, 600);
            r.AddBoq("Mur", "Mur " + b.Size + " + ring (sag rod)", DuaSisi ? 2 : 1, 0);
            double Lg = 500;
            // badan gording di bidang XY (tebal arah Z), gording sepanjang X
            Stub(r, g, new Frame(new Vec3(Lg, 0, g.Family == Family.CNP ? Sec.B(g) / 2 - t / 2 : 0), Vec3.UnitZ, Vec3.UnitY, -Vec3.UnitX), 0, 2 * Lg);
            r.Rule(d >= 10 && d <= 16, "Sag rod Ø" + Fmt.N(d) + " (10-16 mm)", "praktik umum PEB");
            r.Rule(Sudut <= 30, "Sudut rod " + Fmt.N(Sudut) + "° ≤ 30° (ring masih duduk rata)", "praktik umum");
            r.Rule(Sec.H(g) >= 4 * d, "Tinggi gording cukup untuk lubang rod", "geometri");
            r.Views.Add(new ViewSpec("POTONGAN", Vec3.UnitX, Vec3.UnitZ));
            r.Views.Add(new ViewSpec("TAMPAK BADAN", Vec3.UnitX, Vec3.UnitY));
            r.Note(0, Vec3.Zero, rd.Name + " sag rod, lubang Ø" + Fmt.N(d + 2) + " di badan gording, ring + mur " + (DuaSisi ? "kedua sisi" : "satu sisi"), 2, 2);
            return r;
        }
    }
}
