using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelKit.Core
{
    /// <summary>Batang di gambar: garis yang digambar pengguna + profil + acuan + putaran.</summary>
    public class MemberGeom
    {
        public string Id;
        public Profile Profile;
        public Vec3 S, E;           // titik garis yang digambar
        public Anchor Anchor;
        public double Roll;

        public Vec3 Dir => (E - S).Unit;
        public double Length => (E - S).Length;
        public bool IsVertical => Math.Abs(Dir.Z) > 0.98;
        public bool IsIH => Profile.Family == Family.WF || Profile.Family == Family.H;
        public bool IsPurlin => Profile.Family == Family.CNP || Profile.Family == Family.Z;
        public double Depth => Profile.Extent(false);

        public void Axes3(out Vec3 x, out Vec3 y, out Vec3 z) => Axes.Member(E - S, Roll, out x, out y, out z);

        /// <summary>Geser dari garis gambar ke as tengah penampang (koordinat dunia).</summary>
        public Vec3 CenterOffset
        {
            get
            {
                var sh = Profile.AnchorShift(Anchor);
                Axes3(out Vec3 x, out Vec3 y, out Vec3 _);
                return x * sh.X + y * sh.Y;
            }
        }

        public Vec3 CS => S + CenterOffset;
        public Vec3 CE => E + CenterOffset;

        /// <summary>Titik terdekat pada as tengah (parameter t 0..1) dari sebuah titik.</summary>
        public double Param(Vec3 p)
        {
            var d = CE - CS; double l2 = d.Dot(d);
            return l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (p - CS).Dot(d) / l2));
        }
        public Vec3 At(double t) => CS + (CE - CS) * t;
    }

    public class Joint
    {
        public ConnType Type;
        public int Support = -1;        // indeks batang penopang (kolom / balok induk / batang A)
        public int Supported = -1;      // indeks batang yang ditopang (balok / rafter / gording / batang B)
        public bool SupportAtStart, SupportedAtStart;   // ujung mana yang disesuaikan
        /// <summary>True bila sambungan memakai ujung batang penopang (lutut, splice, pelat dasar); false bila di bentangnya.</summary>
        public bool SupportEndUsed;
        /// <summary>Kunci ujung khusus untuk penopang (mis. "M" = tengah rod untuk turnbuckle).</summary>
        public string SupportKey;
        /// <summary>Batang tambahan (buhul truss): indeks dan ujung.</summary>
        public List<int> More = new List<int>();
        public List<bool> MoreAtStart = new List<bool>();
        /// <summary>Batang yang hanya dilubangi baut sambungan ini (tidak dipotong, ujungnya tidak dipakai), mis. balok di bawah tiang.</summary>
        public List<int> Drill = new List<int>();
        public Frame Frame;
        public Connection Conn;
        public List<string> Notes = new List<string>();
        public override string ToString() => Connection.Label(Type) + (Notes.Count > 0 ? " (" + string.Join("; ", Notes) + ")" : "");
    }

    /// <summary>
    /// Mendeteksi sambungan dari susunan batang dan memasang sambungan rekomendasi (praktik umum)
    /// lengkap dengan frame lokalnya. Hasilnya tetap usulan: pengguna bisa mengganti jenis dan dimensi.
    /// </summary>
    public static partial class JointFinder
    {
        public static List<Joint> Find(IList<MemberGeom> ms, double extraTol = 50, bool basePlates = true)
        {
            var joints = new List<Joint>();
            var endUsed = new HashSet<string>();
            Func<int, bool, string> key = (i, start) => i + (start ? "S" : "E");

            // 0) buhul truss 3+ batang ringan, dan rod (bracing / sag rod / turnbuckle)
            joints.AddRange(TrussNodes(ms, extraTol, endUsed, key));
            joints.AddRange(Rods(ms, extraTol));

            // 1) ujung batang bertemu batang lain
            for (int b = 0; b < ms.Count; b++)
            {
                var mb = ms[b];
                if (mb.Profile.Family == Family.RD) continue;
                foreach (bool atStart in new[] { true, false })
                {
                    if (endUsed.Contains(key(b, atStart))) continue;
                    var P = atStart ? mb.CS : mb.CE;
                    int best = -1; double bestD = double.MaxValue, bestT = 0;
                    for (int a = 0; a < ms.Count; a++)
                    {
                        if (a == b) continue;
                        var ma = ms[a];
                        if (ma.Profile.Family == Family.RD) continue;
                        double t = ma.Param(P);
                        double d = (ma.At(t) - P).Length;
                        double tol = 0.5 * Math.Max(ma.Depth, mb.Depth) + extraTol;
                        if (d <= tol && d < bestD) { best = a; bestD = d; bestT = t; }
                    }
                    if (best < 0) continue;
                    var j = Classify(ms, best, b, atStart, bestT, extraTol, endUsed, key);
                    if (j != null) joints.Add(j);
                }
            }

            // 2) gording / girt yang melintas di atas rafter atau di muka kolom
            for (int g = 0; g < ms.Count; g++)
            {
                if (!ms[g].IsPurlin) continue;
                for (int a = 0; a < ms.Count; a++)
                {
                    if (!ms[a].IsIH) continue;
                    var j = PurlinCross(ms, a, g, extraTol);
                    if (j != null) joints.Add(j);
                }
            }

            // 2b) balok tumpuk dan persilangan bracing
            joints.AddRange(Crossings(ms, extraTol));

            // 3) kaki kolom yang tidak bertemu batang lain -> pelat dasar
            if (basePlates)
                for (int c = 0; c < ms.Count; c++)
                {
                    var m = ms[c];
                    if (!m.IsVertical) continue;
                    var fam = m.Profile.Family;
                    if (fam != Family.WF && fam != Family.H && fam != Family.HOLLOW && fam != Family.CHS) continue;
                    bool startLow = m.CS.Z <= m.CE.Z;
                    if (endUsed.Contains(key(c, startLow))) continue;
                    var P = startLow ? m.CS : m.CE;
                    bool touches = ms.Where((o, i) => i != c && !o.IsPurlin).Any(o => (o.At(o.Param(P)) - P).Length <= 0.5 * Math.Max(o.Depth, m.Depth) + extraTol);
                    if (touches) continue;
                    m.Axes3(out Vec3 x, out Vec3 y, out Vec3 _);
                    var bp = new BasePlate { Kolom = m.Profile.Name, PanjangKolom = Math.Round(m.Length) };
                    bp.Pola = fam == Family.HOLLOW || fam == Family.CHS ? AnchorPattern.Luar4 : AnchorPattern.Luar4;
                    bp.Recommend();
                    joints.Add(new Joint { Type = ConnType.PelatDasar, Support = c, SupportAtStart = startLow, SupportEndUsed = true, Frame = new Frame(P, x, y, Vec3.UnitZ), Conn = bp });
                }
            return joints;
        }

        static Joint Classify(IList<MemberGeom> ms, int a, int b, bool bAtStart, double t, double extraTol, HashSet<string> used, Func<int, bool, string> key)
        {
            var ma = ms[a]; var mb = ms[b];
            var P = bAtStart ? mb.CS : mb.CE;
            var other = bAtStart ? mb.CE : mb.CS;
            var uB = (other - P).Unit;                       // dari sambungan ke arah batang B
            double tolEnd = 0.5 * Math.Max(ma.Depth, mb.Depth) + extraTol;
            bool aStartNear = (ma.CS - P).Length <= tolEnd, aEndNear = (ma.CE - P).Length <= tolEnd;
            bool e2eAny = aStartNear || aEndNear;
            bool aNearStart = aStartNear && (!aEndNear || (ma.CS - P).Length <= (ma.CE - P).Length);
            // gording bersambung ujung ke ujung
            if (ma.IsPurlin && mb.IsPurlin && e2eAny && Math.Abs(ma.Dir.Dot(mb.Dir)) > 0.995)
            {
                if (used.Contains(key(a, aNearStart))) return null;
                used.Add(key(a, aNearStart)); used.Add(key(b, bAtStart));
                return PurlinJoint(ms, a, b, aNearStart, bAtStart);
            }
            // cabang hollow/pipa menempel di bentang batang hollow/pipa -> las langsung
            if (Tube(ma) && Tube(mb) && !e2eAny)
            {
                used.Add(key(b, bAtStart));
                return DirectWeldJoint(ms, a, b, bAtStart);
            }
            // bracing ringan ke kolom / balok WF-H
            if (ma.IsIH && LegFace.Supported(mb.Profile.Family) && !mb.IsPurlin)
            {
                var jb = BraceToHost(ms, a, b, bAtStart, extraTol);
                if (jb != null) used.Add(key(b, bAtStart));
                return jb;
            }
            // kolom tegak menyentuh bentang balok / rafter
            if (ma.IsIH && mb.IsIH && mb.IsVertical && !ma.IsVertical && !e2eAny)
            {
                used.Add(key(b, bAtStart));
                return ColumnOnBeam(ms, a, b, bAtStart);
            }
            // balok WF ke kolom hollow / pipa
            if (Tube(ma) && ma.IsVertical && mb.IsIH && !mb.IsVertical && !e2eAny)
            {
                used.Add(key(b, bAtStart));
                return BeamToTube(ms, a, b, bAtStart);
            }
            bool light = LegFace.Supported(ma.Profile.Family) && LegFace.Supported(mb.Profile.Family);
            if (light)
            {
                bool e2e = aStartNear || aEndNear;
                bool aStart = aStartNear && (!aEndNear || (ma.CS - P).Length <= (ma.CE - P).Length);
                if (e2e && used.Contains(key(a, aStart))) return null;
                var g = Gusset(ms, a, b, bAtStart, e2e, aStart);
                if (g == null) return null;
                if (e2e) used.Add(key(a, aStart));
                used.Add(key(b, bAtStart));
                return g;
            }
            if (mb.IsPurlin) return null;                    // gording ditangani oleh pemeriksaan lintasan

            if (aStartNear || aEndNear)
            {
                bool aAtStart = aStartNear && (!aEndNear || (ma.CS - P).Length <= (ma.CE - P).Length);
                if (used.Contains(key(a, aAtStart))) return null;
                used.Add(key(a, aAtStart)); used.Add(key(b, bAtStart));
                var Pa = aAtStart ? ma.CS : ma.CE;
                var uA = ((aAtStart ? ma.CE : ma.CS) - Pa).Unit;

                // kolom + rafter/balok bertemu di ujung -> lutut
                if (ma.IsVertical != mb.IsVertical && ma.IsIH && mb.IsIH)
                {
                    int c = ma.IsVertical ? a : b, r = ma.IsVertical ? b : a;
                    bool cStart = ma.IsVertical ? aAtStart : bAtStart, rStart = ma.IsVertical ? bAtStart : aAtStart;
                    return Knee(ms, c, r, cStart, rStart);
                }
                // dua batang ujung ke ujung -> pelat ujung berpasangan (puncak / splice)
                if (ma.Profile.Family == mb.Profile.Family && (ma.IsIH || ma.Profile.Family == Family.HOLLOW))
                    return Splice(ms, a, b, aAtStart, bAtStart, uA, uB);
                return null;
            }

            // ujung B menempel di bentang A
            used.Add(key(b, bAtStart));
            if (!mb.IsIH || !ma.IsIH) return null;
            if (mb.IsVertical) return null;
            var Q = ClosestOnLine(mb.CS, mb.Dir, ma.CS, ma.Dir);   // as balok di as penopang
            var uH = new Vec3(uB.X, uB.Y, 0);
            if (uH.Length < 1e-6) return null;
            uH = uH.Unit;
            var fr = new Frame(Q, uH, Vec3.UnitZ.Cross(uH), Vec3.UnitZ);
            var j = new Joint { Support = a, Supported = b, SupportedAtStart = bAtStart, Frame = fr };
            if (Math.Abs(uB.Z) > 0.09) j.Notes.Add("balok miring: pelat dibuat tegak");
            if (ma.IsVertical)
            {
                ma.Axes3(out Vec3 cx, out Vec3 cy, out Vec3 _);
                bool flange = Math.Abs(uH.Dot(cy)) >= Math.Abs(uH.Dot(cx));
                double align = Math.Max(Math.Abs(uH.Dot(cy)), Math.Abs(uH.Dot(cx)));
                if (align < 0.996) j.Notes.Add("balok tidak sejajar sumbu kolom");
                var face = flange ? SupportFace.SayapKolom : SupportFace.BadanKolom;
                if (mb.Depth <= 250)
                {
                    var fp = new FinPlate { Balok = mb.Profile.Name, Penopang = ma.Profile.Name, Muka = face };
                    fp.Recommend(); j.Type = ConnType.FinPlate; j.Conn = fp;
                }
                else
                {
                    var ep = new EndPlate { Balok = mb.Profile.Name, Kolom = ma.Profile.Name, Muka = face };
                    if (face == SupportFace.BadanKolom) { ep.PengakuKolom = false; ep.Jenis = EndPlateKind.Rata; }
                    ep.Recommend(); j.Type = ConnType.PelatUjung; j.Conn = ep;
                }
            }
            else
            {
                var dc = new DoubleCleat { Balok = mb.Profile.Name, Penopang = ma.Profile.Name, Muka = SupportFace.BadanBalok, ElevasiPenopang = Math.Round((ma.At(ma.Param(Q)) - Q).Dot(Vec3.UnitZ), 2) };
                dc.Recommend(); j.Type = ConnType.SikuGanda; j.Conn = dc;
                if (Math.Abs(uH.Dot(ma.Dir)) > 0.1) j.Notes.Add("balok anak tidak tegak lurus balok induk");
            }
            return j;
        }

        static Joint Knee(IList<MemberGeom> ms, int c, int r, bool cAtStart, bool rAtStart)
        {
            var mc = ms[c]; var mr = ms[r];
            var Pr = rAtStart ? mr.CS : mr.CE;
            var ur = ((rAtStart ? mr.CE : mr.CS) - Pr).Unit;
            var O = ClosestOnLine(mc.CS, mc.Dir, mr.CS, mr.Dir);
            var uH = new Vec3(ur.X, ur.Y, 0).Unit;
            double slope = Math.Atan2(ur.Z, new Vec3(ur.X, ur.Y, 0).Length) * 180 / Math.PI;
            var fr = new Frame(O, uH, Vec3.UnitZ.Cross(uH), Vec3.UnitZ);
            var j = new Joint { Type = ConnType.LututHaunch, Support = c, Supported = r, SupportAtStart = cAtStart, SupportEndUsed = true, SupportedAtStart = rAtStart, Frame = fr };
            mc.Axes3(out Vec3 cx, out Vec3 cy, out Vec3 _);
            if (Math.Abs(uH.Dot(cy)) < 0.996) j.Notes.Add("rafter tidak tegak lurus sayap kolom");
            if (Math.Abs(slope) < 1) j.Notes.Add("rafter datar");
            var k = new KneeHaunch { Kolom = mc.Profile.Name, Rafter = mr.Profile.Name, Sudut = Math.Round(slope, 2), PanjangRafter = mr.Length };
            k.Recommend();
            j.Conn = k;
            return j;
        }

        static Joint Splice(IList<MemberGeom> ms, int a, int b, bool aAtStart, bool bAtStart, Vec3 uA, Vec3 uB)
        {
            var ma = ms[a]; var mb = ms[b];
            var O = ((aAtStart ? ma.CS : ma.CE) + (bAtStart ? mb.CS : mb.CE)) * 0.5;
            var U = (uB - uA).Unit;
            ma.Axes3(out Vec3 _, out Vec3 ya, out Vec3 _);
            mb.Axes3(out Vec3 _, out Vec3 yb, out Vec3 _);
            var up = ya + yb;
            var W = (up - U * up.Dot(U)).Unit;
            if (W.Length < 0.5) W = Math.Abs(U.Z) < 0.9 ? (Vec3.UnitZ - U * U.Z).Unit : (Vec3.UnitY - U * U.Y).Unit;
            var fr = new Frame(O, U, W.Cross(U), W);
            double ang = Math.Atan2(-uB.Dot(W), uB.Dot(U)) * 180 / Math.PI;
            var j = new Joint { Type = ConnType.SambunganPelatUjung, Support = a, Supported = b, SupportAtStart = aAtStart, SupportEndUsed = true, SupportedAtStart = bAtStart, Frame = fr };
            if (ma.Profile.Name != mb.Profile.Name) j.Notes.Add("profil kedua batang berbeda: pelat mengikuti " + mb.Profile.Name);
            var s = new SplicePlate { Profil = mb.Profile.Name, Sudut = Math.Round(ang, 2) };
            s.Recommend();
            j.Conn = s;
            return j;
        }

        /// <summary>Dua batang ringan (siku, plat, CNP, hollow, pipa) bertemu: pelat buhul di bidang kaki yang sebidang.</summary>
        static Joint Gusset(IList<MemberGeom> ms, int a, int b, bool bAtStart, bool endToEnd, bool aAtStart)
        {
            var ma = ms[a]; var mb = ms[b];
            var q1 = ClosestOnLine(ma.CS, ma.Dir, mb.CS, mb.Dir);      // di as A
            var q2 = ClosestOnLine(mb.CS, mb.Dir, ma.CS, ma.Dir);      // di as B
            if (Math.Abs(ma.Dir.Dot(mb.Dir)) > 0.999) { q1 = endToEnd ? (aAtStart ? ma.CS : ma.CE) : q1; q2 = bAtStart ? mb.CS : mb.CE; }
            var node = (q1 + q2) * 0.5;
            var farB = bAtStart ? mb.CE : mb.CS;
            var uB = (farB - node).Unit;
            Vec3 uA;
            if (endToEnd) uA = ((aAtStart ? ma.CE : ma.CS) - node).Unit;
            else uA = ma.Dir;
            ma.Axes3(out Vec3 xa, out Vec3 ya, out Vec3 _);
            mb.Axes3(out Vec3 xb, out Vec3 yb, out Vec3 _);
            var legsA = LegFace.Of(ma.Profile); var legsB = LegFace.Of(mb.Profile);
            Func<Vec2, Vec3, Vec3, Vec3> W = (n, x, y) => (x * n.X + y * n.Y).Unit;
            var N = uA.Cross(uB);
            int ia = -1;
            if (N.Length > 0.05)
            {
                N = N.Unit;
                for (int i = 0; i < legsA.Count && ia < 0; i++) if (Math.Abs(W(legsA[i].Normal, xa, ya).Dot(N)) > 0.98) ia = i;
            }
            else ia = 0;                                                  // lurus: pakai kaki utama A
            if (ia < 0) return null;                                      // batang tidak sebidang dengan kaki A
            var G = W(legsA[ia].Normal, xa, ya);
            int ib = -1;
            for (int i = 0; i < legsB.Count && ib < 0; i++) if (W(legsB[i].Normal, xb, yb).Dot(G) > 0.98) ib = i;
            double mismatch = 0;
            if (ib < 0)
            {
                for (int i = 0; i < legsB.Count && ib < 0; i++) if (Math.Abs(W(legsB[i].Normal, xb, yb).Dot(G)) > 0.98) ib = i;
                if (ib < 0) return null;
                mismatch = 999;                                           // kaki B menghadap sisi lain
            }
            var la = legsA[ia]; var lb = legsB[ib];
            var faceA = q1 + G * la.Offset; var faceB = q2 + G * lb.Offset;
            if (mismatch == 0) mismatch = Math.Round((faceB - faceA).Dot(G), 2);
            var Wn = -G;
            var X = (uA - Wn * uA.Dot(Wn)).Unit;
            var Y = Wn.Cross(X);
            var fr = new Frame(faceA - X * 0, X, Y, Wn);
            fr.O = node + G * la.Offset;
            double sud = Math.Atan2(uB.Dot(Y), uB.Dot(X)) * 180 / Math.PI;
            Func<LegFace, Vec3, Vec3, Vec3, double> side = (lf, x, y, u) => Math.Sign(W(lf.Axis, x, y).Dot(Wn.Cross(u))) == 0 ? 1 : Math.Sign(W(lf.Axis, x, y).Dot(Wn.Cross(u)));
            double sa = side(la, xa, ya, uA), sb = side(lb, xb, yb, uB);
            var c = new GussetNode
            {
                ProfilA = ma.Profile.Name, ProfilB = mb.Profile.Name, SudutB = Math.Round(sud, 3), MenerusA = !endToEnd,
                MukaA = ia, MukaB = ib, HeelA = sa * la.Heel, ArahA = sa, HeelB = sb * lb.Heel, ArahB = sb, SelisihMuka = mismatch,
            };
            c.Recommend();
            var j = new Joint { Type = ConnType.PelatBuhul, Support = a, Supported = b, SupportAtStart = aAtStart, SupportedAtStart = bAtStart, SupportEndUsed = endToEnd, Frame = fr, Conn = c };
            if (Math.Abs(mismatch) > 1) j.Notes.Add(mismatch > 900 ? "kaki batang B menghadap sisi lain: putar batang B" : "muka kaki tidak sebidang (selisih " + Fmt.N(mismatch) + " mm): samakan acuan/profil");
            return j;
        }

        static Joint PurlinCross(IList<MemberGeom> ms, int a, int g, double extraTol)
        {
            var ma = ms[a]; var mg = ms[g];
            if (Math.Abs(ma.Dir.Dot(mg.Dir)) > 0.5) return null;            // harus menyilang
            SegClosest(ma.CS, ma.CE, mg.CS, mg.CE, out double ta, out double tg);
            if (ta <= 1e-6 || ta >= 1 - 1e-6 || tg < 0 || tg > 1) return null;
            var Pa = ma.At(ta); var Pg = mg.At(tg);
            var dv = Pg - Pa;
            var perp = dv - ma.Dir * dv.Dot(ma.Dir);
            ma.Axes3(out Vec3 x, out Vec3 y, out Vec3 _);
            bool alongY = Math.Abs(perp.Dot(y)) >= Math.Abs(perp.Dot(x));
            var W = (alongY ? y : x) * Math.Sign(alongY ? perp.Dot(y) : perp.Dot(x));
            if (W.Length < 0.5) return null;
            double faceOff = alongY ? ma.Depth / 2 : ma.Profile.Extent(true) / 2;
            double gap = perp.Dot(W) - faceOff - mg.Depth / 2;
            if (Math.Abs(gap) > extraTol + 0.25 * mg.Depth) return null;
            var O = Pa + W * faceOff;
            var U = (mg.Dir - W * mg.Dir.Dot(W)).Unit;
            var fr = new Frame(O, U, W.Cross(U), W);
            var j = new Joint { Type = ConnType.CleatGording, Support = a, Supported = g, Frame = fr };
            if (Math.Abs(gap) > 3) j.Notes.Add("gording tidak duduk tepat di sayap (selisih " + Fmt.N(gap) + " mm)");
            var c = new PurlinCleat { Gording = mg.Profile.Name, Rafter = ma.Profile.Name };
            if (mg.Profile.Family == Family.Z) c.JumlahBaut = 2;
            c.Recommend();
            j.Conn = c;
            return j;
        }

        /// <summary>Titik pada garis 2 (p2 + t d2) yang terdekat ke garis 1.</summary>
        public static Vec3 ClosestOnLine(Vec3 p1, Vec3 d1, Vec3 p2, Vec3 d2)
        {
            // titik pada garis 1 yang terdekat ke garis 2 (dipakai: as penopang ketemu as balok)
            var w0 = p1 - p2;
            double a = d1.Dot(d1), b = d1.Dot(d2), c = d2.Dot(d2), d = d1.Dot(w0), e = d2.Dot(w0);
            double den = a * c - b * b;
            if (Math.Abs(den) < 1e-9) return p1;
            double s = (b * e - c * d) / den;
            return p1 + d1 * s;
        }

        static void SegClosest(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2, out double s, out double t)
        {
            var d1 = q1 - p1; var d2 = q2 - p2; var r = p1 - p2;
            double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), c = d1.Dot(r), b = d1.Dot(d2);
            double den = a * e - b * b;
            s = den > 1e-9 ? Math.Max(0, Math.Min(1, (b * f - c * e) / den)) : 0;
            t = (b * s + f) / e;
            if (t < 0) { t = 0; s = Math.Max(0, Math.Min(1, -c / a)); }
            else if (t > 1) { t = 1; s = Math.Max(0, Math.Min(1, (b - c) / a)); }
        }
    }
}
