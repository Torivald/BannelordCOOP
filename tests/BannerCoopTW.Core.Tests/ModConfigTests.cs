using System;
using System.IO;
using BannerCoopTW.Core.Config;
using BannerCoopTW.Core.Logging;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    public class ModConfigTests
    {
        [Fact]
        public void Defaults()
        {
            var c = new ModConfig();
            Assert.Equal(LogLevel.Info, c.LogLevel);
            Assert.True(c.ShowLoadedMessage);
        }

        [Fact]
        public void Parses_values_case_insensitively_and_ignores_comments()
        {
            var c = ModConfig.Parse("# comment\r\nloglevel = debug\r\n\r\nShowLoadedMessage=False\r\n");
            Assert.Equal(LogLevel.Debug, c.LogLevel);
            Assert.False(c.ShowLoadedMessage);
            Assert.Empty(c.Problems);
        }

        [Fact]
        public void Bad_lines_are_reported_and_keep_defaults()
        {
            var c = ModConfig.Parse("garbage\nLogLevel=Loud\nUnknown=1\nShowLoadedMessage=maybe\nLogLevel=42\n");
            Assert.Equal(LogLevel.Info, c.LogLevel);
            Assert.True(c.ShowLoadedMessage);
            Assert.Equal(5, c.Problems.Count);
            Assert.Contains("line 1", c.Problems[0]);
        }

        [Fact]
        public void Serialize_round_trips()
        {
            var c = new ModConfig { LogLevel = LogLevel.Warning, ShowLoadedMessage = false };
            var back = ModConfig.Parse(c.Serialize());
            Assert.Equal(LogLevel.Warning, back.LogLevel);
            Assert.False(back.ShowLoadedMessage);
            Assert.Empty(back.Problems);
        }

        [Fact]
        public void LoadOrCreate_writes_defaults_when_missing()
        {
            var dir = Path.Combine(Path.GetTempPath(), "BannerCoopTW.Tests", Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, "sub", "config.ini");
            try
            {
                var c = ModConfig.LoadOrCreate(path);
                Assert.True(File.Exists(path));
                Assert.Equal(LogLevel.Info, c.LogLevel);
                Assert.Equal(LogLevel.Info, ModConfig.LoadOrCreate(path).LogLevel);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ModInfo_version_is_not_empty()
        {
            Assert.False(string.IsNullOrWhiteSpace(ModInfo.Version));
            Assert.Equal("BannerCoopTW", ModInfo.Id);
        }
    }
}
