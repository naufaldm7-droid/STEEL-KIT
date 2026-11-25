using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SteelKit.Acad.Ui
{
    /// <summary>
    /// Panel parameter bertema (pengganti PropertyGrid): dikelompokkan per [Category], label dari [DisplayName],
    /// keterangan dari [Description]; pilihan dari TypeConverter/enum, sakelar untuk ya/tidak, isian angka/teks.
    /// </summary>
    public class PropertyPanel : UserControl
    {
        object target;
        readonly StackPanel body = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        readonly TextBlock help = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11.5, Margin = new Thickness(2, 8, 2, 0), MinHeight = 32 };
        bool loading;
        static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>Nama properti yang baru diubah pengguna.</summary>
        public event Action<string> Changed;

        public PropertyPanel()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body };
            grid.Children.Add(sv);
            var hb = new Border { Child = help, BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = new SolidColorBrush(Color.FromArgb(0x22, 255, 255, 255)) };
            Grid.SetRow(hb, 1);
            grid.Children.Add(hb);
            Content = grid;
            Resources.MergedDictionaries.Add(Glass.Theme());
            help.Foreground = (Brush)FindResource("SkText2");
        }

        public object Target { get => target; set { target = value; Rebuild(); } }

        public void Refresh() => Rebuild();

        static string CleanCategory(string c) => Regex.Replace(c ?? "", @"^\d+\.\s*", "").ToUpperInvariant();

        void Rebuild()
        {
            loading = true;
            body.Children.Clear();
            help.Text = "";
            if (target == null) { loading = false; return; }
            var props = TypeDescriptor.GetProperties(target).Cast<PropertyDescriptor>().Where(p => p.IsBrowsable).ToList();
            foreach (var grp in props.GroupBy(p => p.Category ?? "").OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                body.Children.Add(new TextBlock { Text = CleanCategory(grp.Key), Style = (Style)FindResource("SkLabel"), FontSize = 10, Margin = new Thickness(2, body.Children.Count == 0 ? 2 : 14, 0, 6) });
                foreach (var pd in grp) body.Children.Add(Row(pd));
            }
            loading = false;
        }

        FrameworkElement Row(PropertyDescriptor pd)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = new TextBlock { Text = pd.DisplayName, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(2, 0, 8, 0), FontSize = 12.5, ToolTip = string.IsNullOrEmpty(pd.Description) ? pd.DisplayName : pd.Description };
            g.Children.Add(label);
            var ed = Editor(pd);
            Grid.SetColumn(ed, 1);
            g.Children.Add(ed);
            string desc = string.IsNullOrEmpty(pd.Description) ? pd.DisplayName : pd.DisplayName + ": " + pd.Description;
            g.MouseEnter += (s, e) => help.Text = desc;
            ed.GotKeyboardFocus += (s, e) => help.Text = desc;
            return g;
        }

        static string Show(object v)
        {
            if (v == null) return "";
            if (v is double d) return Math.Round(d, 3).ToString("0.###", Ci);
            if (v is float f) return Math.Round(f, 3).ToString("0.###", Ci);
            return Convert.ToString(v, Ci);
        }

        FrameworkElement Editor(PropertyDescriptor pd)
        {
            var val = pd.GetValue(target);
            var t = Nullable.GetUnderlyingType(pd.PropertyType) ?? pd.PropertyType;
            if (pd.IsReadOnly)
                return new TextBox { Style = (Style)FindResource("SkField"), Text = Show(val), IsReadOnly = true };
            if (t == typeof(bool))
            {
                var cb = new CheckBox { Style = (Style)FindResource("SkSwitch"), IsChecked = (bool?)val ?? false, VerticalAlignment = VerticalAlignment.Center };
                cb.Click += (s, e) => Set(pd, cb.IsChecked == true);
                return cb;
            }
            var conv = pd.Converter;
            bool std = t.IsEnum || (conv != null && conv.GetStandardValuesSupported());
            if (std)
            {
                var items = t.IsEnum ? Enum.GetValues(t).Cast<object>().ToList()
                                     : conv.GetStandardValues()?.Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
                bool exclusive = t.IsEnum || conv.GetStandardValuesExclusive();
                var combo = new ComboBox { Style = (Style)FindResource("SkCombo"), IsEditable = !exclusive, IsTextSearchEnabled = true };
                foreach (var it in items) combo.Items.Add(Show(it));
                combo.Text = Show(val);
                if (exclusive) combo.SelectedItem = Show(val);
                combo.SelectionChanged += (s, e) => { if (!loading && combo.SelectedItem != null) Set(pd, combo.SelectedItem.ToString()); };
                if (!exclusive)
                {
                    combo.LostKeyboardFocus += (s, e) => { if (!combo.IsKeyboardFocusWithin && combo.Text != Show(pd.GetValue(target))) Set(pd, combo.Text); };
                    combo.KeyDown += (s, e) => { if (e.Key == Key.Enter) Set(pd, combo.Text); };
                }
                return combo;
            }
            var tb = new TextBox { Style = (Style)FindResource("SkField"), Text = Show(val) };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Set(pd, tb.Text); tb.SelectAll(); } };
            tb.LostKeyboardFocus += (s, e) => { if (tb.Text != Show(pd.GetValue(target))) Set(pd, tb.Text); };
            return tb;
        }

        void Set(PropertyDescriptor pd, object raw)
        {
            if (loading || target == null) return;
            try
            {
                var t = Nullable.GetUnderlyingType(pd.PropertyType) ?? pd.PropertyType;
                object v = raw;
                if (raw is string s)
                {
                    if (t == typeof(string)) v = s.Trim();
                    else if (t.IsEnum) v = Enum.Parse(t, s.Trim(), true);
                    else if (t == typeof(double) || t == typeof(float) || t == typeof(int) || t == typeof(decimal))
                        v = Convert.ChangeType(double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, Ci), t, Ci);
                    else v = pd.Converter.ConvertFromString(null, Ci, s.Trim());
                }
                pd.SetValue(target, v);
                help.Foreground = (Brush)FindResource("SkText2");
                Changed?.Invoke(pd.Name);
            }
            catch
            {
                help.Text = pd.DisplayName + ": nilai \"" + raw + "\" tidak valid.";
                help.Foreground = (Brush)FindResource("SkWarn");
                Rebuild();
            }
        }
    }

    /// <summary>Kotak pesan bertema Steel Kit.</summary>
    public static class SkDialog
    {
        /// <summary>Tampilkan pesan; kembali indeks tombol yang diklik (tombol terakhir = utama), -1 bila ditutup.</summary>
        public static int Show(Window owner, string title, string message, params string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) buttons = new[] { "OK" };
            var w = new Window { Title = "Steel Kit", Width = 460, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, ShowInTaskbar = false, Owner = owner };
            w.Resources.MergedDictionaries.Add(Glass.Theme());
            System.Windows.Shell.WindowChrome.SetWindowChrome(w, new System.Windows.Shell.WindowChrome { CaptionHeight = 30, GlassFrameThickness = new Thickness(-1), ResizeBorderThickness = new Thickness(0), UseAeroCaptionButtons = false });
            var root = new Border();
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(new Image { Source = (ImageSource)w.FindResource("SkLogo"), Width = 22, Height = 22 });
            head.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)w.FindResource("SkText") });
            sp.Children.Add(head);
            sp.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = (Brush)w.FindResource("SkText2"), LineHeight = 19 });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            int result = -1;
            for (int i = 0; i < buttons.Length; i++)
            {
                int k = i;
                var b = new Button { Content = buttons[i], MinWidth = 96, Margin = new Thickness(10, 0, 0, 0), Style = (Style)w.FindResource(i == buttons.Length - 1 ? "SkPrimary" : "SkGhost"), IsDefault = i == buttons.Length - 1, IsCancel = i == 0 && buttons.Length > 1 };
                b.Click += (s, e) => { result = k; w.Close(); };
                bar.Children.Add(b);
            }
            sp.Children.Add(bar);
            root.Child = sp;
            w.Content = root;
            Glass.Apply(w, root);
            w.FontFamily = (FontFamily)w.FindResource("SkFont");
            w.ShowDialog();
            return result;
        }
    }
}
