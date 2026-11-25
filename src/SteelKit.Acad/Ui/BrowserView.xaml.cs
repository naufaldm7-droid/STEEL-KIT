using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.AutoCAD.DatabaseServices;
using SteelKit.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Visibility = System.Windows.Visibility;

namespace SteelKit.Acad.Ui
{
    /// <summary>Palet kiri: batang & sambungan terpasang per level / kategori / profil, dengan properti yang bisa diedit.</summary>
    public partial class BrowserView : UserControl
    {
        public class Node
        {
            public string Text { get; set; }
            public string Info { get; set; }
            public Brush Dot { get; set; }
            public Visibility DotVisible => Dot == null ? Visibility.Collapsed : Visibility.Visible;
            public FontWeight Weight { get; set; } = FontWeights.Normal;
            public bool IsExpanded { get; set; }
            public List<Node> Children { get; } = new List<Node>();
            public List<ObjectId> Ids = new List<ObjectId>();
            public ModelData.Item First;
            public bool IsConn;
            public IEnumerable<ObjectId> AllIds() => Children.Count == 0 ? Ids : Children.SelectMany(c => c.AllIds()).Distinct();
        }

        List<ObjectId> selected = new List<ObjectId>();
        MemberProps props;
        Connection conn; ObjectId connId; string[] connLinks;
        static readonly Brush ConnDot = Freeze(Color.FromRgb(0x7D, 0xD3, 0xFC));
        static Brush Freeze(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        public BrowserView()
        {
            InitializeComponent();
            GroupBox.Items.Add("Level › Kategori"); GroupBox.Items.Add("Kategori › Level"); GroupBox.Items.Add("Profil");
            GroupBox.SelectedIndex = 0;
            foreach (var (t, icon, cmd) in new[] { ("Sambung otomatis", "", "_SKSAMBUNGAUTO "), ("Layout", "", "_SKLAYOUT "), ("Excel", "", "_SKEXCEL "), ("Level", "", "_SKLEVEL "), ("Asisten", "", "_SKASISTEN ") })
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock { Text = icon, FontFamily = (FontFamily)FindResource("SkIcons"), FontSize = 11, Margin = new Thickness(0, 1, 6, 0) });
                sp.Children.Add(new TextBlock { Text = t });
                var b = new Button { Content = sp, Style = (Style)FindResource("SkGhost"), Padding = new Thickness(9, 4, 9, 4), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 6) };
                b.Click += (s, e) => AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("\x03\x03" + cmd, true, false, true);
                Quick.Children.Add(b);
            }
            Props.Changed += OnPropChanged;
        }

        void OnGroup(object s, SelectionChangedEventArgs e) { if (IsLoaded) Reload(); }
        void OnFilter(object s, TextChangedEventArgs e) => Reload();
        void OnReload(object s, RoutedEventArgs e) => Reload();

        public void Reload()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            List<ModelData.Item> items;
            try { using (var tr = doc.Database.TransactionManager.StartTransaction()) { items = ModelData.Read(tr, doc.Database); tr.Commit(); } }
            catch (Exception ex) { Status.Text = "Gagal membaca model: " + ex.Message; return; }
            var members = items.Where(i => i.Member != null).ToList();
            Takeoff.AutoMark(members.Select(m => m.Member).ToList());
            double k = Draw.UnitK(doc.Database);
            var lv = ModelData.Levels;
            string q = Filter.Text.Trim();
            Func<ModelData.Item, bool> match = it => q.Length == 0
                || (it.Member != null && (it.Member.Section + " " + it.Member.Mark + " " + it.Member.Role).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (it.Conn != null && Connection.Label(it.Conn.Type).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            Func<ModelData.Item, string> levelOf = it =>
            {
                if (it.Member != null) return it.Member.Level;
                try { using (var tr = doc.Database.TransactionManager.StartOpenCloseTransaction()) return lv.Of(((BlockReference)tr.GetObject(it.Id, OpenMode.ForRead)).Position.Z / k); }
                catch { return lv.Levels[0].name; }
            };
            Func<ModelData.Item, string> catOf = it => it.Member != null ? it.Member.Category : "Sambungan";
            int mode = GroupBox.SelectedIndex;
            var roots = new List<Node>();
            Node Child(List<Node> list, string text, int depth) { var n = list.FirstOrDefault(x => x.Text == text); if (n == null) { n = new Node { Text = text, Weight = depth == 0 ? FontWeights.SemiBold : FontWeights.Normal, IsExpanded = depth == 0 }; list.Add(n); } return n; }
            foreach (var it in items.Where(match))
            {
                string a, b;
                if (mode == 0) { a = levelOf(it); b = catOf(it); }
                else if (mode == 1) { a = catOf(it); b = levelOf(it); }
                else { a = it.Member != null ? it.Member.Section : "Sambungan"; b = it.Member != null ? it.Member.Role : Connection.Label(it.Conn.Type); }
                var na = Child(roots, a, 0); var nb = Child(na.Children, b, 1);
                string key = it.Member != null ? it.Member.Mark + "  " + it.Member.Section + "  L=" + Draw.N(it.Member.LengthMm, "0") : Connection.Label(it.Conn.Type);
                var ng = Child(nb.Children, key, 2);
                ng.IsConn = it.Conn != null; ng.First = ng.First ?? it;
                ng.Dot = it.Member != null ? IsoView.RoleBrush(it.Member.Role) : ConnDot;
                ng.Children.Add(new Node { Text = (it.Member != null ? it.Member.Mark : "#") + "  (" + it.Handle + ")", Ids = { it.Id }, IsConn = it.Conn != null, First = it });
            }
            void Sum(Node n)
            {
                foreach (var c in n.Children) Sum(c);
                if (n.Children.Count == 0) return;
                var ids = new HashSet<ObjectId>(n.AllIds());
                double kg = items.Where(i => i.Member != null && ids.Contains(i.Id)).Sum(i => i.Member.KgPerM * i.Member.LengthMm / 1000)
                          + items.Where(i => i.Conn != null && ids.Contains(i.Id)).Sum(i => ModelData.ToTakeoff(i).SteelKg);
                n.Info = "×" + ids.Count + "  ·  " + Draw.N(kg, "#,##0") + " kg";
            }
            foreach (var r in roots) Sum(r);
            Tree.ItemsSource = roots;
            Status.Text = members.Count + " batang, " + items.Count(i => i.Conn != null) + " sambungan. Klik = pilih di gambar, klik ganda = zoom.";
        }

        void OnSelect(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var n = Tree.SelectedItem as Node;
            selected = n == null ? new List<ObjectId>() : n.AllIds().ToList();
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            conn = null; props = null;
            if (doc == null || selected.Count == 0) { ShowProps(null, ""); return; }
            try { doc.Editor.SetImpliedSelection(selected.ToArray()); } catch { }
            var d = n.First != null ? n : Flatten(n).FirstOrDefault(x => x.First != null);
            if (d != null && d.IsConn && selected.Count == 1)
            {
                conn = d.First.Conn.Clone(); connId = d.First.Id; connLinks = d.First.Tag.Links;
                ShowProps(conn, "SAMBUNGAN · " + Connection.Label(conn.Type).ToUpperInvariant());
                return;
            }
            var first = d?.First?.Member != null ? d.First : null;
            if (first == null) { ShowProps(null, ""); return; }
            var spec = first.Tag.Spec;
            double totalKg = 0, totalL = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in selected)
                {
                    var t = Draw.ReadTag(tr, (Entity)tr.GetObject(id, OpenMode.ForRead));
                    if (t == null || (t.Kind != "batang3d" && t.Kind != "batang2d")) continue;
                    totalL += t.LengthMm; totalKg += t.LengthMm / 1000 * t.KgPerM;
                }
                tr.Commit();
            }
            props = new MemberProps
            {
                Marking = spec?.Mark ?? "", Level = spec?.Level ?? "", Kategori = spec?.Category ?? first.Member.Category, Profil = first.Member.Section,
                Putar = spec?.Roll ?? 0, Acuan = spec?.Anchor ?? SteelKit.Core.Anchor.Tengah, Panjang = Draw.N(totalL, "#,##0"), Berat = Draw.N(totalKg, "#,##0.0"), Jumlah = selected.Count,
            };
            ShowProps(props, "BATANG · " + selected.Count + " TERPILIH");
        }

        static IEnumerable<Node> Flatten(Node n) { foreach (var c in n.Children) { yield return c; foreach (var x in Flatten(c)) yield return x; } }

        void ShowProps(object target, string title)
        {
            Props.Target = target;
            PropsTitle.Text = target == null ? "PROPERTI" : title;
            PropsEmpty.Visibility = target == null ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnZoom(object s, MouseButtonEventArgs e)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null || selected.Count == 0) return;
            try { doc.Editor.SetImpliedSelection(selected.ToArray()); doc.SendStringToExecute("_.ZOOM _O ", true, false, false); } catch { }
        }

        void OnPropChanged(string name)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    if (conn != null)
                    {
                        var br = (BlockReference)tr.GetObject(connId, OpenMode.ForWrite);
                        ConnCommands.Rebuild(tr, doc.Database, br, conn, connLinks);
                        Status.Text = ConnDraw.Summary(conn.Build());
                    }
                    else if (props != null)
                    {
                        Profile prof = name == "Profil" ? Store.Catalog.Find(props.Profil) : null;
                        if (name == "Profil" && prof == null) { Status.Text = "Profil tidak ada di katalog."; tr.Abort(); return; }
                        foreach (var id in selected)
                        {
                            var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                            var t = Draw.ReadTag(tr, ent);
                            if (t?.Spec == null) continue;
                            var m = MemberBuild.Copy(t.Spec);
                            switch (name)
                            {
                                case "Marking": m.Mark = props.Marking ?? ""; break;
                                case "Level": m.Level = props.Level ?? ""; break;
                                case "Kategori": m.Category = props.Kategori ?? ""; break;
                                case "Profil": m.Profile = prof; break;
                                case "Putar": m.Roll = props.Putar; break;
                                case "Acuan": m.Anchor = props.Acuan; break;
                            }
                            MemberBuild.Apply(tr, ent, m);
                        }
                        Status.Text = selected.Count + " batang diperbarui (" + name + ").";
                    }
                    tr.Commit();
                }
                doc.Editor.Regen();
            }
            catch (Exception ex) { Status.Text = "Gagal: " + ex.Message; }
            if (name == "Marking" || name == "Level" || name == "Kategori" || name == "Profil") Reload();
        }
    }
}
