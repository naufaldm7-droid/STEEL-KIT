using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SteelKit.Core;

namespace SteelKit.Acad.Ui
{
    /// <summary>Asisten: bicara / ketik -> baca maksud -> periksa & edit -> pasang.</summary>
    public partial class AssistantWindow : Window
    {
        public AssistantPlan Plan;
        public bool AutoConnect;

        static readonly string[] Examples =
        {
            "portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150 jarak 1,2 m bracing RD16",
            "gudang mono bentang 12 meter tinggi 5 meter kemiringan 5% 4 portal jarak 5 meter kolom WF300 gording C125 jarak 1 m",
            "mezanin ukuran 6 x 8 m tinggi 3 m kolom H200 balok WF300 balok anak WF200 jarak 2 m",
            "balok WF250 panjang 6 m 3 buah jarak 2 m",
            "kolom H300 tinggi 4 meter 4 buah jarak 6 meter",
        };
        static readonly string[] ExampleTitles = { "Gudang portal 20 × 30 m", "Gudang mono 12 m", "Mezanin 6 × 8 m", "3 balok WF250", "4 kolom H300" };

        readonly ObservableCollection<MemberRow> rows = new ObservableCollection<MemberRow>();
        VoiceClient voice;
        bool listening;

        public AssistantWindow()
        {
            InitializeComponent();
            Glass.Apply(this, Root);
            MemberGrid.ItemsSource = rows;
            for (int i = 0; i < Examples.Length; i++)
            {
                var text = Examples[i];
                var b = new Button { Style = (Style)FindResource("SkChip"), Content = ExampleTitles[i], ToolTip = text };
                b.Click += (s, e) => { Input.Text = text; DoParse(); };
                Chips.Children.Add(b);
            }
            ShowTips();
            UpdateVoiceIdle();
            Loaded += (s, e) => { Input.Focus(); SetMicLabel(VoiceClient.MicChoice); };
            Closed += (s, e) => voice?.Kill();
        }

        // ------------------------------------------------------------------ jendela
        void OnMin(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void OnMax(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void OnCancel(object s, RoutedEventArgs e) { DialogResult = false; }

        void OnInputChanged(object s, TextChangedEventArgs e) => Placeholder.Visibility = string.IsNullOrEmpty(Input.Text) ? Visibility.Visible : Visibility.Collapsed;
        void OnInputKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; DoParse(); }
        }
        void OnParse(object s, RoutedEventArgs e) => DoParse();

        // ------------------------------------------------------------------ baca maksud
        void ShowTips()
        {
            Summary.Text = "Belum ada kalimat. Bicara, ketik, atau pilih contoh di atas.";
            Notes.ItemsSource = new[]
            {
                Note.Info("Sebut jenis struktur: portal / gudang / mezanin / balok / kolom."),
                Note.Info("Sebut ukuran: bentang, tinggi, kemiringan, jumlah bay, jarak (meter atau mm)."),
                Note.Info("Sebut profil bila sudah ada: kolom WF400, rafter WF350, gording C150, bracing RD16."),
                Note.Info("Yang tidak disebut diisi nilai tipikal dan ditampilkan di sini untuk Anda periksa."),
            };
        }

        void DoParse()
        {
            if (string.IsNullOrWhiteSpace(Input.Text)) { Plan = null; rows.Clear(); ShowTips(); Refresh(); Input.Focus(); return; }
            Plan = Assistant.Parse(Input.Text);
            Summary.Text = string.IsNullOrEmpty(Plan.Summary) ? "Belum ada batang yang dikenali dari kalimat ini." : Plan.Summary;
            Notes.ItemsSource = Plan.Unknown.Select(Note.Warn).Concat(Plan.Assumptions.Select(Note.Info)).ToList();
            rows.Clear();
            foreach (var m in Plan.Members) rows.Add(new MemberRow(m, Refresh));
            Refresh();
        }

        void Refresh()
        {
            GridEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var on = rows.Where(r => r.On).Select(r => r.M).ToList();
            Preview.SetMembers(rows.Select(r => (r.M, r.On)).ToList());
            PlaceButton.IsEnabled = on.Count > 0;
            PlaceText.Text = on.Count > 0 ? "Pasang " + on.Count + " batang" : "Pasang di gambar";
            Stats.Children.Clear();
            if (rows.Count == 0) return;
            double len = on.Sum(m => m.Length) / 1000, kg = 0;
            foreach (var m in on)
            {
                var p = Env.Catalog.Find(m.Profile);
                if (p != null) kg += p.Properties().KgPerM(Env.Density) * m.Length / 1000;
            }
            var ci = new CultureInfo("id-ID");
            Stats.Children.Add(Tile(on.Count.ToString(ci), "batang"));
            Stats.Children.Add(Tile(len.ToString("#,0", ci) + " m", "panjang total"));
            Stats.Children.Add(Tile("± " + (kg / 1000).ToString("#,0.0", ci) + " t", "berat baja bersih"));
        }

        Border Tile(string value, string caption)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = value, FontSize = 18, FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("SkFontDisplay") });
            sp.Children.Add(new TextBlock { Text = caption, FontSize = 11, Foreground = (Brush)FindResource("SkText2") });
            return new Border { Child = sp, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(Color.FromArgb(0x12, 255, 255, 255)), BorderBrush = new SolidColorBrush(Color.FromArgb(0x1F, 255, 255, 255)), BorderThickness = new Thickness(1) };
        }

        void OnCellEdit(object s, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(new Action(Refresh));

        void OnPlace(object s, RoutedEventArgs e)
        {
            MemberGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (Plan == null) return;
            var on = rows.Where(r => r.On).Select(r => r.M).ToList();
            var bad = on.Where(m => Env.Catalog.Find(m.Profile) == null).Select(m => m.Profile).Distinct().ToList();
            if (bad.Count > 0)
            {
                MessageBox.Show(this, "Profil tidak ada di katalog: " + string.Join(", ", bad) + "\nPerbaiki di kolom Profil (mis. WF400x200x8x13) atau tambahkan lewat SKPROFIL.", "Steel Kit", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Plan.Members = on;
            AutoConnect = AutoConnectBox.IsChecked == true;
            DialogResult = true;
        }

        // ------------------------------------------------------------------ suara
        void UpdateVoiceIdle()
        {
            VoiceProgress.Visibility = Visibility.Collapsed;
            if (VoiceClient.ExePath() == null)
            {
                VoiceStatus.Text = "Pembantu suara tidak ditemukan. Pasang ulang Steel Kit untuk memakai suara; mengetik tetap bisa.";
                MicButton.IsEnabled = false; MicButton.Opacity = 0.4;
                return;
            }
            bool ready = VoiceClient.ModelReady();
            DownloadButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            FolderButton.Visibility = Visibility.Collapsed;
            VoiceStatus.Text = ready
                ? "Klik mikrofon lalu bicara dalam bahasa Indonesia sehari-hari; berhenti otomatis saat Anda diam. Suara diproses di komputer ini, tidak dikirim ke internet."
                : "Untuk bicara, model suara offline perlu diunduh sekali (" + VoiceClient.ModelMb + " MB). Setelah itu semua jalan tanpa internet. Mengetik bisa langsung dipakai.";
        }

        void OnMic(object s, RoutedEventArgs e)
        {
            if (listening) { voice?.Stop(); VoiceStatus.Text = "Menghentikan..."; return; }
            if (!VoiceClient.ModelReady()) { OnDownload(s, e); return; }
            voice?.Kill();
            voice = new VoiceClient();
            voice.Line += OnVoiceLine;
            voice.Exited += () => Dispatcher.BeginInvoke(new Action(() => SetListening(false)));
            var mic = VoiceClient.MicChoice;
            if (!voice.Start("dengar 30 " + (string.IsNullOrEmpty(mic) ? "auto" : "\"" + mic + "\""))) { VoiceStatus.Text = "Pembantu suara gagal dijalankan."; return; }
            SetListening(true);
            VoiceStatus.Text = "Menyiapkan mikrofon...";
        }

        void SetListening(bool on)
        {
            listening = on;
            MicGlyph.Text = on ? "" : "";
            MicRing.Opacity = on ? 0.35 : 0;
            if (!on) { MicRingScale.ScaleX = MicRingScale.ScaleY = 1; }
        }

        void OnVoiceLine(string key, string rest)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (key)
                {
                    case "STATUS": VoiceStatus.Text = rest; break;
                    case "MIC": SetMicLabel(rest); break;
                    case "LEVEL":
                        if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double lv))
                        {
                            double sc = 1 + Math.Min(1, lv) * 0.55;
                            MicRingScale.ScaleX = MicRingScale.ScaleY = 0.6 * MicRingScale.ScaleX + 0.4 * sc;
                        }
                        break;
                    case "DL":
                        VoiceProgress.Visibility = Visibility.Visible;
                        if (double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double pct)) { VoiceProgress.Value = pct; VoiceStatus.Text = "Mengunduh model suara offline... " + pct.ToString("0") + "% dari " + VoiceClient.ModelMb + " MB (sekali saja)"; }
                        break;
                    case "TEXT":
                        if (string.IsNullOrWhiteSpace(rest)) { VoiceStatus.Text = "Suara terdengar tapi belum bisa diubah menjadi kata. Coba lagi lebih dekat ke mikrofon."; break; }
                        Input.Text = rest.Trim();
                        Input.CaretIndex = Input.Text.Length;
                        VoiceStatus.Text = "Terdengar: “" + rest.Trim() + "”  -  periksa usulan di bawah, edit kalimat bila perlu.";
                        DoParse();
                        break;
                    case "ERR":
                        VoiceStatus.Text = ErrorText(rest);
                        if (rest.StartsWith("MODEL_BELUM_ADA")) UpdateVoiceIdle();
                        if (rest.StartsWith("Unduhan") || rest.Contains("Http") || rest.Contains("WebException") || rest.Contains("Socket")) { DownloadButton.Visibility = Visibility.Visible; FolderButton.Visibility = Visibility.Visible; VoiceProgress.Visibility = Visibility.Collapsed; }
                        break;
                    case "DONE":
                        if (VoiceProgress.Visibility == Visibility.Visible && VoiceClient.ModelReady()) { UpdateVoiceIdle(); VoiceStatus.Text = "Model suara siap. Klik mikrofon lalu bicara."; }
                        break;
                }
            }));
        }

        static string ErrorText(string err)
        {
            if (err.StartsWith("TIDAK_ADA_SUARA")) return "Tidak terdengar suara dari mikrofon ini. Coba pilih mikrofon lain lewat menu di bawah tombol mikrofon, periksa mikrofon tidak dibisukan, atau izin di Pengaturan Windows › Privasi › Mikrofon.";
            if (err.StartsWith("TIDAK_ADA_MIC")) return "Tidak ada mikrofon terdeteksi. Sambungkan mikrofon / headset, lalu coba lagi.";
            if (err.StartsWith("MODEL_BELUM_ADA")) return "Model suara belum ada.";
            if (err.Contains("Http") || err.Contains("WebException") || err.Contains("Socket") || err.StartsWith("Unduhan"))
                return "Unduhan gagal (jaringan mungkin memblokir). Alternatif: unduh manual " + VoiceClient.ModelFile + " dari huggingface.co/ggerganov/whisper.cpp lalu taruh di folder (tombol Buka folder).";
            return "Suara: " + err;
        }

        void SetMicLabel(string name)
        {
            if (MicPick.Template.FindName("MicPickText", MicPick) is TextBlock t)
            {
                var n = string.IsNullOrEmpty(name) ? "Otomatis" : System.Text.RegularExpressions.Regex.Replace(name, @"^(Microphone|Mikrofon)\s*(Array)?\s*\(?", "").TrimEnd(')');
                t.Text = n.Length == 0 ? name : n;
                MicPick.ToolTip = "Mikrofon: " + (string.IsNullOrEmpty(name) ? "otomatis" : name) + ". Klik untuk mengganti.";
            }
        }

        void OnMicPick(object s, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = MicPick, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            string cur = VoiceClient.MicChoice;
            var auto = new MenuItem { Header = "Otomatis (mikrofon fisik yang menerima suara)", IsCheckable = true, IsChecked = string.IsNullOrEmpty(cur) };
            auto.Click += (o, a) => { VoiceClient.MicChoice = ""; SetMicLabel(""); };
            menu.Items.Add(auto);
            foreach (var name in VoiceClient.ListMics())
            {
                var mi = new MenuItem { Header = name, IsCheckable = true, IsChecked = name == cur };
                mi.Click += (o, a) => { VoiceClient.MicChoice = name; SetMicLabel(name); };
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        void OnDownload(object s, RoutedEventArgs e)
        {
            voice?.Kill();
            voice = new VoiceClient();
            voice.Line += OnVoiceLine;
            DownloadButton.Visibility = Visibility.Collapsed;
            VoiceProgress.Visibility = Visibility.Visible; VoiceProgress.Value = 0;
            VoiceStatus.Text = "Menghubungi server model...";
            if (!voice.Start("unduh")) { VoiceStatus.Text = "Pembantu suara gagal dijalankan."; DownloadButton.Visibility = Visibility.Visible; }
        }

        void OnOpenFolder(object s, RoutedEventArgs e)
        {
            try { System.IO.Directory.CreateDirectory(VoiceClient.ModelDir); System.Diagnostics.Process.Start("explorer.exe", "\"" + VoiceClient.ModelDir + "\""); } catch { }
        }

        // ------------------------------------------------------------------ data
        public class Note
        {
            public string Icon { get; set; }
            public Brush IconBrush { get; set; }
            public string Text { get; set; }
            static readonly Brush Cyan = Freeze(new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)));
            static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C)));
            static Brush Freeze(Brush b) { b.Freeze(); return b; }
            public static Note Info(string t) => new Note { Icon = "", IconBrush = Cyan, Text = t };
            public static Note Warn(string t) => new Note { Icon = "", IconBrush = Orange, Text = t };
        }

        /// <summary>Satu baris tabel, langsung mengubah usulan batang.</summary>
        public class MemberRow : INotifyPropertyChanged
        {
            public readonly MemberProposal M;
            readonly Action changed;
            bool on = true;
            public MemberRow(MemberProposal m, Action changed) { M = m; this.changed = changed; }
            public event PropertyChangedEventHandler PropertyChanged;
            void Raise(string n) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); if (n != "Length") PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Length")); changed(); }

            static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
            static string N(double v) => Math.Round(v, 1).ToString(Ci);
            static double D(string s, double old) => double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, Ci, out double d) ? d : old;

            public bool On { get => on; set { on = value; Raise("On"); } }
            public string Role => M.Role;
            public Brush RoleBrush => IsoView.RoleBrush(M.Role);
            public string Profile { get => M.Profile; set { M.Profile = (value ?? "").Trim(); Raise("Profile"); } }
            public string Length => Math.Round(M.Length).ToString("#,0", new CultureInfo("id-ID"));
            public string X1 { get => N(M.S.X); set { M.S = new Vec3(D(value, M.S.X), M.S.Y, M.S.Z); Raise("X1"); } }
            public string Y1 { get => N(M.S.Y); set { M.S = new Vec3(M.S.X, D(value, M.S.Y), M.S.Z); Raise("Y1"); } }
            public string Z1 { get => N(M.S.Z); set { M.S = new Vec3(M.S.X, M.S.Y, D(value, M.S.Z)); Raise("Z1"); } }
            public string X2 { get => N(M.E.X); set { M.E = new Vec3(D(value, M.E.X), M.E.Y, M.E.Z); Raise("X2"); } }
            public string Y2 { get => N(M.E.Y); set { M.E = new Vec3(M.E.X, D(value, M.E.Y), M.E.Z); Raise("Y2"); } }
            public string Z2 { get => N(M.E.Z); set { M.E = new Vec3(M.E.X, M.E.Y, D(value, M.E.Z)); Raise("Z2"); } }
            public string Roll { get => N(M.Roll); set { M.Roll = D(value, M.Roll); Raise("Roll"); } }
        }
    }
}
