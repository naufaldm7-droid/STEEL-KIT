using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelKit.Core;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(SteelKit.Acad.QuantityCommands))]

namespace SteelKit.Acad
{
    /// <summary>Data model untuk quantity takeoff, browser, dan layout: batang + sambungan dengan level/kategori/marking.</summary>
    public static class ModelData
    {
        public static LevelTable Levels => LevelTable.Parse(Store.Get("level", "Level 0:0"));

        public class Item
        {
            public ObjectId Id; public string Handle; public Draw.TagData Tag; public TakeoffMember Member; public Connection Conn; public ConnResult Result;
        }

        public static List<Item> Read(Transaction tr, Database db, IEnumerable<ObjectId> ids = null)
        {
            double k = Draw.UnitK(db);
            var lv = Levels;
            var list = new List<Item>();
            ids = ids ?? ((BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead)).Cast<ObjectId>().ToList();
            foreach (var id in ids)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                var t = Draw.ReadTag(tr, e);
                if (t == null) continue;
                var it = new Item { Id = id, Handle = e.Handle.ToString(), Tag = t };
                if (t.Kind == "batang3d" || t.Kind == "batang2d")
                {
                    var p = Commands.FromTag(t);
                    var pr = p.Properties();
                    Vec3 s = Vec3.Zero, en = Vec3.UnitX * t.LengthMm;
                    if (t.Spec != null) { s = Draw.V(t.Spec.S) * (1 / k); en = Draw.V(t.Spec.E) * (1 / k); }
                    var cl = Categories.Classify(p.Family, s, en);
                    it.Member = new TakeoffMember
                    {
                        Handle = it.Handle, Section = t.Name, Family = t.Family, LengthMm = t.LengthMm, KgPerM = t.KgPerM, PaintPerM = pr.PaintAreaPerM,
                        Role = cl.role,
                        Category = !string.IsNullOrEmpty(t.Spec?.Category) ? t.Spec.Category : cl.category,
                        Level = !string.IsNullOrEmpty(t.Spec?.Level) ? t.Spec.Level : lv.Of(Math.Min(s.Z, en.Z)),
                        Mark = t.Spec?.Mark ?? "",
                        Signature = t.Spec != null && t.Spec.Cuts.Count > 0 ? Signature(t.Spec, p, k) : "",
                    };
                    list.Add(it);
                }
                else if (t.Kind == "sambungan")
                {
                    it.Conn = Connection.Deserialize(t.Params);
                    if (it.Conn == null) continue;
                    try { it.Result = it.Conn.Build(); } catch { continue; }
                    list.Add(it);
                }
            }
            return list;
        }

        /// <summary>Pola lubang/potongan batang dalam koordinat lokalnya (lihat PartSignature).</summary>
        static string Signature(Draw.MemberSpec m, Profile p, double k)
        {
            try
            {
                Draw.MemberAxes(m.E - m.S, m.Roll, out Vector3d x, out Vector3d y, out Vector3d z);
                Vec3 L(Vector3d v) => new Vec3(v.DotProduct(x), v.DotProduct(y), v.DotProduct(z));
                var local = m.Cuts.Select(c => new Cutter
                {
                    Cylinder = c.Cylinder, Radius = c.Radius / k, X0 = c.X0 / k, X1 = c.X1 / k, Y0 = c.Y0 / k, Y1 = c.Y1 / k, Z0 = c.Z0 / k, Z1 = c.Z1 / k,
                    F = new Frame(L(Draw.P(c.F.O) - m.S) * (1 / k), L(Draw.D(c.F.U)), L(Draw.D(c.F.V)), L(Draw.D(c.F.W))),
                });
                return PartSignature.Of(p, m.Anchor, m.S.DistanceTo(m.E) / k, local);
            }
            catch { return ""; }
        }

        public static TakeoffConnection ToTakeoff(Item it)
        {
            var r = it.Result;
            double plate = r.Parts.Where(p => p.Kind == PartKind.Pelat || p.Kind == PartKind.Pengaku).Sum(p => SectionCalc.Compute(new SectionShape { Outer = p.Outline }).Area) * 2 / 1e6;
            double grout = 0;
            if (it.Conn is BasePlate bp && bp.Dudukan == BaseSupport.Beton) grout = bp.LebarPelat * bp.PanjangPelat * bp.Grout * 1e-9;
            return new TakeoffConnection { Type = it.Conn.Type, Boq = r.Boq, GroutM3 = grout, PlateAreaM2 = plate };
        }
    }

    public class QuantityCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        [CommandMethod("STEELKIT", "SKEXCEL", CommandFlags.Modal)]
        public void Excel() => Guard.Run(ExcelCore);
        void ExcelCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            string defName = string.IsNullOrEmpty(db.Filename) ? "STEEL" : Path.GetFileNameWithoutExtension(db.Filename);
            var ps = ed.GetString(new PromptStringOptions("\nNama proyek <" + defName + ">") { AllowSpaces = true });
            if (ps.Status != PromptStatus.OK && ps.Status != PromptStatus.None) return;
            string project = string.IsNullOrWhiteSpace(ps.StringResult) ? defName : ps.StringResult.Trim();
            Store.Set("proyek", project);   // dipakai kotak judul SKLAYOUT
            var pd = ed.GetDouble(new PromptDoubleOptions("\nAllowance potong / sisa (%)") { DefaultValue = Store.Allowance, UseDefaultValue = true, AllowNegative = false });
            if (pd.Status != PromptStatus.OK) return;
            Store.Allowance = pd.Value;
            List<ModelData.Item> items;
            using (var tr = db.TransactionManager.StartTransaction()) { items = ModelData.Read(tr, db); tr.Commit(); }
            var members = items.Where(i => i.Member != null).Select(i => i.Member).ToList();
            var conns = items.Where(i => i.Conn != null).Select(ModelData.ToTakeoff).ToList();
            if (members.Count == 0 && conns.Count == 0) { ed.WriteMessage("\nBelum ada batang / sambungan Steel Kit di model."); return; }
            var wb = Takeoff.Build(project, members, conns, Store.Allowance);
            string path = OutPath(db, project);
            try { wb.Save(path); }
            catch (IOException) { path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_" + DateTime.Now.ToString("HHmmss") + ".xlsx"); wb.Save(path); }
            double kg = members.Sum(m => m.KgPerM * m.LengthMm / 1000) + conns.Sum(c => c.SteelKg);
            ed.WriteMessage("\nQuantity takeoff (Excel): " + path +
                "\n  " + members.Count + " batang, " + conns.Count + " sambungan, baja bersih ±" + Draw.N(kg, "#,##0") + " kg (+" + Draw.N(Store.Allowance) + "% allowance di lembar Summary).");
            if (!Plugin.IsConsole) { try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { } }
        }

        static string OutPath(Database db, string project)
        {
            string safe = new string(project.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
            string name = safe + "_QTO.xlsx";
            try
            {
                var f = db.Filename;
                if (!string.IsNullOrEmpty(f) && File.Exists(f) && !f.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase) && f.IndexOf("Template", StringComparison.OrdinalIgnoreCase) < 0)
                    return Path.Combine(Path.GetDirectoryName(f), name);
            }
            catch { }
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(string.IsNullOrEmpty(docs) ? Store.Folder : docs, name);
        }

        [CommandMethod("STEELKIT", "SKLEVEL", CommandFlags.Modal)]
        public void Level() => Guard.Run(LevelCore);
        void LevelCore()
        {
            var ed = Doc.Editor;
            ed.WriteMessage("\nLevel sekarang: " + ModelData.Levels.Serialize().Replace(";", "  "));
            var ps = ed.GetString(new PromptStringOptions("\nTabel level baru (nama:elevasi mm; ...), mis. Base:0;Lt.2:4000;Atap:8000") { AllowSpaces = true });
            if (ps.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(ps.StringResult)) return;
            var lt = LevelTable.Parse(ps.StringResult);
            Store.Set("level", lt.Serialize());
            ed.WriteMessage("\nLevel disimpan: " + lt.Serialize().Replace(";", "  ") + ". Batang otomatis masuk level tertinggi di bawah titik terendahnya.");
        }
    }
}
