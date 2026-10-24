using BannerCoopTW.Core.Spikes;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    public class WorldHasherTests
    {
        [Fact]
        public void SameInputSameHash()
        {
            var a = new WorldHasher().Add(1u).Add("x").Add(7);
            var b = new WorldHasher().Add(1u).Add("x").Add(7);
            Assert.Equal(a.Value, b.Value);
        }

        [Fact]
        public void OrderMatters()
        {
            var a = new WorldHasher().Add(1u).Add(2u);
            var b = new WorldHasher().Add(2u).Add(1u);
            Assert.NotEqual(a.Value, b.Value);
        }

        [Fact]
        public void StringBoundariesMatter()
        {
            var a = new WorldHasher().Add("ab").Add("c");
            var b = new WorldHasher().Add("a").Add("bc");
            Assert.NotEqual(a.Value, b.Value);
        }

        [Fact]
        public void HexIsSixteenChars() => Assert.Equal(16, new WorldHasher().ToHex().Length);
    }
}
