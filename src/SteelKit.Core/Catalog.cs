using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SteelKit.Core
{
    /// <summary>
    /// Katalog profil = tabel bawaan + tabel pengguna. Format CSV (bisa dibuka di Excel):
    /// family,name,H,B,tw,tf,r,t,C,D,note
    /// Profil pengguna dengan nama yang sama menimpa profil bawaan.
    /// </summary>
    public class Catalog
    {
        public static readonly string[] Columns = { "family", "name", "H", "B", "tw", "tf", "r", "t", "C", "D", "note" };
        static readonly string[] DimCols = { "H", "B", "tw", "tf", "r", "t", "C", "D" };

        readonly List<Profile> builtin = new List<Profile>();
        readonly List<Profile> user = new List<Profile>();
        public List<string> Warnings = new List<string>();

        public static Catalog Load(string builtinCsv, string userCsv)
        {
            var c = new Catalog();
            c.builtin.AddRange(Parse(builtinCsv, false, c.Warnings));
            if (!string.IsNullOrEmpty(userCsv)) c.user.AddRange(Parse(userCsv, true, c.Warnings));
            return c;
        }

        /// <summary>Semua profil (pengguna menimpa bawaan bila namanya sama), urut per keluarga lalu ukuran.</summary>
        public List<Profile> All
        {
            get
            {
                var map = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
                var order = new List<string>();
                foreach (var p in builtin.Concat(user))
                {
                    if (!map.ContainsKey(p.Name)) order.Add(p.Name);
                    map[p.Name] = p;
                }
                return order.Select(n => map[n]).ToList();
            }
        }

        public IEnumerable<Profile> OfFamily(Family f) => All.Where(p => p.Family == f);

        public Profile Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            string n = name.Trim();
            return All.FirstOrDefault(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                ?? All.FirstOrDefault(p => string.Equals(Compact(p.Name), Compact(n), StringComparison.OrdinalIgnoreCase));
        }

        static string Compact(string s) => s.Replace(" ", "").Replace("X", "x").Replace(",", ".");

        public bool IsBuiltin(string name) => builtin.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        public bool HasUser(string name) => user.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tambah atau ubah profil pengguna. Mengembalikan daftar kesalahan (kosong = berhasil).</summary>
        public List<string> SaveUser(Profile p)
        {
            var e = new List<string>();
            if (string.IsNullOrWhiteSpace(p.Name)) e.Add("Nama profil kosong.");
            else if (p.Name.IndexOfAny(new[] { ',', ';', '"', ' ', '\t' }) >= 0) e.Add("Nama profil tidak boleh berisi spasi, koma, titik koma, atau tanda petik.");
            e.AddRange(p.Validate());
            if (e.Count > 0) return e;
            var q = p.Clone(); q.IsUser = true;
            user.RemoveAll(x => string.Equals(x.Name, q.Name, StringComparison.OrdinalIgnoreCase));
            user.Add(q);
            return e;
        }

        /// <summary>Hapus profil pengguna. Profil bawaan yang ditimpa kembali ke nilai bawaan.</summary>
        public bool DeleteUser(string name) => user.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

        public string UserCsv() => ToCsv(user);

        public static string ToCsv(IEnumerable<Profile> list)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns));
            foreach (var p in list)
            {
                var cells = new List<string> { p.Family.ToString(), p.Name };
                foreach (var k in DimCols) cells.Add(p.Dims.TryGetValue(k, out double v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "");
                cells.Add(Quote(p.Note));
                sb.AppendLine(string.Join(",", cells));
            }
            return sb.ToString();
        }

        static string Quote(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        public static List<Profile> Parse(string csv, bool isUser, List<string> warnings)
        {
            var list = new List<Profile>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            var lines = csv.Replace("\r\n", "\n").Split('\n');
            string[] head = null;
            char sep = ',';
            int lineNo = 0;
            foreach (var raw in lines)
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                // Excel berlokal Indonesia menyimpan CSV dengan titik koma (dan koma desimal): tentukan dari baris judul
                if (head == null) sep = line.Contains(";") && !line.Contains(",") ? ';' : ',';
                var cells = Split(line, sep);
                if (head == null) { head = cells.Select(x => x.Trim()).ToArray(); continue; }
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < head.Length && i < cells.Count; i++) row[head[i]] = cells[i].Trim();
                if (!row.TryGetValue("family", out string fam) || !FamilyInfo.TryParse(fam, out Family f))
                {
                    warnings?.Add("Baris " + lineNo + ": keluarga profil tidak dikenal (" + fam + ").");
                    continue;
                }
                var p = new Profile { Family = f, IsUser = isUser };
                foreach (var k in DimCols)
                {
                    if (!row.TryGetValue(k, out string s) || s.Length == 0) continue;
                    if (double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) p.Dims[k] = v;
                    else warnings?.Add("Baris " + lineNo + ": nilai " + k + " bukan angka (" + s + ").");
                }
                p.FillDefaults();
                p.Name = row.TryGetValue("name", out string nm) && nm.Length > 0 ? nm : p.AutoName();
                p.Note = row.TryGetValue("note", out string note) ? note : "";
                var err = p.Validate();
                if (err.Count > 0) { warnings?.Add("Baris " + lineNo + " (" + p.Name + "): " + string.Join(" ", err)); continue; }
                list.Add(p);
            }
            return list;
        }

        static List<string> Split(string line, char sep)
        {
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (ch == '"') q = false;
                    else sb.Append(ch);
                }
                else if (ch == '"') q = true;
                else if (ch == sep) { cells.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            cells.Add(sb.ToString());
            return cells;
        }

        public static string ReadEmbedded()
        {
            var asm = typeof(Catalog).Assembly;
            using (var s = asm.GetManifestResourceStream("SteelKit.profiles.csv"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return r.ReadToEnd();
        }
    }

    /// <summary>Faktor satuan gambar dari INSUNITS: berapa unit gambar per 1 mm.</summary>
    public static class Units
    {
        public static double PerMm(int insunits)
        {
            switch (insunits)
            {
                case 1: return 1 / 25.4;    // inci
                case 2: return 1 / 304.8;   // kaki
                case 5: return 0.1;         // cm
                case 6: return 0.001;       // m
                case 14: return 0.01;       // dm
                default: return 1;          // mm atau tanpa satuan
            }
        }

        public static string Name(int insunits)
        {
            switch (insunits)
            {
                case 1: return "inci"; case 2: return "kaki"; case 5: return "cm"; case 6: return "m"; case 14: return "dm";
                case 4: return "mm"; default: return "tanpa satuan (dianggap mm)";
            }
        }
    }

    /// <summary>Satu batang yang tercatat di gambar (untuk daftar berat).</summary>
    public class MemberRecord
    {
        public string Name;
        public Family Family;
        public double LengthMm;
        public double KgPerM;
    }

    public class BoqRow
    {
        public string Name;
        public Family Family;
        public int Count;
        public double TotalLengthM;
        public double KgPerM;
        public double TotalKg => TotalLengthM * KgPerM;
    }

    public static class Boq
    {
        public static List<BoqRow> Group(IEnumerable<MemberRecord> items)
        {
            return items.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new BoqRow
                {
                    Name = g.First().Name,
                    Family = g.First().Family,
                    Count = g.Count(),
                    TotalLengthM = g.Sum(x => x.LengthMm) / 1000.0,
                    KgPerM = g.First().KgPerM,
                })
                .OrderBy(r => (int)r.Family).ThenByDescending(r => r.KgPerM).ThenBy(r => r.Name)
                .ToList();
        }

        public static string ToCsv(List<BoqRow> rows)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("profil,jenis,jumlah,panjang_total_m,berat_kg_per_m,berat_total_kg");
            foreach (var r in rows)
                sb.AppendLine(string.Join(",", r.Name, r.Family, r.Count, r.TotalLengthM.ToString("0.000", ci), r.KgPerM.ToString("0.00", ci), r.TotalKg.ToString("0.0", ci)));
            sb.AppendLine("TOTAL,,," + rows.Sum(r => r.Count) + "," + rows.Sum(r => r.TotalLengthM).ToString("0.000", ci) + ",," + rows.Sum(r => r.TotalKg).ToString("0.0", ci));
            return sb.ToString();
        }
    }
}
