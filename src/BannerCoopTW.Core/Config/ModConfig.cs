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

        /// <summary>Enables the debug-only Phase 2b spike console commands (<c>coop.*</c>).</summary>
        public bool SpikesEnabled { get; set; }

        /// <summary>Spike automation: save name loaded automatically at the main menu (empty = off).</summary>
        public string SpikeAutoLoadSave { get; set; } = string.Empty;

        /// <summary>Spike automation: comma-separated commands run once the campaign finished loading (e.g. <c>dump_guids,world_hash</c>).</summary>
        public string SpikeAutoRun { get; set; } = string.Empty;

        /// <summary>Spike automation for two instances: script of the instance that wins the role election (host) / of the other one.</summary>
        public string SpikeAutoRunHost { get; set; } = string.Empty;

        public string SpikeAutoRunClient { get; set; } = string.Empty;

        /// <summary>Spike A1 automation: <c>auto</c> = at the main menu start a Custom Battle on both instances (host/client by role election) and run the mission sync. Empty = off.</summary>
        public string SpikeA1 { get; set; } = string.Empty;

        /// <summary>Spike L1 (Phase 4): <c>auto</c> = at the main menu one instance hosts the lobby and the other connects, goes ready and pings. Empty = off.</summary>
        public string SpikeL1 { get; set; } = string.Empty;

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
            sb.Append("SpikesEnabled=").Append(SpikesEnabled ? "true" : "false").Append('\n');
            sb.Append("SpikeAutoLoadSave=").Append(SpikeAutoLoadSave).Append('\n');
            sb.Append("SpikeAutoRun=").Append(SpikeAutoRun).Append('\n');
            sb.Append("SpikeAutoRunHost=").Append(SpikeAutoRunHost).Append('\n');
            sb.Append("SpikeAutoRunClient=").Append(SpikeAutoRunClient).Append('\n');
            sb.Append("SpikeA1=").Append(SpikeA1).Append('\n');
            sb.Append("SpikeL1=").Append(SpikeL1).Append('\n');
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
                case "spikesenabled":
                    if (!bool.TryParse(value, out var spikes))
                        return false;
                    SpikesEnabled = spikes;
                    return true;
                case "spikeautoloadsave":
                    SpikeAutoLoadSave = value;
                    return true;
                case "spikeautorunhost":
                    SpikeAutoRunHost = value;
                    return true;
                case "spikeautorunclient":
                    SpikeAutoRunClient = value;
                    return true;
                case "spikea1":
                    SpikeA1 = value;
                    return true;
                case "spikel1":
                    SpikeL1 = value;
                    return true;
                case "spikeautorun":
                    SpikeAutoRun = value;
                    return true;
                default:
                    return false;
            }
        }
    }
}
