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

[assembly: CommandClass(typeof(SteelKit.Acad.ConnCommands))]

namespace SteelKit.Acad
{
    public class ConnCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        public class MemberRef { public ObjectId Id; public Draw.MemberSpec Spec; public MemberGeom Geom; public string Handle; }

        public static List<MemberRef> Members(Transaction tr, Database db, IEnumerable<ObjectId> ids)
        {
            double k = Draw.UnitK(db);
            var list = new List<MemberRef>();
            foreach (var id in ids)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                var t = Draw.ReadTag(tr, e);
                if (t?.Spec == null || t.Kind != "batang3d") continue;
                string h = e.Handle.ToString();
                list.Add(new MemberRef { Id = id, Spec = t.Spec, Handle = h, Geom = t.Spec.Geom(k, h) });
            }
            return list;
        }

        public static IEnumerable<ObjectId> SpaceIds(Transaction tr, Database db)
            => ((BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead)).Cast<ObjectId>().ToList();

        /// <summary>Kunci yang sudah terpakai: ujung batang (handle:ujung) dan pasangan gording-rafter.</summary>
        static IEnumerable<string> Keys(ConnType t, string[] links)
        {
            if (links == null || links.Length < 4) yield break;
            if (t == ConnType.CleatGording || t == ConnType.FlangeBrace || t == ConnType.BalokTumpuk || t == ConnType.SilangBracing)
            { yield return t + "|" + links[0] + "|" + links[2]; yield break; }
            for (int i = 0; i + 1 < links.Length; i += 2)
                if (!string.IsNullOrEmpty(links[i]) && !string.IsNullOrEmpty(links[i + 1])) yield return links[i] + ":" + links[i + 1];
            if (t == ConnType.LasLangsung || t == ConnType.KolomHollow || t == ConnType.PelatUjung || t == ConnType.FinPlate || t == ConnType.SikuGanda)
                yield return links[2] + ":" + links[3];
        }

        public static string[] LinksOf(Joint j, IList<MemberRef> ms)
        {
            var l = new List<string>
            {
                j.Support >= 0 ? ms[j.Support].Handle : "",
                j.Support >= 0 ? (j.SupportKey ?? (j.SupportEndUsed ? End(j.SupportAtStart) : "")) : "",
                j.Supported >= 0 ? ms[j.Supported].Handle : "",
                j.Supported >= 0 && j.Type != ConnType.CleatGording && j.Type != ConnType.BalokTumpuk && j.Type != ConnType.SilangBracing ? End(j.SupportedAtStart) : "",
            };
            for (int i = 0; i < j.More.Count; i++) { l.Add(ms[j.More[i]].Handle); l.Add(End(j.MoreAtStart[i])); }
            return l.ToArray();
        }

        static bool Taken(HashSet<string> used, ConnType t, string[] links) => Keys(t, links).Any(used.Contains);

        static HashSet<string> ExistingKeys(Transaction tr, Database db)
        {
            var set = new HashSet<string>();
            foreach (var id in SpaceIds(tr, db))
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var t = Draw.ReadTag(tr, br);
                if (t?.Kind != "sambungan" || t.Links == null) continue;
                var c = Connection.Deserialize(t.Params);
                if (c != null) foreach (var key in Keys(c.Type, t.Links)) set.Add(key);
            }
            return set;
        }

        /// <summary>Buat sambungan + potong batang. links = [penopang, ujung, ditopang, ujung].</summary>
        public static ObjectId Place(Transaction tr, Database db, BlockTableRecord space, Connection c, Frame fMm, string[] links, bool trim, out ConnResult r)
        {
            r = c.Build();
            var id = ConnDraw.Insert(tr, db, space, c, r, fMm, links);
            if (trim && links != null) ApplyTrims(tr, db, r, fMm, links);
            return id;
        }

        public static void ApplyTrims(Transaction tr, Database db, ConnResult r, Frame fMm, string[] links)
        {
            if (links.Length < 4) return;
            for (int i = 0; 2 * i + 1 < links.Length; i++)
            {
                Vec3? p = i == 0 ? r.TrimSupport : i == 1 ? r.TrimSupported : (Vec3?)null;
                if (r.TrimMembers.TryGetValue(i, out Vec3 q)) p = q;
                // pemotong khusus batang ini + lubang baut (hanya mengenai batang yang dilalui baut)
                var cuts = (i == 0 ? r.CutSupport : i == 1 ? r.CutSupported : new List<Cutter>()).Concat(r.BoltHoles).ToList();
                Vec3? shift = r.ShiftMembers.TryGetValue(i, out Vec3 sv) ? sv : (Vec3?)null;
                if (p.HasValue || shift.HasValue || (cuts != null && cuts.Count > 0))
                    ConnDraw.TrimMember(tr, db, links[2 * i], links[2 * i + 1], p, fMm, cuts, shift);
            }
        }

        static string End(bool atStart) => atStart ? "S" : "E";

        // ======================================================================== SKSAMBUNGAUTO
        [CommandMethod("STEELKIT", "SKSAMBUNGAUTO", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void Auto() => Guard.Run(AutoCore);
        void AutoCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            double k = Draw.UnitK(db);
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPilih batang 3D yang disambung (Enter = semua batang di gambar)" });
            if (sel.Status != PromptStatus.OK && sel.Status != PromptStatus.Error && sel.Status != PromptStatus.None) return;
            AutoConnect(doc, sel.Status == PromptStatus.OK ? sel.Value.GetObjectIds() : null);
        }

        /// <summary>Sambungan otomatis untuk batang tertentu (null = semua batang di ruang gambar).</summary>
        public static void AutoConnect(Document doc, ObjectId[] only)
        {
            var ed = doc.Editor; var db = doc.Database;
            var counts = new Dictionary<ConnType, int>();
            int skipped = 0, failing = 0;
            var notes = new List<string>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ids = only ?? SpaceIds(tr, db);
                var ms = Members(tr, db, ids);
                if (ms.Count == 0) { ed.WriteMessage("\nTidak ada batang 3D Steel Kit (SKBATANG) yang terpilih."); return; }
                var joints = JointFinder.Find(ms.Select(m => m.Geom).ToList());
                var existing = ExistingKeys(tr, db);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var j in joints)
                {
                    var links = LinksOf(j, ms);
                    if (Taken(existing, j.Type, links)) { skipped++; continue; }
                    foreach (var key in Keys(j.Type, links)) existing.Add(key);
                    try
                    {
                        Place(tr, db, space, j.Conn, j.Frame, links, true, out ConnResult r);
                        counts[j.Type] = counts.TryGetValue(j.Type, out int n) ? n + 1 : 1;
                        if (!r.AllOk) failing++;
                        foreach (var nt in j.Notes) notes.Add(Connection.Label(j.Type) + ": " + nt);
                    }
                    catch (System.Exception ex) { notes.Add(Connection.Label(j.Type) + ": gagal dibuat (" + ex.Message + ")"); }
                }
                tr.Commit();
            }
            ed.WriteMessage("\nSambungan otomatis dibuat: " + (counts.Count == 0 ? "tidak ada" : string.Join(", ", counts.Select(kv => kv.Value + " " + Connection.Label(kv.Key)))));
            if (skipped > 0) ed.WriteMessage("\n" + skipped + " sambungan sudah ada, dilewati.");
            foreach (var n in notes.Distinct()) ed.WriteMessage("\n  Catatan: " + n);
            ed.WriteMessage(failing == 0
                ? "\nSemua sambungan memenuhi aturan jarak/ukuran minimum (praktik umum). Kekuatan tetap dihitung engineer."
                : "\nPERHATIAN: " + failing + " sambungan punya aturan yang tidak terpenuhi. Buka dengan SKSAMBUNG untuk diatur.");
            ed.WriteMessage("\nUbah jenis/dimensi: SKSAMBUNG lalu pilih sambungannya. Gambar detail: SKDETAIL.");
        }

        // ======================================================================== SKSAMBUNGTITIK
        [CommandMethod("STEELKIT", "SKSAMBUNGTITIK", CommandFlags.Modal)]
        public void AtPoint() => Guard.Run(AtPointCore);
        void AtPointCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var pko = new PromptKeywordOptions("\nJenis sambungan [" + string.Join("/", KwMap.Keys) + "]");
            foreach (var kw in KwMap.Keys) pko.Keywords.Add(kw);
            pko.Keywords.Default = "PelatDasar";
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK) return;
            var type = FromKeyword(kr.StringResult);
            var pr = ed.GetPoint("\nTitik sambungan");
            if (pr.Status != PromptStatus.OK) return;
            var c = Connection.Create(type);
            c.Recommend();
            double k = Draw.UnitK(db);
            var ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
            var o = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            var f = new Frame(Draw.V(o) * (1 / k), Draw.V(ucs.Xaxis), Draw.V(ucs.Yaxis), Draw.V(ucs.Zaxis));
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Place(tr, db, space, c, f, null, false, out ConnResult r);
                tr.Commit();
                ed.WriteMessage("\n" + c.Title + " dibuat. " + ConnDraw.Summary(r));
            }
        }

        public static readonly Dictionary<string, ConnType> KwMap = new Dictionary<string, ConnType>
        {
            ["PelatDasar"] = ConnType.PelatDasar, ["PelatUjung"] = ConnType.PelatUjung, ["FinPlate"] = ConnType.FinPlate,
            ["SikuGanda"] = ConnType.SikuGanda, ["Splice"] = ConnType.SambunganPelatUjung, ["Lutut"] = ConnType.LututHaunch,
            ["Gording"] = ConnType.CleatGording, ["Buhul"] = ConnType.PelatBuhul, ["Rod"] = ConnType.BracingRod,
            ["Turnbuckle"] = ConnType.Turnbuckle, ["BuhulBracing"] = ConnType.BracingBuhul, ["Silang"] = ConnType.SilangBracing,
            ["FlangeBrace"] = ConnType.FlangeBrace, ["SagRod"] = ConnType.SagRod, ["PelatTutup"] = ConnType.PelatTutup,
            ["Dudukan"] = ConnType.Dudukan, ["MomenLas"] = ConnType.MomenLas, ["MomenPelat"] = ConnType.MomenPelatSayap,
            ["KolomHollow"] = ConnType.KolomHollow, ["Tumpuk"] = ConnType.BalokTumpuk, ["KolomAngin"] = ConnType.KolomAngin,
            ["Konsol"] = ConnType.KonsolCrane, ["Beton"] = ConnType.PelatBeton, ["SplicePelat"] = ConnType.SplicePelatSambung,
            ["GordingSambung"] = ConnType.GordingSambung, ["Truss"] = ConnType.BuhulTruss, ["LasLangsung"] = ConnType.LasLangsung,
            ["AnakTangga"] = ConnType.AnakTangga,
        };

        public static ConnType FromKeyword(string kw) => KwMap.TryGetValue(kw, out ConnType t) ? t : ConnType.PelatDasar;

        // ======================================================================== SKSAMBUNGPARAM
        [CommandMethod("STEELKIT", "SKSAMBUNGPARAM", CommandFlags.Modal)]
        public void SetParam() => Guard.Run(SetParamCore);
        void SetParamCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var er = ed.GetEntity("\nPilih sambungan Steel Kit");
            if (er.Status != PromptStatus.OK) return;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(er.ObjectId, OpenMode.ForWrite) as BlockReference;
                var t = br == null ? null : Draw.ReadTag(tr, br);
                var c = t?.Kind == "sambungan" ? Connection.Deserialize(t.Params) : null;
                if (c == null) { ed.WriteMessage("\nObjek itu bukan sambungan Steel Kit."); return; }
                var names = c.GetType().GetProperties().Where(p => p.CanWrite).Select(p => p.Name).ToList();
                ed.WriteMessage("\n" + c.Title + ": " + c.Serialize().Replace(";", "  "));
                var sr = ed.GetString(new PromptStringOptions("\nNama parameter (" + string.Join(", ", names) + ")") { AllowSpaces = false });
                if (sr.Status != PromptStatus.OK) return;
                string name = names.FirstOrDefault(n => string.Equals(n, sr.StringResult, StringComparison.OrdinalIgnoreCase));
                if (name == null) { ed.WriteMessage("\nParameter tidak dikenal."); return; }
                var vr = ed.GetString(new PromptStringOptions("\nNilai baru untuk " + name + " (ketik REKOMENDASI untuk isi ulang semua)") { AllowSpaces = true });
                if (vr.Status != PromptStatus.OK) return;
                var nc = Connection.Deserialize(c.Serialize() + ";" + name + "=" + vr.StringResult.Trim());
                if (string.Equals(vr.StringResult.Trim(), "REKOMENDASI", StringComparison.OrdinalIgnoreCase)) { nc = c; nc.Recommend(); }
                Rebuild(tr, db, br, nc, t.Links);
                tr.Commit();
                ed.WriteMessage("\nSambungan diperbarui. " + ConnDraw.Summary(nc.Build()));
            }
        }

        public static void Rebuild(Transaction tr, Database db, BlockReference br, Connection c, string[] links)
        {
            var r = c.Build();
            ConnDraw.Replace(tr, br, c, r, links);
            if (links != null && links.Length >= 4) ApplyTrims(tr, db, r, ConnDraw.FrameOf(br, Draw.UnitK(db)), links);
        }

        // ======================================================================== SKDETAIL
        [CommandMethod("STEELKIT", "SKDETAIL", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public void DetailCmd() => Guard.Run(DetailCmdCore);
        void DetailCmdCore()
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var sel = ed.GetSelection(new PromptSelectionOptions { MessageForAdding = "\nPilih sambungan untuk digambar detailnya" });
            if (sel.Status != PromptStatus.OK) return;
            var pr = ed.GetPoint("\nTitik kiri bawah area detail");
            if (pr.Status != PromptStatus.OK) return;
            var conns = new List<Connection>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in sel.Value.GetObjectIds())
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var t = Draw.ReadTag(tr, br);
                    if (t?.Kind != "sambungan") continue;
                    var c = Connection.Deserialize(t.Params);
                    if (c != null && !conns.Any(x => LayoutCommands.DetailKey(x) == LayoutCommands.DetailKey(c))) conns.Add(c);   // sambungan sejenis cukup satu detail
                }
                tr.Commit();
            }
            if (conns.Count == 0) { ed.WriteMessage("\nTidak ada sambungan Steel Kit yang terpilih."); return; }
            var p = pr.Value.TransformBy(ed.CurrentUserCoordinateSystem);
            double x = p.X;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var c in conns)
                {
                    double w = ConnDraw.Detail(tr, db, space, c, new Point3d(x, p.Y, p.Z));
                    x += w + 20 * ConnDraw.TextHeight(c.Build(), Draw.UnitK(db));
                }
                tr.Commit();
            }
            ed.WriteMessage("\n" + conns.Count + " detail sambungan digambar (sambungan identik digabung).");
        }

        // ======================================================================== daftar bahan
        public static List<BoqLine> ConnectionBoq(Transaction tr, IEnumerable<ObjectId> ids, out int count)
        {
            var all = new List<BoqLine>();
            count = 0;
            foreach (var id in ids)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var t = Draw.ReadTag(tr, br);
                if (t?.Kind != "sambungan") continue;
                var c = Connection.Deserialize(t.Params);
                if (c == null) continue;
                count++;
                foreach (var b in c.Build().Boq)
                {
                    var l = all.FirstOrDefault(x => x.Item == b.Item && x.Spec == b.Spec);
                    if (l == null) all.Add(new BoqLine { Item = b.Item, Spec = b.Spec, Count = b.Count, UnitKg = b.UnitKg });
                    else l.Count += b.Count;
                }
            }
            return all.OrderBy(b => b.Item).ThenBy(b => b.Spec).ToList();
        }
    }
}
