using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelKit.Core
{
    public static partial class JointFinder
    {
        static bool Tube(MemberGeom m) => m.Profile.Family == Family.HOLLOW || m.Profile.Family == Family.CHS;
        static bool TrussFamily(MemberGeom m) => m.Profile.Family == Family.L || m.Profile.Family == Family.PL || Tube(m);
        static double Deg(double rad) => rad * 180 / Math.PI;

        /// <summary>Muka kaki yang normalnya sejajar arah n (dunia). Mengembalikan indeks atau -1.</summary>
        static int FaceAlong(MemberGeom m, Vec3 n, bool sameSign, out LegFace lf)
        {
            m.Axes3(out Vec3 x, out Vec3 y, out Vec3 _);
            var legs = LegFace.Of(m.Profile);
            for (int i = 0; i < legs.Count; i++)
            {
                var w = (x * legs[i].Normal.X + y * legs[i].Normal.Y).Unit;
                double d = w.Dot(n);
                if (sameSign ? d > 0.98 : Math.Abs(d) > 0.98) { lf = legs[i]; return i; }
            }
            lf = null; return -1;
        }

        static Vec3 LegAxisWorld(MemberGeom m, LegFace lf)
        {
            m.Axes3(out Vec3 x, out Vec3 y, out Vec3 _);
            return (x * lf.Axis.X + y * lf.Axis.Y).Unit;
        }

        // ------------------------------------------------------------------ buhul truss (3+ batang ringan)
        static List<Joint> TrussNodes(IList<MemberGeom> ms, double extraTol, HashSet<string> used, Func<int, bool, string> key)
        {
            var res = new List<Joint>();
            var ends = new List<(int i, bool s, Vec3 p)>();
            for (int i = 0; i < ms.Count; i++)
                if (TrussFamily(ms[i])) { ends.Add((i, true, ms[i].CS)); ends.Add((i, false, ms[i].CE)); }
            var done = new HashSet<int>();
            for (int k = 0; k < ends.Count; k++)
            {
                if (done.Contains(k)) continue;
                var node = ends[k].p;
                double tol = 0.5 * ms[ends[k].i].Depth + extraTol;
                var cl = new List<int>();
                for (int q = 0; q < ends.Count; q++)
                {
                    if (done.Contains(q)) continue;
                    if (q != k && ends[q].i == ends[k].i) continue;
                    if ((ends[q].p - node).Length <= tol + 0.5 * ms[ends[q].i].Depth) cl.Add(q);
                }
                cl = cl.GroupBy(q => ends[q].i).Select(g => g.First()).ToList();
                int through = -1;
                for (int i = 0; i < ms.Count; i++)
                {
                    if (!TrussFamily(ms[i]) || cl.Any(q => ends[q].i == i)) continue;
                    double t = ms[i].Param(node);
                    if (t > 0.02 && t < 0.98 && (ms[i].At(t) - node).Length <= tol + 0.5 * ms[i].Depth) { through = i; break; }
                }
                int count = cl.Count + (through >= 0 ? 1 : 0);
                if (count < 3) continue;
                foreach (var q in cl) done.Add(q);
                var j = Truss(ms, cl.Select(q => (ends[q].i, ends[q].s)).ToList(), through, node);
                if (j == null) continue;
                foreach (var q in cl) used.Add(key(ends[q].i, ends[q].s));
                res.Add(j);
            }
            return res;
        }

        static Joint Truss(IList<MemberGeom> ms, List<(int i, bool s)> endMembers, int through, Vec3 node)
        {
            int chord; bool chordStart = false; bool cont = through >= 0;
            var others = new List<(int i, bool s)>(endMembers);
            if (cont) chord = through;
            else { var c = others.OrderByDescending(e => ms[e.i].Depth).First(); chord = c.i; chordStart = c.s; others.Remove(c); }
            var mc = ms[chord];
            var uC = cont ? mc.Dir : ((chordStart ? mc.CE : mc.CS) - node).Unit;
            Func<(int i, bool s), Vec3> dirOf = e => ((e.s ? ms[e.i].CE : ms[e.i].CS) - node).Unit;
            var N = Vec3.Zero;
            foreach (var e in others) { var n = uC.Cross(dirOf(e)); if (n.Length > 0.1) { N = n.Unit; break; } }
            if (N.Length < 0.5) return null;
            int ic = FaceAlong(mc, N, false, out LegFace lc);
            if (ic < 0) return null;
            mc.Axes3(out Vec3 xc, out Vec3 yc, out Vec3 _);
            var G = (xc * lc.Normal.X + yc * lc.Normal.Y).Unit;
            var W = -G;
            var q = cont ? mc.At(mc.Param(node)) : node;
            var O = q + G * lc.Offset;
            var X = (uC - W * uC.Dot(W)).Unit; var Y = W.Cross(X);
            var fr = new Frame(O, X, Y, W);
            Func<MemberGeom, LegFace, Vec3, (double heel, double dir)> side = (m, lf, u) =>
            {
                double s = Math.Sign(LegAxisWorld(m, lf).Dot(W.Cross(u))); if (s == 0) s = 1;
                return (s * lf.Heel, s);
            };
            var sc = side(mc, lc, uC);
            var list = new List<TrussNode.Member>();
            double mismatch = 0;
            foreach (var e in others)
            {
                var m = ms[e.i]; var u = dirOf(e);
                int sideP = 1;
                int idx = FaceAlong(m, G, true, out LegFace lf);
                if (idx < 0) { idx = FaceAlong(m, -G, true, out lf); sideP = -1; }
                if (idx < 0) return null;
                var p = e.s ? m.CS : m.CE;
                double zc = (p - O).Dot(W);
                var sd = side(m, lf, u);
                if (sideP < 0) sd = (sd.heel, sd.dir);
                list.Add(new TrussNode.Member { Profil = m.Profile.Name, Sudut = Math.Round(Deg(Math.Atan2(u.Dot(Y), u.Dot(X))), 3), Muka = idx, Heel = sd.heel, Arah = sd.dir, Sisi = sideP, Zc = Math.Round(zc, 2) });
            }
            var tn = new TrussNode { Chord = mc.Profile.Name, ChordMenerus = cont, Batang = TrussNode.Pack(list), MukaChord = ic, HeelChord = sc.heel, ArahChord = sc.dir, SelisihMuka = Math.Round(mismatch, 2) };
            tn.Recommend();
            var j = new Joint { Type = ConnType.BuhulTruss, Support = chord, SupportAtStart = chordStart, SupportEndUsed = !cont, Frame = fr, Conn = tn };
            j.Supported = others[0].i; j.SupportedAtStart = others[0].s;
            for (int k = 1; k < others.Count; k++) { j.More.Add(others[k].i); j.MoreAtStart.Add(others[k].s); }
            if (mismatch > 1) j.Notes.Add(mismatch > 900 ? "ada batang yang kakinya menghadap sisi lain" : "muka kaki tidak sebidang (selisih " + Fmt.N(mismatch) + " mm)");
            return j;
        }

        // ------------------------------------------------------------------ rod: bracing, sag rod, turnbuckle
        static List<Joint> Rods(IList<MemberGeom> ms, double extraTol)
        {
            var res = new List<Joint>();
            for (int r = 0; r < ms.Count; r++)
            {
                var mr = ms[r];
                if (mr.Profile.Family != Family.RD) continue;
                int hostEnds = 0; bool toPurlin = false;
                foreach (bool atStart in new[] { true, false })
                {
                    var P = atStart ? mr.CS : mr.CE;
                    var uR = ((atStart ? mr.CE : mr.CS) - P).Unit;
                    int best = -1; double bd = double.MaxValue;
                    for (int h = 0; h < ms.Count; h++)
                    {
                        if (h == r || !(ms[h].IsIH || ms[h].IsPurlin)) continue;
                        double d = (ms[h].At(ms[h].Param(P)) - P).Length;
                        if (d <= 0.5 * ms[h].Depth + extraTol && d < bd) { best = h; bd = d; }
                    }
                    if (best < 0) continue;
                    var mh = ms[best];
                    mh.Axes3(out Vec3 xh, out Vec3 yh, out Vec3 _);
                    if (mh.IsIH)
                    {
                        var Z = xh * Math.Sign(uR.Dot(xh) == 0 ? 1 : uR.Dot(xh));
                        var C = mh.At(mh.Param(P));
                        double den = uR.Dot(Z);
                        if (Math.Abs(den) < 0.05) continue;
                        double t = -(P - C).Dot(Z) / den;
                        var O = P + uR * t;
                        var inPlane = uR - Z * uR.Dot(Z);
                        var X = inPlane.Length > 1e-6 ? inPlane.Unit : mh.Dir;
                        var Y = Z.Cross(X);
                        var c = new BracingRod { Host = mh.Profile.Name, Rod = mr.Profile.Name, SudutRod = Math.Round(Deg(Math.Asin(Math.Min(1, Math.Abs(uR.Dot(Z))))), 2) };
                        var hd = mh.Dir;
                        c.SudutHost = Math.Round(Deg(Math.Atan2(hd.Dot(Y), hd.Dot(X))), 2);
                        c.Recommend();
                        res.Add(new Joint { Type = ConnType.BracingRod, Support = best, Supported = r, SupportedAtStart = atStart, Frame = new Frame(O, X, Y, Z), Conn = c });
                        hostEnds++;
                    }
                    else
                    {
                        toPurlin = true;
                        var Z = xh * Math.Sign(uR.Dot(xh) == 0 ? 1 : uR.Dot(xh));
                        double webC = mh.Profile.Family == Family.CNP ? -mh.Profile.Extent(true) / 2 + mh.Profile["t"] / 2 : 0;
                        var C = mh.At(mh.Param(P)) + xh * webC;
                        double den = uR.Dot(Z);
                        if (Math.Abs(den) < 0.3) continue;
                        var O = P + uR * (-(P - C).Dot(Z) / den);
                        var inPlane = uR - Z * uR.Dot(Z);
                        var X = inPlane.Length > 1e-6 ? inPlane.Unit : mh.Dir;
                        var c = new SagRod { Gording = mh.Profile.Name, Rod = mr.Profile.Name, Sudut = Math.Round(Deg(Math.Acos(Math.Min(1, Math.Abs(den)))), 2) };
                        c.Recommend();
                        res.Add(new Joint { Type = ConnType.SagRod, Support = best, Supported = r, SupportedAtStart = atStart, Frame = new Frame(O, X, Z.Cross(X), Z), Conn = c });
                    }
                }
                if (hostEnds == 2 && !toPurlin && mr.Length > 2000)
                {
                    var tb = new Turnbuckle { Rod = mr.Profile.Name };
                    tb.Recommend();
                    var u = mr.Dir;
                    var helper = Math.Abs(u.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX;
                    var y = helper.Cross(u).Unit;
                    // di 0,3 panjang dari ujung awal: tidak di titik silang bracing X (dua turnbuckle tidak bertumpuk) dan mudah dijangkau
                    res.Add(new Joint { Type = ConnType.Turnbuckle, Support = r, SupportKey = "M", Frame = new Frame(mr.At(0.3), u, y, u.Cross(y)), Conn = tb });
                }
            }
            return res;
        }

        // ------------------------------------------------------------------ bracing ringan ke kolom/balok
        static Joint BraceToHost(IList<MemberGeom> ms, int a, int b, bool bAtStart, double extraTol)
        {
            var mh = ms[a]; var mb = ms[b];
            var P = bAtStart ? mb.CS : mb.CE;
            var uB = ((bAtStart ? mb.CE : mb.CS) - P).Unit;
            var q = ClosestOnLine(mh.CS, mh.Dir, mb.CS, mb.Dir);
            var X = mh.Dir;
            var N0 = X.Cross(uB);
            if (N0.Length < 0.1) return null;
            N0 = N0.Unit;
            int fi = FaceAlong(mb, N0, false, out LegFace lf);
            if (fi < 0) return null;
            mb.Axes3(out Vec3 xb, out Vec3 yb, out Vec3 _);
            var fn = (xb * lf.Normal.X + yb * lf.Normal.Y).Unit;
            var Z = N0;
            if (fn.Dot(Z) > 0) { X = -X; Z = -Z; }                  // kaki bracing menghadap -Z (pelat di bawahnya)
            var Y = Z.Cross(X);
            var fr = new Frame(q, X, Y, Z);
            mh.Axes3(out Vec3 xh, out Vec3 yh, out Vec3 _);
            var face = Math.Abs(Y.Dot(yh)) >= Math.Abs(Y.Dot(xh)) ? HostFace.Sayap : HostFace.Badan;
            double s = Math.Sign(LegAxisWorld(mb, lf).Dot(Z.Cross(uB))); if (s == 0) s = 1;
            var c = new BracingGusset
            {
                Host = mh.Profile.Name, Muka = face, Brace = mb.Profile.Name, MukaBrace = fi, HeelBrace = s * lf.Heel, ArahBrace = s,
                SudutBrace = Math.Round(Deg(Math.Atan2(uB.Dot(Y), uB.Dot(X))), 2),
            };
            // batang IH kedua di titik yang sama -> buhul sudut balok-kolom
            for (int k = 0; k < ms.Count; k++)
            {
                if (k == a || k == b || !ms[k].IsIH) continue;
                var mk = ms[k];
                double tk = mk.Param(q);
                if ((mk.At(tk) - q).Length > 0.5 * Math.Max(mk.Depth, mh.Depth) + extraTol) continue;
                if (Math.Abs(mk.Dir.Dot(X)) > 0.95) continue;
                if (Math.Abs(mk.Dir.Dot(Z)) > 0.5) continue;           // tegak lurus bidang buhul (mis. pengaku muka sebelah di sudut menara): bukan batang kedua
                var far = (mk.CS - q).Length > (mk.CE - q).Length ? mk.CS : mk.CE;
                var u2 = (far - q); u2 = (u2 - Z * u2.Dot(Z)).Unit;
                if (u2.Dot(Y) < 0) continue;
                c.Host2 = mk.Profile.Name;
                c.SudutHost2 = Math.Round(Deg(Math.Atan2(u2.Dot(Y), u2.Dot(X))), 2);
                break;
            }
            c.Recommend();
            double zb = (mb.At(mb.Param(q)) - q).Dot(Z);
            c.GeserPelat = Math.Round(zb - lf.Offset - c.TebalPelat / 2, 2);
            var j = new Joint { Type = ConnType.BracingBuhul, Support = a, Supported = b, SupportedAtStart = bAtStart, Frame = fr, Conn = c };
            if (!string.IsNullOrEmpty(c.Host2)) j.Notes.Add("buhul sudut ke " + c.Host2);
            return j;
        }

        // ------------------------------------------------------------------ las langsung pipa / hollow
        static Joint DirectWeldJoint(IList<MemberGeom> ms, int a, int b, bool bAtStart)
        {
            var mc = ms[a]; var mb = ms[b];
            var uB = ((bAtStart ? mb.CE : mb.CS) - (bAtStart ? mb.CS : mb.CE)).Unit;
            var q = ClosestOnLine(mc.CS, mc.Dir, mb.CS, mb.Dir);
            var X = mc.Dir;
            var Yr = uB - X * uB.Dot(X);
            if (Yr.Length < 0.1) return null;
            var Y = Yr.Unit; var Z = X.Cross(Y);
            mc.Axes3(out Vec3 xc, out Vec3 yc, out Vec3 _);
            double ext = mc.Profile.Family == Family.CHS ? mc.Profile["D"] : (Math.Abs(Y.Dot(yc)) >= Math.Abs(Y.Dot(xc)) ? mc.Profile.Extent(false) : mc.Profile.Extent(true));
            var c = new DirectWeld { Chord = mc.Profile.Name, Cabang = mb.Profile.Name, Sudut = Math.Round(Deg(Math.Atan2(uB.Dot(Y), uB.Dot(X))), 2), TepiArahY = ext };
            c.Recommend();
            return new Joint { Type = ConnType.LasLangsung, Support = a, Supported = b, SupportedAtStart = bAtStart, Frame = new Frame(q, X, Y, Z), Conn = c };
        }

        // ------------------------------------------------------------------ kolom tegak menyentuh balok/rafter
        static Joint ColumnOnBeam(IList<MemberGeom> ms, int beam, int col, bool colAtStart)
        {
            var mbm = ms[beam]; var mc = ms[col];
            var P = colAtStart ? mc.CS : mc.CE;
            var other = colAtStart ? mc.CE : mc.CS;
            bool below = other.Z < P.Z;                      // kolom di bawah balok -> ujung atas kolom
            mbm.Axes3(out Vec3 xb, out Vec3 yb, out Vec3 _);
            var Q = mbm.At(mbm.Param(P));
            var up = Vec3.UnitZ;
            mc.Axes3(out Vec3 xc, out Vec3 yc, out Vec3 _);
            double slope = Deg(Math.Asin(Math.Max(-1, Math.Min(1, mbm.Dir.Z))));
            if (below)
            {
                double hb = mbm.Depth / 2;
                // titik pada as kolom setinggi muka bawah balok
                double s = ((Q - yb * hb) - P).Dot(yb) / Math.Max(up.Dot(yb), 0.2);
                var O = P + up * s;
                // balok miring yang hanya ditopang kolom ini (kanopi T, tidak ada kolom lain di bawahnya) -> pelat tutup miring
                bool sole = !ms.Where((o, i) => i != col && i != beam && o.IsVertical).Any(o =>
                {
                    foreach (var pt in new[] { o.CS, o.CE })
                    {
                        double t = mbm.Param(pt);
                        if (t < -0.02 || t > 1.02) continue;
                        if ((mbm.At(t) - pt).Length <= 0.5 * Math.Max(o.Depth, mbm.Depth) + 50) return true;
                    }
                    return false;
                });
                if (Math.Abs(slope) > 2 && sole)
                {
                    var X = mbm.Dir.Z >= 0 ? mbm.Dir : -mbm.Dir;
                    var Z = yb.Z >= 0 ? yb : -yb; var Y = Z.Cross(X);
                    var hx = new Vec3(X.X, X.Y, 0).Unit;
                    var cp = new CapPlate { Kolom = mc.Profile.Name, Balok = mbm.Profile.Name, Sudut = Math.Round(Deg(Math.Asin(Math.Min(1, X.Z))), 2),
                        Orientasi = Math.Abs(hx.Dot(yc)) >= Math.Abs(hx.Dot(xc)) ? ColumnOrient.TinggiSearahBalok : ColumnOrient.TinggiMelintang };
                    cp.Recommend();
                    // titik acuan: as kolom di muka bawah balok miring
                    double t0 = ((Q - Z * hb) - P).Dot(Z) / Math.Max(up.Dot(Z), 0.2);
                    return new Joint { Type = ConnType.PelatTutup, Support = beam, Supported = col, SupportedAtStart = colAtStart, Frame = new Frame(P + up * t0, X, Y, Z), Conn = cp };
                }
                if (Math.Abs(slope) <= 2)
                {
                    var X = new Vec3(mbm.Dir.X, mbm.Dir.Y, 0).Unit; var Y = Vec3.UnitZ.Cross(X);
                    var c = new CapPlate { Kolom = mc.Profile.Name, Balok = mbm.Profile.Name, Orientasi = Math.Abs(X.Dot(yc)) >= Math.Abs(X.Dot(xc)) ? ColumnOrient.TinggiSearahBalok : ColumnOrient.TinggiMelintang };
                    c.Recommend();
                    return new Joint { Type = ConnType.PelatTutup, Support = beam, Supported = col, SupportedAtStart = colAtStart, Frame = new Frame(O, X, Y, Vec3.UnitZ), Conn = c };
                }
                else
                {
                    var X = mbm.Dir.Z >= 0 ? mbm.Dir : -mbm.Dir;
                    var Z = yb; var Y = Z.Cross(X);
                    var c = new WindColumn { Kolom = mc.Profile.Name, Rafter = mbm.Profile.Name, Sudut = Math.Round(Deg(Math.Asin(Math.Min(1, X.Z))), 2) };
                    c.Recommend();
                    var j = new Joint { Type = ConnType.KolomAngin, Support = beam, Supported = col, SupportedAtStart = colAtStart, Frame = new Frame(O, X, Y, Z), Conn = c };
                    if (Math.Abs(X.Dot(yc)) < 0.95 && Math.Abs(X.Dot(xc)) < 0.95) j.Notes.Add("kolom tidak sejajar rafter");
                    return j;
                }
            }
            // kolom di atas balok -> pelat dasar dibaut ke sayap balok
            double ht = mbm.Depth / 2;
            double s2 = ((Q + yb * ht) - P).Dot(yb) / Math.Max(up.Dot(yb), 0.2);
            var Ob = P + up * s2;
            var bp = new BasePlate
            {
                Kolom = mc.Profile.Name, Dudukan = BaseSupport.Baja, TebalSayapDudukan = Sec.Tf(mbm.Profile), LebarSayapDudukan = mbm.Profile.Extent(true),
                Angkur = BoltSize.M16, Pedestal = false, DudukanSearahX = Math.Abs(mbm.Dir.Dot(xc)) >= Math.Abs(mbm.Dir.Dot(yc)),
            };
            bp.Recommend();
            // titik acuan pelat dasar = muka atas pelat (pelat duduk di atas sayap balok, tidak masuk ke dalamnya)
            Ob += Vec3.UnitZ * bp.TebalPelat;
            var jb = new Joint { Type = ConnType.PelatDasar, Support = col, SupportAtStart = colAtStart, SupportEndUsed = true, Frame = new Frame(Ob, xc, yc, Vec3.UnitZ), Conn = bp };
            jb.Drill.Add(beam);                                    // baut pelat dasar melubangi sayap balok dudukan
            return jb;
        }

        // ------------------------------------------------------------------ balok ke kolom hollow / pipa
        static Joint BeamToTube(IList<MemberGeom> ms, int col, int beam, bool bAtStart)
        {
            var mc = ms[col]; var mb = ms[beam];
            var P = bAtStart ? mb.CS : mb.CE;
            var uB = ((bAtStart ? mb.CE : mb.CS) - P).Unit;
            var Q = ClosestOnLine(mb.CS, mb.Dir, mc.CS, mc.Dir);
            var X = new Vec3(uB.X, uB.Y, 0); if (X.Length < 1e-6) return null; X = X.Unit;
            var c = new HollowColumnBeam { Balok = mb.Profile.Name, Kolom = mc.Profile.Name };
            c.Recommend();
            return new Joint { Type = ConnType.KolomHollow, Support = col, Supported = beam, SupportedAtStart = bAtStart, Frame = new Frame(Q, X, Vec3.UnitZ.Cross(X), Vec3.UnitZ), Conn = c };
        }

        // ------------------------------------------------------------------ gording bersambung
        static Joint PurlinJoint(IList<MemberGeom> ms, int a, int b, bool aAtStart, bool bAtStart)
        {
            var ma = ms[a]; var mb = ms[b];
            var O = ((aAtStart ? ma.CS : ma.CE) + (bAtStart ? mb.CS : mb.CE)) * 0.5;
            var X = ((bAtStart ? mb.CE : mb.CS) - O).Unit;
            mb.Axes3(out Vec3 _, out Vec3 yb, out Vec3 _);
            var Z = (yb - X * yb.Dot(X)).Unit; var Y = Z.Cross(X);
            var c = new PurlinSplice { ProfilA = ma.Profile.Name, ProfilB = mb.Profile.Name };
            c.Recommend();
            return new Joint { Type = ConnType.GordingSambung, Support = a, Supported = b, SupportAtStart = aAtStart, SupportedAtStart = bAtStart, SupportEndUsed = true, Frame = new Frame(O, X, Y, Z), Conn = c };
        }

        // ------------------------------------------------------------------ lintasan: balok tumpuk, silang bracing
        static List<Joint> Crossings(IList<MemberGeom> ms, double extraTol)
        {
            var res = new List<Joint>();
            for (int a = 0; a < ms.Count; a++)
                for (int b = a + 1; b < ms.Count; b++)
                {
                    var ma = ms[a]; var mb = ms[b];
                    bool ih = ma.IsIH && mb.IsIH && !ma.IsVertical && !mb.IsVertical;
                    bool lt = TrussFamily(ma) && TrussFamily(mb) && !Tube(ma) && !Tube(mb);
                    if (!ih && !lt) continue;
                    if (Math.Abs(ma.Dir.Dot(mb.Dir)) > 0.94) continue;
                    SegClosest(ma.CS, ma.CE, mb.CS, mb.CE, out double ta, out double tb);
                    if (ta < 0.05 || ta > 0.95 || tb < 0.05 || tb > 0.95) continue;
                    var pa = ma.At(ta); var pb = mb.At(tb);
                    if (ih)
                    {
                        double dz = pb.Z - pa.Z;
                        double want = (ma.Depth + mb.Depth) / 2;
                        if (Math.Abs(Math.Abs(dz) - want) > extraTol || new Vec3(pb.X - pa.X, pb.Y - pa.Y, 0).Length > extraTol + 0.5 * Math.Min(ma.Depth, mb.Depth)) continue;
                        int up = dz > 0 ? b : a, lo = dz > 0 ? a : b;
                        var mu = ms[up]; var ml = ms[lo];
                        var plo = dz > 0 ? pa : pb;
                        var O = plo + Vec3.UnitZ * (ml.Depth / 2);
                        var X = new Vec3(mu.Dir.X, mu.Dir.Y, 0).Unit; var Y = Vec3.UnitZ.Cross(X);
                        var dl = new Vec3(ml.Dir.X, ml.Dir.Y, 0).Unit;
                        var c = new StackedBeams { BalokAtas = mu.Profile.Name, BalokBawah = ml.Profile.Name, Sudut = Math.Round(Deg(Math.Atan2(dl.Dot(Y), dl.Dot(X))), 2) };
                        if (c.Sudut < 0) c.Sudut += 180;
                        c.Recommend();
                        res.Add(new Joint { Type = ConnType.BalokTumpuk, Support = lo, Supported = up, Frame = new Frame(O, X, Y, Vec3.UnitZ), Conn = c });
                    }
                    else
                    {
                        if ((pa - pb).Length > 0.5 * (ma.Depth + mb.Depth) + extraTol) continue;
                        var N = ma.Dir.Cross(mb.Dir).Unit;
                        var X = ma.Dir; var Y = N.Cross(X);
                        var c = new CrossBracing { ProfilA = ma.Profile.Name, ProfilB = mb.Profile.Name, Sudut = Math.Round(Deg(Math.Atan2(mb.Dir.Dot(Y), mb.Dir.Dot(X))), 2) };
                        if (c.Sudut < 0) c.Sudut += 180;
                        c.Recommend();
                        res.Add(new Joint { Type = ConnType.SilangBracing, Support = a, Supported = b, Frame = new Frame((pa + pb) * 0.5, X, Y, N), Conn = c });
                    }
                }
            return res;
        }
    }
}
