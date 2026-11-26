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

[assembly: CommandClass(typeof(SteelKit.Acad.ClashCommands))]

namespace SteelKit.Acad
{
    /// <summary>
    /// Cek tabrakan 3D: batang dengan batang, batang dengan benda sambungan (pelat, baut, mur), dan antar sambungan.
    /// Benda dalam satu sambungan tidak dicek terhadap sesamanya (sudah dirancang bersama), las dan beton/grout diabaikan.
    /// Hasil: daftar di baris perintah + solid irisan di layer SK-BENTROK untuk dilihat.
    /// </summary>
    public class ClashCommands
    {
        public const string LayClash = "SK-BENTROK";

        class Body
        {
            public Solid3d Solid; public Extents3d Box; public string Owner; public string Name; public bool Member; public short Color; public bool Rod; public HashSet<string> Links;
        }

        public class Hit { public string A, B; public double VolumeMm3; public Point3d At; public Vector3d Size; }

        [CommandMethod("STEELKIT", "SKBENTROK", CommandFlags.Modal)]
        public void ClashCmd() => Guard.Run(() => Run(AcCore.DocumentManager.MdiActiveDocument, true));

        /// <summary>Jalankan cek; mengembalikan daftar tabrakan. draw = gambar solid irisan di layer SK-BENTROK.</summary>
        public static List<Hit> Run(Document doc, bool draw)
        {
            var ed = doc.Editor; var db = doc.Database;
            double k = Draw.UnitK(db);
            double minVol = 20 * k * k * k;     // irisan < 20 mm³ dianggap sentuhan / pembulatan
            var hits = new List<Hit>();
            var bodies = new List<Body>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                // hapus hasil cek sebelumnya
                foreach (ObjectId id in ms)
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e0 && e0.Layer == LayClash) { e0.UpgradeOpen(); e0.Erase(); }
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent == null || ent.IsErased) continue;
                    var tag = Draw.ReadTag(tr, ent);
                    if (tag == null) continue;
                    string owner = ent.Handle.ToString();
                    if (tag.Kind == "batang3d" && ent is Solid3d sol)
                    {
                        var b = new Body { Solid = (Solid3d)sol.Clone(), Owner = owner, Member = true, Name = tag.Name, Rod = tag.Family == Family.RD };
                        try { b.Box = b.Solid.GeometricExtents; bodies.Add(b); } catch { b.Solid.Dispose(); }
                    }
                    else if (tag.Kind == "sambungan" && ent is BlockReference br)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        var links = new HashSet<string>((tag.Links ?? new string[0]).Where(x => !string.IsNullOrEmpty(x) && x != "S" && x != "E"), StringComparer.OrdinalIgnoreCase);
                        foreach (ObjectId pid in btr)
                        {
                            if (!(tr.GetObject(pid, OpenMode.ForRead) is Solid3d ps)) continue;
                            short col = (short)ps.ColorIndex;
                            if (col == 200 || col == 9 || col == 252 || col == 4) continue;   // las, grout, beton, potongan batang contoh
                            var c = (Solid3d)ps.Clone();
                            c.TransformBy(br.BlockTransform);
                            var b = new Body { Solid = c, Owner = owner, Member = false, Name = tag.Name, Color = col, Links = links };
                            try { b.Box = c.GeometricExtents; bodies.Add(b); } catch { c.Dispose(); }
                        }
                    }
                }
                // pasangan kandidat: kotak batas saling tumpang (sedikit dikecilkan supaya sentuhan muka tidak ikut)
                double eps = 0.05 * k;
                var order = Enumerable.Range(0, bodies.Count).OrderBy(i => bodies[i].Box.MinPoint.X).ToList();
                for (int ii = 0; ii < order.Count; ii++)
                {
                    var a = bodies[order[ii]];
                    for (int jj = ii + 1; jj < order.Count; jj++)
                    {
                        var b = bodies[order[jj]];
                        if (b.Box.MinPoint.X > a.Box.MaxPoint.X - eps) break;
                        if (a.Owner == b.Owner) continue;
                        if (Expected(a, b) || Expected(b, a)) continue;
                        if (!Overlap(a.Box, b.Box, eps)) continue;
                        double vol = Intersect(a.Solid, b.Solid, out Solid3d piece);
                        if (vol <= minVol) { piece?.Dispose(); continue; }
                        var at = piece.GeometricExtents;
                        hits.Add(new Hit
                        {
                            A = Label(a), B = Label(b), VolumeMm3 = vol / (k * k * k),
                            At = new Point3d((at.MinPoint.X + at.MaxPoint.X) / 2, (at.MinPoint.Y + at.MaxPoint.Y) / 2, (at.MinPoint.Z + at.MaxPoint.Z) / 2),
                            Size = (at.MaxPoint - at.MinPoint) / k,
                        });
                        if (draw)
                        {
                            Layer(tr, db);
                            piece.Layer = LayClash;
                            ms.AppendEntity(piece); tr.AddNewlyCreatedDBObject(piece, true);
                        }
                        else piece.Dispose();
                    }
                }
                foreach (var b in bodies) b.Solid.Dispose();
                tr.Commit();
            }
            if (hits.Count == 0)
                ed.WriteMessage("\nSKBENTROK: tidak ada tabrakan 3D antar batang dan benda sambungan (" + bodies.Count + " benda dicek; las, beton, grout tidak ikut).");
            else
            {
                ed.WriteMessage("\nSKBENTROK: " + hits.Count + " tabrakan ditemukan. Irisannya digambar di layer " + LayClash + ":");
                foreach (var g in hits.GroupBy(h => h.A + " x " + h.B).OrderByDescending(g => g.Sum(h => h.VolumeMm3)).Take(30))
                    ed.WriteMessage(string.Format(CultureInfo.InvariantCulture, "\n  {0}: {1} titik, irisan {2:0} mm³, contoh di ({3:0},{4:0},{5:0}) ukuran {6:0}x{7:0}x{8:0} mm",
                        g.Key, g.Count(), g.Sum(h => h.VolumeMm3), g.First().At.X / k, g.First().At.Y / k, g.First().At.Z / k, g.First().Size.X, g.First().Size.Y, g.First().Size.Z));
            }
            ed.WriteMessage("\nSKBENTROK jumlah=" + hits.Count);
            return hits;
        }

        /// <summary>
        /// Tumpang tindih yang memang begitu bentuknya: mur, ring, turnbuckle, clevis yang terpasang pada rod
        /// (rod berulir masuk ke benda sambungan miliknya sendiri).
        /// </summary>
        static bool Expected(Body m, Body c) => m.Member && m.Rod && !c.Member && c.Links != null && c.Links.Contains(m.Owner) && c.Color == 8;

        static string Label(Body b) => b.Member ? "batang " + b.Name : "sambungan " + b.Name + (b.Color == 8 ? " (baut/mur)" : b.Color == 6 ? " (angkur)" : " (pelat)");

        static bool Overlap(Extents3d a, Extents3d b, double eps)
            => a.MinPoint.X < b.MaxPoint.X - eps && b.MinPoint.X < a.MaxPoint.X - eps
            && a.MinPoint.Y < b.MaxPoint.Y - eps && b.MinPoint.Y < a.MaxPoint.Y - eps
            && a.MinPoint.Z < b.MaxPoint.Z - eps && b.MinPoint.Z < a.MaxPoint.Z - eps;

        /// <summary>Volume irisan dua solid (tanpa mengubah aslinya). piece = solid irisan bila ada.</summary>
        static double Intersect(Solid3d a, Solid3d b, out Solid3d piece)
        {
            piece = null;
            try
            {
                if (!a.CheckInterference(b)) return 0;
                var x = (Solid3d)a.Clone();
                using (var y = (Solid3d)b.Clone()) x.BooleanOperation(BooleanOperationType.BoolIntersect, y);
                double v = 0;
                try { v = x.MassProperties.Volume; } catch { }
                if (v <= 0) { x.Dispose(); return 0; }
                piece = x;
                return v;
            }
            catch { piece?.Dispose(); piece = null; return 0; }
        }

        static void Layer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayClash)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = LayClash, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6), IsPlottable = false };
            lt.Add(rec); tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
