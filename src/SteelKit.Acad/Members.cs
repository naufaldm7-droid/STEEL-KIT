using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Polyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using Autodesk.AutoCAD.Runtime;
using SteelKit.Core;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using MemberSpec = SteelKit.Acad.Draw.MemberSpec;

[assembly: CommandClass(typeof(SteelKit.Acad.MemberCommands))]

namespace SteelKit.Acad
{
    /// <summary>Membangun batang 3D (solid) dan 2D (blok anonim) dari MemberSpec; dipakai saat membuat, pratinjau, dan edit.</summary>
    public static class MemberBuild
    {
        public static double Density => Store.Density;

        /// <summary>Isi geometri solid (membuat ulang badan solid yang sudah ada).</summary>
        public static void Shape3D(Solid3d sol, MemberSpec m, double k)
        {
            var dir = m.E - m.S;
            var shift = m.Profile.AnchorShift(m.Anchor);
            var shape = m.Profile.BuildShape().Translated(shift.X, shift.Y);
            using (var reg = Draw.Region(shape, k)) sol.Extrude(reg, dir.Length, 0);
            Draw.MemberAxes(dir, m.Roll, out Vector3d x, out Vector3d y, out Vector3d z);
            sol.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, m.S, x, y, z));
            foreach (var c in m.Cuts)
            {
                try
                {
                    using (var cut = CutterSolid(c))
                        sol.BooleanOperation(BooleanOperationType.BoolSubtract, cut);
                }
                catch { /* pemotong yang tidak mengenai batang dilewati */ }
            }
        }

        /// <summary>Solid pemotong dari data Cutter (koordinat dunia, satuan gambar).</summary>
        public static Solid3d CutterSolid(Cutter c)
        {
            var s = new Solid3d();
            if (c.Cylinder)
            {
                double h = c.Z1 - c.Z0;
                s.CreateFrustum(h, c.Radius, c.Radius, c.Radius);
                s.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, c.Z0 + h / 2)));
            }
            else
            {
                s.CreateBox(c.X1 - c.X0, c.Y1 - c.Y0, c.Z1 - c.Z0);
                s.TransformBy(Matrix3d.Displacement(new Vector3d((c.X0 + c.X1) / 2, (c.Y0 + c.Y1) / 2, (c.Z0 + c.Z1) / 2)));
            }
            s.TransformBy(Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, Draw.P(c.F.O), Draw.D(c.F.U), Draw.D(c.F.V), Draw.D(c.F.W)));
            return s;
        }

        /// <summary>Pemotong dari koordinat lokal sambungan (mm) ke dunia (satuan gambar).</summary>
        public static Cutter ToWorld(Cutter c, Frame connMm, double k)
        {
            var f = connMm.Compose(c.F);
            return new Cutter
            {
                Cylinder = c.Cylinder, F = new Frame(f.O * k, f.U, f.V, f.W),
                X0 = c.X0 * k, X1 = c.X1 * k, Y0 = c.Y0 * k, Y1 = c.Y1 * k, Z0 = c.Z0 * k, Z1 = c.Z1 * k, Radius = c.Radius * k,
            };
        }

        public static ObjectId Create3D(Transaction tr, Database db, BlockTableRecord space, MemberSpec m)
        {
            double k = Draw.UnitK(db);
            if (m.S.DistanceTo(m.E) < 1e-6) return ObjectId.Null;
            Draw.Layer(tr, db, Draw.LayMember3D);
            var sol = new Solid3d();
            sol.SetDatabaseDefaults(db);
            Shape3D(sol, m, k);
            sol.Layer = Draw.LayMember3D;
            var id = space.AppendEntity(sol);
            tr.AddNewlyCreatedDBObject(sol, true);
            m.Kind = "batang3d";
            Draw.TagMember(tr, sol, m, k, Density);
            return id;
        }

        /// <summary>Garis tampak 2D dalam koordinat blok: X sepanjang batang (0..L), Y = arah normal tampak.</summary>
        public static List<Entity> Lines2D(MemberSpec m, double k, double L)
        {
            var p = m.Profile;
            double half = p.Extent(m.TopView) / 2, sh = 0;
            switch (m.Anchor)
            {
                case Anchor.Atas: sh = -half; break;
                case Anchor.Bawah: sh = half; break;
                case Anchor.TitikBerat: var pr = p.Properties(); sh = m.TopView ? -pr.Cx : -pr.Cy; break;
            }
            var list = new List<Entity>();
            var outline = new Polyline();
            outline.AddVertexAt(0, new Point2d(0, (-half + sh) * k), 0, 0, 0);
            outline.AddVertexAt(1, new Point2d(L, (-half + sh) * k), 0, 0, 0);
            outline.AddVertexAt(2, new Point2d(L, (half + sh) * k), 0, 0, 0);
            outline.AddVertexAt(3, new Point2d(0, (half + sh) * k), 0, 0, 0);
            outline.Closed = true;
            outline.Layer = Draw.LayMember2D;
            list.Add(outline);
            var lines = p.ElevationLines(m.TopView);
            for (int j = 2; j < lines.Count; j++)
                list.Add(new Line(new Point3d(0, (lines[j].Key + sh) * k, 0), new Point3d(L, (lines[j].Key + sh) * k, 0)) { Layer = lines[j].Value ? Draw.LayHidden : Draw.LayMember2D });
            double ext = Math.Max(half * 0.6, 20) * k;
            list.Add(new Line(new Point3d(-ext, 0, 0), new Point3d(L + ext, 0, 0)) { Layer = Draw.LayAxis });
            return list;
        }

        public static Matrix3d Place2D(MemberSpec m)
        {
            var x = (m.E - m.S).GetNormal();
            var z = m.Normal.GetNormal();
            if (Math.Abs(x.DotProduct(z)) > 1e-6) z = (z - x * x.DotProduct(z)).GetNormal();
            var y = z.CrossProduct(x).GetNormal();
            return Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, m.S, x, y, z);
        }

        static ObjectId NewBlock2D(Transaction tr, Database db, MemberSpec m)
        {
            foreach (var l in new[] { Draw.LayMember2D, Draw.LayHidden, Draw.LayAxis }) Draw.Layer(tr, db, l);
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var btr = new BlockTableRecord { Name = "*U", Origin = Point3d.Origin };
            var id = bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);
            foreach (var e in Lines2D(m, Draw.UnitK(db), m.S.DistanceTo(m.E)))
            {
                btr.AppendEntity(e);
                tr.AddNewlyCreatedDBObject(e, true);
            }
            return id;
        }

        public static ObjectId Create2D(Transaction tr, Database db, BlockTableRecord space, MemberSpec m)
        {
            double k = Draw.UnitK(db);
            if (m.S.DistanceTo(m.E) < 1e-6) return ObjectId.Null;
            m.Kind = "batang2d";
            var br = new BlockReference(Point3d.Origin, NewBlock2D(tr, db, m));
            br.TransformBy(Place2D(m));
            br.Layer = Draw.LayMember2D;
            var id = space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            Draw.TagMember(tr, br, m, k, Density);
            return id;
        }

        public static void Rebuild2D(Transaction tr, BlockReference br, MemberSpec m)
        {
            var db = br.Database;
            br.BlockTableRecord = NewBlock2D(tr, db, m);
            br.TransformBy(Place2D(m) * br.BlockTransform.Inverse());
        }

        /// <summary>Terapkan spec baru ke batang yang ada (3D atau 2D) dan perbarui datanya.</summary>
        public static void Apply(Transaction tr, Entity ent, MemberSpec m)
        {
            double k = Draw.UnitK(ent.Database);
            if (!ent.IsWriteEnabled) ent.UpgradeOpen();
            if (ent is Solid3d sol) { m.Kind = "batang3d"; Shape3D(sol, m, k); }
            else if (ent is BlockReference br) { m.Kind = "batang2d"; Rebuild2D(tr, br, m); }
            else return;
            Draw.TagMember(tr, ent, m, k, Density);
        }

        public static MemberSpec Copy(MemberSpec m) => new MemberSpec
        {
            Kind = m.Kind, Profile = m.Profile, S = m.S, E = m.E, Anchor = m.Anchor, Roll = m.Roll, TopView = m.TopView, Normal = m.Normal,
            Cuts = m.Cuts.ToList(), Mark = m.Mark, Level = m.Level, Category = m.Category,
        };
    }

    // =========================================================================================
    /// <summary>Pratinjau batang yang mengikuti kursor (bentuk dan panjang langsung terlihat).</summary>
    class MemberJig : DrawJig
    {
        readonly MemberSpec spec;
        readonly bool is3D;
        readonly double k;
        readonly string message;
        readonly string[] keywords;
        public Point3d Current;

        public MemberJig(MemberSpec m, bool threeD, double unitK, string msg, string[] kws)
        {
            spec = m; is3D = threeD; k = unitK; message = msg; keywords = kws; Current = m.S;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var o = new JigPromptPointOptions(message)
            {
                UseBasePoint = true,
                BasePoint = spec.S,
                Cursor = CursorType.RubberBand,
                UserInputControls = UserInputControls.Accept3dCoordinates | UserInputControls.NullResponseAccepted | UserInputControls.GovernedByOrthoMode,
            };
            foreach (var kw in keywords) o.Keywords.Add(kw);
            var r = prompts.AcquirePoint(o);
            if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (r.Value.DistanceTo(Current) < 1e-9) return SamplerStatus.NoChange;
            Current = r.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (Current.DistanceTo(spec.S) < 1e-6) return true;
            var m = MemberBuild.Copy(spec); m.E = Current;
            try
            {
                if (is3D)
                {
                    using (var s = new Solid3d()) { MemberBuild.Shape3D(s, m, k); draw.Geometry.Draw(s); }
                }
                else
                {
                    var place = MemberBuild.Place2D(m);
                    foreach (var e in MemberBuild.Lines2D(m, k, m.S.DistanceTo(m.E)))
                        using (e) { e.TransformBy(place); draw.Geometry.Draw(e); }
                }
            }
            catch { }
            return true;
        }
    }

    // =========================================================================================
    /// <summary>Grip ujung batang: tarik untuk memanjangkan/memendekkan; grip tengah memindahkan.</summary>
    class MemberGrip : GripData
    {
        public int Which;            // 0 = awal, 1 = akhir, 2 = tengah
        public MemberSpec Spec;
    }

    class MemberGripOverrule : GripOverrule
    {
        public static readonly MemberGripOverrule Instance = new MemberGripOverrule();

        static MemberSpec SpecOf(Entity e)
        {
            if (e.Database == null || e.ExtensionDictionary.IsNull) return null;
            try
            {
                using (var tr = e.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var t = Draw.ReadTag(tr, e);
                    tr.Commit();
                    return t?.Spec != null && (t.Kind == "batang3d" || t.Kind == "batang2d") ? t.Spec : null;
                }
            }
            catch { return null; }
        }

        public override void GetGripPoints(Entity entity, GripDataCollection grips, double curViewUnitSize, int gripSize, Vector3d curViewDir, GetGripPointsFlags bitFlags)
        {
            var s = SpecOf(entity);
            if (s == null) { base.GetGripPoints(entity, grips, curViewUnitSize, gripSize, curViewDir, bitFlags); return; }
            grips.Add(new MemberGrip { Which = 0, Spec = s, GripPoint = s.S });
            grips.Add(new MemberGrip { Which = 1, Spec = s, GripPoint = s.E });
            grips.Add(new MemberGrip { Which = 2, Spec = s, GripPoint = s.S + (s.E - s.S) * 0.5 });
        }

        public override void MoveGripPointsAt(Entity entity, GripDataCollection grips, Vector3d offset, MoveGripPointsFlags bitFlags)
        {
            MemberSpec s = null; bool ms = false, me = false;
            foreach (GripData g in grips)
                if (g is MemberGrip mg) { s = mg.Spec; if (mg.Which != 1) ms = true; if (mg.Which != 0) me = true; }
            if (s == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }
            var m = MemberBuild.Copy(s);
            if (ms) m.S += offset;
            if (me) m.E += offset;
            if (m.S.DistanceTo(m.E) < 1e-6) return;
            var db = entity.Database ?? HostApplicationServices.WorkingDatabase;
            double k = Draw.UnitK(db);
            bool resident = !entity.ObjectId.IsNull && entity.Database != null;
            try
            {
                if (resident)
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        MemberBuild.Apply(tr, entity, m);
                        tr.Commit();
                    }
                }
                else if (entity is Solid3d sol) MemberBuild.Shape3D(sol, m, k);
                else if (entity is BlockReference br)
                {
                    // pratinjau: regangkan blok searah batang
                    double sx = m.S.DistanceTo(m.E) / Math.Max(s.S.DistanceTo(s.E), 1e-9);
                    br.TransformBy(MemberBuild.Place2D(m) * br.BlockTransform.Inverse());
                    br.ScaleFactors = new Scale3d(sx, 1, 1);
                }
            }
            catch { }
        }
    }

    /// <summary>Menjaga data titik batang tetap benar setelah MOVE / ROTATE / COPY / MIRROR / 3DROTATE.</summary>
    class MemberTransformOverrule : TransformOverrule
    {
        public static readonly MemberTransformOverrule Instance = new MemberTransformOverrule();

        public override void TransformBy(Entity entity, Matrix3d transform)
        {
            base.TransformBy(entity, transform);
            if (entity.ObjectId.IsNull || entity.Database == null || entity.ExtensionDictionary.IsNull) return;
            try
            {
                using (var tr = entity.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var t = Draw.ReadTag(tr, entity);
                    if (t?.Spec != null && (t.Kind == "batang3d" || t.Kind == "batang2d"))
                    {
                        var s = t.Spec;
                        Draw.MemberAxes(s.E - s.S, s.Roll, out Vector3d _, out Vector3d yOld, out Vector3d _);
                        var m = MemberBuild.Copy(s);
                        m.S = s.S.TransformBy(transform);
                        m.E = s.E.TransformBy(transform);
                        m.Normal = s.Normal.TransformBy(transform).GetNormal();
                        m.Cuts = s.Cuts.Select(c =>
                        {
                            var o = Draw.P(c.F.O).TransformBy(transform);
                            Func<Vec3, Vec3> d = v => Draw.V(Draw.D(v).TransformBy(transform).GetNormal());
                            return new Cutter { Cylinder = c.Cylinder, F = new Frame(Draw.V(o), d(c.F.U), d(c.F.V), d(c.F.W)), X0 = c.X0, X1 = c.X1, Y0 = c.Y0, Y1 = c.Y1, Z0 = c.Z0, Z1 = c.Z1, Radius = c.Radius };
                        }).ToList();
                        var yNew = yOld.TransformBy(transform).GetNormal();
                        Draw.MemberAxes(m.E - m.S, 0, out Vector3d x0, out Vector3d y0, out Vector3d _);
                        m.Roll = Math.Round(Math.Atan2(-yNew.DotProduct(x0), yNew.DotProduct(y0)) * 180 / Math.PI, 6);
                        Draw.TagMember(tr, entity, m, Draw.UnitK(entity.Database), Store.Density);
                    }
                    tr.Commit();
                }
            }
            catch { }
        }
    }

    public static class MemberOverrules
    {
        static bool on;
        public static void Register()
        {
            if (on) return;
            on = true;
            foreach (var cls in new[] { RXObject.GetClass(typeof(Solid3d)), RXObject.GetClass(typeof(BlockReference)) })
            {
                Overrule.AddOverrule(cls, MemberGripOverrule.Instance, false);
                Overrule.AddOverrule(cls, MemberTransformOverrule.Instance, false);
            }
            MemberGripOverrule.Instance.SetExtensionDictionaryEntryFilter(Draw.Dict);
            MemberTransformOverrule.Instance.SetExtensionDictionaryEntryFilter(Draw.Dict);
            Overrule.Overruling = true;
        }

        /// <summary>Wajib dipanggil saat AutoCAD ditutup: overrule yang masih terdaftar membuat AutoCAD crash saat keluar.</summary>
        public static void Unregister()
        {
            if (!on) return;
            on = false;
            foreach (var cls in new[] { RXObject.GetClass(typeof(Solid3d)), RXObject.GetClass(typeof(BlockReference)) })
            {
                try { Overrule.RemoveOverrule(cls, MemberGripOverrule.Instance); } catch { }
                try { Overrule.RemoveOverrule(cls, MemberTransformOverrule.Instance); } catch { }
            }
        }
    }

    // =========================================================================================
    public class MemberCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        [CommandMethod("STEELKIT", "SKBATANG", CommandFlags.Modal)]
        public void Member3D() => Guard.Run(() => DrawMembers(true));

        [CommandMethod("STEELKIT", "SKBATANG2D", CommandFlags.Modal)]
        public void Member2D() => Guard.Run(() => DrawMembers(false));

        static void DrawMembers(bool is3D)
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            double k = SteelKit.Acad.Draw.UnitK(db);
            var p = Store.Current;
            Describe(ed, p, is3D);
            var kws = is3D ? new[] { "Profil", "Putar", "Acuan" } : new[] { "Profil", "Tampak", "Acuan" };
            string kwText = " [" + string.Join("/", kws) + "]";
            Point3d? start = null;
            int n = 0;
            while (true)
            {
                if (start == null)
                {
                    var ppo = new PromptPointOptions("\nTitik awal batang" + kwText) { AppendKeywordsToMessage = false };
                    foreach (var kw in kws) ppo.Keywords.Add(kw);
                    var r = ed.GetPoint(ppo);
                    if (r.Status == PromptStatus.Keyword) { p = Keyword(ed, r.StringResult, p, is3D); continue; }
                    if (r.Status != PromptStatus.OK) break;
                    start = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    continue;
                }
                var spec = new MemberSpec
                {
                    Profile = p, S = start.Value, E = start.Value, Anchor = Store.Anchor, Roll = Store.Roll, TopView = Store.TopView,
                    Normal = ed.CurrentUserCoordinateSystem.CoordinateSystem3d.Zaxis,
                };
                Point3d end;
                string msg = "\nTitik akhir batang (Enter = selesai)" + kwText;
                if (!Plugin.IsConsole)
                {
                    var jig = new MemberJig(spec, is3D, k, msg, kws);
                    var dr = ed.Drag(jig);
                    if (dr.Status == PromptStatus.Keyword) { p = Keyword(ed, dr.StringResult, p, is3D); continue; }
                    if (dr.Status != PromptStatus.OK) break;
                    end = jig.Current;
                }
                else
                {
                    var ppo = new PromptPointOptions(msg) { AllowNone = true, UseBasePoint = true, BasePoint = start.Value.TransformBy(ed.CurrentUserCoordinateSystem.Inverse()), AppendKeywordsToMessage = false };
                    foreach (var kw in kws) ppo.Keywords.Add(kw);
                    var r = ed.GetPoint(ppo);
                    if (r.Status == PromptStatus.Keyword) { p = Keyword(ed, r.StringResult, p, is3D); continue; }
                    if (r.Status != PromptStatus.OK) break;
                    end = r.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                }
                spec.E = end;
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    var id = is3D ? MemberBuild.Create3D(tr, db, space, spec) : MemberBuild.Create2D(tr, db, space, spec);
                    tr.Commit();
                    if (!id.IsNull) n++;
                }
                start = end;
            }
            if (n > 0) ed.WriteMessage("\n" + n + " batang " + (is3D ? "3D " : "2D ") + p.Name + " dibuat. Tarik grip ujung untuk mengubah panjang; SKUBAH untuk putar/profil/acuan.");
        }

        static void Describe(Editor ed, Profile p, bool is3D)
        {
            var pr = p.Properties();
            ed.WriteMessage("\nProfil: " + p.Name + "  " + SteelKit.Acad.Draw.N(pr.KgPerM(Store.Density)) + " kg/m. Acuan: " + Store.Anchor +
                (is3D ? ", putar: " + SteelKit.Acad.Draw.N(Store.Roll) + "°" : ", tampak: " + (Store.TopView ? "atas" : "samping")) + ".");
        }

        static Profile Keyword(Editor ed, string kw, Profile p, bool is3D)
        {
            switch (kw)
            {
                case "Profil":
                    var q = Commands.AskProfile(ed, p);
                    if (q != null) { Store.Current = q; Describe(ed, q, is3D); return q; }
                    return p;
                case "Putar":
                    var d = ed.GetDouble(new PromptDoubleOptions("\nPutar profil terhadap sumbu batang (derajat)") { DefaultValue = Store.Roll, UseDefaultValue = true });
                    if (d.Status == PromptStatus.OK) Store.Roll = d.Value;
                    return p;
                case "Tampak":
                    Store.TopView = !Store.TopView;
                    ed.WriteMessage("\nTampak: " + (Store.TopView ? "atas (lebar sayap)" : "samping (tinggi profil)"));
                    return p;
                default:
                    var pko = new PromptKeywordOptions("\nTitik acuan profil terhadap garis");
                    foreach (var a in Enum.GetNames(typeof(Anchor))) pko.Keywords.Add(a);
                    pko.Keywords.Default = Store.Anchor.ToString();
                    var r = ed.GetKeywords(pko);
                    if (r.Status == PromptStatus.OK) Store.Anchor = (Anchor)Enum.Parse(typeof(Anchor), r.StringResult);
                    return p;
            }
        }

        // ------------------------------------------------------------------ SKUBAH: edit batang
        [CommandMethod("STEELKIT", "SKUBAH", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Edit() => Guard.Run(EditCore);
        void EditCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPilih batang Steel Kit yang diubah" });
            if (sel.Status != PromptStatus.OK) return;
            var ids = sel.Value.GetObjectIds();
            var pko = new PromptKeywordOptions("\nUbah [Panjang/Ujung/Putar/Profil/Acuan/Balik]");
            foreach (var kw in new[] { "Panjang", "Ujung", "Putar", "Profil", "Acuan", "Balik" }) pko.Keywords.Add(kw);
            pko.Keywords.Default = "Panjang";
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK) return;
            string what = kr.StringResult;
            double val = 0; Profile prof = null; Anchor anchor = Store.Anchor; Point3d? newPt = null;
            switch (what)
            {
                case "Panjang":
                    var dl = ed.GetDistance(new PromptDistanceOptions("\nPanjang baru (titik awal tetap)") { AllowNegative = false, AllowZero = false });
                    if (dl.Status != PromptStatus.OK) return; val = dl.Value; break;
                case "Putar":
                    var da = ed.GetDouble(new PromptDoubleOptions("\nSudut putar profil (derajat, mutlak)") { DefaultValue = 90, UseDefaultValue = true });
                    if (da.Status != PromptStatus.OK) return; val = da.Value; break;
                case "Profil":
                    prof = Commands.AskProfile(ed, Store.Current); if (prof == null) return; break;
                case "Acuan":
                    var ap = new PromptKeywordOptions("\nTitik acuan baru");
                    foreach (var a in Enum.GetNames(typeof(Anchor))) ap.Keywords.Add(a);
                    var ar = ed.GetKeywords(ap); if (ar.Status != PromptStatus.OK) return;
                    anchor = (Anchor)Enum.Parse(typeof(Anchor), ar.StringResult); break;
                case "Ujung":
                    if (ids.Length != 1) { ed.WriteMessage("\nPilih satu batang untuk opsi Ujung."); return; }
                    var near = ed.GetPoint("\nKlik dekat ujung yang dipindah");
                    if (near.Status != PromptStatus.OK) return;
                    var np = ed.GetPoint(new PromptPointOptions("\nTitik ujung baru") { UseBasePoint = true, BasePoint = near.Value });
                    if (np.Status != PromptStatus.OK) return;
                    newPt = np.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    val = 0;
                    var nearW = near.Value.TransformBy(ed.CurrentUserCoordinateSystem);
                    using (var t0 = db.TransactionManager.StartTransaction())
                    {
                        var tg = SteelKit.Acad.Draw.ReadTag(t0, (Entity)t0.GetObject(ids[0], OpenMode.ForRead));
                        t0.Commit();
                        if (tg?.Spec != null) val = nearW.DistanceTo(tg.Spec.S) <= nearW.DistanceTo(tg.Spec.E) ? 0 : 1;
                    }
                    break;
            }
            int n = 0, old = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    var t = SteelKit.Acad.Draw.ReadTag(tr, ent);
                    if (t == null || (t.Kind != "batang3d" && t.Kind != "batang2d")) continue;
                    if (t.Spec == null) { old++; continue; }
                    var m = MemberBuild.Copy(t.Spec);
                    switch (what)
                    {
                        case "Panjang": m.E = m.S + (m.E - m.S).GetNormal() * val; break;
                        case "Putar": m.Roll = val; break;
                        case "Profil": m.Profile = prof; break;
                        case "Acuan": m.Anchor = anchor; break;
                        case "Balik": var tmp = m.S; m.S = m.E; m.E = tmp; break;
                        case "Ujung": if (val < 0.5) m.S = newPt.Value; else m.E = newPt.Value; break;
                    }
                    MemberBuild.Apply(tr, ent, m);
                    n++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\n" + n + " batang diubah." + (old > 0 ? " " + old + " batang versi lama dilewati (gambar ulang dengan SKBATANG)." : ""));
        }

        [CommandMethod("STEELKIT", "SKPUTAR", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Rotate() => Guard.Run(RotateCore);
        void RotateCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPilih batang yang profilnya diputar terhadap sumbunya" });
            if (sel.Status != PromptStatus.OK) return;
            var d = ed.GetDouble(new PromptDoubleOptions("\nTambah putaran (derajat)") { DefaultValue = 90, UseDefaultValue = true });
            if (d.Status != PromptStatus.OK) return;
            int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    var t = SteelKit.Acad.Draw.ReadTag(tr, ent);
                    if (t?.Spec == null || t.Kind != "batang3d") continue;
                    var m = MemberBuild.Copy(t.Spec);
                    m.Roll = (m.Roll + d.Value) % 360;
                    MemberBuild.Apply(tr, ent, m);
                    n++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\n" + n + " batang diputar.");
        }
    }
}
