using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelKit.Core;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(SteelKit.Acad.Commands))]

namespace SteelKit.Acad
{
    /// <summary>
    /// Perintah yang berjalan lewat baris perintah (juga di accoreconsole, tanpa dialog).
    /// Dialog dan ribbon ada di Ui.cs.
    /// </summary>
    public class Commands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        // =====================================================================================
        // Pemilihan profil di baris perintah
        // =====================================================================================
        public static Profile AskProfile(Editor ed, Profile current)
        {
            while (true)
            {
                var pso = new PromptStringOptions("\nNama profil (contoh WF400x200x8x13, C150x65x20x3.2, SCH40-4in, ? = daftar)")
                {
                    AllowSpaces = false,
                    DefaultValue = current?.Name ?? "",
                    UseDefaultValue = current != null,
                };
                var r = ed.GetString(pso);
                if (r.Status != PromptStatus.OK) return null;
                string s = r.StringResult.Trim();
                if (s.Length == 0 && current != null) return current;
                if (s == "?") { ListCatalog(ed); continue; }
                var p = Store.Catalog.Find(s);
                if (p != null) return p;
                var hits = Store.Catalog.All.Where(x => x.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).Take(20).Select(x => x.Name).ToList();
                ed.WriteMessage(hits.Count > 0
                    ? "\nTidak persis ditemukan. Mungkin maksud Anda: " + string.Join(", ", hits)
                    : "\nProfil \"" + s + "\" tidak ada di katalog. Ketik ? untuk daftar, atau buat ukuran sendiri di SKPROFIL.");
            }
        }

        static void ListCatalog(Editor ed)
        {
            var fams = Enum.GetValues(typeof(Family)).Cast<Family>().ToList();
            ed.WriteMessage("\nKeluarga profil:");
            foreach (var f in fams)
                ed.WriteMessage("\n  " + f.ToString().PadRight(7) + FamilyInfo.Of(f).Label + " (" + Store.Catalog.OfFamily(f).Count() + " ukuran)");
            var pko = new PromptKeywordOptions("\nTampilkan ukuran untuk keluarga") { AllowNone = true };
            foreach (var f in fams) pko.Keywords.Add(f.ToString());
            var r = ed.GetKeywords(pko);
            if (r.Status != PromptStatus.OK) return;
            FamilyInfo.TryParse(r.StringResult, out Family fam);
            ed.WriteMessage("\n" + FamilyInfo.Of(fam).Label + ":\n  " + string.Join(", ", Store.Catalog.OfFamily(fam).Select(x => x.Name)));
            ed.WriteMessage("\nPemakaian umum: " + FamilyInfo.Of(fam).Usage);
        }

        static void Describe(Editor ed, Profile p)
        {
            var pr = p.Properties();
            ed.WriteMessage("\nProfil: " + p.Name + "  (" + p.Info.Label + ")  A = " + Draw.N(pr.Area / 100) + " cm²,  " + Draw.N(pr.KgPerM(Store.Density)) + " kg/m");
        }

        // =====================================================================================
        // SKPENAMPANG: sisipkan penampang 2D (blok) + dimensi + label
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKPENAMPANG", CommandFlags.Modal)]
        public void Section() => Guard.Run(SectionCore);
        void SectionCore()
        {
            var doc = Doc; var ed = doc.Editor;
            var p = Store.Current;
            double rot = 0;
            Describe(ed, p);
            while (true)
            {
                var ppo = new PromptPointOptions("\nTitik sisip penampang " + p.Name + " [Profil/Putar/Arsir/Dimensi]")
                {
                    AppendKeywordsToMessage = false,
                };
                ppo.Keywords.Add("Profil"); ppo.Keywords.Add("Putar"); ppo.Keywords.Add("Arsir"); ppo.Keywords.Add("Dimensi");
                var r = ed.GetPoint(ppo);
                if (r.Status == PromptStatus.Keyword)
                {
                    switch (r.StringResult)
                    {
                        case "Profil": var q = AskProfile(ed, p); if (q != null) { p = q; Store.Current = q; Describe(ed, p); } break;
                        case "Putar":
                            var a = ed.GetAngle(new PromptAngleOptions("\nSudut putar penampang") { DefaultValue = rot, UseDefaultValue = true });
                            if (a.Status == PromptStatus.OK) rot = a.Value;
                            break;
                        case "Arsir": Store.Hatch = !Store.Hatch; ed.WriteMessage("\nArsir: " + (Store.Hatch ? "ya" : "tidak")); break;
                        case "Dimensi": Store.Dims = !Store.Dims; ed.WriteMessage("\nDimensi: " + (Store.Dims ? "ya" : "tidak")); break;
                    }
                    continue;
                }
                if (r.Status != PromptStatus.OK) return;
                using (doc.LockDocument())
                    InsertSection(doc.Database, ed.CurrentUserCoordinateSystem, p, r.Value, rot, Store.Hatch, Store.Dims);
                ed.WriteMessage("\nPenampang " + p.Name + " disisipkan (blok " + Draw.BlockName(p.Name) + ").");
                return;
            }
        }

        public static ObjectId InsertSection(Database db, Matrix3d ucs, Profile p, Point3d ptUcs, double rotRad, bool hatch, bool dims)
        {
            double k = Draw.UnitK(db);
            var shape = p.BuildShape();
            var pr = p.Properties();
            double W = pr.Width, Hh = pr.Height, size = Math.Max(W, Hh);
            ObjectId brId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var l in new[] { Draw.LaySection, Draw.LayDim, Draw.LayText }) Draw.Layer(tr, db, l);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                string bname = Draw.BlockName(p.Name);
                BlockTableRecord btr;
                if (bt.Has(bname))
                {
                    btr = (BlockTableRecord)tr.GetObject(bt[bname], OpenMode.ForWrite);
                    foreach (ObjectId id in btr) tr.GetObject(id, OpenMode.ForWrite).Erase();   // bangun ulang: ukuran bisa berubah
                }
                else
                {
                    btr = new BlockTableRecord { Name = bname, Origin = Point3d.Origin };
                    bt.UpgradeOpen();
                    bt.Add(btr);
                    tr.AddNewlyCreatedDBObject(btr, true);
                }
                btr.Comments = p.Info.Label + " " + p.Name;

                var loopIds = new List<ObjectId>();
                foreach (var loop in shape.AllLoops)
                {
                    var pl = Draw.Poly(loop, k);
                    pl.Layer = "0"; pl.ColorIndex = 0;   // ikut layer/warna blok
                    loopIds.Add(btr.AppendEntity(pl));
                    tr.AddNewlyCreatedDBObject(pl, true);
                }
                if (hatch)
                {
                    var h = new Hatch();
                    h.SetDatabaseDefaults(db);
                    h.Layer = "0"; h.ColorIndex = 8;
                    btr.AppendEntity(h);
                    tr.AddNewlyCreatedDBObject(h, true);
                    h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                    h.PatternScale = Math.Max(0.1, size * k / 120.0);
                    h.Associative = false;
                    h.AppendLoop(HatchLoopTypes.Outermost, new ObjectIdCollection { loopIds[0] });
                    for (int i = 1; i < loopIds.Count; i++) h.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { loopIds[i] });
                    h.EvaluateHatch(true);
                }
                // tanda titik berat
                double c = size * 0.05 * k;
                foreach (var ln in new[]
                {
                    new Line(new Point3d(pr.Cx * k - c, pr.Cy * k, 0), new Point3d(pr.Cx * k + c, pr.Cy * k, 0)),
                    new Line(new Point3d(pr.Cx * k, pr.Cy * k - c, 0), new Point3d(pr.Cx * k, pr.Cy * k + c, 0)),
                })
                {
                    ln.Layer = "0"; ln.ColorIndex = 6;
                    btr.AppendEntity(ln); tr.AddNewlyCreatedDBObject(ln, true);
                }

                var m = ucs * Matrix3d.Displacement(ptUcs.GetAsVector()) * Matrix3d.Rotation(rotRad, Vector3d.ZAxis, Point3d.Origin);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var br = new BlockReference(Point3d.Origin, btr.ObjectId) { Layer = Draw.LaySection };
                br.TransformBy(m);
                brId = space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                Draw.Tag(tr, br, "penampang", p, 0, pr.KgPerM(Store.Density));
                // perbarui tampilan semua sisipan lain dari blok yang sama
                foreach (ObjectId rid in btr.GetBlockReferenceIds(true, false))
                    if (rid != brId) ((BlockReference)tr.GetObject(rid, OpenMode.ForWrite)).RecordGraphicsModified(true);

                double off = size * 0.18 * k, txt = size * 0.07 * k;
                if (dims)
                {
                    var dW = new RotatedDimension(0, new Point3d(-W / 2 * k, -Hh / 2 * k, 0), new Point3d(W / 2 * k, -Hh / 2 * k, 0),
                        new Point3d(0, -Hh / 2 * k - off, 0), "", db.Dimstyle);
                    var dH = new RotatedDimension(Math.PI / 2, new Point3d(-W / 2 * k, -Hh / 2 * k, 0), new Point3d(-W / 2 * k, Hh / 2 * k, 0),
                        new Point3d(-W / 2 * k - off, 0, 0), "", db.Dimstyle);
                    foreach (var d in new[] { dW, dH })
                    {
                        d.SetDatabaseDefaults(db);
                        d.Layer = Draw.LayDim;
                        d.Dimscale = 1; d.Dimtxt = txt; d.Dimasz = txt * 0.8; d.Dimexo = txt * 0.4; d.Dimexe = txt * 0.5; d.Dimgap = txt * 0.3;
                        d.Dimlfac = 1 / k;   // tampilkan angka dalam mm walau gambar dalam m / cm
                        d.TransformBy(m);
                        space.AppendEntity(d); tr.AddNewlyCreatedDBObject(d, true);
                    }
                }
                var mt = new MText
                {
                    Contents = p.Name + "\\P" + ThicknessText(p) + "\\PA = " + Draw.N(pr.Area / 100) + " cm²   " + Draw.N(pr.KgPerM(Store.Density)) + " kg/m",
                    Location = new Point3d(0, -Hh / 2 * k - off * (dims ? 2.2 : 1), 0),
                    TextHeight = txt,
                    Attachment = AttachmentPoint.TopCenter,
                    Layer = Draw.LayText,
                };
                mt.TransformBy(m);
                space.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                tr.Commit();
            }
            return brId;
        }

        static string ThicknessText(Profile p)
        {
            var parts = new List<string>();
            foreach (var pd in p.Info.Params)
                if (pd.Key != "H" && pd.Key != "B" && pd.Key != "D" && p[pd.Key] > 0) parts.Add(pd.Key + "=" + Draw.N(p[pd.Key], "0.###"));
            if (p.Family == Family.CHS || p.Family == Family.RD) parts.Insert(0, "D=" + Draw.N(p["D"], "0.###"));
            return parts.Count > 0 ? string.Join("  ", parts) : p.Info.Label;
        }

        // =====================================================================================
        // SKLABEL: label nama profil dan panjang pada batang
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKLABEL", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Label() => Guard.Run(LabelCore);
        void LabelCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPilih batang Steel Kit yang akan diberi label" });
            if (sel.Status != PromptStatus.OK) return;
            int n = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Draw.Layer(tr, db, Draw.LayText);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                double k = Draw.UnitK(db);
                foreach (var id in sel.Value.GetObjectIds())
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                    var tag = Draw.ReadTag(tr, ent);
                    if (tag == null || (tag.Kind != "batang3d" && tag.Kind != "batang2d")) continue;
                    var prof = FromTag(tag);
                    double h = Math.Max(prof.Extent(false) * 0.35, 40) * k;
                    var mt = new MText { Contents = tag.Name + "  L=" + Draw.N(tag.LengthMm, "0"), TextHeight = h, Layer = Draw.LayText, Attachment = AttachmentPoint.BottomCenter };
                    if (tag.Spec != null)
                    {
                        var s = tag.Spec;
                        var mid = s.S + (s.E - s.S) * 0.5;
                        var dir = (s.E - s.S).GetNormal();
                        Vector3d normal, up;
                        if (tag.Kind == "batang2d")
                        {
                            normal = s.Normal.GetNormal();
                            up = normal.CrossProduct(dir).GetNormal();
                            var sh = prof.AnchorShift(s.Anchor);
                            double half = prof.Extent(s.TopView) / 2;
                            double shift = s.Anchor == Anchor.Atas ? -half : s.Anchor == Anchor.Bawah ? half : s.Anchor == Anchor.TitikBerat ? (s.TopView ? sh.X : sh.Y) : 0;
                            mid += up * ((half + shift) * k + h * 0.3);
                        }
                        else
                        {
                            Draw.MemberAxes(s.E - s.S, s.Roll, out Vector3d _, out Vector3d yAx, out Vector3d _);
                            normal = Math.Abs(dir.Z) > 0.98 ? Vector3d.YAxis.Negate() : Vector3d.ZAxis;
                            up = normal.CrossProduct(Math.Abs(dir.Z) > 0.98 ? Vector3d.ZAxis : dir).GetNormal();
                            mid += yAx * (prof.Extent(false) / 2 * k + h * 0.4);
                            if (Math.Abs(dir.Z) > 0.98) dir = Vector3d.ZAxis;   // kolom: teks tegak
                        }
                        if (dir.X < -1e-9 || (Math.Abs(dir.X) <= 1e-9 && dir.Y < -1e-9)) dir = dir.Negate();
                        mt.Normal = normal;
                        mt.Location = mid;
                        mt.Direction = (dir - normal * dir.DotProduct(normal)).GetNormal();
                    }
                    else
                    {
                        var ext = ent.GeometricExtents;
                        mt.Location = ext.MinPoint + (ext.MaxPoint - ext.MinPoint) * 0.5;
                    }
                    space.AppendEntity(mt); tr.AddNewlyCreatedDBObject(mt, true);
                    n++;
                }
                tr.Commit();
            }
            ed.WriteMessage("\n" + n + " label dibuat.");
        }

        public static Profile FromTag(Draw.TagData tag)
        {
            var p = new Profile { Family = tag.Family, Name = tag.Name };
            foreach (var kv in (tag.Dims ?? "").Split(';'))
            {
                int i = kv.IndexOf('=');
                if (i > 0 && double.TryParse(kv.Substring(i + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) p[kv.Substring(0, i)] = v;
            }
            p.FillDefaults();
            return p;
        }

        // =====================================================================================
        // SKTABEL: tabel properti satu profil atau satu keluarga
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKTABEL", CommandFlags.Modal)]
        public void PropertyTable() => Guard.Run(PropertyTableCore);
        void PropertyTableCore()
        {
            var doc = Doc; var ed = doc.Editor;
            var p = Store.Current;
            bool family = false;
            while (true)
            {
                var ppo = new PromptPointOptions("\nTitik sisip tabel " + (family ? "keluarga " + p.Family : p.Name) + " [Profil/Keluarga/Satu/Tinggi]");
                ppo.Keywords.Add("Profil"); ppo.Keywords.Add("Keluarga"); ppo.Keywords.Add("Satu"); ppo.Keywords.Add("Tinggi");
                var r = ed.GetPoint(ppo);
                if (r.Status == PromptStatus.Keyword)
                {
                    switch (r.StringResult)
                    {
                        case "Profil": var q = AskProfile(ed, p); if (q != null) { p = q; Store.Current = q; } break;
                        case "Keluarga": family = true; break;
                        case "Satu": family = false; break;
                        case "Tinggi":
                            var d = ed.GetDouble(new PromptDoubleOptions("\nTinggi teks tabel (mm di gambar)") { DefaultValue = Store.TableText, UseDefaultValue = true, AllowNegative = false, AllowZero = false });
                            if (d.Status == PromptStatus.OK) Store.TableText = d.Value;
                            break;
                    }
                    continue;
                }
                if (r.Status != PromptStatus.OK) return;
                using (doc.LockDocument())
                {
                    if (family) FamilyTable(doc.Database, ed.CurrentUserCoordinateSystem, p.Family, r.Value);
                    else SingleTable(doc.Database, ed.CurrentUserCoordinateSystem, p, r.Value);
                }
                return;
            }
        }

        public static List<string[]> PropertyRows(Profile p, double density)
        {
            var pr = p.Properties();
            var rows = new List<string[]>();
            foreach (var pd in p.Info.Params) rows.Add(new[] { pd.Label, Draw.N(p[pd.Key], "0.###"), "mm" });
            rows.Add(new[] { "Luas penampang A", Draw.N(pr.Area / 100, "0.00"), "cm²" });
            rows.Add(new[] { "Berat", Draw.N(pr.KgPerM(density), "0.00"), "kg/m" });
            rows.Add(new[] { "Ix", Draw.N(pr.Ix / 1e4, "0.0"), "cm⁴" });
            rows.Add(new[] { "Iy", Draw.N(pr.Iy / 1e4, "0.0"), "cm⁴" });
            if (Math.Abs(pr.Ixy) > 1e-3 * pr.Ix)
            {
                rows.Add(new[] { "Iu (maks)", Draw.N(pr.Iu / 1e4, "0.0"), "cm⁴" });
                rows.Add(new[] { "Iv (min)", Draw.N(pr.Iv / 1e4, "0.0"), "cm⁴" });
                rows.Add(new[] { "Sudut sumbu utama", Draw.N(pr.Alpha * 180 / Math.PI, "0.0"), "°" });
            }
            rows.Add(new[] { "Sx (elastis)", Draw.N(pr.Sx / 1e3, "0.0"), "cm³" });
            rows.Add(new[] { "Sy (elastis)", Draw.N(pr.Sy / 1e3, "0.0"), "cm³" });
            rows.Add(new[] { "Zx (plastis)", Draw.N(pr.Zx / 1e3, "0.0"), "cm³" });
            rows.Add(new[] { "Zy (plastis)", Draw.N(pr.Zy / 1e3, "0.0"), "cm³" });
            rows.Add(new[] { "rx", Draw.N(pr.Rx / 10, "0.00"), "cm" });
            rows.Add(new[] { "ry", Draw.N(pr.Ry / 10, "0.00"), "cm" });
            if (Math.Abs(pr.Ixy) > 1e-3 * pr.Ix) rows.Add(new[] { "rv (min)", Draw.N(pr.Rv / 10, "0.00"), "cm" });
            rows.Add(new[] { "J (torsi, pendekatan)", Draw.N(pr.J / 1e4, "0.00"), "cm⁴" });
            var bb = p.BuildShape().Bounds();
            if (p.Family == Family.L || p.Family == Family.UNP || p.Family == Family.CNP || p.Family == Family.T)
            {
                if (p.Family != Family.T) rows.Add(new[] { "Titik berat dari punggung (cx)", Draw.N(pr.Cx - bb[0], "0.0"), "mm" });
                if (p.Family == Family.L || p.Family == Family.T) rows.Add(new[] { "Titik berat dari bawah (cy)", Draw.N(pr.Cy - bb[1], "0.0"), "mm" });
            }
            rows.Add(new[] { "Luas permukaan (cat)", Draw.N(pr.PaintAreaPerM, "0.000"), "m²/m" });
            return rows;
        }

        static void SingleTable(Database db, Matrix3d ucs, Profile p, Point3d pt)
        {
            var rows = new List<string[]> { new[] { "Properti", "Nilai", "Satuan" } };
            rows.AddRange(PropertyRows(p, Store.Density));
            MakeTable(db, ucs, pt, p.Name + " - " + p.Info.Label, rows);
        }

        static void FamilyTable(Database db, Matrix3d ucs, Family f, Point3d pt)
        {
            var info = FamilyInfo.Of(f);
            var head = new List<string> { "Profil" };
            head.AddRange(info.Params.Select(x => x.Key + " (mm)"));
            head.AddRange(new[] { "A (cm²)", "kg/m", "Ix (cm⁴)", "Iy (cm⁴)", "Sx (cm³)", "Sy (cm³)", "Zx (cm³)", "rx (cm)", "ry (cm)" });
            var rows = new List<string[]> { head.ToArray() };
            foreach (var p in Store.Catalog.OfFamily(f))
            {
                var pr = p.Properties();
                var r = new List<string> { p.Name };
                r.AddRange(info.Params.Select(x => Draw.N(p[x.Key], "0.###")));
                r.AddRange(new[]
                {
                    Draw.N(pr.Area / 100, "0.00"), Draw.N(pr.KgPerM(Store.Density), "0.00"), Draw.N(pr.Ix / 1e4, "0.0"), Draw.N(pr.Iy / 1e4, "0.0"),
                    Draw.N(pr.Sx / 1e3, "0.0"), Draw.N(pr.Sy / 1e3, "0.0"), Draw.N(pr.Zx / 1e3, "0.0"), Draw.N(pr.Rx / 10, "0.00"), Draw.N(pr.Ry / 10, "0.00"),
                });
                rows.Add(r.ToArray());
            }
            MakeTable(db, ucs, pt, info.Label + " - properti dihitung dari dimensi", rows);
        }

        public static ObjectId MakeTable(Database db, Matrix3d ucs, Point3d ptUcs, string title, List<string[]> rows, double textMm = 0, ObjectId spaceId = default(ObjectId))
        {
            double k = Draw.UnitK(db);
            double h = (textMm > 0 ? textMm : Store.TableText) * k;
            int nc = rows.Max(r => r.Length);
            // kolom angka rata kanan, kolom teks rata kiri, baris judul & kepala di tengah (baris pertama rows = kepala bila semua sel teks)
            bool headerRow = rows.Count > 1 && rows[0].All(c => !IsNumber(c)) && rows.Skip(1).Any(r => r.Any(IsNumber));
            var numericCol = Enumerable.Range(0, nc).Select(c => rows.Skip(headerRow ? 1 : 0).Where(r => c < r.Length && !string.IsNullOrWhiteSpace(r[c]) && r[c] != "-")
                .All(r => IsNumber(r[c]))).ToArray();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Draw.Layer(tr, db, Draw.LayTable);
                var st = Std.TextStyle(tr, db);
                var t = new Table();
                t.SetDatabaseDefaults(db);
                t.TableStyle = Std.TableStyle(tr, db, h);
                t.SetSize(rows.Count + 1, nc);
                t.Position = ptUcs;
                t.Layer = Draw.LayTable;
                t.MergeCells(CellRange.Create(t, 0, 0, 0, nc - 1));
                t.Cells[0, 0].TextString = title;
                t.Cells[0, 0].TextHeight = h * Std.H3 / Std.H4;
                t.Cells[0, 0].TextStyleId = st;
                t.Cells[0, 0].Alignment = CellAlignment.MiddleCenter;
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < nc; c++)
                    {
                        var cell = t.Cells[r + 1, c];
                        cell.TextString = c < rows[r].Length ? rows[r][c] : "";
                        cell.TextHeight = h;
                        cell.TextStyleId = st;
                        cell.Alignment = headerRow && r == 0 ? CellAlignment.MiddleCenter : numericCol[c] && c > 0 ? CellAlignment.MiddleRight : CellAlignment.MiddleLeft;
                    }
                for (int c = 0; c < nc; c++)
                {
                    int chars = rows.Max(r => c < r.Length ? r[c].Length : 0);
                    t.Columns[c].Width = Math.Max(3, chars) * h * 0.72 + h * 2.0;
                }
                t.Rows[0].Height = h * Std.H3 / Std.H4 * 2.0;
                for (int r = 1; r < rows.Count + 1; r++) t.Rows[r].Height = h * 2.2;
                t.GenerateLayout();
                t.TransformBy(ucs);
                var space = (BlockTableRecord)tr.GetObject(spaceId.IsNull ? db.CurrentSpaceId : spaceId, OpenMode.ForWrite);
                var id = space.AppendEntity(t);
                tr.AddNewlyCreatedDBObject(t, true);
                tr.Commit();
                return id;
            }
        }

        static bool IsNumber(string s) => !string.IsNullOrWhiteSpace(s) && System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), @"^[-+]?\d[\d.,]*$");

        // =====================================================================================
        // SKBERAT: daftar berat (BOQ baja) dari batang di gambar -> tabel + CSV
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKBERAT", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void WeightList() => Guard.Run(WeightListCore);
        void WeightListCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var opts = new PromptSelectionOptions { MessageForAdding = "\nPilih batang (Enter = semua batang di ruang gambar ini)" };
            var sel = ed.GetSelection(opts);
            List<MemberRecord> items;
            List<BoqLine> conn;
            int nConn;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                IEnumerable<ObjectId> ids;
                if (sel.Status == PromptStatus.OK) ids = sel.Value.GetObjectIds();
                else if (sel.Status == PromptStatus.Error || sel.Status == PromptStatus.None)
                    ids = ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().ToList();
                else return;
                items = CollectMembers(tr, ids);
                conn = ConnCommands.ConnectionBoq(tr, ids, out nConn);
                tr.Commit();
            }
            if (items.Count == 0 && nConn == 0) { ed.WriteMessage("\nTidak ada batang atau sambungan Steel Kit yang terpilih."); return; }
            var rows = Boq.Group(items);
            var ci = CultureInfo.CurrentCulture;
            var table = new List<string[]> { new[] { "Profil", "Jumlah", "Panjang total (m)", "kg/m", "Berat (kg)" } };
            foreach (var r in rows)
                table.Add(new[] { r.Name, r.Count.ToString(ci), Draw.N(r.TotalLengthM, "0.000"), Draw.N(r.KgPerM, "0.00"), Draw.N(r.TotalKg, "#,##0.0") });
            double memberKg = rows.Sum(x => x.TotalKg), connKg = conn.Sum(x => x.TotalKg);
            table.Add(new[] { "TOTAL BATANG", rows.Sum(x => x.Count).ToString(ci), Draw.N(rows.Sum(x => x.TotalLengthM), "0.000"), "", Draw.N(memberKg, "#,##0.0") });
            var ctable = new List<string[]> { new[] { "Bahan", "Uraian", "Jumlah", "kg/satuan", "Berat (kg)" } };
            foreach (var b in conn)
                ctable.Add(new[] { b.Item, b.Spec, b.Count.ToString(ci), b.UnitKg > 0 ? Draw.N(b.UnitKg, "0.00") : "-", b.TotalKg > 0 ? Draw.N(b.TotalKg, "#,##0.0") : "-" });
            ctable.Add(new[] { "TOTAL SAMBUNGAN", nConn + " sambungan", "", "", Draw.N(connKg, "#,##0.0") });

            string csvPath = CsvPath(doc);
            try
            {
                var csv = new StringBuilder(Boq.ToCsv(rows));
                if (conn.Count > 0)
                {
                    csv.AppendLine().AppendLine("bahan_sambungan,uraian,jumlah,kg_per_satuan,berat_kg");
                    foreach (var b in conn)
                        csv.AppendLine(string.Join(",", b.Item, "\"" + b.Spec.Replace("\"", "'") + "\"", b.Count, b.UnitKg.ToString("0.000", CultureInfo.InvariantCulture), b.TotalKg.ToString("0.0", CultureInfo.InvariantCulture)));
                    csv.AppendLine("TOTAL SAMBUNGAN,," + nConn + ",," + connKg.ToString("0.0", CultureInfo.InvariantCulture));
                    csv.AppendLine("TOTAL BAJA,,,," + (memberKg + connKg).ToString("0.0", CultureInfo.InvariantCulture));
                }
                File.WriteAllText(csvPath, csv.ToString(), new UTF8Encoding(true));
                ed.WriteMessage("\nCSV: " + csvPath);
            }
            catch (System.Exception ex) { ed.WriteMessage("\nCSV tidak bisa ditulis: " + ex.Message); }
            ed.WriteMessage("\n" + items.Count + " batang, " + rows.Count + " jenis profil: " + Draw.N(memberKg, "#,##0.0") + " kg. " +
                nConn + " sambungan: " + Draw.N(connKg, "#,##0.0") + " kg. Total baja " + Draw.N(memberKg + connKg, "#,##0.0") + " kg (tanpa susut potong).");

            var pr = ed.GetPoint(new PromptPointOptions("\nTitik sisip tabel daftar berat (Enter = tanpa tabel)") { AllowNone = true });
            if (pr.Status != PromptStatus.OK) return;
            using (doc.LockDocument())
            {
                MakeTable(db, ed.CurrentUserCoordinateSystem, pr.Value, "DAFTAR BERAT BAJA", table);
                if (conn.Count > 0)
                {
                    double off = (table.Count + 3) * Store.TableText * 2.0 * Draw.UnitK(db);
                    MakeTable(db, ed.CurrentUserCoordinateSystem, pr.Value + new Vector3d(0, -off, 0), "DAFTAR BAHAN SAMBUNGAN", ctable);
                }
            }
        }

        public static List<MemberRecord> CollectMembers(Transaction tr, IEnumerable<ObjectId> ids)
        {
            var items = new List<MemberRecord>();
            foreach (var id in ids)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                var tag = Draw.ReadTag(tr, ent);
                if (tag == null || (tag.Kind != "batang3d" && tag.Kind != "batang2d")) continue;
                items.Add(new MemberRecord { Name = tag.Name, Family = tag.Family, LengthMm = tag.LengthMm, KgPerM = tag.KgPerM });
            }
            return items;
        }

        static string CsvPath(Document doc)
        {
            string name = "daftar_berat_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
            try
            {
                var f = doc.Database.Filename;
                if (!string.IsNullOrEmpty(f) && File.Exists(f) && !f.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase)
                    && f.IndexOf("Template", StringComparison.OrdinalIgnoreCase) < 0)
                    return Path.Combine(Path.GetDirectoryName(f), Path.GetFileNameWithoutExtension(f) + "_" + name);
            }
            catch { }
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(string.IsNullOrEmpty(docs) ? Store.Folder : docs, name);
        }

        // =====================================================================================
        // SKINFO: properti profil aktif atau batang terpilih di baris perintah
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKINFO", CommandFlags.Modal)]
        public void Info() => Guard.Run(InfoCore);
        void InfoCore()
        {
            var doc = Doc; var ed = doc.Editor;
            var peo = new PromptEntityOptions("\nPilih batang/penampang Steel Kit (Enter = profil aktif)") { AllowNone = true };
            var r = ed.GetEntity(peo);
            Profile p = Store.Current;
            double len = 0;
            if (r.Status == PromptStatus.OK)
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var tag = Draw.ReadTag(tr, (Entity)tr.GetObject(r.ObjectId, OpenMode.ForRead));
                    tr.Commit();
                    if (tag == null) { ed.WriteMessage("\nObjek itu bukan buatan Steel Kit."); return; }
                    p = FromTag(tag); len = tag.LengthMm;
                }
            }
            else if (r.Status != PromptStatus.None) return;
            ed.WriteMessage("\n--- " + p.Name + " (" + p.Info.Label + ") ---");
            foreach (var row in PropertyRows(p, Store.Density)) ed.WriteMessage("\n  " + row[0].PadRight(32) + row[1].PadLeft(12) + " " + row[2]);
            if (len > 0) ed.WriteMessage("\n  Panjang batang".PadRight(35) + Draw.N(len, "0").PadLeft(12) + " mm\n  Berat batang".PadRight(35) + Draw.N(len / 1000 * p.Properties().KgPerM(Store.Density), "0.0").PadLeft(12) + " kg");
            ed.WriteMessage("\n  Pemakaian umum: " + p.Info.Usage);
            ed.WriteMessage("\n  (Properti dihitung dari dimensi. Kapasitas batang tetap dihitung engineer struktur.)");
        }

        // =====================================================================================
        // SKCEK: pemeriksaan isi gambar (dipakai juga untuk uji otomatis)
        // =====================================================================================
        [CommandMethod("STEELKIT", "SKCEKRINCI", CommandFlags.Modal)]
        public void CheckDetail() => Guard.Run(CheckDetailCore);
        void CheckDetailCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead))
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    var tag = ent == null ? null : Draw.ReadTag(tr, ent);
                    if (tag == null || (tag.Kind != "batang3d" && tag.Kind != "sambungan")) continue;
                    var e = ent.GeometricExtents;
                    string sp = tag.Spec == null ? "" : string.Format(CultureInfo.InvariantCulture, " S=({0:0.#},{1:0.#},{2:0.#}) E=({3:0.#},{4:0.#},{5:0.#}) roll={6:0.#}",
                        tag.Spec.S.X, tag.Spec.S.Y, tag.Spec.S.Z, tag.Spec.E.X, tag.Spec.E.Y, tag.Spec.E.Z, tag.Spec.Roll);
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                        "\nSKRINCI {0} L={1:0.#} min=({2:0.#},{3:0.#},{4:0.#}) max=({5:0.#},{6:0.#},{7:0.#}){8}",
                        tag.Kind == "sambungan" ? "SAMBUNGAN:" + tag.Name : tag.Name, tag.LengthMm, e.MinPoint.X, e.MinPoint.Y, e.MinPoint.Z, e.MaxPoint.X, e.MaxPoint.Y, e.MaxPoint.Z, sp));
                }
                tr.Commit();
            }
        }

        [CommandMethod("STEELKIT", "SKCEK", CommandFlags.Modal)]
        public void Check() => Guard.Run(CheckCore);
        void CheckCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            double k = Draw.UnitK(db);
            int n3 = 0, n2 = 0, ns = 0, bad = 0, nc = 0, ncBad = 0, empty = 0, stale = 0;
            double worst = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    var tag = Draw.ReadTag(tr, ent);
                    if (tag == null) continue;
                    if (tag.Kind == "penampang") { ns++; continue; }
                    if (tag.Kind == "sambungan")
                    {
                        nc++;
                        var c = Connection.Deserialize(tag.Params);
                        if (c == null || !c.Build().AllOk) ncBad++;
                        var btr = (BlockTableRecord)tr.GetObject(((BlockReference)ent).BlockTableRecord, OpenMode.ForRead);
                        if (!btr.Cast<ObjectId>().Any()) empty++;
                        continue;
                    }
                    if (tag.Spec != null && Math.Abs(tag.Spec.S.DistanceTo(tag.Spec.E) / k - tag.LengthMm) > 0.01) stale++;
                    if (tag.Kind == "batang2d") { n2++; continue; }
                    n3++;
                    var p = FromTag(tag);
                    double expect = p.Properties().Area * k * k * tag.LengthMm * k;
                    double vol = ((Solid3d)ent).MassProperties.Volume;
                    double err = Math.Abs(vol - expect) / expect;
                    if (tag.Spec != null && tag.Spec.Cuts.Count > 0) err = vol <= expect * 1.001 && vol >= expect * 0.5 ? 0 : err;
                    worst = Math.Max(worst, err);
                    if (err > 0.005) { bad++; ed.WriteMessage("\nSKCEK SELISIH " + tag.Name + ": volume " + vol.ToString("0.###", CultureInfo.InvariantCulture) + " vs " + expect.ToString("0.###", CultureInfo.InvariantCulture)); }
                }
                tr.Commit();
            }
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nSKCEK batang3d={0} batang2d={1} penampang={2} volume_selisih_maks={3:0.0000}% gagal={4} satuan={5} sambungan={6} sambungan_aturan_gagal={7} blok_kosong={8} data_titik_tidak_cocok={9}",
                n3, n2, ns, worst * 100, bad, Units.Name((int)db.Insunits), nc, ncBad, empty, stale));
        }
    }
}
