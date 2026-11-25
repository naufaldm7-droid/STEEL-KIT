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
        static readonly string[] StructWords = { "portal", "gudang", "peb", "pelana", "mono", "sepihak", "lantai", "mezanin", "mezzanine", "grid", "hanggar", "workshop", "bengkel" };

        class Tok { public string Kind, Text; public double Num; public double Num2 = double.NaN; }

        static string Normalize(string s)
        {
            s = (s ?? "").ToLowerInvariant();
            s = Regex.Replace(s, @"(\d),(\d)", "$1.$2");                       // 1,2 -> 1.2
            s = Regex.Replace(s, @"(\d)\s*koma\s*(\d)", "$1.$2");
            s = s.Replace("°", " derajat ").Replace("×", "x").Replace("meter", " m ").Replace("mtr", " m ");
            // angka bertingkat: dua belas, dua puluh, dua puluh lima
            s = Regex.Replace(s, @"\b(satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan)\s+belas\b", m => (10 + NumberWords[m.Groups[1].Value]).ToString(CultureInfo.InvariantCulture));
            s = Regex.Replace(s, @"\b(dua|tiga|empat|lima|enam|tujuh|delapan|sembilan)\s+puluh(\s+(satu|dua|tiga|empat|lima|enam|tujuh|delapan|sembilan))?\b",
                m => (10 * NumberWords[m.Groups[1].Value] + (m.Groups[3].Success ? NumberWords[m.Groups[3].Value] : 0)).ToString(CultureInfo.InvariantCulture));
            s = Regex.Replace(s, @"\b(" + string.Join("|", NumberWords.Keys) + @")\b", m => NumberWords[m.Value].ToString(CultureInfo.InvariantCulture));
            return s;
        }

        static List<Tok> Tokenize(string s)
        {
            var toks = new List<Tok>();
            foreach (Match m in Regex.Matches(s, @"(?<prof>(?:wf|iwf|h|cnp|c|z|unp|l|siku|t|shs|rhs|chs|rd|pl|sch40-|sch80-|med-)\s?\d[\d.]*(?:x\d[\d.]*)*)|(?<dim>\d+(?:\.\d+)?\s*x\s*\d+(?:\.\d+)?)|(?<num>\d+(?:\.\d+)?)|(?<w>[a-z%]+)"))
            {
                if (m.Groups["prof"].Success) toks.Add(new Tok { Kind = "P", Text = m.Value.Replace(" ", "") });
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

        public static AssistantPlan Parse(string sentence)
        {
            var plan = new AssistantPlan();
            var toks = Tokenize(Normalize(sentence));
            string subject = "struktur", pendingKey = null, structure = null;
            bool mono = false;
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
                        else if (w == "lantai" || w == "mezanin" || w == "mezzanine") structure = "lantai";
                        else if (w == "grid") structure = structure ?? "grid";
                        else structure = "portal";
                        subject = "struktur"; continue;
                    }
                    if (w == "bentang" || w == "lebar") { pendingKey = "bentang"; continue; }
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
                bool count = unit == "bay" || unit == "portal" || unit == "frame" || unit == "rangka" || unit == "buah" || unit == "batang" || unit == "bentang" || unit == "lantai" || unit == "tingkat" || unit == "kolom" || unit == "baris";
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
                    string key = unit == "bay" || unit == "bentang" ? "bay" : unit == "portal" || unit == "frame" || unit == "rangka" ? "portal" : unit == "lantai" || unit == "tingkat" ? "lantai" : "jumlah";
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
                string owner = k2 == "jarak" ? subject : (k2 == "tinggi" && subject == "kolom" ? "struktur" : (subject == "struktur" || k2 == "bentang" || k2 == "tinggi" ? "struktur" : subject));
                P(owner)[k2] = mm;
                pendingKey = null;
            }

            var st = P("struktur");
            if (structure == null && st.ContainsKey("bentang") && (prof.ContainsKey("rafter") || prof.ContainsKey("gording") || st.ContainsKey("miring"))) structure = "portal";
            if (structure == null && st.ContainsKey("bentang") && st.ContainsKey("panjang") && prof.ContainsKey("balok")) structure = "lantai";
            if (structure == "portal") Portal(plan, st, P, prof, mono);
            else if (structure == "lantai") Floor(plan, st, P, prof);
            else Singles(plan, st, P, prof);
            if (plan.Members.Count == 0) plan.Unknown.Add("belum bisa menangkap maksud kalimat. Contoh: \"portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150 jarak 1,2 m\"");
            return plan;
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
                int n = (int)Math.Floor((slopeLen - 150) / sp) + 1;
                foreach (int side in mono ? new[] { 1 } : new[] { 1, -1 })
                    for (int k = 0; k < n; k++)
                    {
                        double s = 150 + k * sp;                 // dari eave sepanjang rafter
                        double x0 = side > 0 ? 0 : span;
                        double x = x0 + side * s * c, z = h + s * sn;
                        double off = (Hr + Hp) / 2;
                        x += -side * off * sn; z += off * c;
                        plan.Members.Add(new MemberProposal { Role = "Gording", Profile = g.Name, S = new Vec3(x, 0, z), E = new Vec3(x, L, z), Roll = side > 0 ? -slope : slope });
                    }
            }
            if (prof.ContainsKey("girt") || P("girt").Count > 0)
            {
                var g = Pick(prof, "girt", "C150x65x20x3.2", plan, "Profil girt");
                double sp = Get(P("girt"), "jarak", 1500, plan, "Jarak girt");
                double off = (col.Extent(false) + g.Extent(true)) / 2;
                for (double z = 1200; z < h - 300; z += sp)
                    foreach (var x in new[] { -off, span + off })
                        plan.Members.Add(new MemberProposal { Role = "Girt", Profile = g.Name, S = new Vec3(x, 0, z), E = new Vec3(x, L, z) });
            }
            if (prof.ContainsKey("bracing") || P("bracing").Count > 0)
            {
                var b = Pick(prof, "bracing", "RD16", plan, "Profil bracing");
                foreach (var (y0, y1) in frames > 2 ? new[] { (0.0, bay), (L - bay, L) } : new[] { (0.0, bay) })
                {
                    // dinding: X di kedua sisi; atap: X di tiap paruh
                    foreach (var x in new[] { 0.0, span })
                    {
                        plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(x, y0, 500), E = new Vec3(x, y1, h - 500) });
                        plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(x, y1, 500), E = new Vec3(x, y0, h - 500) });
                    }
                    double xr = mono ? span : span / 2;
                    plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(300, y0, h + 300 * t), E = new Vec3(xr - 300, y1, h + (xr - 300) * t) });
                    plan.Members.Add(new MemberProposal { Role = "Bracing", Profile = b.Name, S = new Vec3(300, y1, h + 300 * t), E = new Vec3(xr - 300, y0, h + (xr - 300) * t) });
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
