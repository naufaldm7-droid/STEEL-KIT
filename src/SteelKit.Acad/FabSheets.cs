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

namespace SteelKit.Acad
{
    /// <summary>
    /// Gambar fabrikasi bagian tunggal per marking: tampak samping & tampak atas dari solid batang yang sebenarnya
    /// (lubang baut, lubang lonjong, potongan ujung ikut terlihat), dimensi posisi lubang dari ujung, panjang potong,
    /// dan tabel data marking. Solid salinan diletakkan jauh di bawah model pada layer SK-FABRIKASI (tanpa data Steel Kit,
    /// jadi tidak ikut quantity, cek, atau tabrakan) dan dibekukan di viewport struktur.
    /// </summary>
    public partial class LayoutCommands
    {
        public const string LayFab = "SK-FABRIKASI";

        class FabHole { public Point3d C; public Vector3d W; public double Dia; public string Kind; }

        class FabPart
        {
            public string Mark; public Draw.TagData Tag; public int Count; public Extents3d Ext;
            public List<FabHole> Holes = new List<FabHole>(); public int EndCuts;
        }

        [CommandMethod("STEELKIT", "SKFABRIKASI", CommandFlags.Modal | CommandFlags.NoPaperSpace)]
        public void Fabrication() => Guard.Run(FabCore);

        void FabCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            double k = Draw.UnitK(db);
            var mems = new List<(Draw.TagData tag, string handle)>();
            var ext = new Extents3d(); bool any = false;
            var marks = new Dictionary<string, string>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var e = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var t = e == null ? null : Draw.ReadTag(tr, e);
                    if (t == null || (t.Kind != "batang3d" && t.Kind != "sambungan")) continue;
                    try { ext.AddExtents(e.GeometricExtents); any = true; } catch { }
                    if (t.Kind == "batang3d" && t.Spec != null) mems.Add((t, e.Handle.ToString()));
                }
                var items = ModelData.Read(tr, db).Where(i => i.Member != null).ToList();
                Takeoff.AutoMark(items.Select(i => i.Member).ToList());
                foreach (var i in items) marks[i.Handle] = i.Member.Mark;
                tr.Commit();
            }
            if (!any || mems.Count == 0) { ed.WriteMessage("\nBelum ada batang 3D Steel Kit di model."); return; }

            var groups = mems.GroupBy(m => marks.TryGetValue(m.handle, out string mk) && !string.IsNullOrEmpty(mk) ? mk : m.tag.Name)
                             .OrderBy(g => MarkRank(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            var parts = new List<FabPart>();
            using (doc.LockDocument())
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    Layers(tr, db);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    foreach (ObjectId id in ms)
                        if (tr.GetObject(id, OpenMode.ForRead) is Entity old && old.Layer == LayFab) { old.UpgradeOpen(); old.Erase(); }
                    // area fabrikasi: di kanan model, jauh di bawah (Y dan Z), batang berjajar sepanjang X
                    double x = ext.MaxPoint.X + 10000 * k, fy = ext.MinPoint.Y - 50000 * k, fz = ext.MinPoint.Z - 50000 * k;
                    foreach (var g in groups)
                    {
                        var rep = g.OrderByDescending(m => m.tag.LengthMm).First();
                        var spec = rep.tag.Spec;
                        var sol = new Solid3d(); sol.SetDatabaseDefaults(db);
                        try { MemberBuild.Shape3D(sol, spec, k); }
                        catch { sol.Dispose(); continue; }
                        Draw.MemberAxes(spec.E - spec.S, spec.Roll, out Vector3d xa, out Vector3d ya, out Vector3d za);
                        // lokal batang (x lebar, y tinggi, z panjang) -> dunia (Y, Z, X): panjang mendatar, tinggi ke atas
                        var p0 = new Point3d(x, fy, fz);
                        var M = Matrix3d.AlignCoordinateSystem(spec.S, xa, ya, za, p0, Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis);
                        sol.TransformBy(M);
                        sol.Layer = LayFab;
                        ms.AppendEntity(sol); tr.AddNewlyCreatedDBObject(sol, true);
                        var part = new FabPart { Mark = g.Key, Tag = rep.tag, Count = g.Count(), Ext = sol.GeometricExtents };
                        double yWeb = (part.Ext.MinPoint.Y + part.Ext.MaxPoint.Y) / 2;
                        foreach (var c in spec.Cuts)
                        {
                            if (!c.Cylinder) { part.EndCuts++; continue; }
                            var o = Draw.P(c.F.O).TransformBy(M); var w = Draw.D(c.F.W).TransformBy(M).GetNormal();
                            var h = new FabHole { W = w, Dia = 2 * c.Radius / k };
                            if (Math.Abs(w.Y) > 0.9) { h.Kind = "badan"; h.C = o; }
                            else if (Math.Abs(w.Z) > 0.9) { h.Kind = "sayap"; h.C = o; }
                            else if (Math.Abs(w.Y) > 0.05) { h.Kind = "lonjong"; h.C = o + w * ((yWeb - o.Y) / w.Y); }
                            else continue;
                            // lubang yang benar-benar mengenai batang ini saja (silinder lubang diberikan ke semua batang sambungan)
                            if (!Inside(part.Ext, h.C, h.Kind, k)) continue;
                            if (part.Holes.Any(q => q.Kind == h.Kind && q.C.DistanceTo(h.C) < 1 * k)) continue;
                            part.Holes.Add(h);
                        }
                        parts.Add(part);
                        x = part.Ext.MaxPoint.X + 3000 * k;
                    }
                    tr.Commit();
                }

                var info = new SheetInfo
                {
                    Project = Store.Get("proyek", doc.IsNamedDrawing ? System.IO.Path.GetFileNameWithoutExtension(db.Filename) : "").ToUpperInvariant(),
                    Title = "GAMBAR FABRIKASI", Count = parts.Count, Prefix = "SK-FB-",
                };
                string first = null;
                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    string name = UniqueLayout(db, "SK-F" + (i + 1).ToString("00", CultureInfo.InvariantCulture) + " " + part.Mark);
                    first = first ?? name;
                    var layId = NewLayout(db, name, A3);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        Layers(tr, db);
                        var lay = (Layout)tr.GetObject(layId, OpenMode.ForRead);
                        var ps = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                        info.Sub = "MARKING " + part.Mark + " - " + part.Tag.Name + " (" + part.Count + " BATANG)"; info.No = i + 1;
                        Border(tr, ps, A3, info);
                        FabSheet(tr, db, ps, part, k);
                        Std.ApplyAll(tr, db, ps);
                        tr.Commit();
                    }
                }
                if (first != null) LayoutManager.Current.CurrentLayout = first;
            }
            ed.WriteMessage("\nGambar fabrikasi dibuat: " + parts.Count + " lembar (" + string.Join(", ", parts.Select(p => p.Mark + " x" + p.Count)) +
                "). Bagian tunggal; pelat yang dilas ke batang lihat lembar detail sambungan.");
        }

        static bool Inside(Extents3d e, Point3d c, string kind, double k)
        {
            double t = 2 * k;
            bool inX = c.X > e.MinPoint.X - t && c.X < e.MaxPoint.X + t;
            bool inY = c.Y > e.MinPoint.Y - t && c.Y < e.MaxPoint.Y + t;
            bool inZ = c.Z > e.MinPoint.Z - t && c.Z < e.MaxPoint.Z + t;
            // lubang badan: pusatnya boleh di mana saja sepanjang sumbu Y (sumbu lubang); lubang sayap: sepanjang Z
            return kind == "badan" ? inX && inZ : kind == "sayap" ? inX && inY : inX && inY && inZ;
        }

        /// <summary>Isi satu lembar fabrikasi: tampak samping (atas), tampak atas (tengah), tabel data (kiri bawah).</summary>
        static void FabSheet(Transaction tr, Database db, BlockTableRecord ps, FabPart part, double k)
        {
            var e = part.Ext;
            double L = (e.MaxPoint.X - e.MinPoint.X) / k, H = (e.MaxPoint.Z - e.MinPoint.Z) / k, B = (e.MaxPoint.Y - e.MinPoint.Y) / k;
            // kolom kiri (x 15..265): tampak samping lalu tampak atas, disusun dari atas dengan jarak tetap.
            // kolom kanan (x 275..405): tabel data, catatan, daftar lubang, rata kiri dan sejajar atas.
            const double cx = 140, colW = 240, xR = 275;
            var web = part.Holes.Where(h => h.Kind != "sayap").ToList();
            var fl = part.Holes.Where(h => h.Kind == "sayap").ToList();
            // tampak samping: lihat dari -Y (kanan = panjang, atas = tinggi profil); tepi atas batang di y 262
            var vE = FabViewport(tr, db, ps, new Point3d(cx, 230, 0), colW, 50, e, -Vector3d.YAxis, k, null, out double sc, out double[] rE);
            MoveVp(vE, rE, 262 - rE[3]);
            double fE = FabDims(tr, db, ps, vE, rE, sc, web.Select(h => h.C).ToList(), e, k, true, out string gE);
            HoleCallouts(tr, ps, vE, rE, web, gE);
            Label(tr, ps, new Point3d(cx, fE - 3, 0), "TAMPAK SAMPING " + part.Mark, sc, Std.H2, 262);
            // tampak atas: lihat dari +Z (kanan = panjang, atas = lebar profil), skala sama; 26 mm di bawah label di atasnya
            var vP = FabViewport(tr, db, ps, new Point3d(cx, 140, 0), colW, 40, e, Vector3d.ZAxis, k, sc, out double scP, out double[] rP);
            MoveVp(vP, rP, (fE - 3 - 11 - 15) - rP[3]);
            double fP = FabDims(tr, db, ps, vP, rP, scP, fl.Select(h => h.C).ToList(), e, k, false, out string gP);
            HoleCallouts(tr, ps, vP, rP, fl, gP);
            Label(tr, ps, new Point3d(cx, fP - 3, 0), "TAMPAK ATAS " + part.Mark, scP, Std.H2, 262);

            // tabel data marking (kiri bawah, di atas catatan umum)
            double kgm = part.Tag.KgPerM, each = kgm * L / 1000;
            var holeText = string.Join(", ", part.Holes.GroupBy(h => (h.Kind, Math.Round(h.Dia))).OrderBy(g => g.Key.Kind)
                .Select(g => g.Count() + " x " + (g.Key.Kind == "lonjong" ? "lonjong lebar " : "Ø") + Fmt.N(g.Key.Item2) + " (" + g.Key.Kind + ")"));
            var rows = new List<string[]>
            {
                new[] { "MARKING", part.Mark },
                new[] { "PROFIL", part.Tag.Name },
                new[] { "JUMLAH", part.Count + " batang" },
                new[] { "PANJANG POTONG", Fmt.N(Math.Round(L)) + " mm" },
                new[] { "BERAT / BATANG", Draw.N(each, "0.0") + " kg (kotor, " + Draw.N(kgm, "0.0") + " kg/m)" },
                new[] { "BERAT TOTAL", Draw.N(each * part.Count, "0.0") + " kg" },
                new[] { "LUBANG / BATANG", string.IsNullOrEmpty(holeText) ? "-" : holeText },
                new[] { "UJUNG", part.EndCuts > 0 ? "dipotong sesuai bentuk pada tampak" : "potong tegak lurus" },
                new[] { "MUTU & PERMUKAAN", "sesuai spesifikasi proyek" },
            };
            if (L > 12000) rows.Add(new[] { "PERHATIAN", "panjang > 12 m: tentukan sambungan (splice) sesuai panjang material tersedia" });
            double yR = 272;
            var tid = Commands.MakeTable(db, Matrix3d.Identity, new Point3d(xR, yR, 0), "DATA MARKING " + part.Mark, rows, Std.H4 / k, ps.ObjectId);
            yR -= TableHeight(tr, tid) + 5;
            var note = Masked(new MText
            {
                Contents = "Gambar bagian tunggal (single part). Pelat, siku, dan baut yang terpasang pada batang ini lihat lembar detail sambungan." +
                    "\\PUkuran lubang = diameter lubang baut standar; posisi lubang diukur dari ujung kiri batang." +
                    (part.Tag.Family == Family.RD ? "\\PRod: panjang ulir di kedua ujung sesuai detail ujung rod dan turnbuckle." : ""),
                Location = new Point3d(xR, yR, 0), TextHeight = Std.H4, Width = 128, Attachment = AttachmentPoint.TopLeft, Layer = Draw.LayText,
            });
            ps.AppendEntity(note); tr.AddNewlyCreatedDBObject(note, true);
            yR -= (note.ActualHeight > 0 ? note.ActualHeight : 12) + 5;
            HoleTable(db, ps, part, k, new Point3d(xR, yR, 0));
        }

        static double TableHeight(Transaction tr, ObjectId id)
        {
            try { return ((Table)tr.GetObject(id, OpenMode.ForRead)).Height; } catch { return 60; }
        }

        /// <summary>Geser viewport vertikal (isi ikut bergeser) dan perbarui batas batangnya di kertas.</summary>
        static void MoveVp(Viewport vp, double[] r, double dy)
        {
            vp.CenterPoint = new Point3d(vp.CenterPoint.X, vp.CenterPoint.Y + dy, 0);
            r[1] += dy; r[3] += dy;
        }

        /// <summary>
        /// Daftar lubang per posisi memanjang: jarak dari ujung kiri, jarak dari tepi bawah (badan) / tepi samping (sayap),
        /// diameter, jenis. Untuk batang panjang yang dimensinya terlalu rapat di skala gambar.
        /// </summary>
        static void HoleTable(Database db, BlockTableRecord ps, FabPart part, double k, Point3d at)
        {
            if (part.Holes.Count == 0) return;
            var e = part.Ext;
            var rows = new List<string[]> { new[] { "No", "Dari ujung kiri", "Dari tepi", "Lubang", "Di" } };
            var groups = part.Holes
                .GroupBy(h => (x: Math.Round((h.C.X - e.MinPoint.X) / k), h.Kind, d: Math.Round(h.Dia)))
                .OrderBy(g => g.Key.x).ToList();
            int n = 0;
            foreach (var g in groups)
            {
                if (rows.Count > 24) { rows.Add(new[] { "", "+" + (groups.Count - n) + " posisi lagi", "", "", "" }); break; }
                n++;
                var across = g.Select(h => Math.Round(((g.Key.Kind == "sayap" ? h.C.Y - e.MinPoint.Y : h.C.Z - e.MinPoint.Z)) / k)).Distinct().OrderBy(v => v);
                rows.Add(new[] { n.ToString(CultureInfo.InvariantCulture), Fmt.N(g.Key.x), string.Join(" / ", across.Select(v => Fmt.N(v))),
                    g.Count() + " x " + (g.Key.Kind == "lonjong" ? "lonjong " : "Ø") + Fmt.N(g.Key.d), g.Key.Kind });
            }
            Commands.MakeTable(db, Matrix3d.Identity, at, "DAFTAR LUBANG " + part.Mark + " (mm)", rows, Std.H4 / k, ps.ObjectId);
        }

        /// <summary>Viewport garis tersembunyi pas ke batang; mengembalikan batas batang di kertas (x0, y0, x1, y1).</summary>
        static Viewport FabViewport(Transaction tr, Database db, BlockTableRecord ps, Point3d c, double w, double h, Extents3d e, Vector3d dir, double k,
            double? fixedScale, out double scale, out double[] rect)
        {
            var scales = fixedScale.HasValue ? Scales.Where(s => s >= fixedScale.Value).ToArray() : Scales;
            var vp = AddViewport(tr, db, ps, c, w, h, e, dir, k, out scale, true, scales, 0.94);
            var pts = new List<Point3d>();
            foreach (var a in new[] { e.MinPoint.X, e.MaxPoint.X }) foreach (var b in new[] { e.MinPoint.Y, e.MaxPoint.Y }) foreach (var z in new[] { e.MinPoint.Z, e.MaxPoint.Z }) pts.Add(ToPaper(vp, new Point3d(a, b, z)));
            rect = new[] { pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y) };
            vp.Width = Math.Min(w, rect[2] - rect[0] + 8); vp.Height = Math.Min(h, rect[3] - rect[1] + 8);
            vp.CustomScale = 1.0 / (scale * k);   // ubah ukuran viewport mengubah skala: kunci lagi
            return vp;
        }

        /// <summary>Dimensi: rantai posisi lubang dari ujung kiri + panjang total (bawah), posisi lubang melintang (kanan).</summary>
        static double FabDims(Transaction tr, Database db, BlockTableRecord ps, Viewport vp, double[] r, double scale, List<Point3d> holes, Extents3d e, double k, bool elev, out string gauge)
        {
            gauge = "";
            var used = new List<double[]>();
            var along = new List<(double p, double pos)> { (r[0], 0), (r[2], (e.MaxPoint.X - e.MinPoint.X) / k) };
            foreach (var h in holes)
            {
                double px = ToPaper(vp, h).X, pos = (h.X - e.MinPoint.X) / k;
                if (along.Any(q => Math.Abs(q.pos - pos) < 1)) continue;
                along.Add((px, pos));
            }
            double far = FabChain(tr, db, ps, along, r[1] - 2, r[1] - 6);
            if (holes.Count == 0) return far;
            // melintang: tinggi (tampak samping, sumbu Z) atau lebar (tampak atas, sumbu Y)
            double lo = elev ? e.MinPoint.Z : e.MinPoint.Y, hi = elev ? e.MaxPoint.Z : e.MaxPoint.Y;
            var across = new List<(double p, double pos)> { (r[1], 0), (r[3], (hi - lo) / k) };
            foreach (var h in holes)
            {
                double py = ToPaper(vp, h).Y, pos = ((elev ? h.Z : h.Y) - lo) / k;
                if (across.Any(q => Math.Abs(q.pos - pos) < 1)) continue;
                across.Add((py, pos));
            }
            if (r[3] - r[1] >= 14) DimChain(tr, db, ps, across, false, r[2] + 2, r[2] + 6, scale, r[1], r[3], used);
            else
            {
                // tampak terlalu tipis untuk dimensi melintang yang terbaca: tulis jarak dari tepi di catatan lubang
                var pos = across.Select(q => q.pos).OrderBy(v => v).ToList();
                gauge = "dari tepi " + string.Join(" / ", pos.Skip(1).Take(pos.Count - 2).Select(v => Fmt.N(Math.Round(v))));
            }
            return far;
        }

        /// <summary>
        /// Rantai dimensi memanjang dari ujung kiri: jarak yang sama berurutan digabung "6x110=660" (praktik gambar bengkel),
        /// angka yang bertumpuk pindah ke lapis bawah, lalu panjang total. Mengembalikan garis terbawah.
        /// </summary>
        static double FabChain(Transaction tr, Database db, BlockTableRecord ps, List<(double p, double pos)> pts, double origin, double line0)
        {
            const double th = 2.5;
            var s = pts.OrderBy(q => q.pos).ToList();
            var seg = new List<(int a, int b, string text)>();
            int i0 = 0;
            while (i0 < s.Count - 1)
            {
                double v = s[i0 + 1].pos - s[i0].pos;
                int j = i0 + 1;
                while (j + 1 < s.Count && Math.Abs((s[j + 1].pos - s[j].pos) - v) < 1) j++;
                int n = j - i0;
                string vt = Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
                seg.Add((i0, j, n >= 2 ? n + "x" + vt + "=" + Math.Round(s[j].pos - s[i0].pos).ToString("0", CultureInfo.InvariantCulture) : vt));
                i0 = j;
            }
            var tiers = DimTiers.Assign(seg.Select(q => (s[q.a].p, s[q.b].p, DimTiers.TextWidth(q.text, th))).ToList(), 0.6);
            for (int i = 0; i < seg.Count; i++)
                AddDim(tr, db, ps, new Point3d(s[seg[i].a].p, origin, 0), new Point3d(s[seg[i].b].p, origin, 0), new Point3d(0, line0 - 4.5 * tiers[i], 0), true, 0, th, seg[i].text);
            double outer = line0 - 4.5 * ((tiers.Length == 0 ? 0 : tiers.Max()) + 1);
            if (seg.Count > 1) AddDim(tr, db, ps, new Point3d(s.First().p, origin, 0), new Point3d(s.Last().p, origin, 0), new Point3d(0, outer, 0), true, s.Last().pos - s.First().pos, th);
            return (seg.Count > 1 ? outer : line0) - 4;
        }

        /// <summary>Satu leader per diameter & jenis lubang: "4 x Ø22".</summary>
        static void HoleCallouts(Transaction tr, BlockTableRecord ps, Viewport vp, double[] r, List<FabHole> holes, string gauge)
        {
            int i = 0;
            foreach (var g in holes.GroupBy(h => (h.Kind, Math.Round(h.Dia))))
            {
                var p = ToPaper(vp, g.OrderBy(h => h.C.X).First().C);
                string txt = g.Count() + " x " + (g.Key.Kind == "lonjong" ? "lonjong lebar " : "Ø") + Fmt.N(g.Key.Item2) + (string.IsNullOrEmpty(gauge) ? "" : ", " + gauge);
                double d = r[3] + 7 + 6 * i - p.Y;                       // kaki 45° sampai di atas tampak
                PaperLeader(tr, ps, p, new Point3d(p.X + d, p.Y + d, 0), 1, txt, Std.H3);
                i++;
            }
        }
    }
}
