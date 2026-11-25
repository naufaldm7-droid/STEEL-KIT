using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteelKit.Core;

namespace SteelKit.Acad.Ui
{
    /// <summary>
    /// Jendela sambungan: galeri bergambar teknik (dari geometri sebenarnya), rekomendasi pemakaian,
    /// parameter, pratinjau dua tampak berdimensi, dan hasil cek aturan jarak/ukuran minimum.
    /// </summary>
    public partial class ConnWindow : Window
    {
        public Connection Result;
        public string Action;              // "buat", "detail", "keduanya"

        Connection conn;
        ConnResult built;
        readonly List<GalleryItem> all = new List<GalleryItem>();

        public class GalleryItem { public ConnType Type { get; set; } public string Label { get; set; } public ImageSource Thumb { get; set; } }

        public class CheckRow
        {
            public string Icon { get; set; } public Brush Brush { get; set; } public Brush TextBrush { get; set; }
            public string Item { get; set; } public string Rule { get; set; }
        }

        public ConnWindow(Connection start, bool lockType, string context)
        {
            InitializeComponent();
            Glass.Apply(this, Root);
            if (!string.IsNullOrEmpty(context)) { ContextText.Text = context; ContextBadge.Visibility = Visibility.Visible; }
            if (lockType) GalleryTitle.Text = "JENIS SAMBUNGAN (DARI BATANG TERPILIH)";
            foreach (ConnType t in Enum.GetValues(typeof(ConnType)))
            {
                ImageSource img = null;
                try
                {
                    var sample = t == start?.Type ? start.Clone() : Connection.Create(t);
                    if (t != start?.Type) sample.Recommend();
                    using (var bmp = ConnRender.Render(sample.Build(), 0, new System.Drawing.Size(188, 140), false)) img = ToImage(bmp);
                }
                catch { }
                all.Add(new GalleryItem { Type = t, Label = Connection.Label(t), Thumb = img });
            }
            Gallery.ItemsSource = all;
            Props.Changed += name => Rebuild();
            SetConn(start ?? MakeDefault());
            Gallery.SelectedItem = all.FirstOrDefault(g => g.Type == conn.Type);
            Loaded += (s, e) => Gallery.ScrollIntoView(Gallery.SelectedItem);
        }

        static Connection MakeDefault() { var c = Connection.Create(ConnType.PelatDasar); c.Recommend(); return c; }

        public static ImageSource ToImage(System.Drawing.Bitmap bmp)
        {
            using (var ms = new System.IO.MemoryStream())
            {
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        void OnMin(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void OnMax(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void OnCancel(object s, RoutedEventArgs e) => DialogResult = false;

        void OnSearch(object s, TextChangedEventArgs e)
        {
            var q = Search.Text.Trim();
            SearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Gallery.ItemsSource = q.Length == 0 ? all : all.Where(g => g.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Gallery.SelectedItem = all.FirstOrDefault(g => g.Type == conn?.Type);
        }

        void OnGallery(object s, SelectionChangedEventArgs e)
        {
            if (!(Gallery.SelectedItem is GalleryItem g) || (conn != null && conn.Type == g.Type)) return;
            var c = Connection.Create(g.Type);
            CarryProfiles(conn, c);
            c.Recommend();
            SetConn(c);
        }

        void OnRecommend(object s, RoutedEventArgs e) { conn.Recommend(); Props.Refresh(); Rebuild(); }

        /// <summary>Bawa profil dari jenis lama ke jenis baru bila bisa (kolom/balok/rafter).</summary>
        static void CarryProfiles(Connection from, Connection to)
        {
            if (from == null) return;
            string col = null, beam = null;
            switch (from)
            {
                case BasePlate b: col = b.Kolom; break;
                case EndPlate e: col = e.Kolom; beam = e.Balok; break;
                case FinPlate f: col = f.Penopang; beam = f.Balok; break;
                case DoubleCleat d: col = d.Penopang; beam = d.Balok; break;
                case KneeHaunch k: col = k.Kolom; beam = k.Rafter; break;
                case SplicePlate s: beam = s.Profil; break;
                case PurlinCleat p: beam = p.Rafter; break;
                case GussetNode g: col = g.ProfilA; beam = g.ProfilB; break;
            }
            bool ih(string n) { var p = n == null ? null : Env.Catalog.Find(n); return p != null && (p.Family == Family.WF || p.Family == Family.H); }
            switch (to)
            {
                case BasePlate b: if (col != null) b.Kolom = col; break;
                case EndPlate e: if (ih(col)) e.Kolom = col; if (ih(beam)) e.Balok = beam; break;
                case FinPlate f: if (ih(col)) f.Penopang = col; if (ih(beam)) f.Balok = beam; break;
                case DoubleCleat d: if (ih(col)) d.Penopang = col; if (ih(beam)) d.Balok = beam; break;
                case KneeHaunch k: if (ih(col)) k.Kolom = col; if (ih(beam)) k.Rafter = beam; break;
                case SplicePlate s: if (ih(beam)) s.Profil = beam; break;
                case PurlinCleat p: if (ih(beam)) p.Rafter = beam; break;
                case GussetNode g:
                    bool lt(string n) { var p = n == null ? null : Env.Catalog.Find(n); return p != null && LegFace.Supported(p.Family); }
                    if (lt(col)) g.ProfilA = col; if (lt(beam)) g.ProfilB = beam; break;
            }
        }

        void SetConn(Connection c)
        {
            conn = c;
            Props.Target = c;
            ConnTitle.Text = Connection.Label(c.Type);
            var recs = Recommendations.All.Where(r => r.Type == c.Type).ToList();
            Recommend.Text = (recs.Count == 0 ? "" : string.Join("\n\n", recs.Select(r => "• " + r.Condition + "\n   " + r.Note)) + "\n\n") + Recommendations.Disclaimer;
            Rebuild();
        }

        static readonly Brush Cyan = Frozen(Color.FromRgb(0x38, 0xBD, 0xF8));
        static readonly Brush Orange = Frozen(Color.FromRgb(0xFB, 0x92, 0x3C));
        static readonly Brush Text1 = Frozen(Color.FromRgb(0xE8, 0xEE, 0xF5));
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        void Rebuild()
        {
            var rows = new List<CheckRow>();
            try
            {
                built = conn.Build();
                foreach (var c in built.Checks.OrderBy(x => x.Ok))
                    rows.Add(new CheckRow { Icon = c.Ok ? "" : "", Brush = c.Ok ? Cyan : Orange, TextBrush = c.Ok ? Text1 : Orange, Item = c.Item, Rule = c.Rule });
                int bad = built.Checks.Count(x => !x.Ok);
                CheckCount.Text = bad == 0 ? built.Checks.Count + " memenuhi" : bad + " belum memenuhi";
                CheckCount.Foreground = bad == 0 ? Cyan : Orange;
                ConnSummary.Text = ConnDraw.Summary(built);
            }
            catch (Exception ex)
            {
                built = null;
                rows.Add(new CheckRow { Icon = "", Brush = Orange, TextBrush = Orange, Item = ex.Message, Rule = "parameter" });
                CheckCount.Text = ""; ConnSummary.Text = "";
            }
            Checks.ItemsSource = rows;
            RenderPreview();
        }

        void OnPreviewSize(object s, SizeChangedEventArgs e) => RenderPreview();

        void RenderPreview()
        {
            int w = (int)PreviewHost.ActualWidth, h = (int)PreviewHost.ActualHeight;
            if (built == null || w < 60 || h < 60) { Preview.Source = null; return; }
            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pw = (int)(w * dpi), ph = (int)(h * dpi);
            using (var bmp = new System.Drawing.Bitmap(pw, ph))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.FromArgb(27, 34, 44));
                int n = Math.Max(1, built.Views.Count);
                for (int i = 0; i < built.Views.Count; i++)
                    using (var v = ConnRender.Render(built, i, new System.Drawing.Size(pw / n, ph), true)) g.DrawImage(v, i * pw / n, 0);
                using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(40, 255, 255, 255)))
                    for (int i = 1; i < n; i++) g.DrawLine(pen, i * pw / n, 12, i * pw / n, ph - 12);
                var img = ToImage(bmp);
                Preview.Source = img;
                Preview.Width = w; Preview.Height = h;
            }
        }

        void Finish(string action)
        {
            if (built == null) { SkDialog.Show(this, "Parameter belum valid", "Periksa parameter yang ditandai di daftar cek.", "OK"); return; }
            int bad = built.Checks.Count(c => !c.Ok);
            if (bad > 0 && SkDialog.Show(this, "Aturan belum terpenuhi", "Ada " + bad + " aturan jarak/ukuran minimum yang belum terpenuhi. Tetap lanjut?", "Periksa lagi", "Tetap lanjut") != 1) return;
            Result = conn;
            Action = action;
            DialogResult = true;
        }

        void OnDetail(object s, RoutedEventArgs e) => Finish("detail");
        void OnMake(object s, RoutedEventArgs e) => Finish("buat");
        void OnBoth(object s, RoutedEventArgs e) => Finish("keduanya");
    }
}
