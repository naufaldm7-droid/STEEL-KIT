using System;
using System.Collections.Generic;
using System.Linq;
using SteelKit.Core;
using Xunit;

namespace SteelKit.Tests
{
    public class SectionMathTests
    {
        static Profile P(Family f, params (string k, double v)[] d)
        {
            var p = new Profile { Family = f };
            foreach (var (k, v) in d) p[k] = v;
            p.FillDefaults();
            p.Name = p.AutoName();
            return p;
        }

        static void Near(double expected, double actual, double relTol, string what = "")
        {
            double rel = Math.Abs(actual - expected) / Math.Abs(expected);
            Assert.True(rel <= relTol, $"{what}: diharapkan {expected}, didapat {actual} (selisih {rel:P2})");
        }

        [Fact]
        public void Plate_matches_closed_form()
        {
            var p = P(Family.PL, ("B", 100), ("t", 10)).Properties();
            Near(1000, p.Area, 1e-9, "A");
            Near(100 * 1000 / 12.0, p.Ix, 1e-9, "Ix");
            Near(10 * 1e6 / 12.0, p.Iy, 1e-9, "Iy");
            Near(100 * 100 / 6.0, p.Sx, 1e-9, "Sx");
            Near(100 * 100 / 4.0, p.Zx, 1e-6, "Zx");
            Near(10 * 100 * 100 / 4.0, p.Zy, 1e-6, "Zy");
            Assert.Equal(0, p.Cx, 9); Assert.Equal(0, p.Cy, 9);
        }

        [Fact]
        public void Rod_and_pipe_match_circle_formulas()
        {
            var rod = P(Family.RD, ("D", 20)).Properties();
            Near(Math.PI * 100, rod.Area, 5e-4, "A rod");
            Near(Math.PI * Math.Pow(20, 4) / 64, rod.Ix, 1e-3, "I rod");
            Near(Math.Pow(20, 3) / 6.0, rod.Zx, 2e-3, "Zp rod");
            var pipe = P(Family.CHS, ("D", 114.3), ("t", 4.5)).Properties();
            double d = 114.3 - 9;
            Near(Math.PI / 4 * (114.3 * 114.3 - d * d), pipe.Area, 5e-4, "A pipa");
            Near(Math.PI / 64 * (Math.Pow(114.3, 4) - Math.Pow(d, 4)), pipe.Ix, 1e-3, "I pipa");
            Near(pipe.Ix, pipe.Iy, 1e-6, "simetris");
        }

        [Fact]
        public void Fillet_removes_expected_area()
        {
            var s = new SectionShape { Outer = Shapes.Rect(0, 0, 100, 60, 10) };
            Near(100 * 60 - (4 - Math.PI) * 100, SectionCalc.Compute(s).Area, 1e-4, "A");
        }

        [Fact]
        public void Sharp_I_section_plastic_modulus_matches_hand_formula()
        {
            var p = P(Family.WF, ("H", 400), ("B", 200), ("tw", 8), ("tf", 13), ("r", 0));
            var pr = p.Properties();
            double zx = 200 * 13 * (400 - 13) + 8 * Math.Pow(400 - 26, 2) / 4;
            Near(zx, pr.Zx, 1e-6, "Zx");
            double ix = (200 * Math.Pow(400, 3) - (200 - 8) * Math.Pow(400 - 26, 3)) / 12;
            Near(ix, pr.Ix, 1e-9, "Ix");
        }

        [Fact]
        public void Sharp_T_section_neutral_axes()
        {
            // T 100 tinggi, sayap 120x10, badan 8: A = 1200 + 720 = 1920
            var p = P(Family.T, ("H", 100), ("B", 120), ("tw", 8), ("tf", 10), ("r", 0));
            var pr = p.Properties();
            Near(1920, pr.Area, 1e-9, "A");
            // titik berat dari bawah: (720*45 + 1200*95)/1920 = 76.25 -> relatif tengah kotak (50): 26.25
            Near(26.25, pr.Cy, 1e-6, "Cy");
            // PNA: luas 960 di atas. Sayap 1200 > 960 -> PNA di sayap, 960/120 = 8 dari atas
            // Zx = 120*8*4 + 120*2*1 + 8*90*(2+45)
            Near(120 * 8 * 4 + 120 * 2 * 1 + 8 * 90 * 47, pr.Zx, 1e-6, "Zx");
        }

        // Pemeriksaan silang terhadap berat tabel JIS yang umum dikenal: memastikan geometri (termasuk fillet) benar.
        [Theory]
        [InlineData(Family.WF, 400, 200, 8, 13, 16, 66.0)]
        [InlineData(Family.WF, 300, 150, 6.5, 9, 13, 36.7)]
        [InlineData(Family.WF, 200, 100, 5.5, 8, 11, 21.3)]
        [InlineData(Family.WF, 600, 200, 11, 17, 22, 106.0)]
        [InlineData(Family.H, 300, 300, 10, 15, 18, 94.0)]
        [InlineData(Family.H, 200, 200, 8, 12, 13, 49.9)]
        [InlineData(Family.UNP, 150, 75, 6.5, 10, 10, 18.6)]
        [InlineData(Family.UNP, 100, 50, 5, 7.5, 8, 9.36)]
        public void Hot_rolled_weights_match_tables(Family f, double H, double B, double tw, double tf, double r, double kgm)
        {
            var p = P(f, ("H", H), ("B", B), ("tw", tw), ("tf", tf), ("r", r));
            Near(kgm, p.Properties().KgPerM(), f == Family.UNP ? 0.035 : 0.015, p.Name);
        }

        [Fact]
        public void WF400_inertia_matches_table()
        {
            var pr = P(Family.WF, ("H", 400), ("B", 200), ("tw", 8), ("tf", 13), ("r", 16)).Properties();
            Near(23700e4, pr.Ix, 0.02, "Ix");
            Near(1740e4, pr.Iy, 0.02, "Iy");
            Near(1190e3, pr.Sx, 0.02, "Sx");
        }

        [Fact]
        public void Angle_centroid_and_minor_axis()
        {
            var pr = P(Family.L, ("H", 50), ("B", 50), ("t", 5), ("r", 6.5)).Properties();
            Near(3.77, pr.KgPerM(), 0.02, "berat");
            // titik berat 14.1 mm dari punggung; koordinat relatif tengah kotak = 14.1 - 25
            Near(14.1 - 25, pr.Cx, 0.03, "Cx");
            Near(pr.Cx, pr.Cy, 1e-6, "Cx=Cy");
            Assert.True(pr.Iv < pr.Ix && pr.Iu > pr.Ix, "sumbu utama siku");
            Near(9.8, pr.Rv, 0.03, "rv");
        }

        [Fact]
        public void Cold_formed_weights()
        {
            Near(6.76, P(Family.CNP, ("H", 150), ("B", 50), ("C", 20), ("t", 3.2)).Properties().KgPerM(), 0.02, "C150x50x20x3.2");
            Near(7.51, P(Family.CNP, ("H", 150), ("B", 65), ("C", 20), ("t", 3.2)).Properties().KgPerM(), 0.02, "C150x65x20x3.2");
            Near(9.52, P(Family.HOLLOW, ("H", 100), ("B", 100), ("t", 3.2), ("r", 6.4)).Properties().KgPerM(), 0.02, "SHS100x3.2");
        }

        [Fact]
        public void Lip_channel_symmetric_and_z_skewed()
        {
            var c = P(Family.CNP, ("H", 200), ("B", 75), ("C", 20), ("t", 2.3)).Properties();
            Assert.True(Math.Abs(c.Ixy) < 1e-3 * c.Ix, "C simetris terhadap sumbu x");
            Assert.True(Math.Abs(c.Cy) < 1e-6, "Cy ~ 0");
            var z = P(Family.Z, ("H", 200), ("B", 75), ("C", 20), ("t", 2.3)).Properties();
            Assert.True(Math.Abs(z.Ixy) > 0.05 * z.Ix, "Z punya Ixy");
            Assert.True(Math.Abs(z.Alpha) > 0.05, "sumbu utama Z miring");
            Near(c.Area, z.Area, 0.01, "luas C ~ luas Z");
        }

        [Fact]
        public void Shapes_are_centred_on_bounding_box()
        {
            foreach (var p in BuiltinCatalog().All.Take(400))
            {
                var bb = p.BuildShape().Bounds();
                Assert.True(Math.Abs(bb[0] + bb[2]) < 1e-6 && Math.Abs(bb[1] + bb[3]) < 1e-6, p.Name);
                Near(p.Extent(false), bb[3] - bb[1], 1e-6, p.Name + " tinggi");
            }
        }

        internal static Catalog BuiltinCatalog() => Catalog.Load(Catalog.ReadEmbedded(), null);
    }

    public class CatalogTests
    {
        [Fact]
        public void Builtin_catalog_is_complete_and_valid()
        {
            var c = Catalog.Load(Catalog.ReadEmbedded(), null);
            Assert.Empty(c.Warnings);
            var all = c.All;
            Assert.True(all.Count > 450, "jumlah profil " + all.Count);
            foreach (Family f in Enum.GetValues(typeof(Family)))
                Assert.True(all.Any(p => p.Family == f), "keluarga kosong: " + f);
            var dup = all.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.Empty(dup);
            foreach (var p in all)
            {
                var pr = p.Properties();
                Assert.True(pr.Area > 0 && pr.Ix > 0 && pr.Iy > 0 && pr.Zx >= pr.Sx * 0.99 && pr.J > 0, p.Name);
            }
        }

        [Fact]
        public void Find_is_forgiving()
        {
            var c = Catalog.Load(Catalog.ReadEmbedded(), null);
            Assert.Equal("WF400x200x8x13", c.Find("wf400X200x8x13").Name);
            Assert.Equal("WF400x200x8x13", c.Find(" WF 400x200x8x13 ").Name);
            Assert.Equal("SCH40-4in", c.Find("sch40-4in").Name);
            Assert.Null(c.Find("WF999"));
        }

        [Fact]
        public void User_profiles_override_and_revert()
        {
            var c = Catalog.Load(Catalog.ReadEmbedded(), null);
            var p = c.Find("WF400x200x8x13").Clone();
            p["tf"] = 14;
            Assert.Empty(c.SaveUser(p));
            Assert.Equal(14, c.Find("WF400x200x8x13")["tf"]);
            Assert.True(c.Find("WF400x200x8x13").IsUser);

            var custom = new Profile { Family = Family.CNP };
            custom["H"] = 175; custom["B"] = 60; custom["C"] = 18; custom["t"] = 2.5; custom.FillDefaults();
            custom.Name = custom.AutoName();
            Assert.Empty(c.SaveUser(custom));

            var reloaded = Catalog.Load(Catalog.ReadEmbedded(), c.UserCsv());
            Assert.Empty(reloaded.Warnings);
            Assert.Equal(14, reloaded.Find("WF400x200x8x13")["tf"]);
            Assert.NotNull(reloaded.Find("C175x60x18x2.5"));

            Assert.True(reloaded.DeleteUser("WF400x200x8x13"));
            Assert.Equal(13, reloaded.Find("WF400x200x8x13")["tf"]);
        }

        [Fact]
        public void Invalid_user_profiles_are_rejected()
        {
            var c = Catalog.Load(Catalog.ReadEmbedded(), null);
            var p = new Profile { Family = Family.WF, Name = "WF bad" };
            p["H"] = 100; p["B"] = 50; p["tw"] = 60; p["tf"] = 7; p["r"] = 0;
            var e = c.SaveUser(p);
            Assert.Contains(e, m => m.Contains("spasi"));
            Assert.Contains(e, m => m.Contains("badan"));
        }

        [Fact]
        public void Semicolon_csv_from_excel_is_accepted()
        {
            var csv = "family;name;H;B;tw;tf;r;t;C;D;note\nWF;WFX;250;125;6;9;12;;;;\nCHS;;;;;;;3,2;;60,5;pipa\n";
            var w = new List<string>();
            var l = Catalog.Parse(csv, true, w);
            Assert.Empty(w);
            Assert.Equal(2, l.Count);
            Assert.Equal("CHS60.5x3.2", l[1].Name);
            Assert.Equal(3.2, l[1]["t"]);
        }

        [Fact]
        public void Boq_groups_by_profile()
        {
            var rows = Boq.Group(new[]
            {
                new MemberRecord { Name = "WF300x150x6.5x9", Family = Family.WF, LengthMm = 6000, KgPerM = 36.7 },
                new MemberRecord { Name = "WF300x150x6.5x9", Family = Family.WF, LengthMm = 4000, KgPerM = 36.7 },
                new MemberRecord { Name = "RD16", Family = Family.RD, LengthMm = 8000, KgPerM = 1.58 },
            });
            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows[0].Count);
            Assert.Equal(10, rows[0].TotalLengthM, 6);
            Assert.Equal(367, rows[0].TotalKg, 6);
            var csv = Boq.ToCsv(rows);
            Assert.Contains("TOTAL,,,3,18.000,,379.6", csv);
        }

        [Fact]
        public void Every_family_has_usage_advice_and_elevation_lines()
        {
            foreach (var kv in FamilyInfo.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(kv.Value.Usage));
                var p = Catalog.Load(Catalog.ReadEmbedded(), null).OfFamily(kv.Key).First();
                Assert.True(p.ElevationLines(false).Count >= 2 && p.ElevationLines(true).Count >= 2);
            }
        }

        [Fact]
        public void Units_factor()
        {
            Assert.Equal(1, Units.PerMm(4));
            Assert.Equal(0.001, Units.PerMm(6));
            Assert.Equal(1, Units.PerMm(0));
        }
    }
}
