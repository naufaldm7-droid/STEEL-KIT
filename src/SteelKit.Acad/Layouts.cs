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
    public partial class LayoutCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        public const string LayVp = "SK-VIEWPORT";
        public const string LayTitle = "SK-JUDUL";
        public const string LayDetailBlock = "SK-DETAIL-BLOK";   // blok gambar detail di model space: dibekukan di viewport struktur
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

            // marking batang: sama persis dengan daftar quantity (SKEXCEL) karena memakai AutoMark yang sama
            var marks = new Dictionary<string, string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var items = ModelData.Read(tr, db).Where(i => i.Member != null).ToList();
                Takeoff.AutoMark(items.Select(i => i.Member).ToList());
                foreach (var i in items) marks[i.Handle] = i.Member.Mark;
                tr.Commit();
            }
            var mems = members.Select(m => new Mem { Tag = m.tag, Mark = marks.TryGetValue(m.e.Handle.ToString(), out string mk) ? mk : (m.tag.Spec.Mark ?? "") }).ToList();
            var axes = mems.Select(m => (Draw.V(m.Tag.Spec.S) * (1 / k), Draw.V(m.Tag.Spec.E) * (1 / k))).ToList();
            var grid = GridSystem.FromMembers(axes);
            var levels = LevelMark.FromMembers(axes, ModelData.Levels.Levels);
            var extM = ext;
            if (mems.Count > 0)
            {
                double pad = 500 * k;
                var pts = mems.SelectMany(m => new[] { m.Tag.Spec.S, m.Tag.Spec.E }).ToList();
                extM = new Extents3d(new Point3d(pts.Min(p => p.X) - pad, pts.Min(p => p.Y) - pad, pts.Min(p => p.Z) - pad),
                                     new Point3d(pts.Max(p => p.X) + pad, pts.Max(p => p.Y) + pad, pts.Max(p => p.Z) + pad));
            }

            int perSheet = sheet.Name == "A1" ? 6 : 1;
            var info = new SheetInfo
            {
                Project = Store.Get("proyek", doc.IsNamedDrawing ? System.IO.Path.GetFileNameWithoutExtension(db.Filename) : "").ToUpperInvariant(),
                Title = title, Count = 1 + (conns.Count + perSheet - 1) / perSheet,
            };

            using (doc.LockDocument())
            {
                string n1 = UniqueLayout(db, "SK-01 " + Short(title));
                var layId = NewLayout(db, n1, sheet);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Layers(tr, db);
                    var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                    var ps1 = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                    info.Sub = "STRUKTUR - ISOMETRIK, DENAH, TAMPAK"; info.No = 1;
                    Border(tr, ps1, sheet, info);
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
                        var cell = new Cell { X0 = c.cx - gw / 2, Y0 = c.cy - gh / 2, X1 = c.cx + gw / 2, Y1 = c.cy + gh / 2 };
                        double scale;
                        if (c.iso) AddViewport(tr, db, ps1, new Point3d(c.cx, c.cy + 5, 0), gw - 12, gh - 22, extM, c.dir, k, out scale, true);
                        else StructureView(tr, db, ps1, cell, extM, c.dir, k, mems, grid, levels, out scale);
                        Label(tr, ps1, new Point3d(c.cx, cell.Y0 + 9, 0), c.title, scale, 4, cell.X1 - 3);
                    }
                    tr.Commit();
                }
                int sheets = 0;
                if (conns.Count > 0) sheets = DetailSheets(db, sheet, info, conns, ext, k);
                LayoutManager.Current.CurrentLayout = n1;
                ed.WriteMessage("\nLayout dibuat: " + n1 + (sheets > 0 ? " + " + sheets + " lembar detail sambungan (" + conns.Count + " detail unik)" : "") +
                    ". Viewport terkunci; skala dipilih otomatis dari skala standar.");
            }
        }

        // ------------------------------------------------------------------ lembar detail sambungan
        static double[] detailScales = { 2.0, 5, 10, 15, 20, 25, 50, 75, 100 };

        static int DetailSheets(Database db, Sheet sheet, SheetInfo info, List<Connection> conns, Extents3d ext, double k)
        {
            // gambar detail di model space (di kanan model), skala dipilih dulu supaya teks tercetak ±2,5 mm
            int perSheet = sheet.Name == "A1" ? 6 : 1, count = 0;
            int colsAll = sheet.Name == "A1" ? 3 : 1, rowsAll = sheet.Name == "A1" ? 2 : 1;
            double cellW = (sheet.W - 30) / colsAll - 10, cellH = (sheet.H - 30 - 45) / rowsAll - 18;
            detailScales = new[] { 2.0, 5, 10, 15, 20, 25, 50, 75, 100 };
            var bounds = new List<(Connection c, double[] b, double s, double[] all)>();
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
                    double[] b = null, all = null; double s = s0; ObjectId blockId = ObjectId.Null;
                    var tryScales = detailScales.Where(v => v >= s0).ToList();
                    foreach (var sc in tryScales)
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        var btr = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
                        blockId = bt.Add(btr); tr.AddNewlyCreatedDBObject(btr, true);
                        ConnDraw.Detail(tr, db, btr, c, Point3d.Origin, 2.5 * sc * k);
                        b = ConnDraw.LastBounds; s = sc; all = ConnDraw.LastBoundsAll ?? b;
                        if ((b[2] - b[0]) / k / sc <= cellW * 0.97 && (b[3] - b[1]) / k / sc <= cellH * 0.97 || sc == tryScales.Last()) break;
                        foreach (ObjectId eid in btr) tr.GetObject(eid, OpenMode.ForWrite).Erase();
                        btr.Erase();
                    }
                    var br = new BlockReference(new Point3d(x - b[0], y - b[1], 0), blockId) { Layer = LayDetailBlock };
                    ms.AppendEntity(br); tr.AddNewlyCreatedDBObject(br, true);
                    var bw = new[] { x, y, x + (b[2] - b[0]), y + (b[3] - b[1]) };
                    double dx0 = x - b[0], dy0 = y - b[1];
                    bounds.Add((c, bw, s, new[] { all[0] + dx0, all[1] + dy0, all[2] + dx0, all[3] + dy0 }));
                    x = Math.Max(bw[2], all[2] + dx0) + Math.Max(5000, (bw[2] - bw[0])) * k;
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
                    info.Sub = "DETAIL SAMBUNGAN"; info.No = count + 1;
                    Border(tr, ps, sheet, info);
                    var part = bounds.Skip(s0).Take(perSheet).ToList();
                    int cols = sheet.Name == "A1" ? 3 : 1, rows = (int)Math.Ceiling(part.Count / (double)cols);
                    if (sheet.Name != "A1") { cols = 1; rows = part.Count; }
                    double m = 15, tb = 45, gw = (sheet.W - 2 * m) / cols, gh = (sheet.H - 2 * m - tb) / Math.Max(rows, 1);
                    for (int i = 0; i < part.Count; i++)
                    {
                        int r = i / cols, cIdx = i % cols;
                        double cx = m + gw * (cIdx + 0.5), cy = sheet.H - m - gh * (r + 0.5);
                        var b = part[i].b;
                        // catatan yang menjorok ikut ditampilkan bila masih muat di sel pada skala terpilih
                        var ab = part[i].all;
                        if ((ab[2] - ab[0]) / k / part[i].s * 1.06 + 4 <= gw - 10 && (ab[3] - ab[1]) / k / part[i].s * 1.06 + 4 <= gh - 18) b = ab;
                        var e = new Extents3d(new Point3d(b[0], b[1], 0), new Point3d(b[2], b[3], 0));
                        double sc0 = part[i].s;
                        double vwP = Math.Min(gw - 10, (b[2] - b[0]) / k / sc0 * 1.06 + 4), vhP = Math.Min(gh - 18, (b[3] - b[1]) / k / sc0 * 1.06 + 4);
                        AddViewport(tr, db, ps, new Point3d(cx, cy + 4, 0), vwP, vhP, e, Vector3d.ZAxis, k, out double scale, false, detailScales.Where(v => v >= sc0).ToArray(), 1.0 / 1.06);
                        Label(tr, ps, new Point3d(cx, cy - gh / 2 + 8, 0), "DETAIL " + (s0 + i + 1) + " - " + part[i].c.Title.ToUpperInvariant(), scale, 3.5, cx + gw / 2 - 3);
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
            foreach (var (n, c, plot) in new[] { (LayVp, (short)8, false), (LayTitle, (short)7, true), (LayDetailBlock, (short)7, true), (LayFab, (short)7, true) })
            {
                if (lt.Has(n)) continue;
                lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = n, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, c), IsPlottable = plot };
                lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
            }
            Draw.Layer(tr, db, Draw.LayDim); Draw.Layer(tr, db, Draw.LayText);
        }

        class SheetInfo { public string Project = "", Title = "", Sub = "", Prefix = "SK-ST-"; public int No = 1, Count = 1; }
        class Cell { public double X0, Y0, X1, Y1; }
        class Mem { public Draw.TagData Tag; public string Mark; }

        /// <summary>Catatan umum bawaan. Isi yang khusus proyek ditulis "sesuai spesifikasi proyek" supaya tidak mengarang mutu.</summary>
        static readonly string[] DefaultNotes =
        {
            "Satuan ukuran mm, elevasi m, kecuali dinyatakan lain. Ukuran dicek ulang di lapangan sebelum fabrikasi.",
            "Mutu baja profil, pelat, baut, mur, ring, angkur, dan elektroda las: sesuai spesifikasi proyek.",
            "Simbol las: praktik ISO 2553 sistem B (simbol di bawah garis referensi = sisi panah), a = tebal leher las sudut. Prosedur las mengikuti WPS yang disetujui.",
            "Pengencangan baut (snug-tight / pratarik) dan lubang baut: sesuai spesifikasi proyek.",
            "Persiapan permukaan, cat, dan galvanis: sesuai spesifikasi proyek.",
            "Marking batang sama dengan daftar quantity Steel Kit (SKEXCEL). Grid as dari titik dasar kolom.",
            "Steel Kit menyiapkan geometri dan cek aturan jarak/ukuran minimum (praktik umum). Kapasitas batang, sambungan, dan pondasi dihitung dan disahkan engineer struktur berlisensi.",
        };

        static void Border(Transaction tr, BlockTableRecord ps, Sheet sh, SheetInfo inf)
        {
            void Add(Entity e) { e.Layer = LayTitle; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            void Ln(double x0, double y0, double x1, double y1) => Add(new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)));
            void Tx(string s, double x, double y, double h, AttachmentPoint ap = AttachmentPoint.TopLeft, double w = 0)
                => Add(new MText { Contents = s, Location = new Point3d(x, y, 0), TextHeight = h, Attachment = ap, Width = w });
            void Field(string label, string value, double x, double y, double w, double h, double vh)
            {
                Tx(label, x + 1.2, y + h - 1, 1.6);
                if (!string.IsNullOrEmpty(value)) Tx(value, x + 1.2, y + (h - 2.6) / 2, vh, AttachmentPoint.MiddleLeft, w - 2.4);
            }
            Add(Rect(10, 10, sh.W - 10, sh.H - 10));
            const double tbw = 185, tbh = 45;
            double x0 = sh.W - 10 - tbw, y0 = 10, xb = x0 + 62, xr = sh.W - 10;
            Add(Rect(x0, y0, xr, y0 + tbh));
            Ln(xb, y0, xb, y0 + tbh);
            // kiri: tabel revisi + keterangan pembuat
            double ry = y0 + tbh;
            Ln(x0, y0 + 20, xb, y0 + 20);
            for (int i = 1; i <= 4; i++) Ln(x0, ry - 5 * i, xb, ry - 5 * i);
            Ln(x0 + 9, y0 + 20, x0 + 9, ry); Ln(x0 + 27, y0 + 20, x0 + 27, ry);
            Tx("REV", x0 + 4.5, ry - 2.5, 1.6, AttachmentPoint.MiddleCenter);
            Tx("TANGGAL", x0 + 18, ry - 2.5, 1.6, AttachmentPoint.MiddleCenter);
            Tx("URAIAN", x0 + 44.5, ry - 2.5, 1.6, AttachmentPoint.MiddleCenter);
            Tx("0", x0 + 4.5, ry - 7.5, 1.8, AttachmentPoint.MiddleCenter);
            Tx(DateTime.Now.ToString("dd-MM-yyyy"), x0 + 18, ry - 7.5, 1.8, AttachmentPoint.MiddleCenter);
            Tx("Terbit untuk review", x0 + 28, ry - 7.5, 1.8, AttachmentPoint.MiddleLeft);
            Tx("{\\H1.4x;STEEL KIT}\\PGeometri, gambar & quantity dibuat dengan Steel Kit untuk AutoCAD.", x0 + 2, y0 + 18, 1.6, AttachmentPoint.TopLeft, 58);
            // kanan: proyek, judul, penanggung jawab, data lembar
            Ln(xb, y0 + 35, xr, y0 + 35); Ln(xb, y0 + 20, xr, y0 + 20); Ln(xb, y0 + 10, xr, y0 + 10);
            Field("PROYEK", inf.Project, xb, y0 + 35, xr - xb, 10, 3);
            Tx("JUDUL GAMBAR", xb + 1.2, y0 + 34, 1.6);
            Tx(inf.Title, xb + 1.2, y0 + 30.5, 3.6, AttachmentPoint.TopLeft, xr - xb - 2.4);
            Tx(inf.Sub, xb + 1.2, y0 + 24.5, 2.4, AttachmentPoint.TopLeft, xr - xb - 2.4);
            double cw = (xr - xb) / 3;
            for (int i = 1; i < 3; i++) Ln(xb + cw * i, y0 + 10, xb + cw * i, y0 + 20);
            Field("DIGAMBAR", Store.Get("digambar", ""), xb, y0 + 10, cw, 10, 2.4);
            Field("DIPERIKSA", Store.Get("diperiksa", ""), xb + cw, y0 + 10, cw, 10, 2.4);
            Field("DISETUJUI", Store.Get("disetujui", ""), xb + 2 * cw, y0 + 10, cw, 10, 2.4);
            double dw = (xr - xb) / 4;
            for (int i = 1; i < 4; i++) Ln(xb + dw * i, y0, xb + dw * i, y0 + 10);
            Field("SKALA", "SEPERTI TERTERA", xb, y0, dw, 10, 2);
            Field("TANGGAL", DateTime.Now.ToString("dd-MM-yyyy"), xb + dw, y0, dw, 10, 2.4);
            Field("NO. GAMBAR", inf.Prefix + inf.No.ToString("00", CultureInfo.InvariantCulture), xb + 2 * dw, y0, dw, 10, 2.4);
            Field("LEMBAR", inf.No + " / " + inf.Count, xb + 3 * dw, y0, dw, 10, 2.4);
            // catatan umum di kiri kotak judul (MText biasa: boleh disunting langsung)
            double nx0 = 15, nx1 = x0 - 5;
            var notes = Store.Get("catatan", "");
            var lines = string.IsNullOrWhiteSpace(notes) ? DefaultNotes : notes.Split('|');
            string body = "{\\H1.3x;\\LCATATAN UMUM}\\P" + string.Join("\\P", lines.Select((t, i) => (i + 1) + ". " + t.Trim()));
            double nh = sh.Name == "A1" ? 2.2 : 1.8;
            Tx(body, nx0, y0 + tbh, nh, AttachmentPoint.TopLeft, Math.Min(nx1 - nx0, sh.Name == "A1" ? 300 : 400));
        }

        static Polyline Rect(double x0, double y0, double x1, double y1)
        {
            var p = new Polyline();
            p.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); p.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
            p.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); p.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
            p.Closed = true;
            return p;
        }

        /// <summary>Judul tampak + skala, dan skala grafis di kanannya (dilewati bila melewati batas kanan sel).</summary>
        static void Label(Transaction tr, BlockTableRecord ps, Point3d at, string text, double scale, double h, double maxX = double.MaxValue)
        {
            void Add(Entity e) { e.Layer = LayTitle; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            Add(new MText { Contents = "{\\L" + text + "}\\P{\\H0.7x;SKALA 1:" + scale.ToString("0.##", CultureInfo.InvariantCulture) + "}", Location = at, TextHeight = h, Attachment = AttachmentPoint.TopCenter });
            if (scale <= 0) return;
            double L = ScaleBar.ModelLength(scale), pl = L / scale;
            string end = ScaleBar.Label(L);
            string sc = "SKALA 1:" + scale.ToString("0.##", CultureInfo.InvariantCulture);
            double x0 = at.X + Math.Max(DimTiers.TextWidth(text, h), DimTiers.TextWidth(sc, h * 0.7)) / 2 + 6, y = at.Y - h * 1.5, bh = 1.4;
            if (x0 + pl + DimTiers.TextWidth(end, 1.8) + 2 > maxX) return;
            for (int i = 0; i < 4; i++)
            {
                double a = x0 + pl * i / 4, b = x0 + pl * (i + 1) / 4;
                if (i % 2 == 0) Add(new Solid(new Point3d(a, y, 0), new Point3d(b, y, 0), new Point3d(a, y + bh, 0), new Point3d(b, y + bh, 0)));
            }
            Add(Rect(x0, y, x0 + pl, y + bh));
            Add(new MText { Contents = "0", Location = new Point3d(x0, y - 0.8, 0), TextHeight = 1.8, Attachment = AttachmentPoint.TopCenter });
            Add(new MText { Contents = end, Location = new Point3d(x0 + pl, y - 0.8, 0), TextHeight = 1.8, Attachment = AttachmentPoint.TopCenter });
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
            // tampak struktur tidak boleh ikut menampilkan blok detail sambungan yang diletakkan di samping model
            if (scales == null)
                try
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    var fz = new List<ObjectId>(); foreach (var ln in new[] { LayDetailBlock, LayFab }) if (lt.Has(ln)) fz.Add(lt[ln]);
                    if (fz.Count > 0) vp.FreezeLayersInViewport(fz.GetEnumerator());
                }
                catch { }
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

        static readonly string[] MarkOrder = { "C", "R", "B", "GP", "PO", "BR", "RB", "P", "ST" };

        static string Prefix(string mark) => new string((mark ?? "").TakeWhile(char.IsLetter).ToArray());
        static int MarkNo(string mark) => int.TryParse(new string((mark ?? "").SkipWhile(char.IsLetter).TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0;

        static int MarkRank(string mark)
        {
            string pre = new string((mark ?? "").TakeWhile(char.IsLetter).ToArray());
            int i = Array.IndexOf(MarkOrder, pre);
            return i < 0 ? MarkOrder.Length : i;
        }

        /// <summary>
        /// Satu tampak denah/tampak berukuran: viewport dipas ke model, grid as + gelembung, dimensi antar as,
        /// penanda elevasi (tampak), dan label marking batang yang tidak saling menimpa.
        /// </summary>
        static void StructureView(Transaction tr, Database db, BlockTableRecord ps, Cell cell, Extents3d ext, Vector3d dir, double k,
            List<Mem> mems, GridSystem grid, List<LevelMark> levels, out double scale)
        {
            Dcs(dir, out Vector3d dx, out Vector3d dy);
            bool elev = Math.Abs(dy.DotProduct(Vector3d.ZAxis)) > 0.99;
            double mL = elev ? 6 : 14, mR = elev ? 28 : 16, mT = 13, mB = 25;
            double ix0 = cell.X0 + mL, ix1 = cell.X1 - mR, iy0 = cell.Y0 + mB, iy1 = cell.Y1 - mT;
            var vp = AddViewport(tr, db, ps, new Point3d((ix0 + ix1) / 2, (iy0 + iy1) / 2, 0), ix1 - ix0, iy1 - iy0, ext, dir, k, out scale, false, null, 0.97);
            // batas model di kertas, lalu viewport dikecilkan pas model (anotasi di luar viewport)
            var mn = ext.MinPoint; var mx = ext.MaxPoint;
            var pp = new List<Point3d>();
            foreach (var a in new[] { mn.X, mx.X }) foreach (var b in new[] { mn.Y, mx.Y }) foreach (var c in new[] { mn.Z, mx.Z }) pp.Add(ToPaper(vp, new Point3d(a, b, c)));
            double mx0 = pp.Min(p => p.X), mx1 = pp.Max(p => p.X), my0 = pp.Min(p => p.Y), my1 = pp.Max(p => p.Y);
            vp.Width = Math.Min(ix1 - ix0, mx1 - mx0 + 6); vp.Height = Math.Min(iy1 - iy0, my1 - my0 + 6);
            vp.CustomScale = 1.0 / (scale * k);   // ubah ukuran viewport mengubah skalanya (ViewHeight tetap): kunci lagi

            void Add(Entity e, string layer) { e.Layer = layer; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            var used = new List<double[]>();
            Draw.Layer(tr, db, Draw.LayAxis);
            const double r = 3.2;
            var vert = new List<(double p, double pos)>(); var hor = new List<(double p, double pos)>();
            var tgt = vp.ViewTarget;
            foreach (var (axis, lines) in new[] { (Vector3d.XAxis, grid.X), (Vector3d.YAxis, grid.Y) })
            {
                bool v = Math.Abs(dx.DotProduct(axis)) > 0.99, h = Math.Abs(dy.DotProduct(axis)) > 0.99;
                if (!v && !h) continue;
                double prev = double.NaN; int stag = 0;
                foreach (var g in lines.OrderBy(q => v ? ToPaper(vp, GridPoint(axis, q.Pos * k, tgt)).X : ToPaper(vp, GridPoint(axis, q.Pos * k, tgt)).Y))
                {
                    var p = ToPaper(vp, GridPoint(axis, g.Pos * k, tgt));
                    double at = v ? p.X : p.Y;
                    stag = !double.IsNaN(prev) && Math.Abs(at - prev) < 2 * r + 1 ? 1 - stag : 0;   // gelembung berdekatan: selang-seling
                    prev = at;
                    double off = 2 + r + stag * (2 * r + 1);
                    Point3d l0, l1, bc;
                    if (v) { l0 = new Point3d(p.X, my0 - 2, 0); l1 = new Point3d(p.X, my1 + off - r, 0); bc = new Point3d(p.X, my1 + off, 0); vert.Add((p.X, g.Pos)); }
                    else { l0 = new Point3d(mx1 + 2, p.Y, 0); l1 = new Point3d(mx0 - off + r, p.Y, 0); bc = new Point3d(mx0 - off, p.Y, 0); hor.Add((p.Y, g.Pos)); }
                    Add(new Line(l0, l1) { LinetypeScale = 0.25 }, Draw.LayAxis);
                    Add(new Circle(bc, Vector3d.ZAxis, r), Draw.LayText);
                    Add(new MText { Contents = g.Label, Location = bc, TextHeight = 2.8, Attachment = AttachmentPoint.MiddleCenter }, Draw.LayText);
                    used.Add(new[] { bc.X - r, bc.Y - r, bc.X + r, bc.Y + r });
                }
            }
            // dimensi antar as (atau total model bila tidak ada as di arah itu)
            DimChain(tr, db, ps, vert, true, my0 - 2, my0 - 6, scale, mx0, mx1, used);
            if (!elev) DimChain(tr, db, ps, hor, false, mx1 + 2, mx1 + 6, scale, my0, my1, used);
            // penanda elevasi di kanan tampak
            if (elev && levels.Count > 0)
            {
                var ys = levels.Select(lv => ToPaper(vp, new Point3d(tgt.X, tgt.Y, lv.Z * k)).Y).ToList();
                for (int i = 0; i < levels.Count; i++)
                {
                    double py = ys[i], x = mx1 + 2;
                    bool tightBelow = i > 0 && py - ys[i - 1] < 6, tightAbove = i < levels.Count - 1 && ys[i + 1] - py < 6;
                    Add(new Line(new Point3d(mx1 + 0.5, py, 0), new Point3d(x + 20, py, 0)), Draw.LayText);
                    Add(new Solid(new Point3d(x + 3, py, 0), new Point3d(x + 1.6, py + 2.2, 0), new Point3d(x + 4.4, py + 2.2, 0)), Draw.LayText);
                    // dua elevasi berdekatan: yang bawah angkanya di bawah garis + nama di sampingnya, yang atas tetap di atas garis
                    bool below = tightAbove && !tightBelow;
                    string txt = levels[i].Text + (below || tightBelow ? "  {\\H0.73x;" + levels[i].Name + "}" : "");
                    Add(Masked(new MText { Contents = txt, Location = new Point3d(x + 5.5, below ? py - 0.6 : py + 0.6, 0), TextHeight = 2.2, Attachment = below ? AttachmentPoint.TopLeft : AttachmentPoint.BottomLeft }), Draw.LayText);
                    if (!below && !tightBelow) Add(Masked(new MText { Contents = levels[i].Name, Location = new Point3d(x + 5.5, py - 0.6, 0), TextHeight = 1.6, Attachment = AttachmentPoint.TopLeft }), Draw.LayText);
                }
                used.Add(new[] { mx1 + 1, ys.Min() - 3, mx1 + 40, ys.Max() + 3.5 });
            }
            // marking batang: satu label per marking per tampak
            var lim = new[] { cell.X0 + 1, cell.Y0 + 11, cell.X1 - 1, cell.Y1 - 1 };
            int placed = 0;
            var offs = new[] { (8.0, 7.0), (-8.0, 7.0), (8.0, -7.0), (-8.0, -7.0), (13.0, 12.0), (-13.0, 12.0), (13.0, -12.0), (-13.0, -12.0), (18.0, 3.0), (-18.0, 3.0), (5.0, 16.0), (-5.0, 16.0) };
            // satu label per jenis + profil; marking yang berbeda pola lubangnya ditulis sebagai rentang (C1–C6), rinciannya di lembar fabrikasi
            foreach (var grp in mems.Where(m => !string.IsNullOrEmpty(m.Mark)).GroupBy(m => Prefix(m.Mark) + "|" + m.Tag.Name)
                .OrderBy(g => MarkRank(g.First().Mark)).ThenBy(g => g.Key, StringComparer.Ordinal))
            {
                if (placed >= 16) break;
                var cand = grp.Select(m =>
                {
                    var s = m.Tag.Spec.S; var e = m.Tag.Spec.E; var along = e - s;
                    if (along.Length < 1e-9 || Math.Abs(along.GetNormal().DotProduct(dir.GetNormal())) > 0.95) return (m, p: Point3d.Origin, len: -1.0);
                    var a = ToPaper(vp, s); var b = ToPaper(vp, e);
                    return (m, p: new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0), len: a.DistanceTo(b));
                }).Where(q => q.len > 4).OrderByDescending(q => q.len).Take(6).ToList();
                if (cand.Count == 0) continue;
                var mk = grp.Select(m => m.Mark).Distinct().OrderBy(m => MarkNo(m)).ToList();
                string text = (mk.Count == 1 ? mk[0] : mk.First() + "–" + mk.Last()) + " " + grp.First().Tag.Name;
                double tw = DimTiers.TextWidth(text, 2.2) + 1, th = 3;
                bool done = false;
                foreach (var c in cand)
                {
                    foreach (var (ox, oy) in offs)
                    {
                        double kx = c.p.X + ox, ky = c.p.Y + oy, sx = ox > 0 ? 1 : -1;
                        var rect = sx > 0 ? new[] { kx + 2.5, ky - th / 2, kx + 2.5 + tw, ky + th / 2 } : new[] { kx - 2.5 - tw, ky - th / 2, kx - 2.5, ky + th / 2 };
                        if (rect[0] < lim[0] || rect[1] < lim[1] || rect[2] > lim[2] || rect[3] > lim[3]) continue;
                        if (used.Any(u => rect[0] < u[2] && rect[2] > u[0] && rect[1] < u[3] && rect[3] > u[1])) continue;
                        PaperLeader(tr, ps, c.p, new Point3d(kx, ky, 0), sx, text, 2.2);
                        used.Add(rect); placed++; done = true;
                        break;
                    }
                    if (done) break;
                }
            }
        }

        static Point3d GridPoint(Vector3d axis, double pos, Point3d tgt)
            => axis.X > 0.5 ? new Point3d(pos, tgt.Y, tgt.Z) : new Point3d(tgt.X, pos, tgt.Z);

        /// <summary>Rantai dimensi antar as di kertas (lapis luar untuk angka yang bertumpuk) + dimensi total.</summary>
        static double DimChain(Transaction tr, Database db, BlockTableRecord ps, List<(double p, double pos)> g, bool horizontal, double origin, double line0,
            double scale, double m0, double m1, List<double[]> used)
        {
            double sgn = horizontal ? -1 : 1;     // bawah untuk horizontal, kanan untuk vertikal
            Point3d Pt(double along, double across) => horizontal ? new Point3d(along, across, 0) : new Point3d(across, along, 0);
            var s = g.OrderBy(q => q.p).ToList();
            const double th = 2.5;
            if (s.Count < 2)
            {
                // tanpa as: dimensi total model
                AddDim(tr, db, ps, Pt(m0, origin), Pt(m1, origin), Pt(0, line0), horizontal, (m1 - m0) * scale, th);
                used.Add(horizontal ? new[] { m0, line0 - 1, m1, origin } : new[] { origin, m0, line0 + 4, m1 });
                return line0 + sgn * 4;
            }
            var items = new List<(double s0, double s1, double textW)>();
            for (int i = 1; i < s.Count; i++)
                items.Add((s[i - 1].p, s[i].p, DimTiers.TextWidth(Math.Round(Math.Abs(s[i].pos - s[i - 1].pos)).ToString("0", CultureInfo.InvariantCulture), th)));
            var tiers = DimTiers.Assign(items, 0.6);
            for (int i = 0; i < items.Count; i++)
            {
                double ln = line0 + sgn * 4.5 * tiers[i];
                AddDim(tr, db, ps, Pt(s[i].p, origin), Pt(s[i + 1].p, origin), Pt(0, ln), horizontal, s[i + 1].pos - s[i].pos, th);
            }
            double outer = line0 + sgn * 4.5 * (tiers.Max() + 1);
            if (s.Count > 2) AddDim(tr, db, ps, Pt(s.First().p, origin), Pt(s.Last().p, origin), Pt(0, outer), horizontal, s.Last().pos - s.First().pos, th);
            double far = outer + sgn * 4;
            used.Add(horizontal ? new[] { s.First().p - 2, far, s.Last().p + 2, origin } : new[] { origin, s.First().p - 2, far, s.Last().p + 2 });
            return far;
        }

        /// <summary>Leader di kertas: panah kecil, garis ke lutut, landasan pendek, teks.</summary>
        static void PaperLeader(Transaction tr, BlockTableRecord ps, Point3d arrow, Point3d knee, double sx, string text, double h)
        {
            void Add(Entity e) { e.Layer = Draw.LayText; ps.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            var u = (knee - arrow).GetNormal(); var n = new Vector3d(-u.Y, u.X, 0);
            var b = arrow + u * 1.8;
            Add(new Solid(arrow, b + n * 0.45, b - n * 0.45));
            var end = new Point3d(knee.X + sx * 2, knee.Y, 0);
            Add(new Line(arrow, knee)); Add(new Line(knee, end));
            Add(Masked(new MText { Contents = text, Location = new Point3d(end.X + sx * 0.5, end.Y, 0), TextHeight = h, Attachment = sx > 0 ? AttachmentPoint.MiddleLeft : AttachmentPoint.MiddleRight }));
        }

        /// <summary>Teks dengan latar penutup (warna latar kertas) supaya terbaca di atas garis gambar.</summary>
        public static MText Masked(MText m)
        {
            try { m.BackgroundFill = true; m.UseBackgroundColor = true; m.BackgroundScaleFactor = 1.15; } catch { }
            return m;
        }

        static void AddDim(Transaction tr, Database db, BlockTableRecord ps, Point3d a, Point3d b, Point3d line, bool horizontal, double valueMm, double h, string text = null)
        {
            if (a.DistanceTo(b) < 0.4) return;
            var dimLine = horizontal ? new Point3d((a.X + b.X) / 2, line.Y, 0) : new Point3d(line.X, (a.Y + b.Y) / 2, 0);
            var d = new RotatedDimension(horizontal ? 0 : Math.PI / 2, a, b, dimLine, text ?? Math.Round(Math.Abs(valueMm)).ToString("0", CultureInfo.InvariantCulture), db.Dimstyle);
            d.SetDatabaseDefaults(db);
            d.Layer = Draw.LayDim;
            d.Dimscale = 1; d.Dimtxt = h; d.Dimasz = h * 0.8; d.Dimexo = 1.5; d.Dimexe = 1.5; d.Dimgap = 0.8; d.Dimtad = 1;
            ps.AppendEntity(d); tr.AddNewlyCreatedDBObject(d, true);
        }
    }
}
