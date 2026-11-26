using System.Linq;
using SteelKit.Core;
using Xunit;

namespace SteelKit.Tests
{
    /// <summary>Kalimat hasil suara (Whisper) dengan angka berupa kata.</summary>
    public class SpokenTests
    {
        [Theory]
        [InlineData("empat ratus", "400")]
        [InlineData("seratus lima puluh", "150")]
        [InlineData("dua puluh empat", "24")]
        [InlineData("tiga ratus lima puluh", "350")]
        [InlineData("seribu dua ratus", "1200")]
        [InlineData("dua belas", "12")]
        [InlineData("sepuluh", "10")]
        [InlineData("dua tiga", "2 3")]
        [InlineData("enam", "6")]
        public void Spoken_number_words(string words, string expect) => Assert.Equal(expect, Assistant.SpokenNumbers(words));

        [Fact]
        public void Spoken_portal_sentence_uses_the_named_profiles()
        {
            var p = Assistant.Parse("Portal bentang dua puluh meter, tinggi enam meter, lima bay jarak enam meter, kolom WF empat ratus, rafter WF tiga ratus lima puluh, gording C seratus lima puluh.");
            Assert.Empty(p.Unknown);
            Assert.All(p.Members.Where(m => m.Role == "Kolom"), m => Assert.StartsWith("WF400", m.Profile));
            Assert.All(p.Members.Where(m => m.Role == "Rafter"), m => Assert.StartsWith("WF350", m.Profile));
            Assert.All(p.Members.Where(m => m.Role == "Gording"), m => Assert.StartsWith("C150", m.Profile));
            Assert.Equal(12, p.Members.Count(m => m.Role == "Kolom"));
        }
    }
}
