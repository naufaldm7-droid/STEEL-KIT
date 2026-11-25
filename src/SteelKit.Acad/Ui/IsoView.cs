using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SteelKit.Core;

namespace SteelKit.Acad.Ui
{
    /// <summary>Pratinjau 3D garis batang: bisa diputar (seret), di-zoom (gulir), reset (klik ganda).</summary>
    public class IsoView : FrameworkElement
    {
        List<(MemberProposal M, bool On)> members = new List<(MemberProposal, bool)>();
        double az = -38, el = 27, zoom = 1;
        Point? drag;

        static readonly Dictionary<string, Color> RoleColors = new Dictionary<string, Color>
        {
            ["Kolom"] = Color.FromRgb(0x38, 0xBD, 0xF8),
            ["Rafter"] = Color.FromRgb(0x81, 0x8C, 0xF8),
            ["Balok"] = Color.FromRgb(0x81, 0x8C, 0xF8),
            ["Balok anak"] = Color.FromRgb(0xA7, 0x8B, 0xFA),
            ["Gording"] = Color.FromRgb(0x94, 0xA3, 0xB8),
            ["Girt"] = Color.FromRgb(0x94, 0xA3, 0xB8),
            ["Bracing"] = Color.FromRgb(0xE8, 0x79, 0xF9),
        };

        public static Color RoleColor(string role) => role != null && RoleColors.TryGetValue(role, out var c) ? c : Color.FromRgb(0xCB, 0xD5, 0xE1);
        public static Brush RoleBrush(string role) { var b = new SolidColorBrush(RoleColor(role)); b.Freeze(); return b; }

        public IsoView()
        {
            ClipToBounds = true;
            Focusable = false;
            Cursor = Cursors.SizeAll;
        }

        public void SetMembers(List<(MemberProposal, bool)> list) { members = list ?? new List<(MemberProposal, bool)>(); InvalidateVisual(); }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) { az = -38; el = 27; zoom = 1; InvalidateVisual(); return; }
            drag = e.GetPosition(this); CaptureMouse();
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { drag = null; ReleaseMouseCapture(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (drag == null) return;
            var p = e.GetPosition(this);
            az -= (p.X - drag.Value.X) * 0.45;
            el = Math.Max(-5, Math.Min(85, el + (p.Y - drag.Value.Y) * 0.35));
            drag = p; InvalidateVisual();
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e) { zoom = Math.Max(0.4, Math.Min(4, zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12))); InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth; var h = ActualHeight;
            if (w < 10 || h < 10) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));   // supaya mouse tertangkap
            // cahaya lembut di tengah
            var glow = new RadialGradientBrush(Color.FromArgb(0x22, 0x63, 0x66, 0xF1), Color.FromArgb(0, 0x63, 0x66, 0xF1)) { RadiusX = 0.6, RadiusY = 0.6 };
            dc.DrawRectangle(glow, null, new Rect(0, 0, w, h));

            if (members.Count == 0)
            {
                var logo = Application.Current?.TryFindResource("SkLogo") as ImageSource ?? TryFindResource("SkLogo") as ImageSource;
                if (logo != null) { dc.PushOpacity(0.18); dc.DrawImage(logo, new Rect(w / 2 - 36, h / 2 - 60, 72, 72)); dc.Pop(); }
                var ft = Text("Pratinjau 3D muncul setelah kalimat dibaca", 12.5, Color.FromRgb(0x6B, 0x7B, 0x8E));
                dc.DrawText(ft, new Point(w / 2 - ft.Width / 2, h / 2 + 24));
                return;
            }

            var pts = members.SelectMany(m => new[] { m.M.S, m.M.E }).ToList();
            var c = new Vec3((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2, (pts.Min(p => p.Z) + pts.Max(p => p.Z)) / 2);
            double ca = Math.Cos(az * Math.PI / 180), sa = Math.Sin(az * Math.PI / 180), ce = Math.Cos(el * Math.PI / 180), se = Math.Sin(el * Math.PI / 180);
            Func<Vec3, Point> proj = p =>
            {
                double x = p.X - c.X, y = p.Y - c.Y, z = p.Z - c.Z;
                double x1 = x * ca - y * sa, y1 = x * sa + y * ca;
                return new Point(x1, -(z * ce + y1 * se));
            };
            var pp = pts.Select(proj).ToList();
            double minX = pp.Min(p => p.X), maxX = pp.Max(p => p.X), minY = pp.Min(p => p.Y), maxY = pp.Max(p => p.Y);
            double sc = Math.Min((w - 48) / Math.Max(maxX - minX, 1), (h - 48) / Math.Max(maxY - minY, 1)) * zoom;
            double ox = w / 2 - (minX + maxX) / 2 * sc, oy = h / 2 - (minY + maxY) / 2 * sc;
            Func<Vec3, Point> T = p => { var q = proj(p); return new Point(ox + q.X * sc, oy + q.Y * sc); };

            // grid lantai di elevasi terendah
            double z0 = pts.Min(p => p.Z), x0 = pts.Min(p => p.X), x1b = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1b = pts.Max(p => p.Y);
            double span = Math.Max(x1b - x0, y1b - y0), step = NiceStep(span / 8);
            var gp = new Pen(new SolidColorBrush(Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF)), 1);
            double gx0 = Math.Floor((x0 - step) / step) * step, gx1 = Math.Ceiling((x1b + step) / step) * step;
            double gy0 = Math.Floor((y0 - step) / step) * step, gy1 = Math.Ceiling((y1b + step) / step) * step;
            for (double x = gx0; x <= gx1 + 1e-6; x += step) dc.DrawLine(gp, T(new Vec3(x, gy0, z0)), T(new Vec3(x, gy1, z0)));
            for (double y = gy0; y <= gy1 + 1e-6; y += step) dc.DrawLine(gp, T(new Vec3(gx0, y, z0)), T(new Vec3(gx1, y, z0)));

            // batang: cahaya lalu garis inti; sekunder dulu supaya utama di atas
            foreach (var (m, on) in members.OrderBy(t => Rank(t.M.Role)))
            {
                var col = RoleColor(m.Role);
                bool main = Rank(m.Role) >= 2;
                byte a = on ? (byte)255 : (byte)60;
                var a1 = T(m.S); var a2 = T(m.E);
                if (on && main) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(0x40, col.R, col.G, col.B)), 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, a1, a2);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(a, col.R, col.G, col.B)), main ? 2.2 : 1.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, a1, a2);
            }

            // legenda
            double ly = 10;
            foreach (var g in members.GroupBy(t => t.M.Role).OrderByDescending(g => Rank(g.Key)))
            {
                var ft = Text(g.Key + "  ×" + g.Count(), 11.5, Color.FromRgb(0xC8, 0xD3, 0xDF));
                dc.DrawRoundedRectangle(RoleBrush(g.Key), null, new Rect(w - ft.Width - 32, ly + 5, 12, 4), 2, 2);
                dc.DrawText(ft, new Point(w - ft.Width - 14, ly));
                ly += 18;
            }
            var hint = Text("grid " + (step / 1000).ToString("0.##", CultureInfo.GetCultureInfo("id-ID")) + " m", 10.5, Color.FromRgb(0x6B, 0x7B, 0x8E));
            dc.DrawText(hint, new Point(12, h - 20));
        }

        static int Rank(string role)
        {
            switch (role) { case "Kolom": return 4; case "Rafter": case "Balok": return 3; case "Balok anak": return 2; case "Bracing": return 1; default: return 0; }
        }

        static double NiceStep(double v)
        {
            if (v <= 0) return 1000;
            double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
            double n = v / p;
            return (n < 1.5 ? 1 : n < 3.5 ? 2 : n < 7.5 ? 5 : 10) * p;
        }

        FormattedText Text(string s, double size, Color c)
        {
#pragma warning disable CS0618
            return new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, new SolidColorBrush(c));
#pragma warning restore CS0618
        }
    }
}
