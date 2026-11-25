using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace SteelKit.Acad.Ui
{
    /// <summary>
    /// Efek kaca jendela Steel Kit: akrilik Windows 11 (22H2+) di belakang isi yang semi-transparan,
    /// judul gelap, sudut membulat. Windows 10 / bila gagal: latar gradasi gelap pekat.
    /// </summary>
    public static class Glass
    {
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int L, R, T, B; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref Margins m);

        const int DarkMode = 20, Corner = 33, Backdrop = 38;

        static ResourceDictionary theme;
        /// <summary>Tema bersama (dimuat sekali) untuk kontrol yang dibuat di kode / palet AutoCAD.</summary>
        public static ResourceDictionary Theme()
            => theme ?? (theme = new ResourceDictionary { Source = new Uri("/" + typeof(Glass).Assembly.GetName().Name + ";component/Ui/Theme.xaml", UriKind.Relative) });

        /// <summary>Pasang efek kaca. <paramref name="root"/> = Border paling luar isi jendela.</summary>
        public static void Apply(Window w, Border root)
        {
            w.Background = Brushes.Transparent;
            root.Background = (Brush)w.FindResource("SkWindowSolid");
            w.SourceInitialized += (s, e) =>
            {
                try
                {
                    var h = new WindowInteropHelper(w).Handle;
                    int one = 1; DwmSetWindowAttribute(h, DarkMode, ref one, 4);
                    int round = 2; DwmSetWindowAttribute(h, Corner, ref round, 4);
                    if (Environment.OSVersion.Version.Build >= 22621)
                    {
                        var src = HwndSource.FromHwnd(h);
                        if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                        var m = new Margins { L = -1, R = -1, T = -1, B = -1 };
                        DwmExtendFrameIntoClientArea(h, ref m);
                        int acrylic = 3;
                        if (DwmSetWindowAttribute(h, Backdrop, ref acrylic, 4) == 0)
                            root.Background = (Brush)w.FindResource("SkWindowGlass");
                    }
                }
                catch { /* tetap latar pekat */ }
            };
            // isi jangan terpotong saat jendela dimaksimalkan
            w.StateChanged += (s, e) => root.Margin = w.WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        }
    }
}
