using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SteelKit.Core;

namespace SteelKit.Acad
{
    /// <summary>
    /// Gambar teknik sambungan untuk antarmuka (galeri & pratinjau), dibangkitkan dari geometri sebenarnya.
    /// Gaya gelap "cetak biru" sesuai tema Steel Kit; gaya terang tetap tersedia.
    /// </summary>
    public static class ConnRender
    {
        static void DarkColors(PartKind k, out Color line, out Color fill)
        {
            switch (k)
            {
                case PartKind.Batang: line = Color.FromArgb(148, 163, 184); fill = Color.FromArgb(46, 148, 163, 184); break;
                case PartKind.Beton: line = Color.FromArgb(100, 112, 126); fill = Color.FromArgb(44, 52, 63); break;
                case PartKind.Grout: line = Color.FromArgb(128, 122, 108); fill = Color.FromArgb(66, 62, 54); break;
                case PartKind.Baut: case PartKind.Mur: case PartKind.Ring: line = Color.FromArgb(235, 240, 245); fill = Color.FromArgb(170, 203, 213, 225); break;
                case PartKind.Angkur: line = Color.FromArgb(232, 121, 249); fill = Color.FromArgb(120, 232, 121, 249); break;
                default: line = Color.FromArgb(56, 189, 248); fill = Color.FromArgb(95, 56, 189, 248); break;
            }
        }

        /// <summary>Gambar teknik satu tampak: garis benda, batang abu-abu, lingkaran baut, dimensi, dan judul.</summary>
        public static Bitmap Render(ConnResult r, int view, Size size, bool withDims, bool dark = true)
        {
            var bmp = new Bitmap(Math.Max(size.Width, 10), Math.Max(size.Height, 10));
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(dark ? Color.FromArgb(27, 34, 44) : Color.White);
                if (view >= r.Views.Count) return bmp;
                var v = r.Views[view];
                var shapes = Projection.Project(r, v);
                var pts = shapes.SelectMany(s => s.IsCircle ? new[] { s.Center + new Vec2(-s.Radius, -s.Radius), s.Center + new Vec2(s.Radius, s.Radius) } : s.Pts.ToArray()).ToList();
                if (pts.Count == 0) return bmp;
                double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
                float margin = withDims ? 60 : 6, top = withDims ? 26 : 0;
                float sc = (float)Math.Min((size.Width - 2 * margin) / Math.Max(x1 - x0, 1), (size.Height - 2 * margin - top) / Math.Max(y1 - y0, 1));
                float cx = size.Width / 2f, cy = (size.Height + top) / 2f;
                double mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
                Func<Vec2, PointF> T = p => new PointF(cx + (float)((p.X - mx) * sc), cy - (float)((p.Y - my) * sc));
                var order = shapes.OrderBy(s => s.Kind == PartKind.Beton ? 0 : s.Kind == PartKind.Grout ? 1 : s.Kind == PartKind.Batang ? 2 : 3);
                foreach (var s in order)
                {
                    Color line, fill;
                    if (dark) DarkColors(s.Kind, out line, out fill); else
                    switch (s.Kind)
                    {
                        case PartKind.Batang: line = Color.FromArgb(150, 160, 170); fill = Color.FromArgb(40, 150, 170, 190); break;
                        case PartKind.Beton: line = Color.FromArgb(170, 170, 170); fill = Color.FromArgb(235, 235, 232); break;
                        case PartKind.Grout: line = Color.FromArgb(160, 150, 130); fill = Color.FromArgb(225, 215, 190); break;
                        case PartKind.Baut: case PartKind.Mur: case PartKind.Ring: line = Color.FromArgb(40, 40, 40); fill = Color.FromArgb(120, 90, 90, 90); break;
                        case PartKind.Angkur: line = Color.FromArgb(120, 40, 120); fill = Color.FromArgb(120, 160, 90, 160); break;
                        default: line = Color.FromArgb(20, 60, 110); fill = Color.FromArgb(110, 230, 160, 60); break;
                    }
                    using (var pen = new Pen(line, s.Kind == PartKind.Batang ? 1f : 1.3f))
                    using (var br = new SolidBrush(fill))
                    {
                        if (s.IsCircle)
                        {
                            var c = T(s.Center); float rr = (float)(s.Radius * sc);
                            g.FillEllipse(br, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                            g.DrawEllipse(pen, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                        }
                        else if (s.Pts.Count >= 3)
                        {
                            var arr = s.Pts.Select(T).ToArray();
                            g.FillPolygon(br, arr);
                            g.DrawPolygon(pen, arr);
                        }
                    }
                }
                if (!withDims) return bmp;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (var pen = new Pen(dark ? Color.FromArgb(125, 211, 252) : Color.FromArgb(0, 130, 70), 1f))
                using (var f = new Font("Segoe UI", 8.5f))
                using (var brush = new SolidBrush(dark ? Color.FromArgb(186, 230, 253) : Color.FromArgb(0, 110, 60)))
                {
                    pen.CustomStartCap = new AdjustableArrowCap(3, 4); pen.CustomEndCap = new AdjustableArrowCap(3, 4);
                    foreach (var d in r.Dims.Where(d => d.View == view))
                    {
                        var a = v.P(d.A); var b = v.P(d.B);
                        var od = new Vec2(d.OffsetDir.Dot(v.R), d.OffsetDir.Dot(v.Up));
                        if (od.Length < 1e-6 || (b - a).Length < 1e-6) continue;
                        od = od.Unit;
                        double offPx = Math.Min(d.Offset * sc, 50);
                        var pa = T(a); var pb = T(b);
                        var shift = new PointF((float)(od.X * offPx), (float)(-od.Y * offPx));
                        var qa = new PointF(pa.X + shift.X, pa.Y + shift.Y); var qb = new PointF(pb.X + shift.X, pb.Y + shift.Y);
                        using (var thin = new Pen(dark ? Color.FromArgb(110, 125, 211, 252) : Color.FromArgb(120, 0, 130, 70), 0.8f)) { g.DrawLine(thin, pa, qa); g.DrawLine(thin, pb, qb); }
                        g.DrawLine(pen, qa, qb);
                        string txt = d.Text ?? Fmt.N((b - a).Length);
                        var st = g.Save();
                        var mid = new PointF((qa.X + qb.X) / 2, (qa.Y + qb.Y) / 2);
                        float ang = (float)(Math.Atan2(qb.Y - qa.Y, qb.X - qa.X) * 180 / Math.PI);
                        if (ang > 90) ang -= 180; if (ang < -90) ang += 180;
                        g.TranslateTransform(mid.X, mid.Y); g.RotateTransform(ang);
                        var sz = g.MeasureString(txt, f);
                        g.DrawString(txt, f, brush, -sz.Width / 2, -sz.Height - 1);
                        g.Restore(st);
                    }
                }
                using (var tf = new Font("Segoe UI Semibold", 9.5f)) g.DrawString(v.Title, tf, dark ? Brushes.WhiteSmoke : Brushes.Black, 8, 4);
            }
            return bmp;
        }
    }
}
