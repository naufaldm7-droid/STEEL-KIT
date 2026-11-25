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
                var chainCount = new Dictionary<string, int>();
                foreach (var d in r.Dims.Where(d => d.View == vi))
                {
                    var a = v.P(d.A); var b = v.P(d.B);
                    var od = new Vec2(d.OffsetDir.Dot(v.R), d.OffsetDir.Dot(v.Up));
                    if (od.Length < 1e-6 || (b - a).Length < 1e-6) continue;
                    od = od.Unit;
                    // dimensi berantai yang pendek: selang-seling jaraknya supaya angka tidak bertumpuk
                    string ck = Math.Round(od.X, 2) + "|" + Math.Round(od.Y, 2) + "|" + Math.Round(d.Offset) + "|" + Math.Round((a + b).X * 0.5 * Math.Abs(od.X)) + "|" + Math.Round((a + b).Y * 0.5 * Math.Abs(od.Y));
                    chainCount.TryGetValue(ck, out int ci);
                    chainCount[ck] = ci + 1;
                    double extra = (b - a).Length * k < 3.5 * h && ci % 2 == 1 ? 2.2 * h / k : 0;
                    var mid = (a + b) * 0.5 + od * (d.Offset + extra);
                    var dim = new AlignedDimension(P(a), P(b), P(mid), d.Text ?? "", db.Dimstyle);
                    dim.SetDatabaseDefaults(db);
                    dim.Layer = Draw.LayDim;
                    dim.Dimscale = 1; dim.Dimtxt = h * 0.8; dim.Dimasz = h * 0.7; dim.Dimexo = h * 0.4; dim.Dimexe = h * 0.5; dim.Dimgap = h * 0.25; dim.Dimdec = 0;
                    dim.Dimlfac = 1 / k;
                    dim.Dimtad = 1; dim.Dimtih = false; dim.Dimtoh = false;
                    space.AppendEntity(dim); tr.AddNewlyCreatedDBObject(dim, true);
                }
                foreach (var n in r.Notes.Where(n => n.View == vi))
                {
                    var at = P(v.P(n.At));
                    var tp = new Point3d(at.X + n.Shift.X * h * 3, at.Y + n.Shift.Y * h * 3, at.Z);
                    var ld = new Line(at, tp) { Layer = Draw.LayText };
                    space.AppendEntity(ld); tr.AddNewlyCreatedDBObject(ld, true);
                    var mt = new MText
                    {
                        Contents = n.Text, Location = tp, TextHeight = h, Layer = Draw.LayText,
                        Attachment = n.Shift.X >= 0 ? (n.Shift.Y >= 0 ? AttachmentPoint.BottomLeft : AttachmentPoint.TopLeft) : (n.Shift.Y >= 0 ? AttachmentPoint.BottomRight : AttachmentPoint.TopRight),
                    };
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
            var rows = new List<string[]> { new[] { "Bahan", "Uraian", "Jml", "kg" } };
            foreach (var b in r.Boq) rows.Add(new[] { b.Item, b.Spec, b.Count.ToString(CultureInfo.CurrentCulture), b.TotalKg > 0 ? Draw.N(b.TotalKg, "0.0") : "-" });
            rows.Add(new[] { "TOTAL", "baja (tanpa grout/beton)", "", Draw.N(r.Boq.Sum(b => b.TotalKg), "0.0") });
            Commands.MakeTable(db, Matrix3d.Identity, new Point3d(x, top, basePt.Z), "DAFTAR BAHAN SAMBUNGAN", rows, h * 0.9 / k, space.ObjectId);
            double tw = Enumerable.Range(0, 4).Sum(ci => Math.Max(4, rows.Max(rw => rw[ci].Length)) * h * 0.9 * 0.85 + h * 0.9 * 1.6);
            x += tw;
            LastBounds = new[] { basePt.X, Math.Min(basePt.Y, top - (rows.Count + 2) * h * 0.9 * 2.0), x, top + 9 * h };
            return x - basePt.X;
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
