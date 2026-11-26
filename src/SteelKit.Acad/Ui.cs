using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;
using SteelKit.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(SteelKit.Acad.UiCommands))]
[assembly: ExtensionApplication(typeof(SteelKit.Acad.Plugin))]

namespace SteelKit.Acad
{
    public class Plugin : IExtensionApplication
    {
        public static string Version => typeof(Plugin).Assembly.GetName().Version.ToString(3);

        public static bool IsConsole =>
            Process.GetCurrentProcess().ProcessName.IndexOf("accoreconsole", StringComparison.OrdinalIgnoreCase) >= 0;

        public void Initialize()
        {
            try
            {
                var ed = AcCore.DocumentManager.MdiActiveDocument?.Editor;
                var w = Store.Catalog.Warnings;
                ed?.WriteMessage("\nSteel Kit " + Version + " dimuat: " + Store.Catalog.All.Count + " profil. Ketik SKPROFIL untuk pustaka profil, SKBANTUAN untuk daftar perintah.");
                if (w.Count > 0) ed?.WriteMessage("\nSteel Kit: " + w.Count + " baris katalog pengguna dilewati: " + string.Join(" | ", w.Take(3)));
            }
            catch { }
            try { MemberOverrules.Register(); } catch (System.Exception ex) { Debug.WriteLine(ex); }
            if (IsConsole) return;
            try { InitUi(); } catch (System.Exception ex) { Debug.WriteLine(ex); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InitUi() => RibbonUi.Init();

        public void Terminate()
        {
            try { MemberOverrules.Unregister(); } catch { }
        }
    }

    /// <summary>Semua perintah dijalankan lewat sini: kesalahan ditampilkan sebagai pesan, AutoCAD tidak crash.</summary>
    public static class Guard
    {
        public static void Run(Action a)
        {
            try { a(); }
            catch (System.Exception ex)
            {
                try
                {
                    var ed = AcCore.DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage("\nSteel Kit: perintah dibatalkan - " + ex.Message + " (" + ex.GetType().Name + ")");
                    System.Diagnostics.Debug.WriteLine(ex);
                }
                catch { }
            }
        }
    }

    public class UiCommands
    {
        [CommandMethod("STEELKIT", "SKPROFIL", CommandFlags.Modal)]
        public void ProfileDialog() => Guard.Run(ProfileDialogCore);
        void ProfileDialogCore()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            {
                var f = new Ui.ProfileWindow(Store.Current);
                if (AcApp.ShowModalWindow(f) != true || f.Result == null) return;
                Store.Current = f.Result;
                string cmd;
                switch (f.Action)
                {
                    case "penampang": cmd = "_SKPENAMPANG "; break;
                    case "batang3d": cmd = "_SKBATANG "; break;
                    case "batang2d": cmd = "_SKBATANG2D "; break;
                    case "keluarga": cmd = "_SKTABEL _Keluarga "; break;
                    default: cmd = "_SKTABEL "; break;
                }
                doc.SendStringToExecute(cmd, true, false, false);
            }
        }

        static readonly ConnType[][] Groups =
        {
            new[] { ConnType.PelatUjung, ConnType.FinPlate, ConnType.SikuGanda, ConnType.Dudukan, ConnType.MomenLas, ConnType.MomenPelatSayap },
            new[] { ConnType.SambunganPelatUjung, ConnType.SplicePelatSambung },
            new[] { ConnType.CleatGording, ConnType.FlangeBrace },
        };
        static bool Compatible(ConnType a, ConnType b) => a == b || Groups.Any(g => g.Contains(a) && g.Contains(b));

        /// <summary>
        /// SKSAMBUNG: pilih sambungan yang ada (ubah), 1-2 batang (sambungan baru otomatis di titik temunya),
        /// atau Enter untuk sambungan bebas di sebuah titik. Dialog bergambar untuk memilih dan mengatur.
        /// </summary>
        [CommandMethod("STEELKIT", "SKSAMBUNG", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void ConnectionDialog() => Guard.Run(ConnectionDialogCore);
        void ConnectionDialogCore()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument; var ed = doc.Editor; var db = doc.Database;
            var sel = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nPilih sambungan untuk diubah, ATAU 1-2 batang 3D (penopang dulu), ATAU Enter untuk sambungan di titik bebas",
            });
            if (sel.Status != PromptStatus.OK && sel.Status != PromptStatus.Error && sel.Status != PromptStatus.None) return;
            var ids = sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : new ObjectId[0];

            Connection start = null; string[] links = null; Frame frame = null; ObjectId existing = ObjectId.Null; string context = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var t = Draw.ReadTag(tr, br);
                    if (t?.Kind != "sambungan") continue;
                    start = Connection.Deserialize(t.Params); links = t.Links; existing = id;
                    context = "mengubah sambungan yang ada";
                    break;
                }
                if (start == null && ids.Length > 0)
                {
                    var ms = ConnCommands.Members(tr, db, ids);
                    var joints = JointFinder.Find(ms.Select(m => m.Geom).ToList());
                    if (ms.Count >= 2) joints = joints.Where(j => j.Supported >= 0).ToList();
                    var j0 = joints.FirstOrDefault();
                    if (j0 == null) { ed.WriteMessage("\nBatang terpilih tidak saling bertemu (atau bukan batang 3D Steel Kit). Enter tanpa pilihan untuk sambungan bebas."); tr.Commit(); return; }
                    start = j0.Conn; frame = j0.Frame;
                    links = ConnCommands.LinksOf(j0, ms);
                    context = "dari batang terpilih" + (j0.Notes.Count > 0 ? " - " + string.Join("; ", j0.Notes) : "");
                }
                tr.Commit();
            }

            {
                var f = new Ui.ConnWindow(start, start != null, context);
                if (AcApp.ShowModalWindow(f) != true || f.Result == null) return;
                var c = f.Result;
                if (start != null && (links != null && links.Any(l => !string.IsNullOrEmpty(l))) && !Compatible(start.Type, c.Type))
                {
                    ed.WriteMessage("\nJenis " + Connection.Label(c.Type) + " tidak cocok dengan posisi batang ini (" + Connection.Label(start.Type) + "). Tidak ada yang diubah.");
                    return;
                }
                ObjectId placed = existing;
                if (f.Action != "detail")
                {
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        if (!existing.IsNull)
                            ConnCommands.Rebuild(tr, db, (BlockReference)tr.GetObject(existing, OpenMode.ForWrite), c, links);
                        else
                        {
                            if (frame == null)
                            {
                                var pr = ed.GetPoint("\nTitik sambungan");
                                if (pr.Status != PromptStatus.OK) return;
                                double k = Draw.UnitK(db);
                                var cs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
                                frame = new Frame(Draw.V(pr.Value.TransformBy(ed.CurrentUserCoordinateSystem)) * (1 / k), Draw.V(cs.Xaxis), Draw.V(cs.Yaxis), Draw.V(cs.Zaxis));
                                links = null;
                            }
                            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                            placed = ConnCommands.Place(tr, db, space, c, frame, links, true, out ConnResult _);
                        }
                        tr.Commit();
                    }
                    ed.WriteMessage("\n" + c.Title + ": " + ConnDraw.Summary(c.Build()));
                }
                if (f.Action == "detail" || f.Action == "keduanya")
                {
                    var dp = ed.GetPoint("\nTitik kiri bawah gambar detail");
                    if (dp.Status != PromptStatus.OK) return;
                    using (doc.LockDocument())
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        ConnDraw.Detail(tr, db, space, c, dp.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                        tr.Commit();
                    }
                }
            }
        }

        [CommandMethod("STEELKIT", "SKKATALOG", CommandFlags.Modal)]
        public void OpenCatalogFolder() => Guard.Run(OpenCatalogFolderCore);
        void OpenCatalogFolderCore()
        {
            var ed = AcCore.DocumentManager.MdiActiveDocument.Editor;
            Directory.CreateDirectory(Store.Folder);
            if (!File.Exists(Store.UserCatalogPath)) Store.SaveCatalog();
            ed.WriteMessage("\nKatalog pengguna: " + Store.UserCatalogPath +
                            "\nBisa diedit di Excel (kolom: family,name,H,B,tw,tf,r,t,C,D,note). Setelah disimpan, ketik SKMUATULANG.");
            try { Process.Start("explorer.exe", "/select,\"" + Store.UserCatalogPath + "\""); } catch { }
        }

        [CommandMethod("STEELKIT", "SKMUATULANG", CommandFlags.Modal)]
        public void Reload() => Guard.Run(ReloadCore);
        void ReloadCore()
        {
            var ed = AcCore.DocumentManager.MdiActiveDocument.Editor;
            Store.ReloadCatalog();
            ed.WriteMessage("\nKatalog dimuat ulang: " + Store.Catalog.All.Count + " profil.");
            foreach (var w in Store.Catalog.Warnings) ed.WriteMessage("\n  Dilewati: " + w);
        }

        [CommandMethod("STEELKIT", "SKBANTUAN", CommandFlags.Modal)]
        public void Help() => Guard.Run(HelpCore);
        void HelpCore()
        {
            var ed = AcCore.DocumentManager.MdiActiveDocument.Editor;
            ed.WriteMessage("\n=== Steel Kit " + Plugin.Version + " - perintah ===" +
                "\n  SKPROFIL     Pustaka profil: pilih/ubah/tambah ukuran, lihat properti, lalu gambar" +
                "\n  SKPENAMPANG  Sisipkan penampang 2D (blok + arsir + dimensi + label)" +
                "\n  SKBATANG     Batang 3D solid dari titik ke titik (berantai). Opsi Profil/Putar/Acuan" +
                "\n  SKBATANG2D   Tampak batang 2D samping/atas dengan garis tersembunyi dan as" +
                "\n  SKLABEL      Label nama profil + panjang pada batang" +
                "\n  SKTABEL      Tabel properti satu profil atau satu jenis profil" +
                "\n  SKUBAH       Ubah batang: panjang, ujung, putar, profil, acuan, balik (atau tarik grip ujung)" +
                "\n  SKPUTAR      Putar profil batang terhadap sumbunya" +
                "\n  SKSAMBUNG    Dialog sambungan bergambar: pilih 1-2 batang / sambungan yang ada / titik bebas" +
                "\n  SKSAMBUNGAUTO  Sambungan otomatis untuk semua titik temu batang (pelat dasar, lutut, puncak, balok, gording)" +
                "\n  SKSAMBUNGTITIK Sambungan rekomendasi di titik (baris perintah)" +
                "\n  SKSAMBUNGPARAM Ubah satu parameter sambungan dari baris perintah" +
                "\n  SKDETAIL     Gambar detail 2D sambungan: tampak, dimensi, catatan, daftar bahan" +
                "\n  SKBERAT      Daftar berat batang + bahan sambungan (tabel + CSV)" +
                "\n  SKASISTEN    Asisten: ketik/ucapkan (Win+H) maksud Anda, periksa usulan batang, pasang (+ sambung otomatis)" +
                "\n  SKBROWSER    Palet model: level, kategori, marking, edit properti batang & sambungan" +
                "\n  SKFABRIKASI  Gambar fabrikasi per marking: tampak, lubang, dimensi dari ujung, daftar lubang" +
                "\n  SKBENTROK    Cek tabrakan 3D batang & sambungan (irisan di layer SK-BENTROK)" +
                "\n  SKLAYOUT     Layout otomatis: isometrik, denah, tampak + lembar detail sambungan (dimensi & leader)" +
                "\n  SKEXCEL      Quantity takeoff ke Excel (Inggris, metrik): batang, pelat, baut, angkur, las, cat, grout" +
                "\n  SKLEVEL      Tabel level untuk pengelompokan (mis. Base:0;Lt.2:4000;Atap:8000)" +
                "\n  SKINFO       Properti profil aktif atau batang terpilih" +
                "\n  SKKATALOG    Buka katalog pengguna (CSV, bisa diedit di Excel)" +
                "\n  SKMUATULANG  Muat ulang katalog setelah diedit" +
                "\n  SKRIBBON     Tampilkan ulang tab ribbon Steel Kit" +
                "\nProperti dihitung dari dimensi. Ukuran tipikal pasar: cocokkan dengan katalog pemasok." +
                "\nPlugin ini menyiapkan geometri dan data, bukan desain struktur.");
        }

        [CommandMethod("STEELKIT", "SKRIBBON", CommandFlags.Modal)]
        public void Ribbon() => Guard.Run(RibbonCore);
        void RibbonCore()
        {
            RibbonUi.Build(true);
        }
    }

    public static class RibbonUi
    {
        const string TabId = "STEELKIT_TAB";
        static bool hooked;

        public static void Init()
        {
            if (ComponentManager.Ribbon == null) ComponentManager.ItemInitialized += OnItemInitialized;
            else Build(false);
            if (!hooked)
            {
                hooked = true;
                AcApp.SystemVariableChanged += (s, e) =>
                {
                    if (string.Equals(e.Name, "WSCURRENT", StringComparison.OrdinalIgnoreCase)) Build(false);
                };
            }
        }

        static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Build(false);
        }

        public static void Build(bool activate)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon == null) return;
            var old = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
            if (old != null) ribbon.Tabs.Remove(old);
            var tab = new RibbonTab { Title = "Steel Kit", Id = TabId };
            ribbon.Tabs.Add(tab);
            tab.Panels.Add(Panel("Mulai",
                Btn("Asisten", "_SKASISTEN ", "asisten", true, "Ketik atau ucapkan (Win+H) maksud Anda, mis. portal bentang 20 m tinggi 6 m 5 bay. Periksa usulan, lalu pasang."),
                Btn("Browser", "_SKBROWSER ", "browser", true, "Palet model di kiri: level, kategori, marking, properti batang dan sambungan.")));
            tab.Panels.Add(Panel("Pustaka",
                Btn("Pustaka\nProfil", "_SKPROFIL ", "profil", true, "Pilih, ubah, atau tambah ukuran profil baja; lihat properti dan pemakaian umum.")));
            tab.Panels.Add(Panel("Gambar",
                Btn("Penampang", "_SKPENAMPANG ", "penampang", true, "Sisipkan penampang 2D dengan arsir, dimensi, dan label."),
                Btn("Batang\n3D", "_SKBATANG ", "batang3d", true, "Gambar batang 3D solid dari titik ke titik."),
                Btn("Batang\n2D", "_SKBATANG2D ", "batang2d", true, "Gambar tampak batang 2D (samping / atas).")));
            tab.Panels.Add(Panel("Ubah",
                Btn("Ubah\nBatang", "_SKUBAH ", "ubah", true, "Ubah panjang, ujung, putar, profil, atau acuan batang. Grip ujung batang juga bisa ditarik."),
                Btn("Putar", "_SKPUTAR ", "putar", false, "Putar profil batang terhadap sumbunya.")));
            tab.Panels.Add(Panel("Sambungan",
                Btn("Sambungan", "_SKSAMBUNG ", "sambungan", true, "Pilih jenis sambungan bergambar, atur pelat, baut, angkur, las."),
                Btn("Sambung\nOtomatis", "_SKSAMBUNGAUTO ", "auto", true, "Buat semua sambungan rekomendasi di titik temu batang."),
                Btn("Detail\n2D", "_SKDETAIL ", "detail", true, "Gambar detail sambungan: tampak, dimensi, catatan, daftar bahan."),
                Btn("Cek\nTabrakan", "_SKBENTROK ", "bentrok", true, "Cek tabrakan 3D batang dan benda sambungan; irisannya digambar di layer SK-BENTROK.")));
            tab.Panels.Add(Panel("Gambar Kerja",
                Btn("Layout\nOtomatis", "_SKLAYOUT ", "layout", true, "Lembar struktur (isometrik, denah, tampak) + lembar detail sambungan dengan dimensi dan leader."),
                Btn("Gambar\nFabrikasi", "_SKFABRIKASI ", "fabrikasi", true, "Satu lembar per marking: tampak batang dengan lubang & potongan, dimensi dari ujung, daftar lubang, data marking."),
                Btn("Excel\nQTO", "_SKEXCEL ", "excel", true, "Quantity takeoff ke Excel (bahasa Inggris, metrik): batang, pelat, baut, angkur, las, cat, grout."),
                Btn("Level", "_SKLEVEL ", "level", false, "Atur tabel level (nama:elevasi) untuk pengelompokan.")));
            tab.Panels.Add(Panel("Data",
                Btn("Label", "_SKLABEL ", "label", false, "Beri label nama profil dan panjang."),
                Btn("Tabel", "_SKTABEL ", "tabel", false, "Tabel properti profil."),
                Btn("Info", "_SKINFO ", "info", false, "Properti profil di baris perintah."),
                Btn("Daftar\nBerat", "_SKBERAT ", "berat", true, "Daftar berat baja dari batang di gambar (tabel + CSV).")));
            tab.Panels.Add(Panel("Katalog",
                Btn("Katalog\nSaya", "_SKKATALOG ", "katalog", false, "Buka katalog pengguna (CSV)."),
                Btn("Bantuan", "_SKBANTUAN ", "bantuan", false, "Daftar perintah Steel Kit.")));
            if (activate) tab.IsActive = true;
        }

        static RibbonPanel Panel(string title, params RibbonItem[] items)
        {
            var src = new RibbonPanelSource { Title = title };
            RibbonRowPanel stack = null;
            foreach (var it in items)
            {
                if (it is RibbonButton b && b.Size == RibbonItemSize.Standard)
                {
                    if (stack == null) { stack = new RibbonRowPanel(); src.Items.Add(stack); }
                    else stack.Items.Add(new RibbonRowBreak());
                    stack.Items.Add(it);
                    if (stack.Items.Count >= 5) stack = null;
                }
                else { stack = null; src.Items.Add(it); }
            }
            return new RibbonPanel { Source = src };
        }

        static RibbonButton Btn(string text, string cmd, string icon, bool large, string tip)
        {
            return new RibbonButton
            {
                Text = large ? text : text.Replace("\n", " "),
                ShowText = true,
                ShowImage = true,
                Image = Icons.Make(icon, 16),
                LargeImage = Icons.Make(icon, 32),
                Size = large ? RibbonItemSize.Large : RibbonItemSize.Standard,
                Orientation = large ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal,
                CommandParameter = cmd,
                CommandHandler = CmdHandler.Instance,
                ToolTip = tip,
            };
        }

        class CmdHandler : System.Windows.Input.ICommand
        {
            public static readonly CmdHandler Instance = new CmdHandler();
            public event EventHandler CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter)
            {
                var cmd = (parameter as RibbonButton)?.CommandParameter as string;
                var doc = AcApp.DocumentManager.MdiActiveDocument;
                if (cmd != null && doc != null) doc.SendStringToExecute("\x03\x03" + cmd, true, false, true);
            }
        }
    }

    /// <summary>Ikon ribbon digambar dengan kode (tanpa file gambar).</summary>
    static class Icons
    {
        static readonly Brush Steel = Frozen(new SolidColorBrush(Color.FromRgb(58, 120, 168)));
        static readonly Brush SteelLight = Frozen(new SolidColorBrush(Color.FromRgb(120, 175, 214)));
        static readonly Brush Accent = Frozen(new SolidColorBrush(Color.FromRgb(230, 140, 40)));
        static readonly Pen Edge = FrozenPen(new Pen(new SolidColorBrush(Color.FromRgb(30, 60, 90)), 1.2));

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static Pen FrozenPen(Pen p) { p.Freeze(); return p; }

        public static BitmapSource Make(string kind, int size)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 32.0, size / 32.0));
                switch (kind)
                {
                    case "profil": case "penampang": IShape(dc, kind == "penampang"); break;
                    case "batang3d": Beam3D(dc); break;
                    case "batang2d": Beam2D(dc); break;
                    case "label": Tag(dc); break;
                    case "tabel": Grid(dc); break;
                    case "berat": Weight(dc); break;
                    case "info": Circle(dc, "i"); break;
                    case "katalog": Grid(dc); dc.DrawRectangle(Accent, null, new System.Windows.Rect(20, 20, 10, 10)); break;
                    case "ubah": Beam2D(dc); dc.DrawEllipse(Accent, Edge, new System.Windows.Point(29, 16), 3, 3); dc.DrawEllipse(Accent, Edge, new System.Windows.Point(3, 16), 3, 3); break;
                    case "putar": IShape(dc, false); dc.DrawEllipse(null, new Pen(Accent, 2), new System.Windows.Point(16, 16), 13, 13); break;
                    case "sambungan": Joint(dc, false); break;
                    case "auto": Joint(dc, true); break;
                    case "detail": Grid(dc); Joint(dc, false); break;
                    case "asisten": Logo(dc); break;
                    case "browser": dc.DrawRectangle(Brushes.White, Edge, new System.Windows.Rect(3, 3, 26, 26)); dc.DrawRectangle(Steel, null, new System.Windows.Rect(3, 3, 9, 26)); foreach (var yy in new[] { 8.0, 14, 20 }) dc.DrawRectangle(SteelLight, null, new System.Windows.Rect(15, yy, 11, 3)); break;
                    case "layout": dc.DrawRectangle(Brushes.White, Edge, new System.Windows.Rect(2, 5, 28, 22)); dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(5, 8, 10, 8)); dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(17, 8, 10, 8)); dc.DrawRectangle(Steel, null, new System.Windows.Rect(19, 20, 9, 5)); break;
                    case "excel": dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(33, 115, 70)), Edge, new System.Windows.Rect(4, 3, 24, 26)); Text(dc, "X", 11, 6, 16); break;
                    case "level": foreach (var yy in new[] { 8.0, 16, 24 }) dc.DrawLine(new Pen(Steel, 2), new System.Windows.Point(4, yy), new System.Windows.Point(28, yy)); dc.DrawGeometry(Accent, null, Poly(4, 4, 10, 4, 7, 8)); break;
                    case "fabrikasi": dc.DrawRectangle(Brushes.White, Edge, new System.Windows.Rect(2, 5, 28, 22)); dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(5, 10, 22, 5)); foreach (var xx in new[] { 9.0, 15, 21 }) dc.DrawEllipse(Brushes.White, Edge, new System.Windows.Point(xx, 12.5), 1.3, 1.3); dc.DrawLine(new Pen(Steel, 1), new System.Windows.Point(5, 20), new System.Windows.Point(27, 20)); break;
                    case "bentrok": dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(3, 10, 16, 16)); dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(13, 4, 16, 16)); dc.DrawRectangle(Accent, null, new System.Windows.Rect(13.5, 10.5, 5, 9)); break;
                    default: Circle(dc, "?"); break;
                }
                dc.Pop();
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        static Geometry Poly(params double[] xy)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new System.Windows.Point(xy[0], xy[1]), true, true);
                for (int i = 2; i < xy.Length; i += 2) c.LineTo(new System.Windows.Point(xy[i], xy[i + 1]), true, false);
            }
            g.Freeze();
            return g;
        }

        /// <summary>Logo Steel Kit (segi enam baut + WF), sama dengan Ui/Theme.xaml dan tools/gen_logo.py.</summary>
        static void Logo(DrawingContext dc)
        {
            dc.PushTransform(new ScaleTransform(0.32, 0.32));
            var grad = new LinearGradientBrush(Color.FromRgb(0x38, 0xBD, 0xF8), Color.FromRgb(0x63, 0x66, 0xF1), new System.Windows.Point(0, 0), new System.Windows.Point(1, 1));
            dc.DrawGeometry(grad, new Pen(new SolidColorBrush(Color.FromArgb(0x55, 0x0F, 0x17, 0x2A)), 1.6), Poly(100, 50, 75, 90.7, 25, 90.7, 0, 50, 25, 9.3, 75, 9.3));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x3A, 255, 255, 255)), null, Poly(100, 50, 0, 50, 25, 9.3, 75, 9.3));
            dc.DrawGeometry(Brushes.White, null, Poly(28, 25, 72, 25, 72, 34, 54.5, 34, 54.5, 66, 72, 66, 72, 75, 28, 75, 28, 66, 45.5, 66, 45.5, 34, 28, 34));
            dc.Pop();
        }

        static void IShape(DrawingContext dc, bool hatch)
        {
            var g = Poly(5, 3, 27, 3, 27, 8, 18.5, 8, 18.5, 24, 27, 24, 27, 29, 5, 29, 5, 24, 13.5, 24, 13.5, 8, 5, 8);
            dc.DrawGeometry(hatch ? SteelLight : Steel, Edge, g);
            if (hatch)
            {
                dc.PushClip(g);
                var p = new Pen(Steel, 1.2);
                for (int i = -32; i < 32; i += 4) dc.DrawLine(p, new System.Windows.Point(i, 32), new System.Windows.Point(i + 32, 0));
                dc.Pop();
            }
        }

        static void Joint(DrawingContext dc, bool spark)
        {
            dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(3, 3, 7, 26));          // kolom
            dc.DrawRectangle(Steel, Edge, new System.Windows.Rect(10, 6, 4, 18));              // pelat ujung
            dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(14, 9, 16, 12));        // balok
            foreach (var y in new[] { 9.0, 21.0 }) dc.DrawEllipse(Accent, null, new System.Windows.Point(12, y), 1.6, 1.6);
            if (spark) dc.DrawGeometry(Accent, null, Poly(24, 1, 26, 6, 31, 7, 27, 10, 28, 15, 24, 12, 20, 15, 21, 10, 17, 7, 22, 6));
        }

        static void Beam3D(DrawingContext dc)
        {
            dc.DrawGeometry(SteelLight, Edge, Poly(3, 12, 21, 4, 29, 8, 11, 16));
            dc.DrawGeometry(Steel, Edge, Poly(3, 12, 11, 16, 11, 28, 3, 24));
            dc.DrawGeometry(Steel, Edge, Poly(11, 16, 29, 8, 29, 20, 11, 28));
        }

        static void Beam2D(DrawingContext dc)
        {
            dc.DrawRectangle(SteelLight, Edge, new System.Windows.Rect(2, 9, 28, 14));
            var hp = new Pen(Steel, 1.2) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
            dc.DrawLine(hp, new System.Windows.Point(2, 12), new System.Windows.Point(30, 12));
            dc.DrawLine(hp, new System.Windows.Point(2, 20), new System.Windows.Point(30, 20));
            dc.DrawLine(new Pen(Accent, 1), new System.Windows.Point(0, 16), new System.Windows.Point(32, 16));
        }

        static void Tag(DrawingContext dc)
        {
            dc.DrawGeometry(Accent, Edge, Poly(3, 9, 22, 9, 29, 16, 22, 23, 3, 23));
            dc.DrawEllipse(Brushes.White, null, new System.Windows.Point(22, 16), 2, 2);
        }

        static void Grid(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, Edge, new System.Windows.Rect(3, 4, 26, 24));
            dc.DrawRectangle(Steel, null, new System.Windows.Rect(3, 4, 26, 6));
            var p = new Pen(Steel, 1);
            for (int y = 16; y < 28; y += 6) dc.DrawLine(p, new System.Windows.Point(3, y), new System.Windows.Point(29, y));
            dc.DrawLine(p, new System.Windows.Point(14, 10), new System.Windows.Point(14, 28));
        }

        static void Weight(DrawingContext dc)
        {
            dc.DrawGeometry(Steel, Edge, Poly(6, 28, 26, 28, 23, 11, 9, 11));
            dc.DrawEllipse(null, new Pen(Steel, 2.2), new System.Windows.Point(16, 7.5), 3.5, 3.5);
            Text(dc, "kg", 10.5, 15, 9);
        }

        static void Circle(DrawingContext dc, string t)
        {
            dc.DrawEllipse(Steel, Edge, new System.Windows.Point(16, 16), 13, 13);
            Text(dc, t, t == "i" ? 13.5 : 11, 6, 18);
        }

        static void Text(DrawingContext dc, string s, double x, double y, double size)
        {
#pragma warning disable CS0618 // konstruktor tanpa DPI cukup untuk ikon
            var ft = new FormattedText(s, System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), size, Brushes.White);
#pragma warning restore CS0618
            dc.DrawText(ft, new System.Windows.Point(x, y));
        }
    }
}
