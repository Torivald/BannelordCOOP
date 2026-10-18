using System;
using System.IO;
using System.Linq;
using BannerCoopTW.Core.Logging;
using Xunit;

namespace BannerCoopTW.Core.Tests
{
    public sealed class FileLogTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "BannerCoopTW.Tests", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Fact]
        public void Writes_lines_with_level_and_respects_min_level()
        {
            string path;
            using (var log = new FileLog(_dir, "Test") { MinLevel = LogLevel.Info })
            {
                log.Debug("hidden");
                log.Info("hello");
                log.Error("boom", new InvalidOperationException("bad"));
                path = log.FilePath;
            }

            var text = File.ReadAllText(path);
            Assert.DoesNotContain("hidden", text);
            Assert.Contains("[Info] hello", text);
            Assert.Contains("[Error] boom", text);
            Assert.Contains("InvalidOperationException: bad", text);
        }

        [Fact]
        public void Keeps_only_newest_max_files()
        {
            var t = new DateTime(2026, 10, 18, 12, 0, 0);
            for (var i = 0; i < 7; i++)
            {
                var stamp = t.AddMinutes(i);
                using var log = new FileLog(_dir, "Test", maxFiles: 3, clock: () => stamp);
                log.Info("run " + i);
            }

            var files = Directory.GetFiles(_dir, "Test_*.log").Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Test_20261018_120400.log", "Test_20261018_120500.log", "Test_20261018_120600.log" }, files);
        }

        [Fact]
        public void Same_second_sessions_get_distinct_files()
        {
            var stamp = new DateTime(2026, 10, 18, 12, 0, 0);
            using var a = new FileLog(_dir, "Test", clock: () => stamp);
            using var b = new FileLog(_dir, "Test", clock: () => stamp);
            Assert.NotEqual(a.FilePath, b.FilePath);
        }

        [Fact]
        public void Mirror_failure_does_not_throw_and_mirror_receives_messages()
        {
            string? mirrored = null;
            using var log = new FileLog(_dir, "Test");
            log.Mirror = (_, m) => mirrored = m;
            log.Info("to mirror");
            Assert.Equal("to mirror", mirrored);

            log.Mirror = (_, _) => throw new Exception("sink down");
            log.Info("still fine");
        }
    }
}
