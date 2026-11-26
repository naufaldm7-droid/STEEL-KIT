using System.Linq;
using SteelKit.Core;
using Xunit;

namespace SteelKit.Tests
{
    /// <summary>Transkrip suara berlogat (hasil Whisper pada rekaman uji) tetap terbaca benar.</summary>
    public class SpokenAccentTests
    {
        [Fact]
        public void Accented_transcripts_parse_correctly()
        {
            var t = Assistant.Parse("Tower air labartiga meter, tinggi duabelis meter, empat segmen.");
            Assert.StartsWith("Menara rangka 4 kaki 3 x 3 m, tinggi 12 m, 4 segmen", t.Summary);
            var b = Assistant.Parse("Jembatan rangka bentang lemabeles meter, labar duameter.");
            Assert.StartsWith("Jembatan rangka (Pratt) bentang 15 m, lebar 2 m", b.Summary);
            var k = Assistant.Parse("Kanopi kantilever Tiga meter, tinggi Tiga meter, jarak Lima meter.");
            Assert.StartsWith("Kanopi kantilever 3 m x 15 m, tinggi 3 m", k.Summary);
            Assert.Equal("15", Assistant.SpokenNumbers("lima belas"));
        }
    }
}
