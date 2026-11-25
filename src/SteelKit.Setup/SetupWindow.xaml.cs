using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SteelKit.Setup
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var app = new Application();
            bool remove = args.Any(a => a.Equals("/lepas", StringComparison.OrdinalIgnoreCase) || a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
            bool quiet = args.Any(a => a.Equals("/diam", StringComparison.OrdinalIgnoreCase) || a.Equals("/quiet", StringComparison.OrdinalIgnoreCase));
            if (quiet) { try { if (remove) SetupWindow.Uninstall(false); else SetupWindow.Install(null); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; } }
            return app.Run(new SetupWindow(remove));
        }
    }

    public partial class SetupWindow : Window
    {
        static readonly string Ver = typeof(SetupWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.1.0";
        static string Dest => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Autodesk", "ApplicationPlugins", "SteelKit.bundle");
        static string ModelDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteelKit");
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SteelKit";

        readonly bool removeMode;
        bool done;

        static readonly Brush Cyan = new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8));
        static readonly Brush Gray = new SolidColorBrush(Color.FromRgb(0x6B, 0x7B, 0x8E));
        static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));

        public SetupWindow(bool remove)
        {
            InitializeComponent();
            SteelKit.Acad.Ui.Glass.Apply(this, Root);
            removeMode = remove;
            Footer.Text = "v" + Ver + " · gratis · MIT · @nrozan";
            var found = FindAutoCad();
            Versions.ItemsSource = found.Count > 0
                ? found.Select(v => new Row("", Cyan, v)).ToList()
                : new List<Row> { new Row("", Orange, "AutoCAD 2021-2027 belum ditemukan. Steel Kit tetap bisa dipasang dan aktif saat AutoCAD terpasang.") };
            var model = new FileInfo(Path.Combine(ModelDir, "suara", "ggml-small-q5_1.bin"));
            if (remove || (model.Exists && model.Length == 190085487)) { VoiceBox.IsChecked = false; VoiceBox.Visibility = Visibility.Collapsed; }
            string installed = InstalledVersion();
            if (removeMode)
            {
                Heading.Text = "Lepas Steel Kit";
                Message.Text = "Steel Kit dan model suaranya akan dihapus dari akun Windows ini. Gambar Anda tidak terpengaruh.";
                MainText.Text = "Lepas";
            }
            else if (installed != null)
            {
                Sub.Text = "Terpasang versi " + installed + " · paket ini versi " + Ver;
                MainText.Text = installed == Ver ? "Pasang ulang" : "Perbarui";
                RemoveButton.Visibility = Visibility.Visible;
            }
        }

        class Row { public string Icon { get; } public Brush Brush { get; } public string Text { get; } public Row(string i, Brush b, string t) { Icon = i; Brush = b; Text = t; } }

        void OnClose(object s, RoutedEventArgs e) => Close();

        static List<string> FindAutoCad()
        {
            var names = new Dictionary<string, string> { ["R24.0"] = "2021", ["R24.1"] = "2022", ["R24.2"] = "2023", ["R24.3"] = "2024", ["R25.0"] = "2025", ["R25.1"] = "2026", ["R26.0"] = "2027" };
            var years = new SortedSet<string>();
            try
            {
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD"))
                    if (hk != null) foreach (var k in hk.GetSubKeyNames()) if (names.TryGetValue(k, out var y)) years.Add(y);
            }
            catch { }
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            for (int y = 2021; y <= 2027; y++) if (File.Exists(Path.Combine(pf, "Autodesk", "AutoCAD " + y, "acad.exe"))) years.Add(y.ToString());
            return years.Select(y => "AutoCAD " + y + "  -  didukung").ToList();
        }

        static string InstalledVersion()
        {
            var pc = Path.Combine(Dest, "PackageContents.xml");
            if (!File.Exists(pc)) return null;
            var t = File.ReadAllText(pc);
            int i = t.IndexOf("AppVersion=\"", StringComparison.Ordinal);
            return i < 0 ? "?" : t.Substring(i + 12, t.IndexOf('"', i + 12) - i - 12);
        }

        static bool AutoCadRunning() => Process.GetProcessesByName("acad").Length > 0 || Process.GetProcessesByName("accoreconsole").Length > 0;

        async void OnMain(object s, RoutedEventArgs e)
        {
            if (done) { Close(); return; }
            if (AutoCadRunning())
            {
                Message.Text = "AutoCAD masih terbuka. Simpan pekerjaan, tutup AutoCAD, lalu klik " + MainText.Text + " lagi.";
                Message.Foreground = Orange;
                return;
            }
            Message.Foreground = (Brush)FindResource("SkText2");
            MainButton.IsEnabled = RemoveButton.IsEnabled = false;
            try
            {
                if (removeMode)
                {
                    Uninstall(true);
                    Heading.Text = "Selesai";
                    Message.Text = "Steel Kit sudah dilepas. Terima kasih sudah mencoba.";
                }
                else
                {
                    Progress.Visibility = Visibility.Visible;
                    VoiceBox.IsEnabled = false;
                    Message.Text = "Memasang Steel Kit...";
                    var prog = new Progress<int>(p => Progress.Value = p);
                    await Task.Run(() => Install(prog));
                    var rows = new List<Row> { new Row("", Cyan, "Steel Kit " + Ver + " terpasang untuk AutoCAD 2021-2027") };
                    string voiceNote;
                    if (VoiceBox.IsChecked == true)
                    {
                        Progress.Value = 0;
                        var err = await DownloadVoice(pct => { Progress.Value = pct; Message.Text = "Mengunduh model suara offline... " + pct + "% dari 181 MB (sekali saja)"; });
                        if (err == null) { rows.Add(new Row("", Cyan, "Suara offline siap (bahasa Indonesia)")); voiceNote = ""; }
                        else { rows.Add(new Row("", Orange, "Model suara belum terunduh: " + err)); voiceNote = " Model suara bisa diunduh nanti dari tombol mikrofon di Asisten."; }
                    }
                    else voiceNote = " Model suara offline bisa diunduh nanti dari tombol mikrofon di Asisten.";
                    Heading.Text = "Selesai";
                    Message.Text = "Buka AutoCAD, lalu pilih tab ribbon Steel Kit (atau ketik SKASISTEN)." + voiceNote;
                    Versions.ItemsSource = rows;
                }
                done = true;
                MainText.Text = "Tutup";
                RemoveButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Message.Text = "Gagal: " + ex.Message;
                Message.Foreground = Orange;
            }
            MainButton.IsEnabled = RemoveButton.IsEnabled = true;
        }

        void OnRemove(object s, RoutedEventArgs e)
        {
            if (MessageBox.Show(this, "Lepas Steel Kit dari akun Windows ini?", "Steel Kit", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (AutoCadRunning()) { Message.Text = "AutoCAD masih terbuka. Tutup AutoCAD dulu."; Message.Foreground = Orange; return; }
            Uninstall(true);
            Heading.Text = "Selesai"; Message.Text = "Steel Kit sudah dilepas.";
            done = true; MainText.Text = "Tutup"; RemoveButton.Visibility = Visibility.Collapsed;
        }

        /// <summary>Unduh model suara lewat pembantu suara yang baru terpasang. Kembali null bila berhasil, atau pesan galat.</summary>
        static Task<string> DownloadVoice(Action<int> progress)
        {
            var exe = Path.Combine(Dest, "Contents", "Suara", "SteelKitSuara.exe");
            var ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            return Task.Run(() =>
            {
                if (!File.Exists(exe)) return "pembantu suara tidak ada";
                string err = null; bool done = false;
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo(exe, "unduh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8 }))
                    {
                        string line;
                        while ((line = p.StandardOutput.ReadLine()) != null)
                        {
                            if (line.StartsWith("DL ") && int.TryParse(line.Substring(3), out int pct)) ui.BeginInvoke(new Action(() => progress(pct)));
                            else if (line.StartsWith("ERR ")) err = line.Substring(4);
                            else if (line == "DONE") done = true;
                        }
                        p.WaitForExit();
                    }
                }
                catch (Exception ex) { err = ex.Message; }
                if (err != null && (err.Contains("Http") || err.Contains("Web") || err.Contains("Socket") || err.Contains("koneksi"))) err = "tidak ada koneksi / diblokir jaringan";
                return done && err == null ? null : (err ?? "gagal");
            });
        }

        public static void Install(IProgress<int> progress)
        {
            var asm = typeof(SetupWindow).Assembly;
            using (var res = asm.GetManifestResourceStream("payload.zip"))
            {
                if (res == null) throw new InvalidOperationException("Isi paket tidak ada di penginstal ini.");
                var tmp = Path.Combine(Path.GetTempPath(), "SteelKitSetup_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                try
                {
                    using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
                    {
                        var files = zip.Entries.Where(en => !string.IsNullOrEmpty(en.Name)).ToList();
                        for (int i = 0; i < files.Count; i++)
                        {
                            var target = Path.GetFullPath(Path.Combine(tmp, files[i].FullName));
                            if (!target.StartsWith(tmp, StringComparison.OrdinalIgnoreCase)) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            files[i].ExtractToFile(target, true);
                            progress?.Report((i + 1) * 90 / files.Count);
                        }
                    }
                    var src = Path.Combine(tmp, "SteelKit.bundle");
                    if (Directory.Exists(Dest)) Directory.Delete(Dest, true);
                    Directory.CreateDirectory(Path.GetDirectoryName(Dest));
                    Directory.Move(src, Dest);
                }
                finally { try { Directory.Delete(tmp, true); } catch { } }
            }
            // salinan penginstal untuk "Lepas" dari Pengaturan Windows > Aplikasi
            var self = asm.Location;
            var keep = Path.Combine(Dest, "SteelKitSetup.exe");
            File.Copy(self, keep, true);
            long kb = new DirectoryInfo(Dest).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Steel Kit untuk AutoCAD");
                k.SetValue("DisplayVersion", Ver);
                k.SetValue("Publisher", "@nrozan");
                k.SetValue("DisplayIcon", keep);
                k.SetValue("InstallLocation", Dest);
                k.SetValue("UninstallString", "\"" + keep + "\" /lepas");
                k.SetValue("QuietUninstallString", "\"" + keep + "\" /lepas /diam");
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            progress?.Report(100);
        }

        public static void Uninstall(bool withModel)
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
            if (withModel) try { if (Directory.Exists(ModelDir)) Directory.Delete(ModelDir, true); } catch { }
            if (!Directory.Exists(Dest)) return;
            var self = Path.GetFullPath(typeof(SetupWindow).Assembly.Location);
            if (self.StartsWith(Path.GetFullPath(Dest), StringComparison.OrdinalIgnoreCase))
            {
                // berjalan dari dalam folder yang dihapus: hapus setelah proses ini selesai
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + Dest + "\"") { CreateNoWindow = true, UseShellExecute = false });
            }
            else Directory.Delete(Dest, true);
        }
    }
}
