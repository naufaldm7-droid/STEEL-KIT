using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SteelKit.Core;

namespace SteelKit.Acad
{
    /// <summary>Katalog + pengaturan yang tersimpan di %APPDATA%\SteelKit (per pengguna Windows).</summary>
    public static class Store
    {
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SteelKit");
        public static string UserCatalogPath => Path.Combine(Folder, "profil_pengguna.csv");
        static string SettingsPath => Path.Combine(Folder, "pengaturan.txt");

        static Catalog catalog;
        static readonly Dictionary<string, string> settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool settingsLoaded;

        public static Catalog Catalog
        {
            get { if (catalog == null) ReloadCatalog(); return catalog; }
        }

        public static void ReloadCatalog()
        {
            string user = File.Exists(UserCatalogPath) ? File.ReadAllText(UserCatalogPath, Encoding.UTF8) : null;
            catalog = Catalog.Load(Catalog.ReadEmbedded(), user);
        }

        public static void SaveCatalog()
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(UserCatalogPath, Catalog.UserCsv(), new UTF8Encoding(true));
        }

        // ---- profil aktif (boleh ukuran sementara yang belum disimpan ke katalog) ----
        static Profile current;
        public static Profile Current
        {
            get
            {
                if (current == null)
                {
                    current = Catalog.Find(Get("profil", "WF300x150x6.5x9")) ?? Catalog.Find("WF300x150x6.5x9");
                }
                return current;
            }
            set { current = value; if (value != null) Set("profil", value.Name); }
        }

        public static Anchor Anchor
        {
            get { return Enum.TryParse(Get("acuan", "Tengah"), out Anchor a) ? a : Anchor.Tengah; }
            set { Set("acuan", value.ToString()); }
        }
        public static double Roll { get { return GetD("putar", 0); } set { Set("putar", value.ToString(CultureInfo.InvariantCulture)); } }
        public static bool TopView { get { return Get("tampak", "samping") == "atas"; } set { Set("tampak", value ? "atas" : "samping"); } }
        public static bool Hatch { get { return Get("arsir", "1") == "1"; } set { Set("arsir", value ? "1" : "0"); } }
        public static bool Dims { get { return Get("dimensi", "1") == "1"; } set { Set("dimensi", value ? "1" : "0"); } }
        public static double Density { get { return GetD("berat_jenis", 7850); } set { Set("berat_jenis", value.ToString(CultureInfo.InvariantCulture)); } }
        /// <summary>Tinggi teks tabel dalam mm gambar (dikali skala satuan).</summary>
        public static double Allowance { get { return GetD("allowance", 5); } set { Set("allowance", value.ToString(CultureInfo.InvariantCulture)); } }
        public static double TableText { get { return GetD("teks_tabel", 50); } set { Set("teks_tabel", value.ToString(CultureInfo.InvariantCulture)); } }

        static double GetD(string k, double d)
        {
            return double.TryParse(Get(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : d;
        }

        public static string Get(string key, string def)
        {
            LoadSettings();
            return settings.TryGetValue(key, out string v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            LoadSettings();
            settings[key] = value;
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder("# Pengaturan Steel Kit\n");
                foreach (var kv in settings) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(SettingsPath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* pengaturan tidak penting untuk menggambar */ }
        }

        static void LoadSettings()
        {
            if (settingsLoaded) return;
            settingsLoaded = true;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0 && !line.StartsWith("#")) settings[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }
    }
}
