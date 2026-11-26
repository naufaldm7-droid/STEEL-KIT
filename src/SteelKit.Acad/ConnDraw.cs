using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using SteelKit.Core;

namespace SteelKit.Acad
{
    /// <summary>Menggambar sambungan: blok 3D, potong batang, dan detail 2D (tampak + dimensi + catatan + daftar bahan).</summary>
    public static class ConnDraw
    {
        public const string LayConn = "SK-SAMBUNGAN";
        public const string LayDetail = "SK-DETAIL";
        public const string LayDetailMember = "SK-DETAIL-BATANG";
        public const string LayDetailConcrete = "SK-DETAIL-BETON";

        static short ColorOf(PartKind k)
        {
            switch (k)
            {
                case PartKind.Baut: case PartKind.Mur: case PartKind.Ring: return 8;
                case PartKind.Angkur: return 6;
                case PartKind.Grout: return 9;
                case PartKind.Beton: return 252;
                case PartKind.Batang: return 4;
                case PartKind.Las: return 200;
                default: return 30;
            }
        }

        static void Layers(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var (name, color) in new[] { (LayConn, (short)30), (LayDetail, (short)7), (LayDetailMember, (short)8), (LayDetailConcrete, (short)252) })
            {
                if (lt.Has(name)) continue;
                lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            foreach (var l in new[] { Draw.LayDim, Draw.LayText }) Draw.Layer(tr, db, l);
        }

        static Matrix3d PartMatrix(Part p, double k)
        {
            var o = p.F.Point(0, 0, p.Z0) * k;
            return Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, Draw.P(o), Draw.D(p.F.U), Draw.D(p.F.V), Draw.D(p.F.W));
        }

        /// <summary>Blok anonim berisi semua benda sambungan (koordinat lokal sambungan, satuan gambar).</summary>
        public static ObjectId Block(Transaction tr, Database db, ConnResult r, bool withStubs)
        {
            double k = Draw.UnitK(db);
            Layers(tr, db);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var btr = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            foreach (var p in r.Parts)
            {
                if (p.Kind == PartKind.Batang && !withStubs) continue;
                var s = new SectionShape { Outer = p.Outline };
                s.Holes.AddRange(p.Holes);
                try
                {
                    var sol = new Solid3d();
                    sol.SetDatabaseDefaults(db);
                    using (var reg = Draw.Region(s, k)) sol.Extrude(reg, p.Thickness * k, 0);
                    sol.TransformBy(PartMatrix(p, k));
                    sol = ClipSolid(sol, p, k);
                    if (sol == null) continue;
                    sol.Layer = "0";
                    sol.ColorIndex = ColorOf(p.Kind);
                    btr.AppendEntity(sol);
                    tr.AddNewlyCreatedDBObject(sol, true);
                }
                catch { /* benda gagal dibentuk dilewati, sambungan lain tetap jalan */ }
            }
            return id;
        }

        /// <summary>
        /// Potong solid dengan bidang Part.Keep (potongan tegak ujung batang, ujung sayap haunch).
        /// Sisi yang dipertahankan ditentukan dari titik berat tiap belahan, jadi tidak bergantung arah normal versi API.
        /// </summary>
        static Solid3d ClipSolid(Solid3d sol, Part p, double k)
        {
            foreach (var pl in p.Keep)
            {
                var plane = new Autodesk.AutoCAD.Geometry.Plane(Draw.P(pl.P * k), Draw.D(pl.N));
                Func<Solid3d, double> side = x => { var c = x.MassProperties.Centroid; return pl.Dist(new Vec3(c.X, c.Y, c.Z) * (1 / k)); };
                Solid3d other = null;
                try { other = sol.Slice(plane, true); }
                catch { other = null; }
                if (other == null)
                {
                    // bidang tidak memotong: buang seluruhnya bila berada di sisi yang dibuang
                    if (side(sol) < 0) { sol.Dispose(); return null; }
                    continue;
                }
                if (side(sol) < side(other)) { sol.Dispose(); sol = other; }
                else other.Dispose();
            }
            return sol;
        }

        /// <summary>Frame sambungan dalam satuan gambar (asal dikali k, sumbu tetap).</summary>
        public static Matrix3d World(Frame fMm, double k)
            => Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, Draw.P(fMm.O * k), Draw.D(fMm.U), Draw.D(fMm.V), Draw.D(fMm.W));

        public static Frame FrameOf(BlockReference br, double k)
        {
            var t = br.BlockTransform;
            var cs = t.CoordinateSystem3d;
            return new Frame(Draw.V(cs.Origin) * (1 / k), Draw.V(cs.Xaxis.GetNormal()), Draw.V(cs.Yaxis.GetNormal()), Draw.V(cs.Zaxis.GetNormal()));
        }

        public static ObjectId Insert(Transaction tr, Database db, BlockTableRecord space, Connection c, ConnResult r, Frame fMm, string[] links)
        {
            double k = Draw.UnitK(db);
            var br = new BlockReference(Point3d.Origin, Block(tr, db, r, links == null || links.All(string.IsNullOrEmpty)));
            br.TransformBy(World(fMm, k));
            br.Layer = LayConn;
            var id = space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            Draw.TagConnection(tr, br, c, r.Boq.Sum(b => b.TotalKg), links);
            return id;
        }

        /// <summary>Ganti isi sambungan yang sudah ada (setelah parameter diubah), frame tetap.</summary>
        public static void Replace(Transaction tr, BlockReference br, Connection c, ConnResult r, string[] links)
        {
            br.UpgradeOpen();
            br.BlockTableRecord = Block(tr, br.Database, r, links == null || links.All(string.IsNullOrEmpty));
            Draw.TagConnection(tr, br, c, r.Boq.Sum(b => b.TotalKg), links);
        }

        /// <summary>Pindahkan ujung batang ke titik potong sambungan (titik as tengah, mm lokal sambungan).</summary>
        public static bool TrimMember(Transaction tr, Database db, string handle, string end, Vec3? localMm, Frame fMm, IEnumerable<Cutter> cuts = null, Vec3? shiftMm = null)
        {
            if (string.IsNullOrEmpty(handle)) return false;
            if (!localMm.HasValue && (cuts == null || !cuts.Any()) && !shiftMm.HasValue) return false;
            double k = Draw.UnitK(db);
            if (!db.TryGetObjectId(new Handle(Convert.ToInt64(handle, 16)), out ObjectId id) || id.IsErased) return false;
            var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
            var t = ent == null ? null : Draw.ReadTag(tr, ent);
            if (t?.Spec == null) return false;
            var m = MemberBuild.Copy(t.Spec);
            if (shiftMm.HasValue)
            {
                var d = Draw.D(fMm.Dir(shiftMm.Value) * k);
                m.S += d; m.E += d;
            }
            var g = m.Geom(k, handle);
            if (localMm.HasValue && (end == "S" || end == "E"))
            {
                var center = fMm.Point(localMm.Value);
                var axis = center - g.CenterOffset;
                var p = Draw.P(axis * k);
                if (end == "S") m.S = p; else m.E = p;
                if (m.S.DistanceTo(m.E) < 1e-6) return false;
            }
            if (cuts != null)
            {
                // pemotong dari sambungan ini menggantikan pemotong lama yang sama posisinya
                foreach (var c in cuts)
                {
                    var w = MemberBuild.ToWorld(c, fMm, k);
                    m.Cuts.RemoveAll(x => (Draw.P(x.F.O) - Draw.P(w.F.O)).Length < 1e-3 * Math.Max(1, k * 1000) && x.Cylinder == w.Cylinder);
                    m.Cuts.Add(w);
                }
            }
            MemberBuild.Apply(tr, ent, m);
            return true;
        }

        // ======================================================================== detail 2D
        public static double TextHeight(ConnResult r, double k)
        {
            var bb = r.Bounds();
            double size = Math.Max(bb[3] - bb[0], Math.Max(bb[4] - bb[1], bb[5] - bb[2]));
            return Math.Max(size / 45, 8) * k;
        }

        /// <summary>Gambar detail 2D di bidang XY dunia mulai dari titik dasar (kiri bawah). Mengembalikan lebar yang dipakai.</summary>
        /// <summary>Batas gambar detail terakhir (x0, y0, x1, y1) untuk viewport lembar detail.</summary>
        public static double[] LastBounds;
        /// <summary>Batas termasuk catatan yang menjorok keluar (untuk ukuran viewport bila muat).</summary>
        public static double[] LastBoundsAll;

        public static double Detail(Transaction tr, Database db, BlockTableRecord space, Connection c, Point3d basePt, double textHeight = 0)
        {
            double k = Draw.UnitK(db);
            Layers(tr, db);
            var r = c.Build();
            double h = textHeight > 0 ? textHeight : TextHeight(r, k);
            double x = basePt.X;
            double top = basePt.Y;
            var viewShapes = r.Views.Select(v => Projection.Project(r, v)).ToList();
            double maxH = 0;
            var boxes = new List<double[]>();
            foreach (var sh in viewShapes)
            {
                var pts = sh.SelectMany(s => s.IsCircle ? new[] { s.Center + new Vec2(-s.Radius, -s.Radius), s.Center + new Vec2(s.Radius, s.Radius) } : s.Pts.ToArray()).ToList();
                var bx = new[] { pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y) };
                boxes.Add(bx);
                maxH = Math.Max(maxH, (bx[3] - bx[1]) * k);
            }
            double gap = 12 * h;
            double y0 = basePt.Y + 8 * h;           // ruang judul tampak di bawah
            var rows = new List<string[]> { new[] { "Bahan", "Uraian", "Jml", "kg" } };
            foreach (var b in r.Boq) rows.Add(new[] { b.Item, b.Spec, b.Count.ToString(CultureInfo.CurrentCulture), b.TotalKg > 0 ? Draw.N(b.TotalKg, "0.0") : "-" });
            rows.Add(new[] { "TOTAL", "baja (tanpa grout/beton)", "", Draw.N(r.Boq.Sum(b => b.TotalKg), "0.0") });
            double tw = Enumerable.Range(0, 4).Sum(ci => Math.Max(4, rows.Max(rw => rw[ci].Length)) * h * 0.9 * 0.85 + h * 0.9 * 1.6);
            double tableH = (rows.Count + 2) * h * 0.9 * 2.0;
            // area terlarang untuk catatan: gambar tampak lain (+ ruang dimensi), judul tampak, tabel bahan
            var blocked = new List<(int vi, double[] r)>();
            double limX0 = basePt.X, limX1 = basePt.X, limY0 = basePt.Y - 6 * h;
            {
                double px = x, ptop = top;
                for (int vi = 0; vi < r.Views.Count; vi++)
                {
                    var bb = boxes[vi];
                    double w = (bb[2] - bb[0]) * k, ht = (bb[3] - bb[1]) * k, gx = px + 4 * h, gy = y0 + 4 * h;
                    blocked.Add((vi, new[] { gx - 3 * h, gy - 3 * h, gx + w + 3 * h, gy + ht + 3 * h }));
                    double tcx = gx + w / 2, ttw = DimTiers.TextWidth(r.Views[vi].Title, h * 1.4) / 2 + h;
                    blocked.Add((-1, new[] { tcx - ttw, y0 - 2 * h, tcx + ttw, y0 + 0.3 * h }));
                    px += w + gap + 8 * h;
                    ptop = Math.Max(ptop, gy + ht);
                }
                blocked.Add((-1, new[] { px - h, ptop - tableH, px + tw, ptop + 2 * h }));
                limX1 = px + tw;
                blocked.Add((-1, new[] { basePt.X + 3 * h, ptop + 4 * h, basePt.X + 5 * h + DimTiers.TextWidth(Summary(r), h * 1.2), ptop + 10 * h }));
            }
            var used = new List<double[]>();
            for (int vi = 0; vi < r.Views.Count; vi++)
            {
                var v = r.Views[vi]; var bx = boxes[vi];
                double ox = x + 4 * h - bx[0] * k, oy = y0 + 4 * h - bx[1] * k;
                Func<Vec2, Point3d> P = q => new Point3d(ox + q.X * k, oy + q.Y * k, basePt.Z);
                foreach (var s in viewShapes[vi])
                {
                    string lay = s.Kind == PartKind.Batang ? LayDetailMember : s.Kind == PartKind.Beton || s.Kind == PartKind.Grout ? LayDetailConcrete : LayDetail;
                    Entity e;
                    if (s.IsCircle) e = new Circle(P(s.Center), Vector3d.ZAxis, s.Radius * k);
                    else
                    {
                        var pl = new Polyline();
                        for (int i = 0; i < s.Pts.Count; i++) pl.AddVertexAt(i, new Point2d(P(s.Pts[i]).X, P(s.Pts[i]).Y), 0, 0, 0);
                        pl.Closed = true;
                        pl.Elevation = basePt.Z;
                        e = pl;
                    }
                    e.Layer = lay;
                    space.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true);
                    if (s.Kind == PartKind.Beton && !s.IsCircle) HatchIt(tr, space, (Polyline)e, "AR-CONC", h * 0.02);
                }
                // dimensi: angka yang bertumpuk dengan dimensi lain di garis yang (hampir) sama digeser ke lapis luar
                var dl = new List<(Vec2 a, Vec2 b, Vec2 od, double off, string text, string key, double line, double ta, double tb)>();
                foreach (var d in r.Dims.Where(d => d.View == vi))
                {
                    var a = v.P(d.A); var b = v.P(d.B);
                    var od = new Vec2(d.OffsetDir.Dot(v.R), d.OffsetDir.Dot(v.Up));
                    if (od.Length < 1e-6 || (b - a).Length < 1e-6) continue;
                    od = od.Unit;
                    var t = new Vec2(-od.Y, od.X);
                    double line = (Vec2.Dot((a + b) * 0.5, od) + d.Offset) * k;
                    string key = Math.Round(od.X, 2) + "|" + Math.Round(od.Y, 2);
                    string txt = d.Text ?? Math.Round((b - a).Length).ToString("0", CultureInfo.InvariantCulture);
                    double s0 = Math.Min(Vec2.Dot(a, t), Vec2.Dot(b, t)) * k, s1 = Math.Max(Vec2.Dot(a, t), Vec2.Dot(b, t)) * k;
                    double dtw = DimTiers.TextWidth(txt, h * 0.8), mid = (s0 + s1) / 2, half = dtw / 2 + h * 0.3;
                    // angka muat di antara garis ekstensi: hanya rentang angka; tidak muat: rentang dimensi + angka
                    double ta = dtw + h * 0.6 <= s1 - s0 ? mid - half : Math.Min(s0, mid - half), tb = dtw + h * 0.6 <= s1 - s0 ? mid + half : Math.Max(s1, mid + half);
                    dl.Add((a, b, od, d.Offset, d.Text, key, line, ta, tb));
                }
                var placedDims = new List<(string key, double line, double ta, double tb)>();
                foreach (var q in dl.OrderBy(q => q.key).ThenBy(q => q.line).ThenBy(q => q.ta))
                {
                    int tier = 0;
                    while (tier < 8 && placedDims.Any(p => p.key == q.key && Math.Abs(p.line - (q.line + tier * 2.0 * h)) < 1.3 * h && q.ta < p.tb && q.tb > p.ta)) tier++;
                    placedDims.Add((q.key, q.line + tier * 2.0 * h, q.ta, q.tb));
                    var mid = (q.a + q.b) * 0.5 + q.od * (q.off + tier * 2.0 * h / k);
                    var dim = new AlignedDimension(P(q.a), P(q.b), P(mid), q.text ?? "", db.Dimstyle);
                    dim.SetDatabaseDefaults(db);
                    dim.Layer = Draw.LayDim;
                    dim.Dimscale = 1; dim.Dimtxt = h * 0.8; dim.Dimasz = h * 0.7; dim.Dimexo = h * 0.4; dim.Dimexe = h * 0.5; dim.Dimgap = h * 0.25; dim.Dimdec = 0;
                    dim.Dimlfac = 1 / k;
                    dim.Dimtad = 1; dim.Dimtih = false; dim.Dimtoh = false; dim.Dimtix = true;
                    space.AppendEntity(dim); tr.AddNewlyCreatedDBObject(dim, true);
                }
                // catatan & simbol las: tidak boleh menimpa catatan lain, tampak lain, judul, atau tabel.
                // Coba geser lutut menjauh (vertikal), lalu sisi sebaliknya; bila tetap bentrok, pakai posisi asal.
                foreach (var n in r.Notes.Where(n => n.View == vi))
                {
                    var at = P(v.P(n.At));
                    var weld = WeldMark.Parse(n.Text);
                    int sx0 = n.Shift.X >= 0 ? 1 : -1, sy = n.Shift.Y >= 0 ? 1 : -1, sx = sx0;
                    Point3d tp = at; double[] box = null; bool ok = false;
                    int myVi = vi;
                    bool Free(double[] bb) => bb[0] >= limX0 && bb[2] <= limX1 && bb[1] >= limY0 && !used.Any(u => bb[0] < u[2] && bb[2] > u[0] && bb[1] < u[3] && bb[3] > u[1])
                        && !blocked.Any(q => q.vi != myVi && bb[0] < q.r[2] && bb[2] > q.r[0] && bb[1] < q.r[3] && bb[3] > q.r[1]);
                    foreach (int side in new[] { sx0, -sx0 })
                    {
                        for (int tryI = 0; tryI < 14 && !ok; tryI++)
                        {
                            var cand = new Point3d(at.X + side * Math.Abs(n.Shift.X) * h * 3, at.Y + n.Shift.Y * h * 3 + sy * tryI * 1.8 * h, at.Z);
                            var cb = weld != null ? WeldExtent(cand, side, weld, h) : NoteExtent(cand, side, sy, n.Text, h);
                            if (Free(cb)) { tp = cand; box = cb; sx = side; ok = true; }
                        }
                        if (ok) break;
                    }
                    if (!ok)
                    {
                        tp = new Point3d(at.X + n.Shift.X * h * 3, at.Y + n.Shift.Y * h * 3, at.Z); sx = sx0;
                        box = weld != null ? WeldExtent(tp, sx, weld, h) : NoteExtent(tp, sx, sy, n.Text, h);
                    }
                    used.Add(box);
                    if (weld != null) { WeldSymbol(tr, space, at, tp, sx, weld, h); continue; }
                    var ld = new Line(at, tp) { Layer = Draw.LayText };
                    space.AppendEntity(ld); tr.AddNewlyCreatedDBObject(ld, true);
                    var mt = LayoutCommands.Masked(new MText
                    {
                        Contents = n.Text, Location = tp, TextHeight = h, Layer = Draw.LayText,
                        Attachment = sx > 0 ? (sy > 0 ? AttachmentPoint.BottomLeft : AttachmentPoint.TopLeft) : (sy > 0 ? AttachmentPoint.BottomRight : AttachmentPoint.TopRight),
                    });
                    space.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                }
                var title = new MText
                {
                    Contents = "{\\L" + v.Title + "}", Location = new Point3d(x + 4 * h + (bx[2] - bx[0]) * k / 2, y0, basePt.Z),
                    TextHeight = h * 1.4, Attachment = AttachmentPoint.TopCenter, Layer = Draw.LayText,
                };
                space.AppendEntity(title); tr.AddNewlyCreatedDBObject(title, true);
                x += (bx[2] - bx[0]) * k + gap + 8 * h;
                top = Math.Max(top, oy + (bx[3] - bx[1]) * k);
            }
            // judul + ringkasan cek + daftar bahan
            var head = new MText
            {
                Contents = "{\\L DETAIL " + c.Title.ToUpperInvariant() + "}\\P" + Summary(r),
                Location = new Point3d(basePt.X + 4 * h, top + 5 * h, basePt.Z), TextHeight = h * 1.2, Attachment = AttachmentPoint.BottomLeft, Layer = Draw.LayText,
            };
            space.AppendEntity(head); tr.AddNewlyCreatedDBObject(head, true);
            Commands.MakeTable(db, Matrix3d.Identity, new Point3d(x, top, basePt.Z), "DAFTAR BAHAN SAMBUNGAN", rows, h * 0.9 / k, space.ObjectId);
            x += tw;
            // batas inti (untuk memilih skala) dan batas termasuk catatan yang menjorok keluar (untuk viewport bila muat)
            double bx0 = basePt.X, by0 = Math.Min(basePt.Y, top - tableH), bx1 = x, by1 = top + 9 * h;
            LastBounds = new[] { bx0, by0, bx1, by1 };
            foreach (var u in used) { bx0 = Math.Min(bx0, u[0] - h); by0 = Math.Min(by0, u[1] - h); bx1 = Math.Max(bx1, u[2] + h); by1 = Math.Max(by1, u[3] + h); }
            LastBoundsAll = new[] { bx0, by0, bx1, by1 };
            return x - basePt.X;
        }

        static string[] Lines(string t) => string.IsNullOrEmpty(t) ? new string[0] : t.Split(new[] { "\\P" }, StringSplitOptions.None);

        /// <summary>Kotak perkiraan teks catatan biasa (untuk cek tumpang tindih).</summary>
        static double[] NoteExtent(Point3d tp, int sx, int sy, string text, double h)
        {
            var ls = Lines(text);
            double w = ls.Length == 0 ? 0 : ls.Max(l => DimTiers.TextWidth(l, h)), ht = Math.Max(1, ls.Length) * 1.67 * h;
            double xa = sx > 0 ? tp.X : tp.X - w, ya = sy > 0 ? tp.Y : tp.Y - ht;
            return new[] { xa, ya, xa + w, ya + ht };
        }

        static double WeldRefLength(WeldMark w, double h)
        {
            double sizeW = DimTiers.TextWidth(w.SizeText, h * 0.8);
            double lenW = w.Length > 0 ? DimTiers.TextWidth(Fmt.N(w.Length), h * 0.8) + h * 0.6 : 0;
            return Math.Max(8 * h, sizeW + lenW + 4.5 * h);
        }

        /// <summary>Kotak perkiraan simbol las + ekor + sisa catatan.</summary>
        static double[] WeldExtent(Point3d tp, int sgn, WeldMark w, double h)
        {
            double s = 1.1 * h, lr = WeldRefLength(w, h);
            double far = lr + (w.Tail.Length > 0 ? 1.3 * h + DimTiers.TextWidth(w.Tail, h * 0.8) : 0);
            double y0 = tp.Y - s - 0.3 * h, y1 = tp.Y + (w.BothSides ? s + 0.3 * h : 0.8 * h);
            var ls = Lines(w.Rest);
            if (ls.Length > 0)
            {
                double rw = ls.Max(l => DimTiers.TextWidth(l, h)) + 0.3 * h, rh = ls.Length * 1.67 * h;
                far = Math.Max(far, rw);
                if (w.BothSides) y0 = tp.Y - s - 0.6 * h - rh; else y1 = tp.Y + 0.7 * h + rh;
            }
            double x1 = tp.X + sgn * far;
            return new[] { Math.Min(tp.X, x1), y0, Math.Max(tp.X, x1), y1 };
        }

        /// <summary>
        /// Simbol las praktik ISO 2553 sistem B (simbol di bawah garis referensi = sisi panah): panah, garis referensi,
        /// segitiga las sudut / alur miring CJP, ukuran leher "a", lingkaran keliling, ekor berisi keterangan.
        /// Sisa catatan (bukan las) ditulis di atas garis referensi.
        /// </summary>
        static void WeldSymbol(Transaction tr, BlockTableRecord space, Point3d at, Point3d tp, int sgn, WeldMark w, double h)
        {
            double z = at.Z;
            void Add(Entity e) { e.Layer = Draw.LayText; space.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); }
            void Ln(double x0, double y0, double x1, double y1) => Add(new Line(new Point3d(x0, y0, z), new Point3d(x1, y1, z)));
            void Tx(string s, double x0, double y0, double th, AttachmentPoint ap) => Add(LayoutCommands.Masked(new MText { Contents = s, Location = new Point3d(x0, y0, z), TextHeight = th, Attachment = ap }));
            // panah (segitiga penuh) dan garis panah
            var dir = tp - at; double len = dir.Length;
            if (len > 1e-9)
            {
                var u = dir / len; var nrm = new Vector3d(-u.Y, u.X, 0);
                var tip = at; var bse = at + u * h * 0.9;
                Add(new Solid(tip, bse + nrm * h * 0.25, bse - nrm * h * 0.25));
                Add(new Line(at, tp));
            }
            string size = w.SizeText;
            double sizeW = DimTiers.TextWidth(size, h * 0.8);
            double lenW = w.Length > 0 ? DimTiers.TextWidth(Fmt.N(w.Length), h * 0.8) + h * 0.6 : 0;
            double lr = WeldRefLength(w, h);
            double y = tp.Y, x0 = tp.X, x1 = tp.X + sgn * lr;
            Ln(x0, y, x1, y);
            // posisi simbol: setelah teks ukuran, dihitung dari sisi panah
            // ukuran selalu di kiri simbol dan panjang di kanannya, jadi jarak dari sisi panah tergantung arah garis
            double xs = x0 + sgn * (1.2 * h + (sgn > 0 ? sizeW : lenW) + 0.9 * h);
            double s = h * 1.1;
            for (int side = -1; side <= (w.BothSides ? 1 : -1); side += 2)
            {
                double yy = y + side * s;
                if (w.Cjp)
                {
                    // alur miring (bevel): kaki tegak kiri + kaki miring melebar menjauh dari garis
                    Ln(xs - 0.5 * s, y, xs - 0.5 * s, yy);
                    Ln(xs - 0.5 * s, y, xs + 0.5 * s, yy);
                }
                else
                {
                    // las sudut: segitiga dengan kaki tegak di kiri (aturan gambar, tidak dicerminkan)
                    var tri = new Polyline();
                    tri.AddVertexAt(0, new Point2d(xs - 0.5 * s, y), 0, 0, 0);
                    tri.AddVertexAt(1, new Point2d(xs - 0.5 * s, yy), 0, 0, 0);
                    tri.AddVertexAt(2, new Point2d(xs + 0.5 * s, y), 0, 0, 0);
                    tri.Closed = true; tri.Elevation = z;
                    Add(tri);
                }
                if (size.Length > 0) Tx(size, xs - 0.5 * s - 0.4 * h, y + side * s * 0.5, h * 0.8, AttachmentPoint.MiddleRight);
                if (w.Length > 0) Tx(Fmt.N(w.Length), xs + 0.5 * s + 0.4 * h, y + side * s * 0.5, h * 0.8, AttachmentPoint.MiddleLeft);
            }
            if (w.AllAround) Add(new Circle(new Point3d(x0, y, z), Vector3d.ZAxis, h * 0.45));
            if (w.Tail.Length > 0)
            {
                Ln(x1, y, x1 + sgn * 0.8 * h, y + 0.8 * h);
                Ln(x1, y, x1 + sgn * 0.8 * h, y - 0.8 * h);
                Tx(w.Tail, x1 + sgn * 1.3 * h, y, h * 0.8, sgn > 0 ? AttachmentPoint.MiddleLeft : AttachmentPoint.MiddleRight);
            }
            if (w.Rest.Length > 0)
            {
                // di atas garis bila sisi atas kosong; bila las dua sisi, catatan di bawah simbol
                double ry = w.BothSides ? y - s - 0.6 * h : y + (w.AllAround ? 0.7 * h : 0.5 * h);
                Tx(w.Rest, x0 + sgn * 0.3 * h, ry, h, w.BothSides ? (sgn > 0 ? AttachmentPoint.TopLeft : AttachmentPoint.TopRight) : (sgn > 0 ? AttachmentPoint.BottomLeft : AttachmentPoint.BottomRight));
            }
        }

        public static string Summary(ConnResult r)
        {
            int bad = r.Checks.Count(c => !c.Ok);
            return bad == 0
                ? "Cek aturan jarak/ukuran minimum: semua " + r.Checks.Count + " memenuhi (praktik umum, bukan cek kekuatan)."
                : "PERHATIAN: " + bad + " dari " + r.Checks.Count + " aturan jarak/ukuran minimum tidak terpenuhi: " + string.Join("; ", r.Checks.Where(c => !c.Ok).Select(c => c.Item));
        }

        static void HatchIt(Transaction tr, BlockTableRecord space, Polyline boundary, string pattern, double scale)
        {
            try
            {
                var h = new Hatch();
                h.SetDatabaseDefaults(space.Database);
                h.Layer = LayDetailConcrete;
                space.AppendEntity(h); tr.AddNewlyCreatedDBObject(h, true);
                h.SetHatchPattern(HatchPatternType.PreDefined, pattern);
                h.PatternScale = Math.Max(scale, 0.01);
                h.Associative = false;
                h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { boundary.ObjectId });
                h.EvaluateHatch(true);
            }
            catch { }
        }
    }
}
