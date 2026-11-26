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
    /// <summary>Pembantu menggambar: layer, linetype, polyline dari loop, region, data batang.</summary>
    public static class Draw
    {
        public const string Dict = "STEELKIT";

        public const string LayMember3D = "SK-BATANG-3D";
        public const string LayMember2D = "SK-BATANG-2D";
        public const string LayHidden = "SK-TERSEMBUNYI";
        public const string LayAxis = "SK-AS";
        public const string LaySection = "SK-PENAMPANG";
        public const string LayHatch = "SK-ARSIR";
        public const string LayDim = "SK-DIMENSI";
        public const string LayText = "SK-TEKS";
        public const string LayTable = "SK-TABEL";

        // warna bawaan tanpa kuning (2), merah (1), dan hijau (3)
        static readonly Dictionary<string, (short color, string ltype)> LayerDefs = new Dictionary<string, (short, string)>
        {
            [LayMember3D] = (4, "Continuous"),
            [LayMember2D] = (4, "Continuous"),
            [LayHidden] = (8, "HIDDEN"),
            [LayAxis] = (6, "CENTER"),
            [LaySection] = (4, "Continuous"),
            [LayHatch] = (8, "Continuous"),
            [LayDim] = (140, "Continuous"),
            [LayText] = (7, "Continuous"),
            [LayTable] = (7, "Continuous"),
        };

        public static double UnitK(Database db) => Units.PerMm((int)db.Insunits);

        public static ObjectId Layer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            var def = LayerDefs.TryGetValue(name, out var d) ? d : ((short)7, "Continuous");
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, def.Item1) };
            var ltId = Linetype(tr, db, def.Item2);
            if (!ltId.IsNull) rec.LinetypeObjectId = ltId;
            lt.UpgradeOpen();
            var id = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            Std.LayerWeights(tr, db);
            return id;
        }

        public static ObjectId Linetype(Transaction tr, Database db, string name)
        {
            var t = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (t.Has(name)) return t[name];
            foreach (var file in new[] { "acadiso.lin", "acad.lin" })
            {
                try { db.LoadLineTypeFile(name, file); } catch { continue; }
                if (t.Has(name)) return t[name];
            }
            return ObjectId.Null;
        }

        public static Polyline Poly(Loop loop, double k, double dx = 0, double dy = 0)
        {
            var pl = new Polyline();
            int i = 0;
            foreach (var v in loop.Vertices)
                pl.AddVertexAt(i++, new Point2d((v.X + dx) * k, (v.Y + dy) * k), v.Bulge, 0, 0);
            pl.Closed = true;
            return pl;
        }

        /// <summary>Region penampang (luar dikurangi lubang). Pemanggil wajib Dispose bila tidak dipakai.</summary>
        public static Region Region(SectionShape s, double k)
        {
            Region outer = null;
            var holes = new List<Region>();
            bool first = true;
            foreach (var loop in s.AllLoops)
            {
                using (var pl = Poly(loop, k))
                {
                    var curves = new DBObjectCollection { pl };
                    var regs = Autodesk.AutoCAD.DatabaseServices.Region.CreateFromCurves(curves);
                    var r = (Region)regs[0];
                    for (int i = 1; i < regs.Count; i++) regs[i].Dispose();
                    if (first) outer = r; else holes.Add(r);
                    first = false;
                }
            }
            foreach (var h in holes) { outer.BooleanOperation(BooleanOperationType.BoolSubtract, h); h.Dispose(); }
            return outer;
        }

        /// <summary>Pergeseran penampang (mm) supaya titik acuan berada di sumbu batang.</summary>
        public static Vec2 AnchorShift(Profile p, Anchor a) => p.AnchorShift(a);

        public static Vec3 V(Point3d p) => new Vec3(p.X, p.Y, p.Z);
        public static Vec3 V(Vector3d p) => new Vec3(p.X, p.Y, p.Z);
        public static Point3d P(Vec3 v) => new Point3d(v.X, v.Y, v.Z);
        public static Vector3d D(Vec3 v) => new Vector3d(v.X, v.Y, v.Z);
        public static Matrix3d ToWorld(Frame f) =>
            Matrix3d.AlignCoordinateSystem(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, P(f.O), D(f.U), D(f.V), D(f.W));

        /// <summary>Sumbu lokal batang (sama dengan Core.Axes.Member): Z searah batang, Y ke atas, diputar roll.</summary>
        public static void MemberAxes(Vector3d dir, double rollDeg, out Vector3d x, out Vector3d y, out Vector3d z)
        {
            Axes.Member(V(dir), rollDeg, out Vec3 a, out Vec3 b, out Vec3 c);
            x = D(a); y = D(b); z = D(c);
        }

        // ---- data di extension dictionary ----------------------------------------------------
        /// <summary>Data batang lengkap (bisa dibangun ulang): profil, titik garis, acuan, putaran, tampak, normal bidang.</summary>
        public class MemberSpec
        {
            public string Kind = "batang3d";
            public Profile Profile;
            public Point3d S, E;
            public Anchor Anchor;
            public double Roll;
            public bool TopView;
            public Vector3d Normal = Vector3d.ZAxis;
            /// <summary>Pemotong dalam koordinat dunia (satuan gambar): coping, potongan miring, mulut ikan.</summary>
            public List<Cutter> Cuts = new List<Cutter>();
            /// <summary>Data pengelompokan: marking, level, kategori (kosong = otomatis).</summary>
            public string Mark = "", Level = "", Category = "";
            public double LengthMm(double k) => S.DistanceTo(E) / k;
            public MemberGeom Geom(double k, string id) => new MemberGeom
            {
                Id = id, Profile = Profile, Anchor = Anchor, Roll = Roll,
                S = new Vec3(S.X / k, S.Y / k, S.Z / k), E = new Vec3(E.X / k, E.Y / k, E.Z / k),
            };
        }

        public class TagData
        {
            public string Kind, Name; public Family Family; public double LengthMm, KgPerM; public string Dims;
            public MemberSpec Spec;          // batang versi baru (punya titik)
            public string Params;            // sambungan: parameter
            public string[] Links;           // sambungan: handle batang + ujung
        }

        static Xrecord Record(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            if (dict.Contains(Dict)) return (Xrecord)tr.GetObject(dict.GetAt(Dict), OpenMode.ForWrite);
            var xr = new Xrecord();
            dict.SetAt(Dict, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
            return xr;
        }

        static string DimsText(Profile p) => string.Join(";", p.Dims.Select(kv => kv.Key + "=" + kv.Value.ToString("0.###", CultureInfo.InvariantCulture)));

        /// <summary>Penanda sederhana (penampang).</summary>
        public static void Tag(Transaction tr, Entity ent, string kind, Profile p, double lengthMm, double kgm)
        {
            Record(tr, ent).Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, kind),
                new TypedValue((int)DxfCode.Text, p.Family.ToString()),
                new TypedValue((int)DxfCode.Text, p.Name),
                new TypedValue((int)DxfCode.Real, lengthMm),
                new TypedValue((int)DxfCode.Real, kgm),
                new TypedValue((int)DxfCode.Text, DimsText(p)));
        }

        public static void TagMember(Transaction tr, Entity ent, MemberSpec m, double k, double density)
        {
            var p = m.Profile;
            Record(tr, ent).Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, m.Kind),
                new TypedValue((int)DxfCode.Text, p.Family.ToString()),
                new TypedValue((int)DxfCode.Text, p.Name),
                new TypedValue((int)DxfCode.Real, m.LengthMm(k)),
                new TypedValue((int)DxfCode.Real, p.Properties().KgPerM(density)),
                new TypedValue((int)DxfCode.Text, DimsText(p)),
                new TypedValue((int)DxfCode.Real, m.S.X), new TypedValue((int)DxfCode.Real, m.S.Y), new TypedValue((int)DxfCode.Real, m.S.Z),
                new TypedValue((int)DxfCode.Real, m.E.X), new TypedValue((int)DxfCode.Real, m.E.Y), new TypedValue((int)DxfCode.Real, m.E.Z),
                new TypedValue((int)DxfCode.Text, m.Anchor.ToString()),
                new TypedValue((int)DxfCode.Real, m.Roll),
                new TypedValue((int)DxfCode.Real, m.TopView ? 1.0 : 0.0),
                new TypedValue((int)DxfCode.Real, m.Normal.X), new TypedValue((int)DxfCode.Real, m.Normal.Y), new TypedValue((int)DxfCode.Real, m.Normal.Z),
                new TypedValue((int)DxfCode.Text, string.Join("|", m.Cuts.Select(c => c.Serialize()))),
                new TypedValue((int)DxfCode.Text, m.Mark ?? ""), new TypedValue((int)DxfCode.Text, m.Level ?? ""), new TypedValue((int)DxfCode.Text, m.Category ?? ""));
        }

        public static void TagConnection(Transaction tr, Entity ent, Connection c, double kg, string[] links)
        {
            var vals = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Text, "sambungan"),
                new TypedValue((int)DxfCode.Text, c.Type.ToString()),
                new TypedValue((int)DxfCode.Text, c.Title),
                new TypedValue((int)DxfCode.Real, 0.0),
                new TypedValue((int)DxfCode.Real, kg),
                new TypedValue((int)DxfCode.Text, c.Serialize()),
            };
            foreach (var l in links ?? new string[0]) vals.Add(new TypedValue((int)DxfCode.Text, l ?? ""));
            Record(tr, ent).Data = new ResultBuffer(vals.ToArray());
        }

        public static TagData ReadTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return null;
            var dict = tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (dict == null || !dict.Contains(Dict)) return null;
            var xr = (Xrecord)tr.GetObject(dict.GetAt(Dict), OpenMode.ForRead);
            return Parse(xr.Data?.AsArray());
        }

        public static TagData Parse(TypedValue[] v)
        {
            if (v == null || v.Length < 6) return null;
            var t = new TagData { Kind = (string)v[0].Value, Name = (string)v[2].Value, LengthMm = (double)v[3].Value, KgPerM = (double)v[4].Value, Dims = (string)v[5].Value };
            if (t.Kind == "sambungan")
            {
                t.Params = t.Dims;
                t.Links = v.Skip(6).Select(x => (string)x.Value).ToArray();
                return t;
            }
            FamilyInfo.TryParse((string)v[1].Value, out Family f);
            t.Family = f;
            if (v.Length >= 18)
            {
                Func<int, double> r = i => (double)v[i].Value;
                Enum.TryParse((string)v[12].Value, out Anchor a);
                t.Spec = new MemberSpec
                {
                    Kind = t.Kind, Profile = Commands.FromTag(t), Anchor = a, Roll = r(13), TopView = r(14) > 0.5,
                    S = new Point3d(r(6), r(7), r(8)), E = new Point3d(r(9), r(10), r(11)), Normal = new Vector3d(r(15), r(16), r(17)),
                };
                if (v.Length >= 19)
                    foreach (var c in ((string)v[18].Value).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var cut = Cutter.Parse(c);
                        if (cut != null) t.Spec.Cuts.Add(cut);
                    }
                if (v.Length >= 22) { t.Spec.Mark = (string)v[19].Value; t.Spec.Level = (string)v[20].Value; t.Spec.Category = (string)v[21].Value; }
            }
            return t;
        }

        public static string BlockName(string profileName)
        {
            var bad = "<>/\\\":;?*|,=`";
            var chars = profileName.Select(c => bad.IndexOf(c) >= 0 ? '_' : c).ToArray();
            return "SK_" + new string(chars);
        }

        public static string N(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.CurrentCulture);
    }
}
