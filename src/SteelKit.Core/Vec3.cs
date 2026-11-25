using System;
using System.Globalization;

namespace SteelKit.Core
{
    /// <summary>Titik acuan penampang terhadap garis batang yang digambar.</summary>
    public enum Anchor { Tengah, Atas, Bawah, TitikBerat }

    public struct Vec3
    {
        public double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;
        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec3 Unit { get { double l = Length; return l > 1e-12 ? this * (1 / l) : this; } }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);

        /// <summary>Putar vektor terhadap sumbu (satuan) sebesar sudut (rad), rumus Rodrigues.</summary>
        public Vec3 Rotate(Vec3 axis, double ang)
        {
            var k = axis.Unit;
            double c = Math.Cos(ang), s = Math.Sin(ang);
            return this * c + k.Cross(this) * s + k * (k.Dot(this) * (1 - c));
        }
    }

    /// <summary>Sistem koordinat: titik asal + tiga sumbu satuan (U, V, W) saling tegak lurus.</summary>
    public class Frame
    {
        public Vec3 O, U, V, W;
        public Frame() { O = Vec3.Zero; U = Vec3.UnitX; V = Vec3.UnitY; W = Vec3.UnitZ; }
        public Frame(Vec3 o, Vec3 u, Vec3 v, Vec3 w) { O = o; U = u; V = v; W = w; }

        /// <summary>Frame dari asal, sumbu U, dan arah "kira-kira" V (diluruskan tegak lurus U).</summary>
        public static Frame FromUV(Vec3 o, Vec3 u, Vec3 vApprox)
        {
            var uu = u.Unit;
            var ww = uu.Cross(vApprox).Unit;
            var vv = ww.Cross(uu).Unit;
            return new Frame(o, uu, vv, ww);
        }

        public Vec3 Point(double x, double y, double z) => O + U * x + V * y + W * z;
        public Vec3 Point(Vec3 p) => Point(p.X, p.Y, p.Z);
        public Vec3 Dir(Vec3 d) => U * d.X + V * d.Y + W * d.Z;
        public Vec3 ToLocal(Vec3 p) { var d = p - O; return new Vec3(d.Dot(U), d.Dot(V), d.Dot(W)); }
        public Vec3 DirToLocal(Vec3 d) => new Vec3(d.Dot(U), d.Dot(V), d.Dot(W));

        /// <summary>Komposisi: frame lokal 'child' (dinyatakan dalam koordinat frame ini) -> frame dunia.</summary>
        public Frame Compose(Frame child) => new Frame(Point(child.O), Dir(child.U), Dir(child.V), Dir(child.W));
    }

    public static class Axes
    {
        /// <summary>
        /// Sumbu lokal batang (sama dengan SKBATANG): Z searah batang, Y "ke atas" (tinggi profil tegak),
        /// X = Y x Z; batang tegak memakai Y dunia; lalu diputar sebesar roll (derajat) terhadap Z.
        /// </summary>
        public static void Member(Vec3 dir, double rollDeg, out Vec3 x, out Vec3 y, out Vec3 z)
        {
            z = dir.Unit;
            if (Math.Abs(z.Dot(Vec3.UnitZ)) > 0.999) y = Vec3.UnitY;
            else y = (Vec3.UnitZ - z * z.Dot(Vec3.UnitZ)).Unit;
            x = y.Cross(z).Unit;
            y = z.Cross(x).Unit;
            if (Math.Abs(rollDeg) > 1e-9)
            {
                double a = rollDeg * Math.PI / 180;
                x = x.Rotate(z, a); y = y.Rotate(z, a);
            }
        }
    }
}
