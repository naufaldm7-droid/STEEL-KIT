using System;
using System.Collections.Generic;
using System.Linq;
using SteelKit.Core;
using Xunit;

namespace SteelKit.Tests
{
    /// <summary>Lubang baut, lubang rod, snipe pengaku, dan tata letak asisten yang bebas tabrakan.</summary>
    public class FitTests
    {
        [Fact]
        public void Every_bolt_has_a_coaxial_hole_for_the_members()
        {
            int bolts = 0;
            foreach (var c in ConnectionTests.Samples())
            {
                c.Recommend();
                var r = c.Build();
                // batang baut = silinder PartKind.Baut dengan outline lingkaran (bukan kepala segi enam)
                var shanks = r.Parts.Where(p => p.Kind == PartKind.Baut && p.Label != "pin" && p.Outline.Vertices.Count <= 4).ToList();
                foreach (var s in shanks)
                {
                    bolts++;
                    double rs = s.Outline.Vertices.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y));
                    var hole = r.BoltHoles.FirstOrDefault(h => Math.Abs(h.F.W.Dot(s.F.W)) > 0.9999 && Perp(h.F.O - s.F.O, s.F.W) < 0.01);
                    Assert.True(hole != null, c.Title + ": baut tanpa lubang di batang");
                    Assert.True(hole.Radius > rs, c.Title + ": lubang " + hole.Radius * 2 + " tidak lebih besar dari baut " + rs * 2);
                }
            }
            Assert.True(bolts > 100, "baut dicek: " + bolts);
        }

        static double Perp(Vec3 d, Vec3 u) => (d - u * d.Dot(u)).Length;

        [Theory]
        [InlineData(30)]
        [InlineData(50.19)]
        [InlineData(80)]
        public void Rod_slot_passes_through_the_whole_web(double angle)
        {
            var c = new BracingRod { Host = "WF400x200x8x13", Rod = "RD16", SudutRod = angle };
            c.Recommend();
            var r = c.Build();
            var h = Assert.Single(r.CutSupport);
            double phi = angle * Math.PI / 180, tw = 8, rh = h.Radius;
            // titik terjauh tepi rod/lubang yang menembus badan (z = ±tw/2) harus di dalam rentang silinder
            var u = h.F.W;
            foreach (double z in new[] { -tw / 2, tw / 2 })
                foreach (double side in new[] { -1.0, 1 })
                {
                    // titik pada permukaan silinder di bidang rod-normal badan yang memotong muka badan
                    var n = new Vec3(-Math.Sin(phi), 0, Math.Cos(phi)) * side * rh;     // tegak lurus rod di bidang XZ
                    double s = (z - (h.F.O + n).Z) / u.Z;                                   // sepanjang sumbu sampai muka badan
                    double along = s;
                    Assert.InRange(along, h.Z0 - 1e-6, h.Z1 + 1e-6);
                }
            Assert.True(rh * 2 >= 16 + 3 - 1e-9, "lubang rod ≥ d + 3");
        }

        [Fact]
        public void Column_stiffeners_clear_the_root_radius()
        {
            var c = new KneeHaunch { Kolom = "WF400x200x8x13", Rafter = "WF400x200x8x13", Sudut = 10, PanjangRafter = 10150 };
            c.Recommend();
            var st = c.Build().Parts.Where(p => p.Kind == PartKind.Pengaku && p.Thickness < 30 && p.Outline.Vertices.Count == 6).ToList();
            Assert.NotEmpty(st);
            var col = Env.Catalog.Find("WF400x200x8x13");
            double R = col["r"], tw = col["tw"];
            foreach (var p in st)
                foreach (var v in p.Outline.Vertices)
                {
                    // tidak ada titik sudut pengaku di dalam kotak R x R di pertemuan badan-sayap
                    bool nearWeb = Math.Abs(Math.Abs(v.Y) - tw / 2) < R - 1e-6;
                    bool nearFlange = Math.Abs(Math.Abs(v.X) - (col.Extent(false) / 2 - col["tf"])) < R - 1e-6;
                    Assert.False(nearWeb && nearFlange, "sudut pengaku masuk radius profil di " + v.X + "," + v.Y);
                }
        }

        [Fact]
        public void Assistant_portal_layout_keeps_clear_of_knees_and_crossings()
        {
            var p = Assistant.Parse("portal bentang 20 m tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150x65x20x3,2 jarak 1,2 m bracing RD16");
            var purl = p.Members.Where(m => m.Role == "Gording").ToList();
            // gording terdekat ke kolom: di luar setengah tinggi kolom (200) + pelat ujung + mur baut lutut
            Assert.True(purl.Min(m => Math.Min(m.S.X, 20000 - m.S.X)) >= 300, "gording tepi di zona lutut");
            // rod X tidak saling menembus: jarak antar garis rod di titik silang ≥ diameter rod
            var rods = p.Members.Where(m => m.Role == "Bracing").ToList();
            for (int i = 0; i < rods.Count; i++)
                for (int j = i + 1; j < rods.Count; j++)
                {
                    var a = rods[i]; var b = rods[j];
                    var u = (a.E - a.S).Unit; var v = (b.E - b.S).Unit; var n = u.Cross(v);
                    if (n.Length < 1e-6) continue;
                    double dist = Math.Abs((b.S - a.S).Dot(n.Unit));
                    // hanya pasangan yang benar-benar bersilang di dalam bentangnya
                    if (!Crosses(a, b)) continue;
                    Assert.True(dist >= 16 - 1e-6, "rod bersilang terlalu rapat: " + dist.ToString("0.0"));
                }
            // turnbuckle tidak di titik silang
            var ms = p.Members.Select(m => new MemberGeom { Profile = Env.Catalog.Find(m.Profile), S = m.S, E = m.E, Roll = m.Roll, Anchor = m.Anchor, Id = m.Role }).ToList();
            var tb = JointFinder.Find(ms).Where(j => j.Type == ConnType.Turnbuckle).Select(j => j.Frame.O).ToList();
            Assert.NotEmpty(tb);
            for (int i = 0; i < tb.Count; i++)
                for (int j = i + 1; j < tb.Count; j++)
                    Assert.True((tb[i] - tb[j]).Length > 500, "dua turnbuckle bertumpuk");
        }

        [Fact]
        public void Marks_split_by_hole_pattern_but_not_by_direction()
        {
            var p = Env.Catalog.Find("WF400x200x8x13");
            Cutter Web(double y, double z) => Cutter.Cyl(new Frame(new Vec3(-50, y, z), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 11, 0, 100);
            Cutter Miss(double z) => Cutter.Cyl(new Frame(new Vec3(-50, 400, z), Vec3.UnitY, Vec3.UnitZ, Vec3.UnitX), 11, 0, 100);
            string a = PartSignature.Of(p, Anchor.Tengah, 6000, new[] { Web(100, 500), Web(-100, 500) });
            string flipped = PartSignature.Of(p, Anchor.Tengah, 6000, new[] { Web(100, 5500), Web(-100, 5500) });
            string extra = PartSignature.Of(p, Anchor.Tengah, 6000, new[] { Web(100, 500), Web(-100, 500), Web(0, 3000) });
            string withMiss = PartSignature.Of(p, Anchor.Tengah, 6000, new[] { Web(100, 500), Web(-100, 500), Miss(3000) });
            Assert.Equal(a, flipped);          // batang sama dipasang terbalik: marking sama
            Assert.NotEqual(a, extra);         // lubang tambahan: marking lain
            Assert.Equal(a, withMiss);         // silinder yang tidak mengenai batang tidak dihitung

            var ms = new List<TakeoffMember>
            {
                new TakeoffMember { Role = "Column", Section = "WF400x200x8x13", LengthMm = 6000, Signature = a },
                new TakeoffMember { Role = "Column", Section = "WF400x200x8x13", LengthMm = 6000, Signature = flipped },
                new TakeoffMember { Role = "Column", Section = "WF400x200x8x13", LengthMm = 6000, Signature = extra },
            };
            Takeoff.AutoMark(ms);
            Assert.Equal(ms[0].Mark, ms[1].Mark);
            Assert.NotEqual(ms[0].Mark, ms[2].Mark);
        }

        static bool Crosses(MemberProposal a, MemberProposal b)
        {
            // titik terdekat kedua garis berada di dalam kedua batang (bukan di perpanjangannya)
            var d1 = a.E - a.S; var d2 = b.E - b.S; var r = a.S - b.S;
            double aa = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), c = d1.Dot(r), bb = d1.Dot(d2), den = aa * e - bb * bb;
            if (Math.Abs(den) < 1e-9) return false;
            double s = (bb * f - c * e) / den, t = (aa * f - bb * c) / den;
            return s > 0.05 && s < 0.95 && t > 0.05 && t < 0.95;
        }
    }
}
