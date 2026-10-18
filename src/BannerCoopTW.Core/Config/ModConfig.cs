using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BannerCoopTW.Core.Logging;

namespace BannerCoopTW.Core.Config
{
    /// <summary>
    /// Mod settings stored as a simple <c>key=value</c> file (lines starting with <c>#</c> are comments).
    /// Unknown keys and malformed lines are ignored and reported in <see cref="Problems"/>.
    /// </summary>
    public sealed class ModConfig
    {
        public LogLevel LogLevel { get; set; } = LogLevel.Info;

        /// <summary>Show "BannerCoopTW vX loaded" in the game's message log.</summary>
        public bool ShowLoadedMessage { get; set; } = true;

        public List<string> Problems { get; } = new List<string>();

        public static ModConfig Parse(string text)
        {
            var config = new ModConfig();
            var lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    config.Problems.Add($"line {lineNo}: expected key=value");
                    continue;
                }

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (!config.TrySet(key, value))
                    config.Problems.Add($"line {lineNo}: bad or unknown setting '{key}'");
            }

            return config;
        }

        /// <summary>Reads the file; if it does not exist, writes defaults and returns them.</summary>
        public static ModConfig LoadOrCreate(string path)
        {
            if (File.Exists(path))
                return Parse(File.ReadAllText(path, Encoding.UTF8));

            var config = new ModConfig();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, config.Serialize(), new UTF8Encoding(false));
            return config;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("# BannerCoopTW settings\n");
            sb.Append("# LogLevel: Debug | Info | Warning | Error\n");
            sb.Append("LogLevel=").Append(LogLevel).Append('\n');
            sb.Append("ShowLoadedMessage=").Append(ShowLoadedMessage ? "true" : "false").Append('\n');
            return sb.ToString();
        }

        private bool TrySet(string key, string value)
        {
            switch (key.ToLowerInvariant())
            {
                case "loglevel":
                    if (!Enum.TryParse(value, true, out LogLevel level) || !Enum.IsDefined(typeof(LogLevel), level))
                        return false;
                    LogLevel = level;
                    return true;
                case "showloadedmessage":
                    if (!bool.TryParse(value, out var show))
                        return false;
                    ShowLoadedMessage = show;
                    return true;
                default:
                    return false;
            }
        }
    }
}
