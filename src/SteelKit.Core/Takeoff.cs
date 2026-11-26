using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SteelKit.Core
{
    /// <summary>Kategori pengelompokan model (dipakai browser dan quantity takeoff).</summary>
    public static class Categories
    {
        public const string Sub = "Struktur Bawah";
        public const string Super = "Struktur Atas";
        public const string Roof = "Atap";
        public const string Secondary = "Sekunder";
        public static readonly string[] All = { Sub, Super, Roof, Secondary };

        public static string English(string c)
        {
            switch (c)
            {
                case Sub: return "Sub-structure";
                case Super: return "Super-structure";
                case Roof: return "Roof";
                case Secondary: return "Secondary";
                default: return c;
            }
        }

        /// <summary>Jenis batang dari geometri: (jenis, kategori, awalan marking).</summary>
        public static (string role, string category, string prefix) Classify(Family f, Vec3 s, Vec3 e)
        {
            var d = (e - s).Unit;
            bool vertical = Math.Abs(d.Z) > 0.98;
            double slope = Math.Asin(Math.Max(-1, Math.Min(1, Math.Abs(d.Z)))) * 180 / Math.PI;
            switch (f)
            {
                case Family.CNP: case Family.Z:
                    return vertical ? ("Girt post", Secondary, "GP") : ("Purlin / girt", Roof, "P");
                case Family.RD: return ("Rod bracing", Super, "RB");
                case Family.L: case Family.PL: case Family.T:
                    return vertical ? ("Post", Secondary, "PO") : ("Bracing", Super, "BR");
                case Family.UNP: return ("Stringer / channel", Secondary, "ST");
                case Family.CHS: case Family.HOLLOW:
                    return vertical ? ("Column", Super, "C") : ("Beam", Secondary, "SB");
                default:
                    if (vertical) return ("Column", Super, "C");
                    if (slope > 2 && slope < 60) return ("Rafter", Roof, "R");
                    if (slope >= 60) return ("Bracing", Super, "BR");
                    return ("Beam", Super, "B");
            }
        }
    }

    /// <summary>Tabel level: nama + elevasi (mm). Batang masuk level tertinggi yang elevasinya ≤ titik terendah batang + toleransi.</summary>
    public class LevelTable
    {
        public List<(string name, double z)> Levels = new List<(string, double)>();

        public static LevelTable Parse(string s)
        {
            var t = new LevelTable();
            foreach (var part in (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && double.TryParse(kv[1].Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                    t.Levels.Add((kv[0].Trim(), z));
            }
            if (t.Levels.Count == 0) t.Levels.Add(("Level 0", 0));
            t.Levels = t.Levels.OrderBy(l => l.z).ToList();
            return t;
        }

        public string Serialize() => string.Join(";", Levels.Select(l => l.name + ":" + l.z.ToString("0.###", CultureInfo.InvariantCulture)));

        public string Of(double zMin, double tol = 50)
        {
            string name = Levels[0].name;
            foreach (var l in Levels) if (zMin + tol >= l.z) name = l.name;
            return name;
        }
    }

    public class TakeoffMember
    {
        public string Handle, Section, Mark, Level, Category, Role;
        /// <summary>Pola lubang & potongan ujung (lihat PartSignature): batang dengan pola berbeda mendapat marking berbeda.</summary>
        public string Signature = "";
        public Family Family;
        public double LengthMm, KgPerM, PaintPerM;
    }

    public class TakeoffConnection
    {
        public ConnType Type;
        public List<BoqLine> Boq;
        public double SteelKg => Boq.Where(b => b.Item != "Grout" && b.Item != "Las").Sum(b => b.TotalKg);
        public double WeldMm;
        public double GroutM3;
        public double PlateAreaM2;      // luas permukaan pelat untuk cat (dua sisi)
    }

    /// <summary>Quantity takeoff dari model: pengelompokan batang, marking otomatis, rekap sambungan.</summary>
    public static class Takeoff
    {
        /// <summary>Beri marking otomatis per kategori+profil+panjang (pengguna boleh menimpa).</summary>
        public static void AutoMark(List<TakeoffMember> ms)
        {
            // satu marking = bagian yang benar-benar sama di bengkel: peran, profil, panjang, dan pola lubang/potongan
            var groups = ms.GroupBy(m => (m.Role, m.Section, Math.Round(m.LengthMm), m.Signature ?? "")).OrderBy(g => g.Key.Role).ThenByDescending(g => g.First().KgPerM)
                .ThenBy(g => g.Key.Item3).ThenBy(g => g.Key.Item4.Length).ThenBy(g => g.Key.Item4, StringComparer.Ordinal).ToList();
            var counters = new Dictionary<string, int>();
            foreach (var g in groups)
            {
                var first = g.First();
                string prefix = Categories.Classify(first.Family, Vec3.Zero, first.Role == "Column" ? Vec3.UnitZ : Vec3.UnitX).prefix;
                prefix = PrefixOf(first.Role, prefix);
                counters[prefix] = counters.TryGetValue(prefix, out int n) ? n + 1 : 1;
                foreach (var m in g) if (string.IsNullOrWhiteSpace(m.Mark)) m.Mark = prefix + counters[prefix];
            }
        }

        static string PrefixOf(string role, string fallback)
        {
            switch (role)
            {
                case "Column": return "C";
                case "Beam": return "B";
                case "Rafter": return "R";
                case "Bracing": return "BR";
                case "Rod bracing": return "RB";
                case "Purlin / girt": return "P";
                case "Girt post": return "GP";
                case "Post": return "PO";
                case "Stringer / channel": return "ST";
                default: return fallback;
            }
        }

        static readonly (string id, string en)[] Words =
        {
            ("pelat ujung lutut", "knee end plate"), ("pelat ujung", "end plate"), ("pelat dasar", "base plate"), ("pelat buhul bracing", "bracing gusset plate"),
            ("pelat buhul truss", "truss gusset plate"), ("pelat buhul", "gusset plate"), ("pelat sirip", "fin plate"), ("pelat tutup kolom", "column cap plate"),
            ("pelat tutup", "cap plate"), ("pelat sayap", "flange plate"), ("pelat sambung sayap", "flange splice plate"), ("pelat sambung badan", "web splice plate"),
            ("pelat kepala kolom angin", "wind column head plate"), ("pelat dudukan rel/balok crane", "crane seat plate"), ("pelat dinding", "wall plate"),
            ("pelat tanam", "embed plate"), ("pelat mata clevis", "clevis eye plate"), ("pelat bawah angkur", "anchor bottom plate"), ("pelat pengisi", "filler plate"),
            ("pengisi silang", "crossing filler"), ("pengisi", "filler"), ("pelat penahan las", "weld backing bar"), ("pengaku kolom", "column stiffener"),
            ("pengaku balok", "beam stiffener"), ("pengaku dudukan", "seat stiffener"), ("pengaku flange brace", "flange brace stiffener"), ("pengaku", "stiffener"),
            ("badan haunch", "haunch web"), ("sayap haunch", "haunch flange"), ("shear key", "shear key"), ("siku dudukan", "seat angle"), ("siku atas", "top angle"),
            ("dudukan anak tangga", "stair tread seat"), ("Pelat bordes", "Checker plate"), ("anak tangga", "stair tread"), ("flange brace", "flange brace"),
            ("selongsong", "sleeve"), ("konsol", "bracket"), ("cleat gording", "purlin cleat"),
            ("Las tumpul penetrasi penuh (CJP) sayap balok", "Full penetration butt weld (CJP) beam flanges"), ("Las CJP sayap konsol ke kolom", "CJP weld bracket flanges to column"),
            ("Las sudut", "Fillet weld"), ("Las keliling", "All-around weld"), ("panjang", "length"), ("kedua sisi", "both sides"), ("dua sisi", "both sides"),
            ("keliling", "all around"), ("ke kolom", "to column"), ("ke host", "to host"), ("ke penopang", "to support"), ("ke sayap balok", "to beam flange"),
            ("ke badan & sayap rafter", "to rafter web & flange"), ("ke gording", "to purlin"), ("ke pengaku rafter", "to rafter stiffener"),
            ("ke pelat buhul", "to gusset plate"), ("ke pelat dinding", "to wall plate"), ("ke muka kolom", "to column face"), ("ke dinding kolom", "to column wall"),
            ("kolom ke pelat tutup", "column to cap plate"), ("kolom ke pelat kepala", "column to head plate"), ("badan konsol", "bracket web"),
            ("cabang ke batang tepi", "branch to chord"), ("batang ke pelat buhul", "members to gusset plate"), ("bracing ke pelat buhul", "bracing to gusset plate"),
            ("pelat tembus", "through plate"), ("pelat tempel", "face plate"), ("lubang slot", "slotted holes"), ("lubang", "hole"),
            ("Ring miring (hillside washer)", "Hillside washer"), ("Ring plat", "Plate washer"), ("+ mur + 2 ring", "+ nut + 2 washers"), ("+ mur", "+ nut"),
            ("+ ring", "+ washer"), ("2 mur kunci", "2 lock nuts"), ("mur", "nut"), ("ring", "washer"), ("badan terbuka", "open body"),
            ("Angkur kimia", "Chemical anchor"), ("Angkur", "Anchor"), ("angkur L (kait 90°)", "L-hook anchor"), ("angkur J (kait 180°)", "J-hook anchor"),
            ("angkur berkepala (headed)", "headed anchor"), ("angkur ulir penuh + mur & pelat bawah", "threaded rod + nut & bottom plate"),
            ("angkur kimia (chemical)", "chemical anchor"), ("ulir", "thread"), ("Stud berkepala", "Headed stud"), ("dilas ke pelat", "welded to plate"),
            ("Grout non-susut", "Non-shrink grout"), ("badan", "web"), ("sayap", "flange"), ("silang", "crossing"), ("bracing", "bracing"),
            ("Pelat penahan las (backing bar)", "Weld backing bar"), ("bordes ke siku (kepala benam)", "tread to angle (countersunk)"),
            ("siku ke ibu tangga", "angle to stringer"), ("siku ke penopang", "angle to support"), ("siku ke sayap balok", "angle to beam flange"),
            ("pelat ke", "plate to"), ("Pelat ", "Plate "), ("pelat ", "plate "), ("Baut ", "Bolt "), ("kimia", "chemical"), ("Mur ", "Nut "),
            ("gording", "purlin"), ("kolom", "column"), ("balok", "beam"), ("rafter", "rafter"), ("dinding", "wall"), ("bawah", "bottom"), ("atas", "top"),
        };

        public static string English(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            foreach (var w in Words.OrderByDescending(x => x.id.Length))
            {
                int i = 0;
                while ((i = s.IndexOf(w.id, i, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    s = s.Substring(0, i) + w.en + s.Substring(i + w.id.Length);
                    i += w.en.Length;
                }
            }
            return s;
        }

        public static string English(ConnType t)
        {
            switch (t)
            {
                case ConnType.PelatDasar: return "Column base plate + anchors";
                case ConnType.PelatUjung: return "Beam-column end plate";
                case ConnType.FinPlate: return "Fin plate";
                case ConnType.SikuGanda: return "Double angle cleat";
                case ConnType.SambunganPelatUjung: return "End plate splice / apex";
                case ConnType.LututHaunch: return "Portal knee + haunch";
                case ConnType.CleatGording: return "Purlin cleat";
                case ConnType.PelatBuhul: return "Light member gusset";
                case ConnType.BracingRod: return "Rod bracing end";
                case ConnType.Turnbuckle: return "Turnbuckle";
                case ConnType.BracingBuhul: return "Bracing gusset to column/beam";
                case ConnType.SilangBracing: return "Bracing crossing";
                case ConnType.FlangeBrace: return "Flange brace";
                case ConnType.SagRod: return "Sag rod";
                case ConnType.PelatTutup: return "Column cap plate";
                case ConnType.Dudukan: return "Seated angle";
                case ConnType.MomenLas: return "Welded moment connection";
                case ConnType.MomenPelatSayap: return "Bolted flange plate moment";
                case ConnType.KolomHollow: return "Beam to hollow column";
                case ConnType.BalokTumpuk: return "Stacked beams";
                case ConnType.KolomAngin: return "Wind column to rafter";
                case ConnType.KonsolCrane: return "Crane bracket";
                case ConnType.PelatBeton: return "Beam to concrete";
                case ConnType.SplicePelatSambung: return "Bolted cover plate splice";
                case ConnType.GordingSambung: return "Purlin splice (lap/sleeve)";
                case ConnType.BuhulTruss: return "Truss node gusset";
                case ConnType.LasLangsung: return "Direct welded tube joint";
                case ConnType.AnakTangga: return "Stair tread";
                default: return t.ToString();
            }
        }

        static string ItemEn(string item)
        {
            switch (item)
            {
                case "Pelat": return "Plate";
                case "Siku": return "Angle";
                case "Profil": return "Section";
                case "Baut": return "Bolt";
                case "Mur": return "Nut";
                case "Ring": return "Washer";
                case "Ring plat": return "Plate washer";
                case "Angkur": return "Anchor";
                case "Grout": return "Grout";
                case "Las": return "Weld";
                default: return item;
            }
        }

        /// <summary>Buku kerja quantity takeoff (bahasa Inggris, satuan metrik).</summary>
        public static Xlsx.Workbook Build(string project, List<TakeoffMember> members, List<TakeoffConnection> conns, double allowancePct = 5)
        {
            var ci = CultureInfo.InvariantCulture;
            var wb = new Xlsx.Workbook();
            AutoMark(members);
            // ---- Members
            var ms = wb.Add("Members", new[] { "Mark", "Level", "Category", "Member type", "Section", "Qty", "Length each (mm)", "Total length (m)", "Unit weight (kg/m)", "Total weight (kg)", "Paint area (m2)" },
                new double[] { 8, 12, 16, 18, 22, 6, 14, 14, 14, 14, 13 });
            var mg = members.GroupBy(m => (m.Mark, m.Level, m.Category, m.Role, m.Section, L: Math.Round(m.LengthMm))).OrderBy(g => g.Key.Level).ThenBy(g => g.Key.Category).ThenBy(g => g.Key.Mark).ToList();
            foreach (var g in mg)
            {
                int r = ms.Row;
                var m0 = g.First();
                ms.AddRow(g.Key.Mark, g.Key.Level, Categories.English(g.Key.Category), g.Key.Role, g.Key.Section, (double)g.Count(), g.Key.L,
                    Xlsx.F("=F{0}*G{0}/1000", r), m0.KgPerM, Xlsx.F("=H{0}*I{0}", r), Math.Round(m0.PaintPerM * g.Sum(x => x.LengthMm) / 1000, 3));
            }
            int mEnd = ms.Row - 1;
            ms.AddTotal("TOTAL", new Dictionary<int, string> { [5] = "=SUM(F2:F" + mEnd + ")", [7] = "=SUM(H2:H" + mEnd + ")", [9] = "=SUM(J2:J" + mEnd + ")", [10] = "=SUM(K2:K" + mEnd + ")" });
            int mTotal = ms.Row - 1;

            // ---- Connections
            var cs = wb.Add("Connections", new[] { "Connection type", "Qty", "Steel each (kg)", "Steel total (kg)" }, new double[] { 34, 8, 16, 16 });
            foreach (var g in conns.GroupBy(c => c.Type).OrderBy(g => g.Key.ToString()))
            {
                int r = cs.Row;
                cs.AddRow(English(g.Key), (double)g.Count(), Math.Round(g.Average(c => c.SteelKg), 2), Xlsx.F("=B{0}*C{0}", r));
            }
            int cEnd = cs.Row - 1;
            cs.AddTotal("TOTAL", new Dictionary<int, string> { [1] = "=SUM(B2:B" + Math.Max(cEnd, 2) + ")", [3] = "=SUM(D2:D" + Math.Max(cEnd, 2) + ")" });
            int cTotal = cs.Row - 1;

            var allLines = conns.SelectMany(c => c.Boq).ToList();
            // ---- Plates & sections
            var ps = wb.Add("Plates & Sections", new[] { "Item", "Description", "Qty", "Unit weight (kg)", "Total weight (kg)" }, new double[] { 12, 60, 8, 16, 16 });
            foreach (var g in allLines.Where(b => b.Item == "Pelat" || b.Item == "Siku" || b.Item == "Profil" || b.Item == "Ring plat").GroupBy(b => (b.Item, b.Spec)).OrderBy(g => g.Key.Item).ThenBy(g => g.Key.Spec))
            {
                int r = ps.Row;
                ps.AddRow(ItemEn(g.Key.Item), English(g.Key.Spec), (double)g.Sum(b => b.Count), Math.Round(g.First().UnitKg, 3), Xlsx.F("=C{0}*D{0}", r));
            }
            int pEnd = ps.Row - 1;
            ps.AddTotal("TOTAL", new Dictionary<int, string> { [4] = "=SUM(E2:E" + Math.Max(pEnd, 2) + ")" });
            int pTotal = ps.Row - 1;

            // ---- Bolts & anchors
            var bs = wb.Add("Bolts & Anchors", new[] { "Item", "Description", "Qty", "Unit weight (kg)", "Total weight (kg)" }, new double[] { 14, 64, 8, 16, 16 });
            foreach (var g in allLines.Where(b => b.Item == "Baut" || b.Item == "Angkur" || b.Item == "Mur" || b.Item == "Ring" || b.Item == "Turnbuckle" || b.Item == "Clevis").GroupBy(b => (b.Item, b.Spec)).OrderBy(g => g.Key.Item).ThenBy(g => g.Key.Spec))
            {
                int r = bs.Row;
                bs.AddRow(ItemEn(g.Key.Item), English(g.Key.Spec), (double)g.Sum(b => b.Count), Math.Round(g.First().UnitKg, 3), Xlsx.F("=C{0}*D{0}", r));
            }
            int bEnd = bs.Row - 1;
            bs.AddTotal("TOTAL", new Dictionary<int, string> { [2] = "=SUM(C2:C" + Math.Max(bEnd, 2) + ")", [4] = "=SUM(E2:E" + Math.Max(bEnd, 2) + ")" });

            // ---- Welds & others
            var ws = wb.Add("Welds & Others", new[] { "Item", "Description", "Qty", "Unit", "Total" }, new double[] { 12, 70, 8, 8, 14 });
            foreach (var g in allLines.Where(b => b.Item == "Las").GroupBy(b => b.Spec.Split(new[] { ", panjang" }, StringSplitOptions.None)[0]))
            {
                double mm = g.Sum(b => LengthOf(b.Spec) * b.Count);
                ws.AddRow("Weld", English(g.Key), (double)g.Count(), "m", Math.Round(mm / 1000, 2));
            }
            double grout = conns.Sum(c => c.GroutM3);
            if (grout > 0) ws.AddRow("Grout", "Non-shrink grout under base plates", (double)conns.Count(c => c.GroutM3 > 0), "m3", Math.Round(grout, 3));

            // ---- Summary (lembar pertama)
            var su = wb.Add("Summary", new[] { "Description", "Unit", "Quantity", "Note" }, new double[] { 48, 10, 16, 60 }, true);
            su.AddRow("Project", "", project, "Generated by Steel Kit " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", ci));
            su.AddRow("Members (count)", "pcs", Xlsx.F("=Members!F" + mTotal), "");
            su.AddRow("Members total length", "m", Xlsx.F("=Members!H" + mTotal), "");
            int rMem = su.Row;
            su.AddRow("Members steel weight", "kg", Xlsx.F("=Members!J" + mTotal), "From section area x 7850 kg/m3");
            su.AddRow("Connections (count)", "pcs", Xlsx.F("=Connections!B" + cTotal), "");
            int rCon = su.Row;
            su.AddRow("Connection steel (plates, angles, sections)", "kg", Xlsx.F("='Plates & Sections'!E" + pTotal), "Excludes bolts, nuts, washers");
            int rBolt = su.Row;
            su.AddRow("Bolts, anchors, nuts (weight)", "kg", Xlsx.F("='Bolts & Anchors'!E" + (bs.Row - 1)), "Approximate");
            int net = su.Row;
            su.AddRow("TOTAL STEEL (net)", "kg", Xlsx.F("=C" + rMem + "+C" + rCon + "+C" + rBolt), "");
            su.AddRow("Cutting / wastage allowance", "%", allowancePct, "Assumption - adjust per supplier & nesting");
            su.AddRow("Recommended order weight", "kg", Xlsx.F("=C" + net + "*(1+C" + (net + 1) + "/100)"), "");
            su.AddRow("Paint area (members)", "m2", Xlsx.F("=Members!K" + mTotal), "Outer surface; add plates if painted");
            su.AddRow("Paint area (connection plates, both sides)", "m2", Math.Round(conns.Sum(c => c.PlateAreaM2), 2), "Approximate");
            su.AddRow("Grout volume", "m3", Math.Round(grout, 3), "Non-shrink grout");
            su.AddRow("Weld length (fillet + CJP, total)", "m", Math.Round(allLines.Where(b => b.Item == "Las").Sum(b => LengthOf(b.Spec) * b.Count) / 1000, 2), "");
            foreach (var g in allLines.Where(b => b.Item == "Baut").GroupBy(b => b.Spec.Split(' ')[1].Split('x')[0]).OrderBy(g => g.Key))
                su.AddRow("Bolts " + g.Key, "pcs", (double)g.Sum(b => b.Count), "All lengths and grades");
            foreach (var g in allLines.Where(b => b.Item == "Angkur").GroupBy(b => English(b.Spec).Split(' ').FirstOrDefault(w => w.StartsWith("M") && w.Length <= 3 && char.IsDigit(w[w.Length - 1])) ?? "other"))
                su.AddRow("Anchors " + g.Key, "pcs", (double)g.Sum(b => b.Count), "All types");
            su.AddRow("", "", "", "");
            su.AddRow("Note", "", "", "Quantities are from the 3D model geometry. Structural design, capacity and fabrication tolerances by the engineer.");
            return wb;
        }

        static double LengthOf(string spec)
        {
            int i = spec.IndexOf("panjang ", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return 0;
            var rest = spec.Substring(i + 8).Split(' ')[0];
            return double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }
    }
}
