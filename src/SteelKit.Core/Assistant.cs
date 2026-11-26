using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SteelKit.Core
{
    /// <summary>Satu batang usulan dari asisten (koordinat mm relatif titik dasar).</summary>
    public class MemberProposal
    {
        public string Role;          // Kolom, Rafter, Balok, Balok anak, Gording, Girt, Bracing
        public string Profile;
        public Vec3 S, E;
        public double Roll;
        public Anchor Anchor = Anchor.Tengah;
        public double Length => (E - S).Length;
    }

    public class AssistantPlan
    {
        public string Summary = "";
        public List<MemberProposal> Members = new List<MemberProposal>();
        public List<string> Assumptions = new List<string>();
        public List<string> Unknown = new List<string>();
    }

    /// <summary>
    /// Asisten berbasis aturan (tanpa AI, tanpa internet): membaca kalimat bahasa Indonesia sehari-hari
    /// ("portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150 jarak 1,2 m")
    /// lalu mengusulkan batang untuk diperiksa pengguna. Ukuran profil yang tidak disebut diisi contoh dan ditandai.
    /// </summary>
    public static class Assistant
    {
        static readonly Dictionary<string, double> NumberWords = new Dictionary<string, double>
        {
            ["nol"] = 0, ["satu"] = 1, ["dua"] = 2, ["tiga"] = 3, ["empat"] = 4, ["lima"] = 5, ["enam"] = 6, ["tujuh"] = 7, ["delapan"] = 8,
            ["sembilan"] = 9, ["sepuluh"] = 10, ["sebelas"] = 11, ["seratus"] = 100, ["setengah"] = 0.5,
        };

        static readonly string[] Subjects = { "kolom", "rafter", "balok", "gording", "girt", "bracing", "ikatan", "rod", "anak" };
        static readonly string[] StructWords = { "portal", "gudang", "peb", "pelana", "mono", "sepihak", "lantai", "mezanin", "mezzanine", "grid", "hanggar", "workshop", "bengkel",
            "kanopi", "kantilever", "menara", "jembatan", "truss", "duasisi" };

        class Tok { public string Kind, Text; public double Num; public double Num2 = double.NaN; }

        /// <summary>
        /// Istilah teknik yang umum dipakai engineer & drafter (Indonesia/Inggris) dan ejaan yang sering keluar dari pengenal suara,
        /// diseragamkan ke kata kunci asisten. Urutan penting: frasa panjang dulu.
        /// </summary>
        static readonly (string pattern, string to)[] Synonyms =
        {
            (@"\blabar\b|\blebarnya\b", "lebar"),
            (@"(be(l|ll)(a|e|i)s)\b", "belas"),
            (@"\b(pulu|pula|polo|puloh)\b", "puluh"),
            (@"\b(satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan|lema)(belas|puluh|ratus)\b", "$1 $2"),
            (@"\b(lebar|tinggi|bentang|jarak|panjang)(satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan|sepuluh|sebelas|seratus)", "$1 $2"),
            (@"\b(satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan|belas|puluh|ratus)(meter|m)\b", "$1 $2"),
            (@"\b(lema|lemah|limah)\b(?=\s*(belas|puluh|ratus|meter|m\b|bay|portal|segmen|panel))", "lima"),
            (@"\bsegment\b", "segmen"),
            (@"\bgirter\b", "gelagar"),
            (@"\bjembatan\s+rangka\b", "jembatan truss"),
            (@"\brangka\s+batang\b|\bkuda[\s-]*kuda\b|\btrus+\b", "truss"),
            (@"\brigid\s+frame\b|\bframes?\b|\brangka(\s+baja)?\b", "portal"),
            (@"\bcanop(y|i)\b|\bkanop(y|i)\b|\bcar\s?port\b|\bshelter\b|\bselasar\b|\bteras\b", "kanopi"),
            (@"\bcantil(e|i)ver\b|\bkantil(e|i)(v|f)er\b|\boverhang\b", "kantilever"),
            (@"\bwater\s+tower\b|\btower\s+air\b|\btowers?\b|\bmenara(\s+air)?\b|\btandon\b|\btangki\s+air\b", "menara"),
            (@"\bfoot\s?bridge\b|\bbridges?\b|\bjpo\b|\bjembatan\s+penyeberangan(\s+orang)?\b", "jembatan"),
            (@"\bgirders?\b|\bgelagar\b", "gelagar"),
            (@"\bcolumns?\b|\btiang\b|\bkaki\b", "kolom"),
            (@"\bbeams?\b", "balok"),
            (@"\bpurlins?\b", "gording"),
            (@"\bbraces?\b|\bbracing\b|\bikat(an)?\s+angin\b|\bdiagonal\b", "bracing"),
            (@"\bspan\b", "bentang"),
            (@"\bheight\b", "tinggi"),
            (@"\bwidth\b", "lebar"),
            (@"\blength\b", "panjang"),
            (@"\bspacing\b", "jarak"),
            (@"\bmono\s?slope\b|\bsingle\s+slope\b|\bsatu\s+arah\b", "mono"),
            (@"\bdua\s+sisi\b|\bkiri\s+kanan\b|\b(bentuk|tipe)\s+t\b", "duasisi"),
            (@"\bsegmen\b|\bpanel\b", "panel")
        };

        static string Normalize(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            foreach (var (pat, to) in Synonyms) s = Regex.Replace(s, pat, to);
            s = Regex.Replace(s, @"(\d),(\d)", "$1.$2");                       // 1,2 -> 1.2
            s = Regex.Replace(s, @"(\d)\s*koma\s*(\d)", "$1.$2");
            s = s.Replace("°", " derajat ").Replace("×", "x").Replace("meter", " m ").Replace("mtr", " m ");
            // angka yang diucapkan: "empat ratus", "seratus lima puluh", "dua puluh empat", "seribu dua ratus" -> angka
            const string W = "nol|satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan|sepuluh|sebelas|seratus|seribu|belas|puluh|ratus|ribu";
            s = Regex.Replace(s, @"\b(?:" + W + @")(?:\s+(?:" + W + @"))*\b", m => SpokenNumbers(m.Value));
            s = Regex.Replace(s, @"\bsetengah\b", "0.5");
            s = Regex.Replace(s, @"(\d)\s*kali\s*(\d)", "$1x$2");                 // 150 kali 65 -> 150x65
            return s;
        }

        /// <summary>
        /// Rangkaian kata bilangan -> angka. Dua satuan berurutan tanpa pengali ("dua tiga") dianggap dua angka terpisah.
        /// </summary>
        public static string SpokenNumbers(string words)
        {
            var units = new Dictionary<string, int> { ["nol"] = 0, ["satu"] = 1, ["dua"] = 2, ["tiga"] = 3, ["empat"] = 4, ["lima"] = 5, ["enam"] = 6, ["tujuh"] = 7, ["delapan"] = 8, ["sembilan"] = 9 };
            var outp = new List<string>();
            long total = 0, cur = 0; bool any = false, lastUnit = false;
            void Flush() { if (any) outp.Add((total + cur).ToString(CultureInfo.InvariantCulture)); total = 0; cur = 0; any = false; lastUnit = false; }
            foreach (var w in Regex.Split(words.Trim(), @"\s+"))
            {
                if (units.TryGetValue(w, out int u))
                {
                    if (lastUnit) Flush();
                    cur += u; any = true; lastUnit = true; continue;
                }
                lastUnit = false;
                switch (w)
                {
                    case "sepuluh": total += 10; any = true; break;
                    case "sebelas": total += 11; any = true; break;
                    case "seratus": total += 100; any = true; break;
                    case "seribu": total += 1000; any = true; break;
                    case "belas": total += 10 + cur; cur = 0; any = true; break;
                    case "puluh": total += 10 * Math.Max(cur, 1); cur = 0; any = true; break;
                    case "ratus": total += 100 * Math.Max(cur, 1); cur = 0; any = true; break;
                    case "ribu": total = (total + Math.Max(cur, 1)) * 1000; cur = 0; any = true; break;
                }
            }
            Flush();
            return string.Join(" ", outp);
        }

        static List<Tok> Tokenize(string s)
        {
            var toks = new List<Tok>();
            foreach (Match m in Regex.Matches(s, @"(?<prof>(?:wf|iwf|h|cnp|c|z|unp|l|siku|t|shs|rhs|chs|rd|pl|sch40-|sch80-|med-)\s?\d[\d.]*(?:x\d[\d.]*)*)|(?<dim>\d+(?:\.\d+)?\s*x\s*\d+(?:\.\d+)?)|(?<num>\d+(?:\.\d+)?)|(?<w>[a-z%]+)"))
            {
                if (m.Groups["prof"].Success) toks.Add(new Tok { Kind = "P", Text = m.Value.Replace(" ", "").TrimEnd('.') });
                else if (m.Groups["dim"].Success)
                {
                    var p = Regex.Split(m.Value.Replace(" ", ""), "x");
                    toks.Add(new Tok { Kind = "D", Num = Dbl(p[0]), Num2 = Dbl(p[1]) });
                }
                else if (m.Groups["num"].Success) toks.Add(new Tok { Kind = "N", Num = Dbl(m.Value) });
                else toks.Add(new Tok { Kind = "W", Text = m.Value });
            }
            return toks;
        }

        static double Dbl(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Cocokkan teks profil ("wf400", "c150", "siku50", "rd16", "h300x300") ke katalog.</summary>
        public static Profile MatchProfile(string t)
        {
            var cat = Env.Catalog;
            t = t.Replace("iwf", "wf").Replace("siku", "l").Replace("cnp", "c");
            var exact = cat.Find(t);
            if (exact != null) return exact;
            var m = Regex.Match(t, @"^(wf|h|c|z|unp|l|t|shs|rhs|chs|rd|pl)(\d[\d.]*)((?:x\d[\d.]*)*)$");
            if (!m.Success) return null;
            string pre = m.Groups[1].Value.ToUpperInvariant();
            var nums = new List<double> { Dbl(m.Groups[2].Value) };
            nums.AddRange(m.Groups[3].Value.Split(new[] { 'x' }, StringSplitOptions.RemoveEmptyEntries).Select(Dbl));
            IEnumerable<Profile> cands = cat.All.Where(p => p.Name.StartsWith(pre, StringComparison.OrdinalIgnoreCase) && !(pre == "H" && p.Name.StartsWith("HOLLOW")));
            if (pre == "C") cands = cat.OfFamily(Family.CNP);
            if (pre == "H") cands = cat.OfFamily(Family.H);
            if (pre == "WF") cands = cat.OfFamily(Family.WF);
            if (pre == "L") cands = cat.OfFamily(Family.L);
            if (pre == "RD") return cat.OfFamily(Family.RD).OrderBy(p => Math.Abs(p["D"] - nums[0])).FirstOrDefault();
            if (pre == "CHS") cands = cat.OfFamily(Family.CHS);
            var keys = pre == "CHS" ? new[] { "D", "t" } : pre == "L" ? new[] { "H", "B", "t" } : pre == "PL" ? new[] { "B", "t" } : new[] { "H", "B", "tw", "tf" };
            if (pre == "C" || pre == "Z") keys = new[] { "H", "B", "C", "t" };
            if (pre == "SHS" || pre == "RHS") keys = new[] { "H", "B", "t" };
            var best = cands.Where(p => nums.Select((v, i) => i < keys.Length && Math.Abs(p[keys[i]] - v) < 0.6).All(x => x))
                            .OrderBy(p => p.Properties().KgPerM()).ToList();
            if (best.Count == 0) return null;
            // tanpa ukuran lengkap: ambil ukuran tengah (bukan yang paling ringan/berat) untuk siku & C; WF/H ambil seri utama (berat terdekat seri JIS)
            if (nums.Count >= keys.Length) return best[0];
            return pre == "WF" || pre == "H" || pre == "RD" ? best[0] : best[best.Count / 2];
        }

        /// <summary>
        /// Gording/girt diteruskan melewati rafter/kolom ujung (mm), supaya baut cleat ujung mengenai gording dengan jarak tepi
        /// yang layak. Nilai tipikal praktik umum (ASSUMED), bisa diubah lewat grip.
        /// </summary>
        public const double Overhang = 100;

        public static AssistantPlan Parse(string sentence)
        {
            var plan = new AssistantPlan();
            var toks = Tokenize(Normalize(sentence));
            string subject = "struktur", pendingKey = null, structure = null;
            bool mono = false, cant = false, twoSided = false, truss = false;
            var prm = new Dictionary<string, Dictionary<string, double>>();
            var prof = new Dictionary<string, Profile>();
            Func<string, Dictionary<string, double>> P = k => prm.TryGetValue(k, out var d) ? d : (prm[k] = new Dictionary<string, double>());
            for (int i = 0; i < toks.Count; i++)
            {
                var t = toks[i];
                if (t.Kind == "W")
                {
                    string w = t.Text;
                    if (w == "anak" && subject == "balok") { subject = "balok anak"; continue; }
                    if (w == "balok" && i + 1 < toks.Count && toks[i + 1].Text == "anak") { subject = "balok anak"; i++; continue; }
                    if (Subjects.Contains(w)) { subject = w == "ikatan" || w == "rod" ? "bracing" : w; pendingKey = null; continue; }
                    if (StructWords.Contains(w))
                    {
                        if (w == "mono" || w == "sepihak") mono = true;
                        else if (w == "kanopi") structure = "kanopi";
                        else if (w == "kantilever") { cant = true; structure = structure ?? "kanopi"; }
                        else if (w == "menara") structure = "menara";
                        else if (w == "jembatan") structure = "jembatan";
                        else if (w == "truss") { truss = true; }
                        else if (w == "duasisi") { twoSided = true; cant = true; }
                        else if (w == "lantai" || w == "mezanin" || w == "mezzanine") structure = "lantai";
                        else if (w == "grid") structure = structure ?? "grid";
                        else if (structure != "kanopi" && structure != "menara" && structure != "jembatan") structure = "portal";
                        subject = "struktur"; continue;
                    }
                    if (w == "gelagar") { subject = "balok"; P("struktur")["gelagar"] = 1; pendingKey = null; continue; }
                    if (w == "bentang") { pendingKey = "bentang"; continue; }
                    if (w == "lebar") { pendingKey = "lebar"; continue; }
                    if (w == "panjang") { pendingKey = subject == "struktur" ? "panjang" : "panjang"; continue; }
                    if (w == "tinggi" || w == "elevasi") { pendingKey = "tinggi"; continue; }
                    if (w == "kemiringan" || w == "miring" || w == "sudut" || w == "slope") { pendingKey = "miring"; continue; }
                    if (w == "jarak" || w == "spasi" || w == "setiap" || w == "tiap") { pendingKey = "jarak"; continue; }
                    if (w == "jumlah" || w == "sebanyak") { pendingKey = "jumlah"; continue; }
                    if (w == "ukuran" || w == "luas") { pendingKey = "ukuran"; continue; }
                    continue;
                }
                if (t.Kind == "P")
                {
                    var p = MatchProfile(t.Text);
                    if (p == null) { plan.Unknown.Add("profil \"" + t.Text + "\" tidak ditemukan di katalog"); continue; }
                    if (!string.Equals(p.Name.Replace(" ", ""), t.Text.Replace("iwf", "wf").Replace("siku", "l").Replace("cnp", "c"), StringComparison.OrdinalIgnoreCase))
                        plan.Assumptions.Add("Profil \"" + t.Text + "\" dilengkapi menjadi " + p.Name + " (periksa)");
                    string role = subject == "struktur" ? GuessRole(p) : subject;
                    if (subject == "bracing" && p.Family != Family.RD && p.Family != Family.L && p.Family != Family.CHS && p.Family != Family.HOLLOW) role = "bracing";
                    prof[role] = p;
                    continue;
                }
                // angka (+ satuan)
                string unit = i + 1 < toks.Count && toks[i + 1].Kind == "W" ? toks[i + 1].Text : "";
                double f = unit == "mm" ? 1 : unit == "cm" ? 10 : unit == "m" ? 1000 : double.NaN;
                bool deg = unit == "derajat" || unit == "deg";
                bool pct = unit == "%" || unit == "persen";
                bool count = unit == "bay" || unit == "portal" || unit == "frame" || unit == "rangka" || unit == "buah" || unit == "batang" || unit == "bentang" || unit == "lantai" || unit == "tingkat" || unit == "kolom" || unit == "baris" || unit == "panel";
                if (t.Kind == "D")
                {
                    double ff = double.IsNaN(f) ? (t.Num < 200 ? 1000 : 1) : f;
                    P("struktur")["bentang"] = t.Num * ff; P("struktur")["panjang"] = t.Num2 * ff;
                    if (!double.IsNaN(f)) i++;
                    pendingKey = null;
                    continue;
                }
                double v = t.Num;
                if (count)
                {
                    string key = unit == "bay" || unit == "bentang" ? "bay" : unit == "portal" || unit == "frame" || unit == "rangka" ? "portal" : unit == "lantai" || unit == "tingkat" ? "lantai" : unit == "panel" ? "panel" : "jumlah";
                    P(key == "jumlah" ? subject : "struktur")[key] = v;
                    i++; pendingKey = null; continue;
                }
                if (pendingKey == "miring" || deg || pct)
                {
                    P("struktur")["miring"] = pct ? Math.Atan(v / 100) * 180 / Math.PI : v;
                    if (deg || pct) i++;
                    pendingKey = null; continue;
                }
                double mm = double.IsNaN(f) ? (v < 100 ? v * 1000 : v) : v * f;
                if (!double.IsNaN(f)) i++;
                string k2 = pendingKey ?? (subject == "struktur" ? "bentang" : "panjang");
                if (pendingKey == "jumlah") { P(subject)["jumlah"] = v; pendingKey = null; continue; }
                string owner = k2 == "jarak" ? subject : (k2 == "tinggi" && subject == "kolom" ? "struktur" : (subject == "struktur" || k2 == "bentang" || k2 == "tinggi" || k2 == "lebar" ? "struktur" : subject));
                P(owner)[k2] = mm;
                pendingKey = null;
            }

            var st = P("struktur");
            // "lebar" = lebar jembatan; untuk bentuk lain berarti bentang bila bentang tidak disebut
            if (structure != "jembatan" && !st.ContainsKey("bentang") && st.TryGetValue("lebar", out double lb)) st["bentang"] = lb;
            if (structure == null && truss && (st.ContainsKey("bentang") || st.ContainsKey("lebar"))) structure = "jembatan";
            if (structure == null && st.ContainsKey("bentang") && (prof.ContainsKey("rafter") || prof.ContainsKey("gording") || st.ContainsKey("miring"))) structure = "portal";
            if (structure == null && st.ContainsKey("bentang") && st.ContainsKey("panjang") && prof.ContainsKey("balok")) structure = "lantai";
            if (structure == "kanopi") Canopy(plan, st, P, prof, cant, twoSided);
            else if (structure == "menara") Tower(plan, st, P, prof);
            else if (structure == "jembatan") Bridge(plan, st, P, prof, truss);
            else if (structure == "portal") Portal(plan, st, P, prof, mono);
            else if (structure == "lantai") Floor(plan, st, P, prof);
            else Singles(plan, st, P, prof);
            if (plan.Members.Count == 0) plan.Unknown.Add("belum bisa menangkap maksud kalimat. Contoh: \"portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150 jarak 1,2 m\"");
            return plan;
        }

        // ------------------------------------------------------------------ kanopi / kantilever
        /// <summary>
        /// Kanopi. Kantilever: satu baris kolom, balok menjorok (satu sisi atau dua sisi/T) dari puncak kolom, gording di atasnya.
        /// Tanpa kantilever: kolom di depan & belakang, rafter miring satu arah (portal mono kecil), gording.
        /// </summary>
        static void Canopy(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof, bool cant, bool twoSided)
        {
            double depth = Get(st, "bentang", cant ? 3000 : 5000, plan, cant ? "Panjang kantilever" : "Lebar kanopi");
            double h = Get(st, "tinggi", 3000, plan, "Tinggi kolom");
            double slope = Get(st, "miring", 5, plan, "Kemiringan atap", "derajat");
            if (!st.ContainsKey("jarak") && P("kolom").TryGetValue("jarak", out double cj)) st["jarak"] = cj;
            double bay = Get(st, "jarak", 5000, plan, "Jarak kolom");
            double L = st.TryGetValue("panjang", out double lg) ? lg : (st.TryGetValue("bay", out double nb) ? nb * bay : 3 * bay);
            if (!st.ContainsKey("panjang") && !st.ContainsKey("bay")) plan.Assumptions.Add("Panjang kanopi tidak disebut: dipakai " + Fmt.N(L) + " mm (3 bay)");
            int nCol = Math.Max(2, (int)Math.Round(L / bay) + 1);
            double sp = L / (nCol - 1);
            var col = Pick(prof, "kolom", "H200x200x8x12", plan, "Profil kolom");
            var beam = Pick(prof, cant ? "balok" : "rafter", "WF250x125x6x9", plan, cant ? "Profil balok kantilever" : "Profil rafter");
            if (!cant && prof.ContainsKey("balok") && !prof.ContainsKey("rafter")) beam = prof["balok"];
            double t = Math.Tan(slope * Math.PI / 180);
            var sides = cant && twoSided ? new[] { 1, -1 } : new[] { 1 };
            for (int j = 0; j < nCol; j++)
            {
                double y = j * sp;
                plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = col.Name, S = new Vec3(0, y, 0), E = new Vec3(0, y, h), Roll = 90 });
                if (cant && twoSided)
                    // T: satu balok menerus miring satu arah, duduk di atas kolom (pelat tutup), menjorok ke kedua sisi
                    plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = beam.Name, S = new Vec3(-depth, y, h + depth * t), E = new Vec3(depth, y, h - depth * t), Anchor = Anchor.Bawah });
                else if (cant)
                    // balok menjorok turun ke luar (air mengalir ke ujung kantilever)
                    plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = beam.Name, S = new Vec3(0, y, h), E = new Vec3(depth, y, h - depth * t) });
                else
                {
                    plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = col.Name, S = new Vec3(depth, y, 0), E = new Vec3(depth, y, h - depth * t), Roll = 90 });
                    plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = beam.Name, S = new Vec3(0, y, h), E = new Vec3(depth, y, h - depth * t) });
                }
            }
            if (prof.ContainsKey("gording") || P("gording").Count > 0 || true)
            {
                var g = Pick(prof, "gording", "C150x65x20x3.2", plan, "Profil gording");
                double gs = Get(P("gording"), "jarak", 1200, plan, "Jarak gording");
                double Hr = beam.Extent(false), Hp = g.Extent(false), c = Math.Cos(slope * Math.PI / 180), sn = Math.Sin(slope * Math.PI / 180);
                double s0 = Math.Max(150, col.Extent(false) / 2 + 250), run = depth / c - s0 - 100;
                int n = Math.Max(1, (int)Math.Ceiling(run / gs - 1e-9) + 1);
                double step = n > 1 ? run / (n - 1) : 0;
                foreach (int sd in sides)
                    for (int k = 0; k < n; k++)
                    {
                        // T: balok miring satu arah, jadi sisi kiri lebih tinggi (z naik ke kiri) dan gording mengikuti satu kemiringan
                        double s = s0 + k * step, x = sd * s * c, z = h - sd * s * sn, off = (Hr + Hp) / 2 + (cant && twoSided ? Hr / 2 : 0);
                        x += off * sn; z += off * c;
                        plan.Members.Add(new MemberProposal { Role = "Gording", Profile = g.Name, S = new Vec3(x, -Overhang, z), E = new Vec3(x, L + Overhang, z), Roll = slope });
                    }
            }
            if (cant && twoSided) plan.Assumptions.Add("Kanopi T: balok menerus miring satu arah di atas kolom (pelat tutup); kekakuan sambungan dicek engineer");
            plan.Summary = (cant ? "Kanopi kantilever" + (twoSided ? " dua sisi (T)" : "") : "Kanopi dua kolom") + " " + Fmt.N(depth / 1000) + " m x " + Fmt.N(L / 1000) + " m, tinggi " + Fmt.N(h / 1000) +
                " m, miring " + Fmt.N(slope) + "°: kolom " + col.Name + " ×" + plan.Members.Count(m => m.Role == "Kolom") + ", " + (cant ? "balok kantilever " : "rafter ") + beam.Name +
                " ×" + plan.Members.Count(m => m.Role == "Rafter") + ", gording ×" + plan.Members.Count(m => m.Role == "Gording") + ".";
            if (cant) plan.Assumptions.Add("Sambungan kantilever ke kolom harus kaku (momen): periksa jenis sambungan & kapasitasnya dengan engineer");
        }

        // ------------------------------------------------------------------ menara rangka
        /// <summary>Menara 4 kaki (mis. tower air / tandon): kaki tegak di sudut, pengaku datar tiap segmen, diagonal zig-zag tiap muka, balok platform di puncak.</summary>
        static void Tower(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof)
        {
            double w = Get(st, "bentang", 3000, plan, "Lebar menara (as kaki)");
            double h = Get(st, "tinggi", 9000, plan, "Tinggi menara");
            int seg = st.TryGetValue("panel", out double ns) ? Math.Max(1, (int)ns) : st.TryGetValue("lantai", out double nl) ? Math.Max(1, (int)nl) : Math.Max(1, (int)Math.Round(h / 3000));
            if (!st.ContainsKey("panel") && !st.ContainsKey("lantai")) plan.Assumptions.Add("Jumlah segmen tidak disebut: dipakai " + seg + " segmen (±3 m)");
            var leg = Pick(prof, "kolom", "H200x200x8x12", plan, "Profil kaki menara");
            var strut = Pick(prof, "balok", "WF200x100x5.5x8", plan, "Profil pengaku datar / balok platform");
            var diag = Pick(prof, "bracing", "L75x75x8", plan, "Profil diagonal");
            double dz = h / seg, a = w / 2;
            var c = new[] { new Vec3(-a, -a, 0), new Vec3(a, -a, 0), new Vec3(a, a, 0), new Vec3(-a, a, 0) };
            // kaki diteruskan 300 mm di atas balok puncak: semua balok datar tersambung ke badan/sayap kaki (fin plate), tidak bertemu di ujung kaki
            foreach (var p in c) plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = leg.Name, S = p, E = p + new Vec3(0, 0, h + 300), Roll = 90 });
            for (int k = 1; k <= seg; k++)
                for (int f = 0; f < 4; f++)
                {
                    var p0 = c[f] + new Vec3(0, 0, k * dz); var p1 = c[(f + 1) % 4] + new Vec3(0, 0, k * dz);
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = strut.Name, S = p0, E = p1, Anchor = k == seg ? Anchor.Atas : Anchor.Tengah });
                    // diagonal zig-zag: arah berganti tiap segmen, jadi tidak ada diagonal yang saling menyilang
                    var b0 = c[f] + new Vec3(0, 0, (k - 1) * dz); var b1 = c[(f + 1) % 4] + new Vec3(0, 0, (k - 1) * dz);
                    var d0 = k % 2 == 1 ? b0 : b1; var d1 = k % 2 == 1 ? p1 : p0;
                    if (k == 1) d0 += new Vec3(0, 0, 500);          // segmen bawah: buhul 500 mm di atas pelat dasar kaki
                    if (k == seg) d1 -= new Vec3(0, 0, 500);        // segmen atas: buhul 500 mm di bawah balok puncak
                    // ujung diagonal di titik buhul (kaki + pengaku datar); jarak mundur & pelat buhul diatur sambungannya
                    plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = diag.Name, S = d0, E = d1 });
                }
            // balok platform tengah di puncak (dua arah)
            plan.Members.Add(new MemberProposal { Role = "Balok", Profile = strut.Name, S = new Vec3(0, -a, h), E = new Vec3(0, a, h), Anchor = Anchor.Atas });
            plan.Summary = "Menara rangka 4 kaki " + Fmt.N(w / 1000) + " x " + Fmt.N(w / 1000) + " m, tinggi " + Fmt.N(h / 1000) + " m, " + seg + " segmen: kaki " + leg.Name + " ×4, pengaku datar " +
                strut.Name + " ×" + (4 * seg) + ", diagonal " + diag.Name + " ×" + (4 * seg) + ", balok platform ×1.";
            plan.Assumptions.Add("Beban tangki/peralatan dan angin menara dihitung engineer; ukuran profil di sini contoh");
        }

        // ------------------------------------------------------------------ jembatan
        /// <summary>
        /// Jembatan pejalan kaki. Rangka (truss Pratt): dua rangka di tepi, batang tepi atas & bawah, tegak, diagonal, balok lantai.
        /// Gelagar: dua gelagar memanjang + balok lantai melintang.
        /// </summary>
        static void Bridge(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof, bool truss)
        {
            double span = Get(st, "bentang", 15000, plan, "Bentang jembatan");
            double width = Get(st, "lebar", 2000, plan, "Lebar jembatan");
            if (!truss && span > 12000 && !st.ContainsKey("gelagar")) { truss = true; plan.Assumptions.Add("Bentang > 12 m: dipakai jembatan rangka (truss), bukan gelagar"); }
            var beam = Pick(prof, "balok anak", "WF200x100x5.5x8", plan, "Profil balok lantai");
            if (truss)
            {
                double ht = Get(st, "tinggi", 1800, plan, "Tinggi rangka");
                int np = st.TryGetValue("panel", out double npn) ? Math.Max(2, (int)npn) : Math.Max(2, (int)Math.Round(span / 2500));
                if (!st.ContainsKey("panel")) plan.Assumptions.Add("Jumlah panel tidak disebut: dipakai " + np + " panel (±2,5 m)");
                var chord = Pick(prof, "balok", "WF250x125x6x9", plan, "Profil batang tepi (chord)");
                var vert = Pick(prof, "kolom", "WF200x100x5.5x8", plan, "Profil batang tegak");
                var diag = Pick(prof, "bracing", "L75x75x8", plan, "Profil diagonal");
                double dx = span / np, hc = chord.Extent(false) / 2;
                foreach (double y in new[] { 0.0, width })
                {
                    // batang tepi diteruskan 300 mm melewati tegak ujung (dudukan perletakan), jadi semua tegak tersambung sama
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = chord.Name, S = new Vec3(-300, y, 0), E = new Vec3(span + 300, y, 0), Roll = 0 });
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = chord.Name, S = new Vec3(-300, y, ht), E = new Vec3(span + 300, y, ht), Roll = 0 });
                    // tegak dari muka atas tepi bawah ke muka bawah tepi atas (pelat dasar & pelat tutup ke sayap tepi)
                    for (int i = 0; i <= np; i++)
                        plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = vert.Name, S = new Vec3(i * dx, y, hc), E = new Vec3(i * dx, y, ht - hc), Roll = 90 });
                    // Pratt: diagonal turun ke arah tengah bentang (tarik akibat beban gravitasi)
                    for (int i = 0; i < np; i++)
                    {
                        bool left = (i + 0.5) * dx < span / 2;
                        // diagonal dibaut ke tegak lewat pelat buhul, 400 mm dari muka tepi (titik buhul tepi dipakai pelat dasar/tutup tegak)
                        var d0 = left ? new Vec3(i * dx, y, ht - hc - 400) : new Vec3((i + 1) * dx, y, ht - hc - 400);
                        var d1 = left ? new Vec3((i + 1) * dx, y, hc + 400) : new Vec3(i * dx, y, hc + 400);
                        plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = diag.Name, S = d0, E = d1 });
                    }
                }
                // balok lantai di tengah panel (titik panel dipakai kaki batang tegak), muka atas rata dengan batang tepi bawah
                for (int i = 0; i < np; i++)
                    plan.Members.Add(new MemberProposal { Role = "Balok anak", Profile = beam.Name, S = new Vec3((i + 0.5) * dx, 0, hc), E = new Vec3((i + 0.5) * dx, width, hc), Anchor = Anchor.Atas });
                plan.Assumptions.Add("Diagonal disambung ke batang tegak 400 mm dari muka batang tepi (bukan di titik buhul): eksentrisitas dicek engineer");
                plan.Summary = "Jembatan rangka (Pratt) bentang " + Fmt.N(span / 1000) + " m, lebar " + Fmt.N(width / 1000) + " m, tinggi rangka " + Fmt.N(ht / 1000) + " m, " + np +
                    " panel: batang tepi " + chord.Name + " ×4, tegak " + vert.Name + " ×" + (2 * (np + 1)) + ", diagonal " + diag.Name + " ×" + (2 * np) + ", balok lantai " + beam.Name + " ×" + np + ".";
            }
            else
            {
                var girder = Pick(prof, "balok", "WF300x150x6.5x9", plan, "Profil gelagar");
                double bsp = Get(P("balok anak"), "jarak", 1500, plan, "Jarak balok lantai");
                foreach (double y in new[] { 0.0, width })
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = girder.Name, S = new Vec3(0, y, 0), E = new Vec3(span, y, 0), Anchor = Anchor.Atas });
                int n = Math.Max(2, (int)Math.Ceiling(span / bsp - 1e-9) + 1);
                for (int i = 0; i < n; i++)
                {
                    double x = span * i / (n - 1);
                    plan.Members.Add(new MemberProposal { Role = "Balok anak", Profile = beam.Name, S = new Vec3(x, 0, 0), E = new Vec3(x, width, 0), Anchor = Anchor.Atas });
                }
                plan.Summary = "Jembatan gelagar bentang " + Fmt.N(span / 1000) + " m, lebar " + Fmt.N(width / 1000) + " m: gelagar " + girder.Name + " ×2, balok lantai " + beam.Name + " ×" + n + ".";
            }
            plan.Assumptions.Add("Lendutan, getaran, dan kapasitas jembatan dihitung engineer; tumpuan (perletakan) belum dimodelkan");
        }

        static string GuessRole(Profile p)
        {
            switch (p.Family)
            {
                case Family.CNP: case Family.Z: return "gording";
                case Family.RD: case Family.L: return "bracing";
                case Family.H: return "kolom";
                default: return "balok";
            }
        }

        static Profile Pick(Dictionary<string, Profile> prof, string role, string fallback, AssistantPlan plan, string label)
        {
            if (prof.TryGetValue(role, out var p)) return p;
            var d = Env.Catalog.Find(fallback);
            plan.Assumptions.Add(label + " belum disebut: dipakai " + d.Name + " sebagai contoh (GANTI sesuai desain engineer)");
            return d;
        }

        static double Get(Dictionary<string, double> d, string k, double def, AssistantPlan plan, string label, string unit = "mm")
        {
            if (d.TryGetValue(k, out double v)) return v;
            plan.Assumptions.Add(label + " tidak disebut: dipakai " + Fmt.N(def) + " " + unit);
            return def;
        }

        static void Portal(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof, bool mono)
        {
            double span = Get(st, "bentang", 20000, plan, "Bentang");
            double h = Get(st, "tinggi", 6000, plan, "Tinggi kolom (eave)");
            double slope = Get(st, "miring", 10, plan, "Kemiringan atap", "derajat");
            if (!st.ContainsKey("jarak") && P("kolom").TryGetValue("jarak", out double cj)) st["jarak"] = cj;
            double bay = Get(st, "jarak", 6000, plan, "Jarak portal");
            int frames;
            if (st.TryGetValue("portal", out double nf)) frames = (int)nf;
            else if (st.TryGetValue("bay", out double nb)) frames = (int)nb + 1;
            else if (st.TryGetValue("panjang", out double len)) frames = (int)Math.Round(len / bay) + 1;
            else { frames = 4; plan.Assumptions.Add("Jumlah portal tidak disebut: dipakai 4 portal (3 bay)"); }
            frames = Math.Max(2, Math.Min(frames, 40));
            var col = Pick(prof, "kolom", span <= 12000 ? "WF300x150x6.5x9" : span <= 20000 ? "WF400x200x8x13" : span <= 30000 ? "WF500x200x10x16" : "WF600x200x11x17", plan, "Profil kolom");
            var raf = Pick(prof, "rafter", col.Name, plan, "Profil rafter");
            var t = Math.Tan(slope * Math.PI / 180);
            double rise = (mono ? span : span / 2) * t;
            for (int i = 0; i < frames; i++)
            {
                double y = i * bay;
                plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = col.Name, S = new Vec3(0, y, 0), E = new Vec3(0, y, h), Roll = 90 });
                plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = col.Name, S = new Vec3(span, y, 0), E = new Vec3(span, y, mono ? h + rise : h), Roll = 90 });
                if (mono) plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = raf.Name, S = new Vec3(0, y, h), E = new Vec3(span, y, h + rise) });
                else
                {
                    plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = raf.Name, S = new Vec3(0, y, h), E = new Vec3(span / 2, y, h + rise) });
                    plan.Members.Add(new MemberProposal { Role = "Rafter", Profile = raf.Name, S = new Vec3(span, y, h), E = new Vec3(span / 2, y, h + rise) });
                }
            }
            double L = (frames - 1) * bay;
            if (prof.ContainsKey("gording") || P("gording").Count > 0)
            {
                var g = Pick(prof, "gording", "C150x65x20x3.2", plan, "Profil gording");
                double sp = Get(P("gording"), "jarak", 1200, plan, "Jarak gording");
                double Hr = raf.Extent(false), Hp = g.Extent(false), c = Math.Cos(slope * Math.PI / 180), sn = Math.Sin(slope * Math.PI / 180);
                double slopeLen = (mono ? span : span / 2) / c;
                // gording pertama di luar zona lutut (pelat ujung, baut, pengaku), bukan di atas kolom
                double s0 = Math.Max(150, col.Extent(false) / 2 + 250);
                // dibagi rata dari gording tepi sampai 150 mm dari puncak: jarak sebenarnya ≤ jarak yang diminta
                double run = Math.Max(0, slopeLen - s0 - 150);
                int n = (int)Math.Ceiling(run / sp - 1e-9) + 1;
                double spU = n > 1 ? run / (n - 1) : 0;
                plan.Assumptions.Add("Gording diteruskan " + Fmt.N(Overhang) + " mm melewati rafter ujung supaya baut cleat ujung kena gording");
                if (n > 1 && spU < sp - 1) plan.Assumptions.Add("Jarak gording dibagi rata " + Fmt.N(Math.Round(spU)) + " mm (≤ " + Fmt.N(sp) + " mm), gording tepi " + Fmt.N(s0) + " mm dari as kolom di luar zona lutut");
                foreach (int side in mono ? new[] { 1 } : new[] { 1, -1 })
                    for (int k = 0; k < n; k++)
                    {
                        double s = s0 + k * spU;                 // dari eave sepanjang rafter
                        double x0 = side > 0 ? 0 : span;
                        double x = x0 + side * s * c, z = h + s * sn;
                        double off = (Hr + Hp) / 2;
                        x += -side * off * sn; z += off * c;
                        plan.Members.Add(new MemberProposal { Role = "Gording", Profile = g.Name, S = new Vec3(x, -Overhang, z), E = new Vec3(x, L + Overhang, z), Roll = side > 0 ? -slope : slope });
                    }
            }
            if (prof.ContainsKey("girt") || P("girt").Count > 0)
            {
                var g = Pick(prof, "girt", "C150x65x20x3.2", plan, "Profil girt");
                double sp = Get(P("girt"), "jarak", 1500, plan, "Jarak girt");
                double off = (col.Extent(false) + g.Extent(true)) / 2;
                for (double z = 1200; z < h - 300; z += sp)
                    foreach (var x in new[] { -off, span + off })
                        plan.Members.Add(new MemberProposal { Role = "Girt", Profile = g.Name, S = new Vec3(x, -Overhang, z), E = new Vec3(x, L + Overhang, z) });
            }
            if (prof.ContainsKey("bracing") || P("bracing").Count > 0)
            {
                var b = Pick(prof, "bracing", "RD16", plan, "Profil bracing");
                foreach (var (y0, y1) in frames > 2 ? new[] { (0.0, bay), (L - bay, L) } : new[] { (0.0, bay) })
                {
                    // dinding: X di kedua sisi; atap: X di tiap paruh
                    // dua rod X saling melewati di titik silang: digeser ±(d/2 + 0,5) tegak lurus bidang bracing supaya tidak saling menembus
                    double e = b.Extent(true) / 2 + 0.5;
                    foreach (var x in new[] { 0.0, span })
                    {
                        plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(x - e, y0, 500), E = new Vec3(x - e, y1, h - 500) });
                        plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(x + e, y1, 500), E = new Vec3(x + e, y0, h - 500) });
                    }
                    double xr = mono ? span : span / 2;
                    var nRoof = new Vec3(-t, 0, 1).Unit * e;   // normal bidang atap
                    double rx = Math.Max(300, col.Extent(false) / 2 + 400);   // ujung bawah rod atap di luar zona baut lutut
                    plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(rx, y0, h + rx * t) - nRoof, E = new Vec3(xr - 300, y1, h + (xr - 300) * t) - nRoof });
                    plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(rx, y1, h + rx * t) + nRoof, E = new Vec3(xr - 300, y0, h + (xr - 300) * t) + nRoof });
                }
            }
            int nc = plan.Members.Count(m => m.Role == "Kolom"), nr = plan.Members.Count(m => m.Role == "Rafter"), ng = plan.Members.Count(m => m.Role == "Gording");
            plan.Summary = "Portal " + (mono ? "miring sepihak" : "pelana") + " bentang " + Fmt.N(span / 1000) + " m, tinggi " + Fmt.N(h / 1000) + " m, kemiringan " + Fmt.N(slope) + "°, " +
                frames + " portal (" + (frames - 1) + " bay @" + Fmt.N(bay / 1000) + " m, panjang " + Fmt.N(L / 1000) + " m): kolom " + col.Name + " ×" + nc + ", rafter " + raf.Name + " ×" + nr +
                (ng > 0 ? ", gording ×" + ng : "") + (plan.Members.Any(m => m.Role == "Girt") ? ", girt ×" + plan.Members.Count(m => m.Role == "Girt") : "") +
                (plan.Members.Any(m => m.Role == "Bracing") ? ", bracing ×" + plan.Members.Count(m => m.Role == "Bracing") : "") + ".";
        }

        static void Floor(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof)
        {
            double W = Get(st, "bentang", 6000, plan, "Lebar lantai"), Lg = Get(st, "panjang", 8000, plan, "Panjang lantai");
            double h = Get(st, "tinggi", 3000, plan, "Tinggi lantai");
            var col = Pick(prof, "kolom", "H200x200x8x12", plan, "Profil kolom");
            var beam = Pick(prof, "balok", "WF300x150x6.5x9", plan, "Profil balok induk");
            double colSp = P("kolom").TryGetValue("jarak", out double cs) ? cs : Math.Min(6000, Lg);
            int nx = Math.Max(1, (int)Math.Ceiling(W / 6000)), ny = Math.Max(1, (int)Math.Ceiling(Lg / colSp));
            double dx = W / nx, dy = Lg / ny;
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= ny; j++)
                    plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = col.Name, S = new Vec3(i * dx, j * dy, 0), E = new Vec3(i * dx, j * dy, h), Roll = 90 });
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j < ny; j++)
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = beam.Name, S = new Vec3(i * dx, j * dy, h), E = new Vec3(i * dx, (j + 1) * dy, h), Anchor = Anchor.Atas });
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i < nx; i++)
                    plan.Members.Add(new MemberProposal { Role = "Balok", Profile = beam.Name, S = new Vec3(i * dx, j * dy, h), E = new Vec3((i + 1) * dx, j * dy, h), Anchor = Anchor.Atas });
            if (prof.ContainsKey("balok anak") || P("balok anak").Count > 0)
            {
                var sb = Pick(prof, "balok anak", "WF200x100x5.5x8", plan, "Profil balok anak");
                double sp = Get(P("balok anak"), "jarak", 2000, plan, "Jarak balok anak");
                for (int i = 0; i < nx; i++)
                    for (double y = sp; y < Lg - 1; y += sp)
                    {
                        if (Enumerable.Range(0, ny + 1).Any(j => Math.Abs(j * dy - y) < 1)) continue;
                        plan.Members.Add(new MemberProposal { Role = "Balok anak", Profile = sb.Name, S = new Vec3(i * dx, y, h), E = new Vec3((i + 1) * dx, y, h), Anchor = Anchor.Atas });
                    }
            }
            plan.Summary = "Lantai / mezanin " + Fmt.N(W / 1000) + " x " + Fmt.N(Lg / 1000) + " m, tinggi " + Fmt.N(h / 1000) + " m: kolom " + col.Name + " ×" + plan.Members.Count(m => m.Role == "Kolom") +
                ", balok induk " + beam.Name + " ×" + plan.Members.Count(m => m.Role == "Balok") + (plan.Members.Any(m => m.Role == "Balok anak") ? ", balok anak ×" + plan.Members.Count(m => m.Role == "Balok anak") : "") +
                ". Balok memakai acuan atas (muka atas balok = elevasi lantai).";
        }

        static void Singles(AssistantPlan plan, Dictionary<string, double> st, Func<string, Dictionary<string, double>> P, Dictionary<string, Profile> prof)
        {
            foreach (var kv in prof)
            {
                var d = P(kv.Key);
                int n = d.TryGetValue("jumlah", out double c) ? Math.Max(1, (int)c) : 1;
                double sp = d.TryGetValue("jarak", out double j) ? j : 3000;
                if (kv.Key == "kolom")
                {
                    double h = d.TryGetValue("panjang", out double hh) ? hh : Get(st, "tinggi", 3000, plan, "Tinggi kolom");
                    for (int i = 0; i < n; i++) plan.Members.Add(new MemberProposal { Role = "Kolom", Profile = kv.Value.Name, S = new Vec3(i * sp, 0, 0), E = new Vec3(i * sp, 0, h), Roll = 90 });
                }
                else
                {
                    double L = d.TryGetValue("panjang", out double ll) ? ll : st.TryGetValue("bentang", out double bb) ? bb : Get(d, "panjang", 6000, plan, "Panjang " + kv.Key);
                    double z = st.TryGetValue("tinggi", out double zz) ? zz : 0;
                    string role = char.ToUpper(kv.Key[0]) + kv.Key.Substring(1);
                    for (int i = 0; i < n; i++) plan.Members.Add(new MemberProposal { Role = role, Profile = kv.Value.Name, S = new Vec3(0, i * sp, z), E = new Vec3(L, i * sp, z) });
                }
            }
            plan.Summary = plan.Members.Count == 0 ? "" : string.Join(", ", plan.Members.GroupBy(m => m.Role + " " + m.Profile).Select(g => g.Key + " ×" + g.Count() + " (L=" + Fmt.N(g.First().Length) + " mm)")) + ".";
        }
    }
}
