using System;
using System.Linq;
using SteelKit.Core;
using Xunit;
using Xunit.Abstractions;

namespace SteelKit.Tests
{
    /// <summary>Istilah teknik umum (Indonesia/Inggris, ejaan pengenal suara) dan bentuk struktur baru.</summary>
    public class VocabTests
    {
        readonly ITestOutputHelper log;
        public VocabTests(ITestOutputHelper o) { log = o; }

        AssistantPlan Run(string s)
        {
            var p = Assistant.Parse(s);
            log.WriteLine(s + "\n  -> " + p.Summary + "\n  asumsi: " + string.Join(" ; ", p.Assumptions) + "\n  tidak paham: " + string.Join(" ; ", p.Unknown));
            // semua usulan harus bisa disambung otomatis tanpa galat
            var ms = p.Members.Select(m => new MemberGeom { Profile = Env.Catalog.Find(m.Profile), S = m.S, E = m.E, Roll = m.Roll, Anchor = m.Anchor, Id = m.Role }).ToList();
            Assert.All(ms, m => Assert.NotNull(m.Profile));
            var js = JointFinder.Find(ms);
            foreach (var j in js) { var r = j.Conn.Build(); Assert.NotNull(r); }
            log.WriteLine("  sambungan: " + string.Join(", ", js.GroupBy(j => j.Type).Select(g => g.Key + " x" + g.Count())));
            return p;
        }

        [Theory]
        [InlineData("kanopi kantilever 3 meter tinggi 3 meter jarak 5 meter panjang 15 meter")]
        [InlineData("canopy cantilever span 3 m height 3 m spacing 5 m length 15 m")]
        [InlineData("kanopy kantiliver tiga meter tinggi tiga meter")]
        public void Cantilever_canopy(string s)
        {
            var p = Run(s);
            Assert.StartsWith("Kanopi kantilever", p.Summary);
            Assert.Equal(p.Members.Count(m => m.Role == "Kolom"), p.Members.Count(m => m.Role == "Rafter"));
            Assert.All(p.Members.Where(m => m.Role == "Rafter"), m => Assert.Equal(3000, Math.Abs(m.E.X - m.S.X), 0));
            Assert.True(p.Members.Count(m => m.Role == "Gording") >= 2);
        }

        [Fact]
        public void Two_sided_carport_and_supported_canopy()
        {
            var t = Run("carport kantilever dua sisi 2,5 m tinggi 3 m panjang 10 m");
            Assert.Contains("dua sisi", t.Summary);
            Assert.Equal(t.Members.Count(m => m.Role == "Kolom"), t.Members.Count(m => m.Role == "Rafter"));            // T: satu balok menerus per kolom
            Assert.All(t.Members.Where(m => m.Role == "Rafter"), m => Assert.Equal(5000, Math.Abs(m.E.X - m.S.X), 0));
            var c = Run("kanopi lebar 5 m panjang 10 m tinggi 3,5 m");
            Assert.StartsWith("Kanopi dua kolom", c.Summary);
            Assert.Equal(2 * c.Members.Count(m => m.Role == "Rafter"), c.Members.Count(m => m.Role == "Kolom"));
        }

        [Theory]
        [InlineData("tower air lebar 3 meter tinggi 12 meter 4 segmen")]
        [InlineData("menara tandon lebar tiga meter tinggi dua belas meter empat segmen")]
        [InlineData("water tower width 3 m height 12 m 4 panel")]
        public void Tower(string s)
        {
            var p = Run(s);
            Assert.StartsWith("Menara rangka 4 kaki 3 x 3 m, tinggi 12 m, 4 segmen", p.Summary);
            Assert.Equal(4, p.Members.Count(m => m.Role == "Kolom"));
            Assert.Equal(16, p.Members.Count(m => m.Role == "Bracing"));
        }

        [Theory]
        [InlineData("jembatan rangka bentang 15 m lebar 2 m tinggi 1,8 m 6 panel")]
        [InlineData("truss bridge span 15 m width 2 m height 1,8 m 6 panel")]
        [InlineData("jembatan penyeberangan orang rangka batang bentang lima belas meter lebar dua meter")]
        public void Truss_bridge(string s)
        {
            var p = Run(s);
            Assert.StartsWith("Jembatan rangka (Pratt) bentang 15 m, lebar 2 m", p.Summary);
            Assert.All(p.Members.Where(m => m.Role == "Balok anak"), m => Assert.Equal(2000, (m.E - m.S).Length, 0));
        }

        [Fact]
        public void Girder_bridge_and_english_portal()
        {
            var g = Run("jembatan gelagar bentang 8 m lebar 1,5 m");
            Assert.StartsWith("Jembatan gelagar bentang 8 m, lebar 1.5 m", g.Summary.Replace(",5", ".5"));
            var f = Run("rigid frame span 20 m height 6 m 5 bay spacing 6 m column WF400 rafter WF350 purlin C150");
            Assert.StartsWith("Portal pelana bentang 20 m, tinggi 6 m", f.Summary);
            Assert.All(f.Members.Where(m => m.Role == "Kolom"), m => Assert.StartsWith("WF400", m.Profile));
            // istilah lama tetap jalan; "lebar" gudang = bentang
            var w = Run("gudang lebar 24 m panjang 30 m tinggi 7 m");
            Assert.StartsWith("Portal pelana bentang 24 m", w.Summary);
        }
    }
}
