using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;

namespace SteelKit.Acad
{
    /// <summary>
    /// Standar gambar Steel Kit: satu gaya huruf teknik (ISOCPEUR) dan seri tinggi teks ISO 3098 (mm di kertas).
    /// Semua lembar (struktur, detail, fabrikasi) memakai ukuran dari sini supaya seragam.
    /// </summary>
    public static class Std
    {
        public const double H1 = 5.0;    // judul utama kotak judul
        public const double H2 = 3.5;    // judul tampak, gelembung as, judul detail
        public const double H3 = 2.5;    // teks biasa, dimensi, leader, nilai kotak judul
        public const double H4 = 1.8;    // label kecil, isi tabel, catatan umum
        public const double Arrow = 2.0; // panah dimensi & leader
        public const string StyleName = "SK-ISO";

        /// <summary>Gaya huruf SK-ISO (ISOCPEUR, lebar 1,0); dibuat sekali per gambar.</summary>
        public static ObjectId TextStyle(Transaction tr, Database db)
        {
            var tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tt.Has(StyleName)) return tt[StyleName];
            tt.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = StyleName, FileName = "isocpeur.ttf", XScale = 1.0, TextSize = 0 };
            try { rec.Font = new FontDescriptor("ISOCPEUR", false, false, 0, 0); } catch { }
            var id = tt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        /// <summary>Pasang gaya SK-ISO ke teks, dimensi, leader, dan tabel tertentu (hanya entitas yang diberikan).</summary>
        public static void Apply(Transaction tr, Database db, IEnumerable<ObjectId> ids)
        {
            var st = TextStyle(tr, db);
            foreach (var id in ids)
            {
                if (id.IsErased) continue;
                var o = tr.GetObject(id, OpenMode.ForRead);
                try
                {
                    switch (o)
                    {
                        case MText m: m.UpgradeOpen(); m.TextStyleId = st; break;
                        case DBText t: t.UpgradeOpen(); t.TextStyleId = st; break;
                        case Dimension d: d.UpgradeOpen(); d.TextStyleId = st; break;
                        case MLeader ml: ml.UpgradeOpen(); ml.TextStyleId = st; break;
                        case Table tb:
                            tb.UpgradeOpen();
                            for (int r = 0; r < tb.Rows.Count; r++)
                                for (int c = 0; c < tb.Columns.Count; c++)
                                    tb.Cells[r, c].TextStyleId = st;
                            tb.GenerateLayout();
                            break;
                    }
                }
                catch { /* entitas yang tidak bisa diubah dilewati */ }
            }
        }

        /// <summary>Pasang gaya ke semua isi satu ruang (dipakai untuk layout baru buatan Steel Kit).</summary>
        public static void ApplyAll(Transaction tr, Database db, BlockTableRecord space) => Apply(tr, db, space.Cast<ObjectId>().ToList());

        /// <summary>
        /// Tebal garis per layer (seri pena ISO 0,13–0,50 mm): benda utama tebal, pendukung tipis.
        /// Hanya diisi bila layer masih "Default", jadi pengaturan pengguna tidak ditimpa.
        /// </summary>
        static readonly Dictionary<string, LineWeight> Weights = new Dictionary<string, LineWeight>(StringComparer.OrdinalIgnoreCase)
        {
            ["SK-BATANG-3D"] = LineWeight.LineWeight035, ["SK-BATANG-2D"] = LineWeight.LineWeight035, ["SK-PENAMPANG"] = LineWeight.LineWeight035,
            ["SK-SAMBUNGAN"] = LineWeight.LineWeight025, ["SK-FABRIKASI"] = LineWeight.LineWeight035,
            ["SK-DETAIL"] = LineWeight.LineWeight035, ["SK-DETAIL-BATANG"] = LineWeight.LineWeight025, ["SK-DETAIL-BETON"] = LineWeight.LineWeight018,
            ["SK-DETAIL-BLOK"] = LineWeight.LineWeight025, ["SK-TERSEMBUNYI"] = LineWeight.LineWeight018, ["SK-AS"] = LineWeight.LineWeight018,
            ["SK-ARSIR"] = LineWeight.LineWeight013, ["SK-DIMENSI"] = LineWeight.LineWeight018, ["SK-TEKS"] = LineWeight.LineWeight025,
            ["SK-TABEL"] = LineWeight.LineWeight018, ["SK-JUDUL"] = LineWeight.LineWeight025,
        };

        public static void LayerWeights(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var kv in Weights)
            {
                if (!lt.Has(kv.Key)) continue;
                var rec = (LayerTableRecord)tr.GetObject(lt[kv.Key], OpenMode.ForRead);
                if (rec.LineWeight != LineWeight.ByLineWeightDefault) continue;
                rec.UpgradeOpen(); rec.LineWeight = kv.Value;
            }
        }

        /// <summary>Gaya tabel dengan margin sel proporsional tinggi teks (bawaan AutoCAD 0,06 unit membuat teks menempel garis).</summary>
        public static ObjectId TableStyle(Transaction tr, Database db, double textH)
        {
            string name = "SK-TABEL-" + Math.Round(textH * 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var dict = (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead);
            if (dict.Contains(name)) return dict.GetAt(name);
            var ts = new TableStyle();
            dict.UpgradeOpen();
            var id = dict.SetAt(name, ts);
            tr.AddNewlyCreatedDBObject(ts, true);
            // margin hanya bisa diisi setelah gaya masuk ke database (sebelumnya: eOwnerNotSet)
            try { ts.Name = name; ts.HorizontalCellMargin = textH * 0.6; ts.VerticalCellMargin = textH * 0.35; } catch { }
            return id;
        }

        /// <summary>Lebar teks perkiraan untuk ISOCPEUR (±0,7 tinggi per karakter).</summary>
        public static double Width(string s, double h) => (string.IsNullOrEmpty(s) ? 0 : s.Length) * h * 0.7;
    }
}
