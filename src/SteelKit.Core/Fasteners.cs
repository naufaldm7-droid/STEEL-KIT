using System;
using System.Collections.Generic;
using System.Linq;

namespace SteelKit.Core
{
    public enum BoltSize { M12, M16, M20, M22, M24, M27, M30, M36 }
    public enum BoltGrade { Kelas4_6, Kelas8_8, Kelas10_9, A325, A490 }
    public enum AnchorType { L_Kait, J_Kait, Berkepala, UlirPelatBawah, Kimia }

    /// <summary>
    /// Data baut hex (dimensi kepala/mur/ring tipikal ISO 4014/4032/7089) + aturan jarak minimum
    /// (praktik umum, merujuk AISC 360 Tabel J3.3M/J3.4M; padanan SNI 1729). Bukan cek kekuatan.
    /// </summary>
    public class BoltData
    {
        public BoltSize Size;
        public double D, Pitch, Head, Nut, Across, WasherOD, WasherT, Hole, MinEdge;
        public double MinSpacing => Math.Ceiling(3 * D);       // praktik umum 3d (AISC: minimum 2,67d, disarankan 3d)

        static readonly Dictionary<BoltSize, BoltData> table = new Dictionary<BoltSize, BoltData>
        {
            [BoltSize.M12] = B(BoltSize.M12, 12, 1.75, 7.5, 10.8, 18, 24, 2.5, 14, 18),
            [BoltSize.M16] = B(BoltSize.M16, 16, 2.0, 10, 14.8, 24, 30, 3, 18, 22),
            [BoltSize.M20] = B(BoltSize.M20, 20, 2.5, 12.5, 18, 30, 37, 3, 22, 26),
            [BoltSize.M22] = B(BoltSize.M22, 22, 2.5, 14, 19.4, 32, 39, 3, 24, 28),
            [BoltSize.M24] = B(BoltSize.M24, 24, 3.0, 15, 21.5, 36, 44, 4, 27, 30),
            [BoltSize.M27] = B(BoltSize.M27, 27, 3.0, 17, 23.8, 41, 50, 4, 30, 34),
            [BoltSize.M30] = B(BoltSize.M30, 30, 3.5, 18.7, 25.6, 46, 56, 4, 33, 38),
            [BoltSize.M36] = B(BoltSize.M36, 36, 4.0, 22.5, 31, 55, 66, 5, 39, 46),
        };

        static BoltData B(BoltSize s, double d, double p, double k, double m, double sw, double wod, double wt, double hole, double edge)
            => new BoltData { Size = s, D = d, Pitch = p, Head = k, Nut = m, Across = sw, WasherOD = wod, WasherT = wt, Hole = hole, MinEdge = edge };

        public static BoltData Of(BoltSize s) => table[s];

        /// <summary>Panjang baut = jepitan + ring(2) + mur + 3 ulir, dibulatkan ke 5 mm (≤100) atau 10 mm.</summary>
        public double Length(double grip, int washers = 2)
        {
            double l = grip + washers * WasherT + Nut + 3 * Pitch;
            return l <= 100 ? Math.Ceiling(l / 5) * 5 : Math.Ceiling(l / 10) * 10;
        }

        /// <summary>Lubang angkur di pelat dasar: lubang besar (praktik umum, AISC Design Guide 1).</summary>
        public double AnchorHole => D <= 24 ? D + 12 : D + 16;
        /// <summary>Ring plat angkur (praktik umum DG1): sisi ≈ 2,6d, tebal ≈ 0,3d (min 6).</summary>
        public double PlateWasherSize => Fmt.Round5(2.6 * D);
        public double PlateWasherT => Math.Max(6, Fmt.StdPlate(0.3 * D));
    }

    public static class Grades
    {
        public static string Label(BoltGrade g)
        {
            switch (g)
            {
                case BoltGrade.Kelas4_6: return "4.6";
                case BoltGrade.Kelas8_8: return "8.8";
                case BoltGrade.Kelas10_9: return "10.9";
                default: return g.ToString();
            }
        }

        public static string Label(AnchorType a)
        {
            switch (a)
            {
                case AnchorType.L_Kait: return "angkur L (kait 90°)";
                case AnchorType.J_Kait: return "angkur J (kait 180°)";
                case AnchorType.Berkepala: return "angkur berkepala (headed)";
                case AnchorType.UlirPelatBawah: return "angkur ulir penuh + mur & pelat bawah";
                default: return "angkur kimia (chemical)";
            }
        }

        /// <summary>Kedalaman tanam bawaan (praktik umum; harus disesuaikan hasil hitungan ACI 318 Bab 17 / data pabrik).</summary>
        public static double DefaultEmbed(AnchorType a, double d)
        {
            switch (a)
            {
                case AnchorType.L_Kait: return Fmt.Round5(20 * d);
                case AnchorType.J_Kait: return Fmt.Round5(20 * d);
                case AnchorType.Kimia: return Fmt.Round5(10 * d);
                default: return Fmt.Round5(12 * d);
            }
        }

        public static double DefaultHook(AnchorType a, double d)
        {
            switch (a)
            {
                case AnchorType.L_Kait: return Fmt.Round5(4 * d);
                case AnchorType.J_Kait: return Fmt.Round5(4.5 * d);
                default: return 0;
            }
        }
    }

    public static class Fmt
    {
        public static readonly double[] Plates = { 6, 8, 9, 10, 12, 14, 16, 19, 20, 22, 25, 28, 30, 32, 36, 40, 45, 50, 55, 60 };
        public static double StdPlate(double t) => Plates.FirstOrDefault(p => p >= t - 1e-9) is double v && v > 0 ? v : Math.Ceiling(t);
        public static double Round5(double v) => Math.Ceiling(v / 5 - 1e-9) * 5;
        public static double Round10(double v) => Math.Ceiling(v / 10 - 1e-9) * 10;

        /// <summary>Ukuran las sudut minimum (praktik umum, AISC 360 Tabel J2.4) dari tebal bagian tertipis.</summary>
        public static double MinFillet(double tThin)
        {
            if (tThin <= 6) return 3;
            if (tThin <= 13) return 5;
            if (tThin <= 19) return 6;
            return 8;
        }

        public static string N(double v) => v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
