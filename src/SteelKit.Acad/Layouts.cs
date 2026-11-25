using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelKit.Core;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(SteelKit.Acad.LayoutCommands))]

namespace SteelKit.Acad
{
    /// <summary>
    /// Layout otomatis: lembar struktur (isometrik, denah, tampak depan, tampak samping) dan lembar detail sambungan,
    /// dengan skala standar, viewport terkunci, dimensi grid, leader label profil, dan kotak judul.
    /// </summary>
    public class LayoutCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        public const string LayVp = "SK-VIEWPORT";
        public const string LayTitle = "SK-JUDUL";
        static readonly double[] Scales = { 1, 2, 5, 10, 15, 20, 25, 50, 75, 100, 125, 150, 200, 250, 300, 400, 500, 750, 1000, 1500, 2000 };

        class Sheet { public double W, H; public string Media; public string Name; }
        static Sheet A3 => new Sheet { W = 420, H = 297, Media = "ISO_full_bleed_A3_(420.00_x_297.00_MM)", Name = "A3" };
        static Sheet A1 => new Sheet { W = 841, H = 594, Media = "ISO_full_bleed_A1_(841.00_x_594.00_MM)", Name = "A1" };

        [CommandMethod("STEELKIT", "SKLAYOUT", CommandFlags.Modal | CommandFlags.NoPaperSpace)]
        public void MakeLayouts() => Guard.Run(MakeLayoutsCore);
        void MakeLayoutsCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var pko = new PromptKeywordOptions("\nUkuran kertas [A3/A1]") { AllowNone = true };
            pko.Keywords.Add("A3"); pko.Keywords.Add("A1"); pko.Keywords.Default = "A3";
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
            var sheet = kr.StringResult == "A1" ? A1 : A3;
            var ps = ed.GetString(new PromptStringOptions("\nJudul gambar <STRUKTUR BAJA>") { AllowSpaces = true });
            if (ps.Status != PromptStatus.OK && ps.Status != PromptStatus.None) return;
            string title = string.IsNullOrWhiteSpace(ps.StringResult) ? "STRUKTUR BAJA" : ps.StringResult.Trim().ToUpperInvariant();

            double k = Draw.UnitK(db);
            Extents3d ext; var members = new List<(Draw.TagData tag, Entity e)>(); var conns = new List<Connection>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                ext = new Extents3d(); bool any = false;
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var t = e == null ? null : Draw.ReadTag(tr, e);
                    if (t == null) continue;
                    if (t.Kind == "batang3d" || t.Kind == "sambungan")
                    {
                        try { ext.AddExtents(e.GeometricExtents); any = true; } catch { }
                        if (t.Kind == "batang3d" && t.Spec != null) members.Add((t, e));
                        if (t.Kind == "sambungan") { var c = Connection.Deserialize(t.Params); if (c != null && !conns.Any(x => DetailKey(x) == DetailKey(c))) conns.Add(c); }
                    }
                }
                tr.Commit();
                if (!any) { ed.WriteMessage("\nBelum ada batang 3D / sambungan Steel Kit di model."); return; }
            }

            using (doc.LockDocument())
            {
                string n1 = UniqueLayout(db, "SK-01 " + Short(title));
                var layId = NewLayout(db, n1, sheet);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Layers(tr, db);
                    var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                    var ps1 = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    Border(tr, ps1, sheet, title, "STRUKTUR - ISOMETRIK, DENAH, TAMPAK", "01");
                    // tata letak 2 x 2 di dalam bingkai (kanan bawah untuk kotak judul)
                    double m = 15, tb = 45, gw = (sheet.W - 2 * m) / 2, gh = (sheet.H - 2 * m - tb) / 2;
                    var cells = new[]
                    {
                        (title: "ISOMETRIK", dir: new Vector3d(1, -1, 1), cx: m + gw / 2, cy: m + tb + gh * 1.5, iso: true),
                        (title: "DENAH", dir: Vector3d.ZAxis, cx: m + gw * 1.5, cy: m + tb + gh * 1.5, iso: false),
                        (title: "TAMPAK DEPAN", dir: -Vector3d.YAxis, cx: m + gw / 2, cy: m + tb + gh / 2, iso: false),
                        (title: "TAMPAK SAMPING", dir: Vector3d.XAxis, cx: m + gw * 1.5, cy: m + tb + gh / 2, iso: false),
                    };
                    foreach (var c in cells)
                    {
                        var vp = AddViewport(tr, db, ps1, new Point3d(c.cx, c.cy + 5, 0), gw - 12, gh - 22, ext, c.dir, k, out double scale, c.iso);
                        Label(tr, ps1, new Point3d(c.cx, c.cy - gh / 2 + 9, 0), c.title, scale, 4);
                        if (!c.iso) Annotate(tr, db, ps1, vp, members, c.dir, k, scale);
                    }
                    tr.Commit();
                }
                int sheets = 0;
                if (conns.Count > 0) sheets = DetailSheets(db, sheet, title, conns, ext, k);
                LayoutManager.Current.CurrentLayout = n1;
                ed.WriteMessage("\nLayout dibuat: " + n1 + (sheets > 0 ? " + " + sheets + " lembar detail sambungan (" + conns.Count + " detail unik)" : "") +
                    ". Viewport terkunci; skala dipilih otomatis dari skala standar.");
            }
        }

        // ------------------------------------------------------------------ lembar detail sambungan
        static double[] detailScales = { 2.0, 5, 10, 15, 20, 25, 50, 75, 100 };

        static int DetailSheets(Database db, Sheet sheet, string title, List<Connection> conns, Extents3d ext, double k)
        {
            // gambar detail di model space (di kanan model), skala dipilih dulu supaya teks tercetak ±2,5 mm
            int perSheet = sheet.Name == "A1" ? 6 : 1, count = 0;
            int colsAll = sheet.Name == "A1" ? 3 : 1, rowsAll = sheet.Name == "A1" ? 2 : 1;
            double cellW = (sheet.W - 30) / colsAll - 10, cellH = (sheet.H - 30 - 45) / rowsAll - 18;
            detailScales = new[] { 2.0, 5, 10, 15, 20, 25, 50, 75, 100 };
            var bounds = new List<(Connection c, double[] b, double s)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                double x = ext.MaxPoint.X + 10000 * k, y = ext.MinPoint.Y;
                foreach (var c in conns)
                {
                    var r = c.Build();
                    double vw = 0, vh = 0;
                    foreach (var v in r.Views)
                    {
                        var sh = Projection.Project(r, v);
                        var pts = sh.SelectMany(q => q.IsCircle ? new[] { q.Center + new Vec2(-q.Radius, -q.Radius), q.Center + new Vec2(q.Radius, q.Radius) } : q.Pts.ToArray()).ToList();
                        if (pts.Count == 0) continue;
                        vw += pts.Max(p => p.X) - pts.Min(p => p.X);
                        vh = Math.Max(vh, pts.Max(p => p.Y) - pts.Min(p => p.Y));
                    }
                    // kertas: jarak antar tampak + tabel bahan ±120 mm, judul ±25 mm
                    double s0 = detailScales.FirstOrDefault(sc => vw / sc + 2 * 30 + 140 <= cellW && vh / sc + 45 <= cellH);
                    if (s0 <= 0) s0 = detailScales.Last();
                    // gambar sebagai blok; bila hasilnya tidak muat di sel pada skala ini, gambar ulang dengan skala berikutnya
                    double[] b = null; double s = s0; ObjectId blockId = ObjectId.Null;
                    var tryScales = detailScales.Where(v => v >= s0).ToList();
                    foreach (var sc in tryScales)
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        var btr = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
                        blockId = bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                        ConnDraw.Detail(tr, db, btr, c, Point3d.Origin, 2.5 * sc * k);
                        b = ConnDraw.LastBounds; s = sc;
                        if ((b[2] - b[0]) / k / sc <= cellW * 0.97 && (b[3] - b[1]) / k / sc <= cellH * 0.97 || sc == tryScales.Last()) break;
                        foreach (ObjectId eid in btr) tr.GetObject(eid, OpenMode.ForWrite).Erase();
                        btr.Erase();
                    }
                    var br = new BlockReference(new Point3d(x - b[0], y - b[1], 0), blockId) { Layer = "0" };
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                    var bw = new[] { x, y, x + (b[2] - b[0]), y + (b[3] - b[1]) };
                    bounds.Add((c, bw, s));
                    x = bw[2] + Math.Max(5000, (bw[2] - bw[0])) * k;
                }
                tr.Commit();
            }
            for (int s0 = 0; s0 < bounds.Count; s0 += perSheet)
            {
                count++;
                string name = UniqueLayout(db, "SK-" + (count + 1).ToString("00", CultureInfo.InvariantCulture) + " Detail Sambungan");
                var layId = NewLayout(db, name, sheet);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Layers(tr, db);
                    var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                    var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    Border(tr, ps, sheet, title, "DETAIL SAMBUNGAN", (count + 1).ToString("00"));
                    var part = bounds.Skip(s0).Take(perSheet).ToList();
                    int cols = sheet.Name == "A1" ? 3 : 1, rows = (int)Math.Ceiling(part.Count / (double)cols);
                    if (sheet.Name != "A1") { cols = 1; rows = part.Count; }
                    double m = 15, tb = 45, gw = (sheet.W - 2 * m) / cols, gh = (sheet.H - 2 * m - tb) / Math.Max(rows, 1);
                    for (int i = 0; i < part.Count; i++)
                    {
                        int r = i / cols, cIdx = i % cols;
                        double cx = m + gw * (cIdx + 0.5), cy = sheet.H - m - gh * (r + 0.5);
                        var b = part[i].b;
                        var e = new Extents3d(new Point3d(b[0], b[1], 0), new Point3d(b[2], b[3], 0));
                        double sc0 = part[i].s;
                        double vwP = Math.Min(gw - 10, (b[2] - b[0]) / k / sc0 * 1.06 + 4), vhP = Math.Min(gh - 18, (b[3] - b[1]) / k / sc0 * 1.06 + 4);
                        AddViewport(tr, db, ps, new Point3d(cx, cy + 4, 0), vwP, vhP, e, Vector3d.ZAxis, k, out double scale, false, detailScales.Where(v => v >= sc0).ToArray(), 1.0 / 1.06);
                        Label(tr, ps, new Point3d(cx, cy - gh / 2 + 8, 0), "DETAIL " + (s0 + i + 1) + " - " + part[i].c.Title.ToUpperInvariant(), scale, 3.5);
                    }
                    tr.Commit();
                }
            }
            return count;
        }

        /// <summary>Kunci detail: sambungan yang hanya beda sudut/posisi kecil memakai satu detail yang sama.</summary>
        public static string DetailKey(Connection c)
            => System.Text.RegularExpressions.Regex.Replace(c.Serialize(), @"(Sudut\w*|Heel\w*|Arah\w*|Geser\w*|Selisih\w*|Elevasi\w*|PanjangRafter|Batang)=[^;]*;?", "");

        // ------------------------------------------------------------------ pembantu
        static string Short(string t) => t.Length > 24 ? t.Substring(0, 24) : t;

        static string UniqueLayout(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                string n = name; int i = 2;
                while (dict.Contains(n)) n = name + " (" + i++ + ")";
                tr.Commit();
                return n;
            }
        }

        static ObjectId NewLayout(Database db, string name, Sheet sheet)
        {
            var lm = LayoutManager.Current;
            var id = lm.CreateLayout(name);
            try { lm.CurrentLayout = name; } catch { }
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = (Layout)tr.GetObject(id, OpenMode.ForWrite);
                try
                {
                    var psv = PlotSettingsValidator.Current;
                    using (var pset = new PlotSettings(lay.ModelType))
                    {
                        pset.CopyFrom(lay);
                        psv.SetPlotConfigurationName(pset, "DWG To PDF.pc3", sheet.Media);
                        psv.SetPlotPaperUnits(pset, PlotPaperUnit.Millimeters);
                        psv.SetPlotRotation(pset, PlotRotation.Degrees000);
                        psv.SetPlotType(pset, Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);
                        psv.SetUseStandardScale(pset, true);
                        psv.SetStdScaleType(pset, StdScaleType.StdScale1To1);
                        psv.SetPlotCentered(pset, false);
                        lay.CopyFrom(pset);
                    }
                }
                catch { /* alat plot tidak tersedia: layout tetap dibuat dengan ukuran kertas bawaan */ }
                // hapus viewport bawaan (selain viewport kertas pertama)
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                bool first = true;
                foreach (ObjectId eid in btr)
                {
                    if (!(tr.GetObject(eid, OpenMode.ForRead) is Viewport v)) continue;
                    if (first && v.Number == 1) { first = false; continue; }
                    v.UpgradeOpen(); v.Erase();
                }
                tr.Commit();
            }
            return id;
        }

        static void Layers(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var (n, c, plot) in new[] { (LayVp, (short)8, false), (LayTitle, (short)7, true) })
            {
                if (lt.Has(n)) continue;
                lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = n, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, c), IsPlottable = plot };
                lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
            }
            Draw.Layer(tr, db, Draw.LayDim); Draw.Layer(tr, db, Draw.LayText);
        }

        static void Border(Transaction tr, BlockTableRecord ps, Sheet sh, string title, string sub, string no)
        {
            void Add(Entity e) { e.Layer = LayTitle; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            Add(Rect(10, 10, sh.W - 10, sh.H - 10));
            double tbw = 180, tbh = 35, x0 = sh.W - 10 - tbw, y0 = 10;
            Add(Rect(x0, y0, sh.W - 10, y0 + tbh));
            Add(new Line(new Point3d(x0, y0 + 20, 0), new Point3d(sh.W - 10, y0 + 20, 0)));
            Add(new Line(new Point3d(x0 + 120, y0, 0), new Point3d(x0 + 120, y0 + 20, 0)));
            Add(new MText { Contents = title, Location = new Point3d(x0 + 4, y0 + tbh - 3, 0), TextHeight = 5, Attachment = AttachmentPoint.TopLeft });
            Add(new MText { Contents = sub, Location = new Point3d(x0 + 4, y0 + 17, 0), TextHeight = 3, Attachment = AttachmentPoint.TopLeft });
            Add(new MText { Contents = "Skala: seperti tertera\\PTanggal: " + DateTime.Now.ToString("dd-MM-yyyy") + "\\PDibuat dengan Steel Kit", Location = new Point3d(x0 + 4, y0 + 11, 0), TextHeight = 2.2, Attachment = AttachmentPoint.TopLeft });
            Add(new MText { Contents = "LEMBAR\\P{\\H2x;" + no + "}", Location = new Point3d(x0 + 150, y0 + 10, 0), TextHeight = 3, Attachment = AttachmentPoint.MiddleCenter });
            Add(new MText { Contents = "Geometri & data dari Steel Kit. Kapasitas batang dan sambungan dihitung dan disahkan engineer struktur.", Location = new Point3d(15, 14, 0), TextHeight = 2, Attachment = AttachmentPoint.BottomLeft });
        }

        static Polyline Rect(double x0, double y0, double x1, double y1)
        {
            var p = new Polyline();
            p.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); p.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            p.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); p.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            p.Closed = true;
            return p;
        }

        static void Label(Transaction tr, BlockTableRecord ps, Point3d at, string text, double scale, double h)
        {
            var mt = new MText { Contents = "{\\L" + text + "}\\P{\\H0.7x;SKALA 1:" + scale.ToString("0.##", CultureInfo.InvariantCulture) + "}", Location = at, TextHeight = h, Attachment = AttachmentPoint.TopCenter, Layer = LayTitle };
            ps.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
        }

        /// <summary>Sumbu DCS viewport (kanan, atas) untuk arah pandang tertentu.</summary>
        public static void Dcs(Vector3d dir, out Vector3d x, out Vector3d y)
        {
            var d = dir.GetNormal();
            x = Vector3d.ZAxis.CrossProduct(d);
            x = x.Length < 1e-9 ? Vector3d.XAxis : x.GetNormal();
            y = d.CrossProduct(x).GetNormal();
        }

        static Viewport AddViewport(Transaction tr, Database db, BlockTableRecord ps, Point3d center, double w, double h, Extents3d ext, Vector3d dir, double k, out double scale, bool hidden, double[] scales = null, double fill = 0.88)
        {
            Dcs(dir, out Vector3d dx, out Vector3d dy);
            var mn = ext.MinPoint; var mx = ext.MaxPoint;
            var corners = new List<Point3d>();
            foreach (var a in new[] { mn.X, mx.X }) foreach (var b in new[] { mn.Y, mx.Y }) foreach (var c in new[] { mn.Z, mx.Z }) corners.Add(new Point3d(a, b, c));
            var target = mn + (mx - mn) * 0.5;
            double u0 = corners.Min(p => (p - target).DotProduct(dx)), u1 = corners.Max(p => (p - target).DotProduct(dx));
            double v0 = corners.Min(p => (p - target).DotProduct(dy)), v1 = corners.Max(p => (p - target).DotProduct(dy));
            double needW = (u1 - u0) / k, needH = (v1 - v0) / k;                  // mm model
            scale = (scales ?? Scales).FirstOrDefault(s => needW / s <= w * fill + 1e-6 && needH / s <= h * fill + 1e-6);
            if (scale <= 0) scale = Math.Ceiling(Math.Max(needW / (w * fill), needH / (h * fill)) / 50) * 50;
            var vp = new Viewport { CenterPoint = center, Width = w, Height = h, Layer = LayVp };
            ps.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
            vp.ViewDirection = dir;
            vp.ViewTarget = target;
            vp.ViewCenter = new Point2d((u0 + u1) / 2, (v0 + v1) / 2);
            vp.CustomScale = 1.0 / (scale * k);
            try { if (hidden) vp.ShadePlot = ShadePlotType.Hidden; } catch { }
            try { vp.On = true; } catch { /* layout belum aktif: viewport menyala saat layout dibuka */ }
            vp.Locked = true;
            return vp;
        }

        /// <summary>Titik model -> titik kertas untuk viewport.</summary>
        static Point3d ToPaper(Viewport vp, Point3d p)
        {
            Dcs(vp.ViewDirection, out Vector3d dx, out Vector3d dy);
            var rel = p - vp.ViewTarget;
            double u = rel.DotProduct(dx) - vp.ViewCenter.X, v = rel.DotProduct(dy) - vp.ViewCenter.Y;
            return new Point3d(vp.CenterPoint.X + u * vp.CustomScale, vp.CenterPoint.Y + v * vp.CustomScale, 0);
        }

        /// <summary>Dimensi grid (as kolom) dan leader label profil di viewport denah/tampak.</summary>
        static void Annotate(Transaction tr, Database db, BlockTableRecord ps, Viewport vp, List<(Draw.TagData tag, Entity e)> members, Vector3d dir, double k, double scale)
        {
            Dcs(dir, out Vector3d dx, out Vector3d dy);
            double txt = 2.5;
            var pts = new List<Point3d>();
            foreach (var m in members) { pts.Add(m.tag.Spec.S); pts.Add(m.tag.Spec.E); }
            if (pts.Count == 0) return;
            // posisi grid: titik ujung batang yang diproyeksikan, dibulatkan 1 mm, dipakai bila ada kolom/batang di sana
            var us = pts.Select(p => Math.Round((p - vp.ViewTarget).DotProduct(dx) / k)).Distinct().OrderBy(v => v).ToList();
            var vs = pts.Select(p => Math.Round((p - vp.ViewTarget).DotProduct(dy) / k)).Distinct().OrderBy(v => v).ToList();
            us = Thin(us, scale * 12); vs = Thin(vs, scale * 12);
            var pMin = ToPaper(vp, vp.ViewTarget + dx * us.First() * k + dy * vs.First() * k);
            double bottom = vp.CenterPoint.Y - vp.Height / 2 + 4, left = vp.CenterPoint.X - vp.Width / 2 + 4;
            for (int i = 1; i < us.Count; i++)
            {
                var a = ToPaper(vp, vp.ViewTarget + dx * us[i - 1] * k + dy * vs.First() * k);
                var b = ToPaper(vp, vp.ViewTarget + dx * us[i] * k + dy * vs.First() * k);
                AddDim(tr, db, ps, a, b, new Point3d(0, bottom, 0), true, (us[i] - us[i - 1]), txt);
            }
            if (us.Count > 2)
            {
                var a = ToPaper(vp, vp.ViewTarget + dx * us.First() * k + dy * vs.First() * k);
                var b = ToPaper(vp, vp.ViewTarget + dx * us.Last() * k + dy * vs.First() * k);
                AddDim(tr, db, ps, a, b, new Point3d(0, bottom - 6, 0), true, us.Last() - us.First(), txt);
            }
            for (int i = 1; i < vs.Count; i++)
            {
                var a = ToPaper(vp, vp.ViewTarget + dx * us.First() * k + dy * vs[i - 1] * k);
                var b = ToPaper(vp, vp.ViewTarget + dx * us.First() * k + dy * vs[i] * k);
                AddDim(tr, db, ps, a, b, new Point3d(left, 0, 0), false, vs[i] - vs[i - 1], txt);
            }
            // leader: satu label per profil, di batang yang paling panjang dari profil itu
            var done = new HashSet<string>();
            foreach (var m in members.OrderByDescending(x => x.tag.LengthMm))
            {
                if (!done.Add(m.tag.Name)) continue;
                var mid = m.tag.Spec.S + (m.tag.Spec.E - m.tag.Spec.S) * 0.5;
                var p = ToPaper(vp, mid);
                if (Math.Abs(p.X - vp.CenterPoint.X) > vp.Width / 2 - 2 || Math.Abs(p.Y - vp.CenterPoint.Y) > vp.Height / 2 - 2) continue;
                var along = m.tag.Spec.E - m.tag.Spec.S;
                if (Math.Abs(along.GetNormal().DotProduct(dir.GetNormal())) > 0.95) continue;   // batang searah pandang: tampak sebagai titik
                var tp = new Point3d(p.X + 10, p.Y + 8 + 4 * (done.Count % 3), 0);
                Leader(tr, db, ps, p, tp, (string.IsNullOrEmpty(m.tag.Spec.Mark) ? "" : m.tag.Spec.Mark + " - ") + m.tag.Name, txt);
                if (done.Count >= 8) break;
            }
        }

        static List<double> Thin(List<double> v, double minGapMm)
        {
            var o = new List<double>();
            foreach (var x in v) if (o.Count == 0 || x - o.Last() >= minGapMm) o.Add(x);
            return o;
        }

        static void AddDim(Transaction tr, Database db, BlockTableRecord ps, Point3d a, Point3d b, Point3d line, bool horizontal, double valueMm, double h)
        {
            if (a.DistanceTo(b) < 2) return;
            var dimLine = horizontal ? new Point3d((a.X + b.X) / 2, line.Y, 0) : new Point3d(line.X, (a.Y + b.Y) / 2, 0);
            var d = new RotatedDimension(horizontal ? 0 : Math.PI / 2, a, b, dimLine, Math.Round(Math.Abs(valueMm)).ToString("0", CultureInfo.InvariantCulture), db.Dimstyle);
            d.SetDatabaseDefaults(db);
            d.Layer = Draw.LayDim;
            d.Dimscale = 1; d.Dimtxt = h; d.Dimasz = h * 0.8; d.Dimexo = 1.5; d.Dimexe = 1.5; d.Dimgap = 0.8; d.Dimtad = 1;
            ps.AppendEntity(d); tr.AddNewlyCreatedDBObject(d, true);
        }

        static void Leader(Transaction tr, Database db, BlockTableRecord ps, Point3d arrow, Point3d text, string s, double h)
        {
            try
            {
                var ml = new MLeader();
                ml.SetDatabaseDefaults(db);
                ml.ContentType = ContentType.MTextContent;
                var mt = new MText(); mt.SetDatabaseDefaults(db);
                mt.Contents = s; mt.TextHeight = h; mt.Location = text;
                ml.MText = mt;
                int li = ml.AddLeaderLine(arrow);
                ml.ArrowSize = h * 0.8;
                ml.LandingGap = h * 0.4;
                ml.Layer = Draw.LayText;
                ps.AppendEntity(ml); tr.AddNewlyCreatedDBObject(ml, true);
            }
            catch
            {
                var ln = new Line(arrow, text) { Layer = Draw.LayText };
                ps.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                var mt = new MText { Contents = s, TextHeight = h, Location = text, Attachment = AttachmentPoint.BottomLeft, Layer = Draw.LayText };
                ps.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
            }
        }
    }
}
