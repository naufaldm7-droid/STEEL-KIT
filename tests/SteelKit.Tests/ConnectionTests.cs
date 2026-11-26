using System;
using System.Collections.Generic;
using System.Linq;
using SteelKit.Core;
using Xunit;
using Xunit.Abstractions;

namespace SteelKit.Tests
{
    public class ConnectionTests
    {
        readonly ITestOutputHelper log;
        public ConnectionTests(ITestOutputHelper o) { log = o; }

        internal static IEnumerable<Connection> Samples()
        {
            yield return new BasePlate { Kolom = "H300x300x10x15", Pola = AnchorPattern.Luar4 };
            yield return new BasePlate { Kolom = "WF400x200x8x13", Pola = AnchorPattern.Dalam4, TipeAngkur = AnchorType.Berkepala, Angkur = BoltSize.M20 };
            yield return new BasePlate { Kolom = "H400x400x13x21", Pola = AnchorPattern.Luar8, TipeAngkur = AnchorType.UlirPelatBawah, Angkur = BoltSize.M30, Pengaku = true, ShearKey = true };
            yield return new BasePlate { Kolom = "H250x250x9x14", Pola = AnchorPattern.Luar6, TipeAngkur = AnchorType.J_Kait };
            yield return new BasePlate { Kolom = "CHS114.3x4.5", Angkur = BoltSize.M16, TipeAngkur = AnchorType.Kimia };
            yield return new BasePlate { Kolom = "SHS150x150x6", Angkur = BoltSize.M20 };
            yield return new EndPlate { Balok = "WF400x200x8x13", Kolom = "H300x300x10x15" };
            yield return new EndPlate { Balok = "WF600x200x11x17", Kolom = "H400x400x13x21", Jenis = EndPlateKind.DiperpanjangKeduanya, BautPerBaris = 4 };
            yield return new EndPlate { Balok = "WF250x125x6x9", Kolom = "H300x300x10x15", Muka = SupportFace.BadanKolom, Jenis = EndPlateKind.Rata, PengakuKolom = false };
            yield return new FinPlate { Balok = "WF300x150x6.5x9", Penopang = "H300x300x10x15" };
            yield return new FinPlate { Balok = "WF200x100x5.5x8", Penopang = "WF400x200x8x13", Muka = SupportFace.BadanBalok };
            yield return new DoubleCleat { Balok = "WF300x150x6.5x9", Penopang = "WF500x200x10x16" };
            yield return new SplicePlate { Profil = "WF400x200x8x13", Sudut = 10 };
            yield return new SplicePlate { Profil = "WF300x150x6.5x9", Sudut = 0 };
            yield return new KneeHaunch { Kolom = "WF400x200x8x13", Rafter = "WF400x200x8x13", Sudut = 10, PanjangRafter = 10150 };
            yield return new KneeHaunch { Kolom = "WF500x200x10x16", Rafter = "WF450x200x9x14", Sudut = 5.7, PanjangRafter = 15000 };
            yield return new PurlinCleat { Gording = "C150x65x20x3.2", Rafter = "WF400x200x8x13" };
            yield return new PurlinCleat { Gording = "Z200x75x20x2.3", Rafter = "WF400x200x8x13" };
            yield return new PurlinCleat { Gording = "C250x75x25x4.5", Rafter = "WF450x200x9x14" };
            yield return new GussetNode { ProfilA = "L75x75x8", ProfilB = "L75x75x8", SudutB = 90 };
            yield return new GussetNode { ProfilA = "L50x50x5", ProfilB = "L50x50x5", SudutB = 45, MenerusA = true };
            yield return new GussetNode { ProfilA = "L100x100x10", ProfilB = "L75x75x8", SudutB = 180 };
            yield return new GussetNode { ProfilA = "SHS50x50x2.3", ProfilB = "SHS50x50x2.3", SudutB = 90 };
            yield return new GussetNode { ProfilA = "CHS60.5x3.2", ProfilB = "CHS60.5x3.2", SudutB = 60, MenerusA = true };
            yield return new GussetNode { ProfilA = "PL100x10", ProfilB = "PL100x10", SudutB = 120 };
            yield return new BracingRod { Host = "WF400x200x8x13", Rod = "RD16", SudutRod = 40 };
            yield return new BracingRod { Host = "H300x300x10x15", Rod = "RD20", SudutRod = 30, Metode = RodEnd.Clevis };
            yield return new Turnbuckle { Rod = "RD16" };
            yield return new BracingGusset { Host = "WF400x200x8x13", Brace = "L75x75x8", SudutBrace = 45 };
            yield return new BracingGusset { Host = "H300x300x10x15", Brace = "CHS76.3x3.2", SudutBrace = 50, Muka = HostFace.Badan };
            yield return new BracingGusset { Host = "H300x300x10x15", Host2 = "WF400x200x8x13", SudutHost2 = 90, Brace = "L75x75x8", SudutBrace = 45 };
            yield return new CrossBracing { ProfilA = "L75x75x8", ProfilB = "L75x75x8", Sudut = 70 };
            yield return new FlangeBrace { Rafter = "WF400x200x8x13", Gording = "C150x65x20x3.2" };
            yield return new SagRod { Gording = "C150x65x20x3.2", Rod = "RD12" };
            yield return new CapPlate { Kolom = "H300x300x10x15", Balok = "WF400x200x8x13" };
            yield return new SeatAngle { Balok = "WF300x150x6.5x9", Penopang = "H300x300x10x15", Berpengaku = true };
            yield return new WeldedMoment { Balok = "WF400x200x8x13", Kolom = "H300x300x10x15" };
            yield return new FlangePlateMoment { Balok = "WF400x200x8x13", Kolom = "H300x300x10x15" };
            yield return new HollowColumnBeam { Balok = "WF250x125x6x9", Kolom = "SHS200x200x9" };
            yield return new HollowColumnBeam { Balok = "WF200x100x5.5x8", Kolom = "CHS216.3x8" };
            yield return new StackedBeams { BalokAtas = "WF250x125x6x9", BalokBawah = "WF400x200x8x13" };
            yield return new WindColumn { Kolom = "WF250x125x6x9", Rafter = "WF400x200x8x13", Sudut = 10 };
            yield return new CraneBracket { Kolom = "H400x400x13x21", Konsol = "WF350x175x7x11" };
            yield return new ConcretePlate { Balok = "WF300x150x6.5x9" };
            yield return new ConcretePlate { Balok = "WF300x150x6.5x9", Metode = ConcreteFix.StudTanam };
            yield return new CoverPlateSplice { ProfilA = "WF400x200x8x13", ProfilB = "WF400x200x8x13" };
            yield return new CoverPlateSplice { ProfilA = "WF400x200x8x13", ProfilB = "WF396x199x7x11" };
            yield return new PurlinSplice { ProfilA = "Z200x75x20x2.3", ProfilB = "Z200x75x20x2.3" };
            yield return new PurlinSplice { ProfilA = "C150x65x20x3.2", ProfilB = "C150x65x20x3.2" };
            yield return new TrussNode { Chord = "L75x75x8", Batang = "L50x50x5@45|L50x50x5@90|L50x50x5@135" };
            yield return new DirectWeld { Chord = "SHS100x100x4.5", Cabang = "SHS60x60x3.2", Sudut = 90 };
            yield return new DirectWeld { Chord = "CHS114.3x4.5", Cabang = "CHS60.5x3.2", Sudut = 60 };
            yield return new StairTread { IbuTangga = "UNP200x80x7.5x11" };
        }

        [Fact]
        public void Recommended_connections_pass_their_own_geometry_rules()
        {
            foreach (var c in Samples())
            {
                c.Recommend();
                var r = c.Build();
                var bad = r.Checks.Where(k => !k.Ok).ToList();
                foreach (var k in bad) log.WriteLine(c.Title + " " + c.Serialize() + "\n   " + k);
                Assert.True(bad.Count == 0, c.Title + ": " + string.Join(" | ", bad));
                Assert.NotEmpty(r.Parts);
                Assert.NotEmpty(r.Boq);
                Assert.Equal(2, r.Views.Count);
                Assert.True(r.Checks.Count >= 3, c.Title + " punya cek");
                foreach (var p in r.Parts) Assert.True(p.Thickness > 0 && p.Outline.Vertices.Count >= 2, c.Title + " benda rusak");
            }
        }

        [Fact]
        public void Serialization_roundtrip_keeps_every_value()
        {
            foreach (var c in Samples())
            {
                c.Recommend();
                var s = c.Serialize();
                var back = Connection.Deserialize(s);
                Assert.Equal(c.Type, back.Type);
                Assert.Equal(s, back.Serialize());
            }
        }

        [Fact]
        public void Bad_dimensions_are_reported()
        {
            var bp = new BasePlate { Kolom = "H300x300x10x15" };
            bp.Recommend();
            bp.LebarPelat = 310;          // angkur jadi terlalu dekat ke tepi
            bp.Grout = 15;
            var r = bp.Build();
            Assert.Contains(r.Checks, k => !k.Ok && k.Item.Contains("tepi pelat"));
            Assert.Contains(r.Checks, k => !k.Ok && k.Item.Contains("grout"));

            var fp = new FinPlate { Balok = "WF300x150x6.5x9", Penopang = "H300x300x10x15" };
            fp.Recommend();
            fp.TebalPelat = 20;
            fp.JarakBaut = 40;
            var r2 = fp.Build();
            Assert.Contains(r2.Checks, k => !k.Ok && k.Item.Contains("Tebal fin plate"));
            Assert.Contains(r2.Checks, k => !k.Ok && k.Item.Contains("jarak antar baut"));
        }

        [Fact]
        public void Base_plate_anchor_assembly_is_complete()
        {
            var bp = new BasePlate { Kolom = "H300x300x10x15", Angkur = BoltSize.M24, TipeAngkur = AnchorType.L_Kait };
            bp.Recommend();
            var r = bp.Build();
            Assert.Equal(4, bp.AnchorPositions().Count);
            var a = r.Boq.Single(l => l.Item == "Angkur");
            Assert.Equal(4, a.Count);
            Assert.Contains("M24", a.Spec);
            Assert.Equal(4 * 3, r.Boq.Single(l => l.Item == "Mur").Count);            // perata + mur ganda
            Assert.Equal(4, r.Boq.Single(l => l.Item == "Ring plat").Count);
            Assert.Equal(4, r.Parts.Single(p => p.Kind == PartKind.Pelat).Holes.Count);
            var bb = r.Bounds();
            Assert.True(bb[2] < -bp.TebalPelat - bp.Grout - bp.Tanam + 1, "angkur tertanam sedalam hef");
        }

        [Fact]
        public void End_plate_trims_beam_to_plate_face()
        {
            var ep = new EndPlate { Balok = "WF400x200x8x13", Kolom = "H300x300x10x15" };
            ep.Recommend();
            var r = ep.Build();
            Assert.Equal(150 + ep.TebalPelat, r.TrimSupported.Value.X, 6);
            // baut menembus pelat + sayap kolom, kepala di muka pelat
            var plate = r.Parts.First(p => p.Kind == PartKind.Pelat);
            Assert.Equal(150, plate.Outline.Vertices.Min(v => v.X), 6);
        }

        [Fact]
        public void Projection_gives_outlines_and_circles()
        {
            var ep = new EndPlate { Balok = "WF400x200x8x13", Kolom = "H300x300x10x15" };
            ep.Recommend();
            var r = ep.Build();
            var face = Projection.Project(r, r.Views[0]);
            Assert.True(face.Count(s => s.IsCircle) >= 8, "baut tampak sebagai lingkaran di muka pelat");
            var side = Projection.Project(r, r.Views[1]);
            Assert.All(side, s => Assert.True(s.IsCircle || s.Pts.Count >= 3));
        }

        static MemberGeom M(string prof, double x0, double y0, double z0, double x1, double y1, double z1, double roll = 0)
            => new MemberGeom { Profile = Env.Catalog.Find(prof), S = new Vec3(x0, y0, z0), E = new Vec3(x1, y1, z1), Roll = roll, Id = prof };

        [Fact]
        public void Portal_frame_joints_are_detected()
        {
            // portal pelana 20 m, tinggi 6 m, kemiringan 10°; gording C di atas rafter
            double h = 6000, span = 20000, rise = span / 2 * Math.Tan(10 * Math.PI / 180);
            var ms = new List<MemberGeom>
            {
                M("WF400x200x8x13", 0, 0, 0, 0, 0, h, 90),               // kolom kiri (sayap menghadap X)
                M("WF400x200x8x13", span, 0, 0, span, 0, h, 90),         // kolom kanan
                M("WF400x200x8x13", 0, 0, h, span / 2, 0, h + rise),     // rafter kiri
                M("WF400x200x8x13", span, 0, h, span / 2, 0, h + rise),  // rafter kanan
            };
            // gording melintang (arah Y) duduk di sayap atas rafter kiri pada x = 3000
            var raf = ms[2];
            raf.Axes3(out Vec3 _, out Vec3 ry, out Vec3 _);
            var onTop = raf.At(0.3) + ry * (200 + 75);
            ms.Add(new MemberGeom { Profile = Env.Catalog.Find("C150x65x20x3.2"), S = onTop + new Vec3(0, -3000, 0), E = onTop + new Vec3(0, 3000, 0), Id = "gording" });
            // balok lantai di tengah tinggi kolom kiri (masuk ke sayap)
            ms.Add(M("WF300x150x6.5x9", 0, 0, 3000, -5000, 0, 3000));

            var joints = JointFinder.Find(ms);
            foreach (var j in joints) log.WriteLine(j.ToString());
            Assert.Equal(2, joints.Count(j => j.Type == ConnType.PelatDasar));
            Assert.Equal(2, joints.Count(j => j.Type == ConnType.LututHaunch));
            Assert.Equal(1, joints.Count(j => j.Type == ConnType.SambunganPelatUjung));
            Assert.Equal(1, joints.Count(j => j.Type == ConnType.CleatGording));
            Assert.Equal(1, joints.Count(j => j.Type == ConnType.PelatUjung));
            var apex = joints.Single(j => j.Type == ConnType.SambunganPelatUjung);
            Assert.Equal(10, ((SplicePlate)apex.Conn).Sudut, 1);
            var knee = joints.First(j => j.Type == ConnType.LututHaunch);
            Assert.Equal(10, Math.Abs(((KneeHaunch)knee.Conn).Sudut), 1);
            Assert.Empty(knee.Notes);
            var cleat = joints.Single(j => j.Type == ConnType.CleatGording);
            Assert.Empty(cleat.Notes);
            foreach (var j in joints)
            {
                var r = j.Conn.Build();
                Assert.True(r.AllOk, j + ": " + string.Join(" | ", r.Checks.Where(k => !k.Ok)));
            }
        }

        [Fact]
        public void Angle_corner_and_tee_get_gussets()
        {
            var ms = new List<MemberGeom>
            {
                M("L75x75x8", 0, 0, 0, 3000, 0, 0),
                M("L75x75x8", 3000, 0, 0, 3000, 2000, 0),     // sudut di (3000,0)
                M("L75x75x8", 1500, 0, 0, 1500, 2000, 0),     // siku-T di tengah batang pertama
            };
            var joints = JointFinder.Find(ms, basePlates: false);
            foreach (var j in joints) log.WriteLine(j + "  O=" + j.Frame.O + " sudut=" + ((GussetNode)j.Conn).SudutB);
            Assert.Equal(2, joints.Count);
            Assert.All(joints, j => Assert.Equal(ConnType.PelatBuhul, j.Type));
            Assert.All(joints, j => Assert.Empty(j.Notes));
            var corner = joints.Single(j => !((GussetNode)j.Conn).MenerusA);
            Assert.Equal(90, Math.Abs(((GussetNode)corner.Conn).SudutB), 3);
            Assert.Equal(-37.5, corner.Frame.O.Z, 6);                  // pelat di bawah kaki datar siku
            var tee = joints.Single(j => ((GussetNode)j.Conn).MenerusA);
            foreach (var j in joints)
            {
                var r = j.Conn.Build();
                Assert.True(r.AllOk, j + ": " + string.Join(" | ", r.Checks.Where(k => !k.Ok)));
                Assert.True(r.TrimSupported.HasValue);
            }
            Assert.False(tee.Conn.Build().TrimSupport.HasValue);       // batang menerus tidak dipotong
        }

        List<Joint> Detect(params MemberGeom[] ms)
        {
            var j = JointFinder.Find(ms.ToList(), basePlates: false);
            foreach (var x in j)
            {
                var r = x.Conn.Build();
                log.WriteLine(x + "  OK=" + r.AllOk + (r.AllOk ? "" : " -> " + string.Join(" | ", r.Checks.Where(k => !k.Ok))));
            }
            return j;
        }

        static void AllOk(List<Joint> js) { foreach (var j in js) { var r = j.Conn.Build(); Assert.True(r.AllOk, j + ": " + string.Join(" | ", r.Checks.Where(k => !k.Ok))); } }

        [Fact]
        public void Rod_bracing_gets_ends_and_turnbuckle()
        {
            var js = Detect(
                M("WF400x200x8x13", 0, 0, 0, 0, 0, 6000, 90),
                M("WF400x200x8x13", 0, 6000, 0, 0, 6000, 6000, 90),
                M("RD16", 0, 0, 500, 0, 6000, 5500));
            Assert.Equal(2, js.Count(j => j.Type == ConnType.BracingRod));
            Assert.Equal(1, js.Count(j => j.Type == ConnType.Turnbuckle));
            AllOk(js);
        }

        [Fact]
        public void Angle_brace_to_column_and_corner()
        {
            var js = Detect(
                M("H300x300x10x15", 0, 0, 0, 0, 0, 4000, 90),
                M("WF400x200x8x13", 0, 0, 4000, 6000, 0, 4000),
                M("L75x75x8", 0, 0, 1000, 3000, 0, 4000, 90));
            Assert.Contains(js, j => j.Type == ConnType.BracingBuhul);
            AllOk(js.Where(j => j.Type == ConnType.BracingBuhul).ToList());
        }

        [Fact]
        public void Column_beam_variants()
        {
            // balok menerus di atas kolom
            var a = Detect(M("H300x300x10x15", 0, 0, 0, 0, 0, 3000), M("WF400x200x8x13", -3000, 0, 3200, 3000, 0, 3200));
            Assert.Contains(a, j => j.Type == ConnType.PelatTutup); AllOk(a);
            // kolom angin di bawah rafter miring
            double t = Math.Tan(10 * Math.PI / 180);
            var b = Detect(M("WF250x125x6x9", 3000, 0, 0, 3000, 0, 6000 + 3000 * t - 200 / Math.Cos(10 * Math.PI / 180)), M("WF400x200x8x13", 0, 0, 6000, 10000, 0, 6000 + 10000 * t));
            Assert.Contains(b, j => j.Type == ConnType.KolomAngin); AllOk(b);
            // tiang di atas balok
            var c = Detect(M("WF400x200x8x13", -3000, 0, 0, 3000, 0, 0), M("H150x150x7x10", 0, 0, 200, 0, 0, 2000, 90));
            Assert.Contains(c, j => j.Type == ConnType.PelatDasar && ((BasePlate)j.Conn).Dudukan == BaseSupport.Baja); AllOk(c);
            // balok ke kolom hollow
            var d = Detect(M("SHS200x200x9", 0, 0, 0, 0, 0, 4000), M("WF250x125x6x9", 0, 0, 3000, 5000, 0, 3000));
            Assert.Contains(d, j => j.Type == ConnType.KolomHollow); AllOk(d);
            // balok tumpuk
            var e = Detect(M("WF400x200x8x13", 0, -3000, 0, 0, 3000, 0), M("WF250x125x6x9", -3000, 0, 325, 3000, 0, 325));
            Assert.Contains(e, j => j.Type == ConnType.BalokTumpuk); AllOk(e);
        }

        [Fact]
        public void Purlins_rods_truss_tubes_and_crossings()
        {
            var a = Detect(M("Z200x75x20x2.3", 0, 0, 0, 6000, 0, 0), M("Z200x75x20x2.3", 6000, 0, 0, 12000, 0, 0));
            Assert.Contains(a, j => j.Type == ConnType.GordingSambung); AllOk(a);
            var b = Detect(M("C150x65x20x3.2", 0, 0, 0, 6000, 0, 0), M("C150x65x20x3.2", 0, 1500, 0, 6000, 1500, 0), M("RD12", 3000, 0, 0, 3000, 1500, 0));
            Assert.Equal(2, b.Count(j => j.Type == ConnType.SagRod)); AllOk(b.Where(j => j.Type == ConnType.SagRod).ToList());
            var c = Detect(M("L75x75x8", -2000, 0, 0, 2000, 0, 0, 90), M("L50x50x5", 0, 0, 0, 1500, 0, 1500, 90), M("L50x50x5", 0, 0, 0, 0, 0, 1500, 90), M("L50x50x5", 0, 0, 0, -1500, 0, 1500, 90));
            Assert.Contains(c, j => j.Type == ConnType.BuhulTruss);
            var tn = c.First(j => j.Type == ConnType.BuhulTruss);
            Assert.True(((TrussNode)tn.Conn).ChordMenerus);
            Assert.Equal(2, tn.More.Count);
            AllOk(new List<Joint> { tn });
            var d = Detect(M("SHS100x100x4.5", -2000, 0, 0, 2000, 0, 0), M("SHS60x60x3.2", 0, 0, 0, 0, 0, 1500));
            Assert.Contains(d, j => j.Type == ConnType.LasLangsung); AllOk(d);
            var e = Detect(M("L75x75x8", 0, 0, 0, 4000, 0, 3000, 90), M("L75x75x8", 0, 0, 3000, 4000, 0, 0, 90));
            Assert.Contains(e, j => j.Type == ConnType.SilangBracing); AllOk(e);
        }

        [Fact]
        public void Secondary_beam_level_with_girder_is_coped()
        {
            var js = Detect(M("WF500x200x10x16", 0, 0, 0, 8000, 0, 0), M("WF300x150x6.5x9", 4000, 0, 100, 4000, 6000, 100));
            var j = Assert.Single(js);
            var dc = (DoubleCleat)j.Conn;
            Assert.True(dc.CopingAtas > 0, "coping atas");
            var r = dc.Build();
            Assert.True(r.AllOk, string.Join(" | ", r.Checks.Where(k => !k.Ok)));
            Assert.NotEmpty(r.CutSupported);
        }

        [Fact]
        public void Takeoff_workbook_is_written()
        {
            var ms = new List<TakeoffMember>();
            foreach (var (sec, fam, s, e) in new[] { ("WF400x200x8x13", Family.WF, new Vec3(0, 0, 0), new Vec3(0, 0, 6000)), ("WF400x200x8x13", Family.WF, new Vec3(20000, 0, 0), new Vec3(20000, 0, 6000)),
                ("WF400x200x8x13", Family.WF, new Vec3(0, 0, 6000), new Vec3(10000, 0, 7763)), ("C150x65x20x3.2", Family.CNP, new Vec3(0, 0, 0), new Vec3(6000, 0, 0)) })
            {
                var p = Env.Catalog.Find(sec); var cl = Categories.Classify(fam, s, e); var pr = p.Properties();
                ms.Add(new TakeoffMember { Section = sec, Family = fam, LengthMm = (e - s).Length, KgPerM = pr.KgPerM(), PaintPerM = pr.PaintAreaPerM, Role = cl.role, Category = cl.category, Level = "Level 0" });
            }
            var conns = Samples().Take(6).Select(c => { c.Recommend(); var r = c.Build(); return new TakeoffConnection { Type = c.Type, Boq = r.Boq }; }).ToList();
            var wb = Takeoff.Build("TEST", ms, conns);
            Assert.Contains(wb.Sheets, x => x.Name == "Summary");
            Assert.Equal(2, ms.Count(m => m.Mark == "C1"));
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "steelkit_qto_test.xlsx");
            wb.Save(path);
            using (var z = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.Contains(z.Entries, x => x.FullName == "xl/workbook.xml");
                Assert.Equal(wb.Sheets.Count, z.Entries.Count(x => x.FullName.StartsWith("xl/worksheets/")));
            }
            log.WriteLine(path);
        }

        static List<MemberGeom> Geo(AssistantPlan p) => p.Members.Select(m => new MemberGeom { Profile = Env.Catalog.Find(m.Profile), S = m.S, E = m.E, Roll = m.Roll, Anchor = m.Anchor, Id = m.Role }).ToList();

        [Fact]
        public void Assistant_portal_sentence()
        {
            var p = Assistant.Parse("Portal bentang 20 meter tinggi 6 m miring 10 derajat 5 bay jarak 6 m kolom WF400 rafter WF350 gording C150x65x20x3,2 jarak 1,2 m");
            log.WriteLine(p.Summary); foreach (var a in p.Assumptions) log.WriteLine(" asumsi: " + a); foreach (var u in p.Unknown) log.WriteLine(" ? " + u);
            Assert.Empty(p.Unknown);
            Assert.Equal(12, p.Members.Count(m => m.Role == "Kolom"));
            Assert.Equal(12, p.Members.Count(m => m.Role == "Rafter"));
            Assert.Equal("WF350x175x7x11", p.Members.First(m => m.Role == "Rafter").Profile);
            Assert.Equal(18, p.Members.Count(m => m.Role == "Gording"));
            var js = JointFinder.Find(Geo(p));
            Assert.Equal(12, js.Count(j => j.Type == ConnType.PelatDasar));
            Assert.Equal(12, js.Count(j => j.Type == ConnType.LututHaunch));
            Assert.Equal(6, js.Count(j => j.Type == ConnType.SambunganPelatUjung));
            Assert.Equal(18 * 6, js.Count(j => j.Type == ConnType.CleatGording));
            AllOk(js);
        }

        /// <summary>
        /// Batang miring di lutut dan puncak: ujung dipotong tegak rapat ke pelat (tidak menembus, tidak bercelah) di seluruh tinggi
        /// penampang; badan & sayap haunch menempel sisi bawah rafter dan muka pelat.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(5.7)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(25)]
        public void Sloped_members_sit_flush_on_plates(double slope)
        {
            const double tol = 0.5;
            // ---- lutut
            var k = new KneeHaunch { Sudut = slope }; k.Recommend();
            var rk = k.Build();
            var col = Env.Catalog.Find(k.Kolom); var raf = Env.Catalog.Find(k.Rafter);
            double x1 = col.Properties().Height / 2 + k.TebalPelat;
            var stub = rk.Parts.Single(p => p.Kind == PartKind.Batang && p.Keep.Count > 0);
            var c = stub.Corners().ToList();
            Assert.True(c.Min(p => p.X) >= x1 - tol, "rafter menembus pelat lutut");
            var face = c.Where(p => Math.Abs(p.X - x1) < tol).ToList();
            double cs = Math.Cos(slope * Math.PI / 180);
            Assert.True(face.Max(p => p.Z) - face.Min(p => p.Z) >= raf.Properties().Height / cs - 1, "muka potongan rafter tidak penuh setinggi penampang (ada celah)");
            // penampang awal batang (sebelum dipotong) seluruhnya di sisi yang dibuang -> setelah dipotong rapat penuh
            var cap0 = stub.Outline.Discretize(Math.PI / 24).Select(q => stub.F.Point(q.X, q.Y, stub.Z0));
            Assert.All(cap0, p => Assert.True(p.X <= x1 + 1e-6));
            double tan = Math.Tan(slope * Math.PI / 180);
            Func<double, double> zBot = x => x * tan - raf.Properties().Height / (2 * cs);
            var hParts = rk.Parts.Where(p => p.Kind == PartKind.Pelat && p.Keep.Count == 2).ToList();
            Assert.Single(hParts);
            foreach (var p in hParts[0].Corners())
            {
                Assert.True(p.X >= x1 - tol, "sayap haunch menembus pelat");
                Assert.True(p.Z <= zBot(p.X) + tol, "sayap haunch menembus sayap bawah rafter");
            }
            var web = rk.Parts.First(p => p.Kind == PartKind.Pelat && p.Outline.Vertices.Count == 3);
            var wz = web.Corners().ToList();
            Assert.Contains(wz, p => Math.Abs(p.X - x1) < tol && Math.Abs(p.Z - zBot(x1)) < tol);   // badan haunch menempel rafter

            // ---- puncak / splice
            var s = new SplicePlate { Sudut = slope }; s.Recommend();
            var rs = s.Build();
            var m = Env.Catalog.Find(s.Profil);
            var stubs = rs.Parts.Where(p => p.Kind == PartKind.Batang).ToList();
            Assert.Equal(2, stubs.Count);
            foreach (var st in stubs)
            {
                var pts = st.Corners().ToList();
                bool left = pts.Average(p => p.X) < 0;
                double plane = left ? -s.TebalPelat : s.TebalPelat;
                Assert.True(left ? pts.Max(p => p.X) <= plane + tol : pts.Min(p => p.X) >= plane - tol, "batang puncak menembus pelat");
                var f = pts.Where(p => Math.Abs(p.X - plane) < tol).ToList();
                Assert.True(f.Max(p => p.Z) - f.Min(p => p.Z) >= m.Properties().Height / cs - 1, "muka potongan puncak tidak rapat penuh");
            }
            Assert.Single(rs.CutSupport); Assert.Single(rs.CutSupported);
            Assert.True(rk.AllOk && rs.AllOk);
        }

        /// <summary>Gaya keluaran pengenal suara (Whisper): tanda baca, titik desimal, spasi di nama profil, angka berupa kata.</summary>
        [Fact]
        public void Assistant_speech_style_sentences()
        {
            var a = Assistant.Parse("Portal bentang 20 meter, tinggi 6 meter, miring 10 derajat. 5 bay, jarak 6 meter. Kolom WF400, rafter WF350, gording C150, jarak 1.2 meter, bracing RD16.");
            log.WriteLine(a.Summary); foreach (var u in a.Unknown) log.WriteLine(" ? " + u);
            Assert.Empty(a.Unknown);
            Assert.Equal(12, a.Members.Count(m => m.Role == "Kolom"));
            Assert.Equal(12, a.Members.Count(m => m.Role == "Rafter"));
            Assert.Equal(18, a.Members.Count(m => m.Role == "Gording"));
            Assert.Equal(20000, a.Members.Where(m => m.Role == "Rafter").Max(m => Math.Max(m.S.X, m.E.X)), 3);

            var b = Assistant.Parse("Portal, bentang dua puluh meter, tinggi enam meter, lima bay jarak enam meter, kolom WF 400.");
            log.WriteLine(b.Summary); foreach (var u in b.Unknown) log.WriteLine(" ? " + u);
            Assert.Equal(12, b.Members.Count(m => m.Role == "Kolom"));
            Assert.Equal("WF400x200x8x13", b.Members.First(m => m.Role == "Kolom").Profile);

            var c = Assistant.Parse("Balok WF 250 panjang 6 meter, 3 buah, jarak 2 meter.");
            log.WriteLine(c.Summary);
            Assert.Equal(3, c.Members.Count);
            Assert.Equal(6000, c.Members[0].Length, 3);
        }

        [Fact]
        public void Assistant_other_sentences()
        {
            var a = Assistant.Parse("gudang mono bentang dua belas meter tinggi lima meter kemiringan 5% 4 portal jarak 5 meter kolom wf300 bracing rd16");
            log.WriteLine(a.Summary);
            Assert.Equal(8, a.Members.Count(m => m.Role == "Kolom"));
            Assert.Equal(4, a.Members.Count(m => m.Role == "Rafter"));
            Assert.True(a.Members.Count(m => m.Role == "Bracing") >= 8);
            Assert.Equal(12000, a.Members.Where(m => m.Role == "Rafter").Max(m => m.E.X), 3);
            var js = JointFinder.Find(Geo(a));
            Assert.Contains(js, j => j.Type == ConnType.BracingRod);

            var b = Assistant.Parse("mezanin ukuran 6 x 8 m tinggi 3 m kolom h200 balok wf300 balok anak wf200 jarak 2 m");
            log.WriteLine(b.Summary);
            Assert.True(b.Members.Count(m => m.Role == "Kolom") >= 4);
            Assert.True(b.Members.Count(m => m.Role == "Balok anak") >= 2);
            Assert.Equal("H200x200x8x12", b.Members.First(m => m.Role == "Kolom").Profile);

            var c = Assistant.Parse("balok wf250 panjang 6 m 3 buah jarak 2 m");
            log.WriteLine(c.Summary);
            Assert.Equal(3, c.Members.Count);
            Assert.Equal(6000, c.Members[0].Length, 3);

            var d = Assistant.Parse("kolom h300 tinggi empat meter");
            Assert.Single(d.Members);
            Assert.Equal(4000, d.Members[0].Length, 3);
            Assert.Equal("H300x300x10x15", d.Members[0].Profile);
        }

        [Fact]
        public void Secondary_beam_gets_cleats_on_girder_web()
        {
            var ms = new List<MemberGeom>
            {
                M("WF500x200x10x16", 0, 0, 0, 8000, 0, 0),
                M("WF300x150x6.5x9", 4000, 0, 0, 4000, 6000, 0),
            };
            var joints = JointFinder.Find(ms, basePlates: false);
            var j = Assert.Single(joints);
            Assert.Equal(ConnType.SikuGanda, j.Type);
            Assert.Equal(new Vec3(4000, 0, 0).ToString(), j.Frame.O.ToString());
            Assert.Equal(1, j.Frame.U.Y, 6);
        }
    }
}
