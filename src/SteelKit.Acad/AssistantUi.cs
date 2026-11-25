using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using SteelKit.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcCore = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Font = System.Drawing.Font;
using FlowDirection = System.Windows.Forms.FlowDirection;

[assembly: CommandClass(typeof(SteelKit.Acad.AssistantCommands))]

namespace SteelKit.Acad
{
    public class AssistantCommands
    {
        static Document Doc => AcCore.DocumentManager.MdiActiveDocument;

        [CommandMethod("STEELKIT", "SKASISTEN", CommandFlags.Modal)]
        public void Assist() => Guard.Run(AssistCore);
        void AssistCore()
        {
            if (Plugin.IsConsole) { TextAssist(); return; }
            var w = new Ui.AssistantWindow();
            if (AcApp.ShowModalWindow(w) != true || w.Plan == null) return;
            Place(w.Plan, w.AutoConnect);
        }

        /// <summary>Versi baris perintah (juga dipakai uji otomatis): kalimat -> usulan -> pasang.</summary>
        [CommandMethod("STEELKIT", "-SKASISTEN", CommandFlags.Modal)]
        public void AssistText() => Guard.Run(TextAssist);
        static void TextAssist()
        {
            var ed = Doc.Editor;
            var ps = ed.GetString(new PromptStringOptions("\nTulis maksud Anda (mis. portal bentang 20 m tinggi 6 m 5 bay kolom WF400 rafter WF350)") { AllowSpaces = true });
            if (ps.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(ps.StringResult)) return;
            var plan = Assistant.Parse(ps.StringResult);
            ed.WriteMessage("\nAsisten: " + (string.IsNullOrEmpty(plan.Summary) ? "belum dikenali" : plan.Summary));
            foreach (var u in plan.Unknown) ed.WriteMessage("\n  ? " + u);
            foreach (var a in plan.Assumptions) ed.WriteMessage("\n  asumsi: " + a);
            if (plan.Members.Count == 0) return;
            var pko = new PromptKeywordOptions("\nPasang " + plan.Members.Count + " batang ini? [Ya/SambungJuga/Tidak]");
            pko.Keywords.Add("Ya"); pko.Keywords.Add("SambungJuga"); pko.Keywords.Add("Tidak"); pko.Keywords.Default = "SambungJuga";
            var kr = ed.GetKeywords(pko);
            if (kr.Status != PromptStatus.OK || kr.StringResult == "Tidak") return;
            Place(plan, kr.StringResult == "SambungJuga");
        }

        static void Place(AssistantPlan plan, bool connect)
        {
            var doc = Doc; var ed = doc.Editor; var db = doc.Database;
            var pr = ed.GetPoint("\nTitik dasar (sudut kiri bawah struktur)");
            if (pr.Status != PromptStatus.OK) return;
            var ucs = ed.CurrentUserCoordinateSystem;
            double k = Draw.UnitK(db);
            var ids = new List<ObjectId>();
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                foreach (var m in plan.Members)
                {
                    var p = Store.Catalog.Find(m.Profile);
                    if (p == null) continue;
                    var spec = new Draw.MemberSpec
                    {
                        Profile = p, Anchor = m.Anchor, Roll = m.Roll, Normal = Vector3d.ZAxis,
                        S = new Point3d(pr.Value.X + m.S.X * k, pr.Value.Y + m.S.Y * k, pr.Value.Z + m.S.Z * k).TransformBy(ucs),
                        E = new Point3d(pr.Value.X + m.E.X * k, pr.Value.Y + m.E.Y * k, pr.Value.Z + m.E.Z * k).TransformBy(ucs),
                    };
                    var id = MemberBuild.Create3D(tr, db, space, spec);
                    if (!id.IsNull) ids.Add(id);
                }
                tr.Commit();
            }
            ed.WriteMessage("\nAsisten: " + ids.Count + " batang dipasang. Ubah lewat grip, SKUBAH, atau Steel Kit Browser (SKBROWSER).");
            if (connect && ids.Count > 0) ConnCommands.AutoConnect(doc, ids.ToArray());
        }
    }
}
