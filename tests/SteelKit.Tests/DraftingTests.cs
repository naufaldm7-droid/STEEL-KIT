using System.Collections.Generic;
using System.Linq;
using SteelKit.Core;
using Xunit;
using Xunit.Abstractions;

namespace SteelKit.Tests
{
    public class DraftingTests
    {
        readonly ITestOutputHelper log;
        public DraftingTests(ITestOutputHelper o) { log = o; }

        static List<(Vec3, Vec3)> Portal()
        {
            // portal bentang 20 m, tinggi 6 m, puncak +7 m, 3 bay x 6 m; gording mendatar sepanjang Y
            var m = new List<(Vec3, Vec3)>();
            for (int j = 0; j < 4; j++)
            {
                double y = j * 6000;
                m.Add((new Vec3(0, y + (j == 2 ? 12 : 0), 0), new Vec3(0, y + (j == 2 ? 12 : 0), 6000)));   // satu kolom meleset sedikit
                m.Add((new Vec3(20000, y, 0), new Vec3(20000, y, 6000)));
                m.Add((new Vec3(0, y, 6000), new Vec3(10000, y, 7000)));
                m.Add((new Vec3(20000, y, 6000), new Vec3(10000, y, 7000)));
            }
            m.Add((new Vec3(1000, 0, 6100), new Vec3(1000, 18000, 6100)));
            return m;
        }

        [Fact]
        public void Grid_from_column_bases_letters_on_x_numbers_on_y()
        {
            var g = GridSystem.FromMembers(Portal());
            Assert.Equal(new[] { "A", "B" }, g.X.Select(x => x.Label));
            Assert.Equal(new[] { 0.0, 20000 }, g.X.Select(x => x.Pos));
            Assert.Equal(new[] { "1", "2", "3", "4" }, g.Y.Select(x => x.Label));
            Assert.InRange(g.Y[2].Pos, 12000, 12012);             // kolom meleset 12 mm tetap satu as (toleransi 50)
            Assert.Equal("H", GridSystem.Letter(7));
            Assert.Equal("J", GridSystem.Letter(8));                // I dilewati
            Assert.Equal("P", GridSystem.Letter(13));               // O dilewati
            Assert.Equal("AA", GridSystem.Letter(24));
            Assert.False(GridSystem.FromMembers(new[] { (Vec3.Zero, new Vec3(5000, 0, 0)) }).Any);
        }

        [Fact]
        public void Levels_base_column_top_and_ridge()
        {
            var lv = LevelMark.FromMembers(Portal(), new[] { ("Level 0", 0.0), ("Mezzanine", 3500.0) });
            Assert.Equal(new[] { "±0.000", "+3.500", "+6.000", "+7.000" }, lv.Select(l => l.Text));
            Assert.Equal("PUNCAK KOLOM", lv[2].Name);
            Assert.Equal("PUNCAK RANGKA", lv[3].Name);
            Assert.Equal("−1.250", LevelMark.Elevation(-1250));
        }

        [Theory]
        [InlineData("Las sudut a=6 keliling", "a6", false, true, false, "")]
        [InlineData("Pelat sirip t=10, 3 baut M16 8.8\\PLas a=6 dua sisi", "a6", false, false, true, "")]
        [InlineData("Las sayap a=8, badan a=6", "a8/a6", false, false, false, "sayap/badan")]
        [InlineData("Las CJP sayap atas & bawah, lubang akses las\\PPelat sirip t=10, 3 baut M20", "", true, false, false, "CJP, sayap atas & bawah")]
        [InlineData("Pelat mata t=10, las a=6 dua sisi", "a6", false, false, true, "")]
        [InlineData("L75x75x8, 2 baut M16\\PPelat buhul t=10, las a=6 ke host", "a6", false, false, false, "ke host")]
        public void Weld_symbol_read_from_note(string note, string size, bool cjp, bool around, bool both, string tail)
        {
            var w = WeldMark.Parse(note);
            Assert.NotNull(w);
            Assert.Equal(size, w.SizeText);
            Assert.Equal(cjp, w.Cjp);
            Assert.Equal(around, w.AllAround);
            Assert.Equal(both, w.BothSides);
            Assert.Equal(tail, w.Tail);
            Assert.DoesNotContain("a=", w.Rest);
        }

        [Fact]
        public void Weld_length_and_non_weld_notes()
        {
            var w = WeldMark.Parse("Pelat buhul t=8\\PLas sudut a=5 sepanjang 150 kedua sisi kaki");
            Assert.Equal(150, w.Length);
            Assert.True(w.BothSides);
            Assert.Equal("Pelat buhul t=8", w.Rest);
            Assert.Null(WeldMark.Parse("Lubang lonjong di badan, ring miring di sisi belakang"));
            Assert.Null(WeldMark.Parse("Pelat ujung t=20, 8 baut M20"));
        }

        [Fact]
        public void Every_weld_note_in_the_catalog_becomes_a_symbol()
        {
            int n = 0;
            foreach (var c in ConnectionTests.Samples())
            {
                c.Recommend();
                foreach (var note in c.Build().Notes)
                {
                    bool mentions = System.Text.RegularExpressions.Regex.IsMatch(note.Text, @"\b[Ll]as\b.*\ba=|\bCJP\b");
                    var w = WeldMark.Parse(note.Text);
                    if (mentions) { Assert.True(w != null, c.Title + ": " + note.Text); n++; log.WriteLine(c.Title + " -> " + w.SizeText + " | " + w.Tail + " | " + w.Rest); }
                    if (w != null) Assert.True(w.Cjp || w.Sizes.Count > 0, note.Text);
                }
            }
            Assert.True(n >= 10, "catatan las ditemukan: " + n);
        }

        [Fact]
        public void Chain_dimension_texts_do_not_overlap()
        {
            // rantai 6 dimensi pendek bersebelahan: teks 3 digit lebih lebar dari dimensinya
            var dims = Enumerable.Range(0, 6).Select(i => (i * 4.0, i * 4.0 + 4, DimTiers.TextWidth("120", 2.5))).ToList();
            var t = DimTiers.Assign(dims, 0.5);
            for (int i = 0; i < dims.Count; i++)
                for (int j = i + 1; j < dims.Count; j++)
                {
                    if (t[i] != t[j]) continue;
                    double ci = (dims[i].Item1 + dims[i].Item2) / 2, cj = (dims[j].Item1 + dims[j].Item2) / 2;
                    Assert.True(System.Math.Abs(ci - cj) >= dims[i].Item3 + 1, "teks " + i + " & " + j + " bertumpuk");
                }
            Assert.True(t.Max() >= 1);
            // dimensi lebar yang muat: semua di lapis pertama
            Assert.All(DimTiers.Assign(new[] { (0.0, 30.0, 8.0), (30.0, 60.0, 8.0), (60.0, 90.0, 8.0) }, 0.5), x => Assert.Equal(0, x));
        }

        [Fact]
        public void Scale_bar_length_is_round()
        {
            Assert.Equal(5000, ScaleBar.ModelLength(200));
            Assert.Equal(2500, ScaleBar.ModelLength(100));
            Assert.Equal("5 m", ScaleBar.Label(5000));
            Assert.Equal("500 mm", ScaleBar.Label(500));
        }
    }
}
