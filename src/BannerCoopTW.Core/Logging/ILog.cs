using System;

namespace BannerCoopTW.Core.Logging
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public interface ILog
    {
        LogLevel MinLevel { get; set; }

        void Write(LogLevel level, string message, Exception? exception = null);
    }

    public static class LogExtensions
    {
        public static void Debug(this ILog log, string message) => log.Write(LogLevel.Debug, message);

        public static void Info(this ILog log, string message) => log.Write(LogLevel.Info, message);

        public static void Warning(this ILog log, string message) => log.Write(LogLevel.Warning, message);

        public static void Error(this ILog log, string message, Exception? exception = null) => log.Write(LogLevel.Error, message, exception);
    }
}
