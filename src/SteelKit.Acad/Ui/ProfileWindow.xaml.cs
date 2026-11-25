using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SteelKit.Core;
using Anchor = SteelKit.Core.Anchor;

namespace SteelKit.Acad.Ui
{
    /// <summary>Pustaka profil: pilih / ubah / tambah ukuran, lihat properti, lalu pilih aksi menggambar.</summary>
    public partial class ProfileWindow : Window
    {
        public string Action;            // "penampang", "batang3d", "batang2d", "tabel", "keluarga"
        public Profile Result;

        Profile work;
        bool loading;
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public class SizeItem { public Profile P { get; set; } public string Name => P.Name; public string Kg { get; set; } }
        class FamItem { public Family F; public override string ToString() => FamilyInfo.Of(F).Label; }

        public ProfileWindow(Profile current)
        {
            InitializeComponent();
            Glass.Apply(this, Root);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) DialogResult = false; };
            CountBadge.Text = Store.Catalog.All.Count + " ukuran";
            foreach (Family f in Enum.GetValues(typeof(Family))) Families.Items.Add(new FamItem { F = f });
            foreach (var a in Enum.GetNames(typeof(Anchor))) AnchorBox.Items.Add(a == "TitikBerat" ? "Titik berat" : a);
            AnchorBox.SelectedIndex = (int)Store.Anchor;
            RollBox.Text = Store.Roll.ToString("0.#", Ci);
            ViewBox.Items.Add("Samping (tinggi)"); ViewBox.Items.Add("Atas (lebar sayap)");
            ViewBox.SelectedIndex = Store.TopView ? 1 : 0;
            HatchBox.IsChecked = Store.Hatch; DimsBox.IsChecked = Store.Dims;

            var start = current ?? Store.Catalog.All.First();
            loading = true;
            Families.SelectedIndex = (int)start.Family;
            loading = false;
            FillSizes(start.Name);
            ShowProfile(start.Clone());
        }

        void OnMin(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void OnMax(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void OnClose(object s, RoutedEventArgs e) => DialogResult = false;

        Family SelFamily => ((FamItem)Families.SelectedItem).F;

        void OnFamily(object s, SelectionChangedEventArgs e) { if (!loading) FillSizes(null); }
        void OnSearch(object s, TextChangedEventArgs e)
        {
            SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            FillSizes(work?.Name);
        }

        void FillSizes(string select)
        {
            loading = true;
            string q = Search.Text.Trim();
            var list = Store.Catalog.OfFamily(SelFamily)
                .Where(p => q.Length == 0 || p.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (p.Note ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(p => new SizeItem { P = p, Kg = Draw.N(p.Properties().KgPerM(Store.Density), "0.0") + " kg/m" }).ToList();
            Sizes.ItemsSource = list;
            var sel = select == null ? null : list.FirstOrDefault(i => string.Equals(i.Name, select, StringComparison.OrdinalIgnoreCase));
            loading = false;
            if (sel != null) { Sizes.SelectedItem = sel; Sizes.ScrollIntoView(sel); }
            else if (list.Count > 0 && (work == null || work.Family != SelFamily)) Sizes.SelectedIndex = 0;
        }

        void OnSize(object s, SelectionChangedEventArgs e) { if (!loading && Sizes.SelectedItem is SizeItem it) ShowProfile(it.P.Clone()); }
        void OnSizeDouble(object s, MouseButtonEventArgs e) { if (Sizes.SelectedItem != null) Finish("batang3d"); }

        void ShowProfile(Profile p)
        {
            loading = true;
            work = p;
            var rows = new List<FrameworkElement>();
            foreach (var pd in p.Info.Params)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.Children.Add(new TextBlock { Text = pd.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 });
                var tb = new TextBox { Style = (Style)FindResource("SkField"), Text = p[pd.Key].ToString("0.##", Ci), HorizontalContentAlignment = HorizontalAlignment.Right };
                string key = pd.Key;
                tb.TextChanged += (s, e) =>
                {
                    if (loading) return;
                    if (double.TryParse(tb.Text.Replace(',', '.'), NumberStyles.Float, Ci, out double v) && v >= 0 && v <= 5000) { work[key] = v; OnDimsChanged(); }
                };
                Grid.SetColumn(tb, 1);
                g.Children.Add(tb);
                rows.Add(g);
            }
            Dims.ItemsSource = rows;
            bool auto = string.Equals(p.Name, p.AutoName(), StringComparison.OrdinalIgnoreCase);
            AutoName.IsChecked = auto;
            NameBox.IsReadOnly = auto;
            NameBox.Text = p.Name;
            Usage.Text = p.Info.Usage + (string.IsNullOrEmpty(p.Note) ? "" : "\nCatatan: " + p.Note);
            loading = false;
            Refresh2();
        }

        void OnDimsChanged() { if (AutoName.IsChecked == true) SyncName(); Refresh2(); }

        void SyncName()
        {
            loading = true;
            work.Name = work.AutoName();
            NameBox.Text = work.Name;
            loading = false;
        }

        void OnName(object s, TextChangedEventArgs e) { if (!loading && work != null && AutoName.IsChecked != true) { work.Name = NameBox.Text.Trim(); ProfName.Text = work.Name; UpdateButtons(); } }
        void OnAutoName(object s, RoutedEventArgs e) { NameBox.IsReadOnly = AutoName.IsChecked == true; if (AutoName.IsChecked == true) { SyncName(); Refresh2(); } }

        void Refresh2()
        {
            var err = work.Validate();
            ProfName.Text = work.Name;
            if (err.Count == 0)
            {
                PropsList.ItemsSource = Commands.PropertyRows(work, Store.Density).Where(r => !work.Info.Params.Any(pd => pd.Label == r[0])).ToList();
                ProfKg.Text = Draw.N(work.Properties().KgPerM(Store.Density), "0.00") + " kg/m";
                SetStatus("Properti dihitung langsung dari dimensi." + (Store.Catalog.Find(work.Name) == null ? " Ukuran ini belum ada di katalog: bisa langsung dipakai atau disimpan." : ""), false);
                Section.Profile = work;
            }
            else
            {
                PropsList.ItemsSource = null;
                ProfKg.Text = "";
                SetStatus(string.Join(" ", err), true);
                Section.Profile = null;
            }
            UpdateButtons();
        }

        void SetStatus(string text, bool warn)
        {
            Status.Text = text;
            Status.Foreground = (Brush)FindResource(warn ? "SkWarn" : "SkText2");
        }

        void UpdateButtons()
        {
            bool valid = work != null && work.Validate().Count == 0;
            SaveBtn.IsEnabled = valid;
            DeleteBtn.IsEnabled = work != null && Store.Catalog.HasUser(work.Name);
            DeleteBtn.Content = work != null && Store.Catalog.IsBuiltin(work.Name) ? "Kembalikan ke bawaan" : "Hapus dari katalog saya";
        }

        void OnSave(object s, RoutedEventArgs e)
        {
            var err = Store.Catalog.SaveUser(work);
            if (err.Count > 0) { SkDialog.Show(this, "Belum bisa disimpan", string.Join("\n", err), "OK"); return; }
            try { Store.SaveCatalog(); }
            catch (Exception ex) { SkDialog.Show(this, "Gagal menyimpan katalog", ex.Message, "OK"); return; }
            FillSizes(work.Name);
            CountBadge.Text = Store.Catalog.All.Count + " ukuran";
            SetStatus(work.Name + " tersimpan di " + Store.UserCatalogPath, false);
            UpdateButtons();
        }

        void OnDelete(object s, RoutedEventArgs e)
        {
            if (!Store.Catalog.DeleteUser(work.Name)) return;
            Store.SaveCatalog();
            var back = Store.Catalog.Find(work.Name);
            FillSizes(back?.Name);
            CountBadge.Text = Store.Catalog.All.Count + " ukuran";
            if (back != null) ShowProfile(back.Clone());
        }

        void Finish(string action)
        {
            if (work == null || work.Validate().Count > 0) { System.Media.SystemSounds.Beep.Play(); return; }
            if (string.IsNullOrWhiteSpace(work.Name)) work.Name = work.AutoName();
            Action = action;
            Result = work;
            Store.Anchor = (Anchor)Math.Max(0, AnchorBox.SelectedIndex);
            if (double.TryParse(RollBox.Text.Replace(',', '.'), NumberStyles.Float, Ci, out double roll)) Store.Roll = Math.Max(-360, Math.Min(360, roll));
            Store.TopView = ViewBox.SelectedIndex == 1;
            Store.Hatch = HatchBox.IsChecked == true;
            Store.Dims = DimsBox.IsChecked == true;
            DialogResult = true;
        }

        void On3D(object s, RoutedEventArgs e) => Finish("batang3d");
        void On2D(object s, RoutedEventArgs e) => Finish("batang2d");
        void OnSectionDraw(object s, RoutedEventArgs e) => Finish("penampang");
        void OnTable(object s, RoutedEventArgs e) => Finish("tabel");
        void OnTableFamily(object s, RoutedEventArgs e) => Finish("keluarga");
    }

    /// <summary>Gambar penampang vektor: bidang bergradasi, sumbu titik berat, dimensi luar.</summary>
    public class SectionView : FrameworkElement
    {
        Profile profile;
        public Profile Profile { get => profile; set { profile = value; InvalidateVisual(); } }

        protected override void OnRender(DrawingContext dc)
        {
            double W = ActualWidth, H = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, W, H));
            var glow = new RadialGradientBrush(Color.FromArgb(0x26, 0x38, 0xBD, 0xF8), Color.FromArgb(0, 0x38, 0xBD, 0xF8)) { RadiusX = 0.55, RadiusY = 0.55 };
            dc.DrawRectangle(glow, null, new Rect(0, 0, W, H));
            if (profile == null || W < 80 || H < 80) return;
            var shape = profile.BuildShape();
            var pr = profile.Properties();
            double w = Math.Max(pr.Width, 1), h = Math.Max(pr.Height, 1), margin = 76;
            double sc = Math.Min((W - 2 * margin) / w, (H - 2 * margin) / h);
            if (sc <= 0) return;
            double cx = W / 2, cy = H / 2;
            Func<Vec2, Point> tp = v => new Point(cx + v.X * sc, cy - v.Y * sc);

            var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var c = geo.Open())
                foreach (var loop in shape.AllLoops)
                {
                    var pts = loop.Discretize().Select(tp).ToList();
                    if (pts.Count < 3) continue;
                    c.BeginFigure(pts[0], true, true);
                    c.PolyLineTo(pts.Skip(1).ToList(), true, true);
                }
            geo.Freeze();
            var fill = new LinearGradientBrush(Color.FromArgb(0xB0, 0x38, 0xBD, 0xF8), Color.FromArgb(0xB0, 0x63, 0x66, 0xF1), new Point(0, 0), new Point(1, 1));
            dc.DrawGeometry(fill, new Pen(new SolidColorBrush(Color.FromRgb(0xBA, 0xE6, 0xFD)), 1.4), geo);

            // sumbu titik berat
            var c0 = tp(new Vec2(pr.Cx, pr.Cy));
            var axis = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0xE8, 0x79, 0xF9)), 1) { DashStyle = new DashStyle(new double[] { 8, 3, 2, 3 }, 0) };
            dc.DrawLine(axis, new Point(c0.X - w * sc / 2 - 22, c0.Y), new Point(c0.X + w * sc / 2 + 22, c0.Y));
            dc.DrawLine(axis, new Point(c0.X, c0.Y - h * sc / 2 - 22), new Point(c0.X, c0.Y + h * sc / 2 + 22));
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xE8, 0x79, 0xF9)), null, c0, 3.5, 3.5);

            // dimensi luar
            var dimC = Color.FromRgb(0x7D, 0xD3, 0xFC);
            var dp = new Pen(new SolidColorBrush(dimC), 1);
            double l = cx - w / 2 * sc, r = cx + w / 2 * sc, t = cy - h / 2 * sc, b = cy + h / 2 * sc;
            Dim(dc, dp, dimC, new Point(l, b + 30), new Point(r, b + 30), Draw.N(w, "0.###"), false);
            Dim(dc, dp, dimC, new Point(l - 30, b), new Point(l - 30, t), Draw.N(h, "0.###"), true);
            var ext = new Pen(new SolidColorBrush(Color.FromArgb(0x66, dimC.R, dimC.G, dimC.B)), 0.8);
            dc.DrawLine(ext, new Point(l, b + 4), new Point(l, b + 34)); dc.DrawLine(ext, new Point(r, b + 4), new Point(r, b + 34));
            dc.DrawLine(ext, new Point(l - 4, b), new Point(l - 34, b)); dc.DrawLine(ext, new Point(l - 4, t), new Point(l - 34, t));
        }

        static void Dim(DrawingContext dc, Pen p, Color c, Point a, Point b, string text, bool vertical)
        {
            dc.DrawLine(p, a, b);
            var d = b - a; d.Normalize(); var n = new Vector(-d.Y, d.X);
            foreach (var (pt, dir) in new[] { (a, d), (b, -d) })
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(pt, true, true);
                    ctx.LineTo(pt + dir * 8 + n * 3, true, false);
                    ctx.LineTo(pt + dir * 8 - n * 3, true, false);
                }
                dc.DrawGeometry(new SolidColorBrush(c), null, g);
            }
#pragma warning disable CS0618
            var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, new SolidColorBrush(c));
#pragma warning restore CS0618
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (vertical)
            {
                dc.PushTransform(new RotateTransform(-90, mid.X, mid.Y));
                dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y - ft.Height - 2));
                dc.Pop();
            }
            else dc.DrawText(ft, new Point(mid.X - ft.Width / 2, mid.Y + 3));
        }
    }
}
