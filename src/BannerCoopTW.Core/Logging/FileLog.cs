using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BannerCoopTW.Core.Logging
{
    /// <summary>
    /// Appends timestamped lines to one file per session (<c>{prefix}_yyyyMMdd_HHmmss.log</c>)
    /// and keeps only the newest <see cref="MaxFiles"/> session files. Thread-safe.
    /// Never throws: logging must not crash the game.
    /// </summary>
    public sealed class FileLog : ILog, IDisposable
    {
        private readonly object _gate = new object();
        private StreamWriter? _writer;

        public FileLog(string directory, string prefix, int maxFiles = 5, Func<DateTime>? clock = null)
        {
            Directory = directory;
            Prefix = prefix;
            MaxFiles = Math.Max(1, maxFiles);
            Clock = clock ?? (() => DateTime.Now);

            System.IO.Directory.CreateDirectory(directory);
            FilePath = NewFilePath();
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false))
            {
                AutoFlush = true,
            };
            DeleteOldFiles();
        }

        public string Directory { get; }

        public string Prefix { get; }

        public int MaxFiles { get; }

        public string FilePath { get; }

        public LogLevel MinLevel { get; set; } = LogLevel.Info;

        /// <summary>Optional extra sink (e.g. the game's own log). Exceptions from it are swallowed.</summary>
        public Action<LogLevel, string>? Mirror { get; set; }

        private Func<DateTime> Clock { get; }

        public void Write(LogLevel level, string message, Exception? exception = null)
        {
            if (level < MinLevel)
                return;

            var line = string.Format(CultureInfo.InvariantCulture, "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}", Clock(), level, message);
            if (exception != null)
                line += Environment.NewLine + exception;

            lock (_gate)
            {
                try
                {
                    _writer?.WriteLine(line);
                }
                catch (IOException)
                {
                    // Disk full / file locked: drop the line rather than crash the game.
                }
            }

            try
            {
                Mirror?.Invoke(level, message);
            }
            catch
            {
                // The mirror is best-effort.
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        private string NewFilePath()
        {
            var stamp = Clock().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            var path = Path.Combine(Directory, $"{Prefix}_{stamp}.log");
            for (var i = 1; File.Exists(path); i++)
                path = Path.Combine(Directory, $"{Prefix}_{stamp}_{i}.log");
            return path;
        }

        private void DeleteOldFiles()
        {
            try
            {
                var old = new DirectoryInfo(Directory)
                    .GetFiles(Prefix + "_*.log")
                    .Where(f => !string.Equals(f.FullName, Path.GetFullPath(FilePath), StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.Name, StringComparer.Ordinal)
                    .Skip(MaxFiles - 1);
                foreach (var file in old)
                    file.Delete();
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
