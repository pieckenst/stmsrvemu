using System;

namespace StmServerEmulator.Logging
{
    /// <summary>
    /// Log levels for the Steam Server Emulator
    /// </summary>
    public enum LogLevel
    {
        /// <summary>
        /// More verbose than DEBUG, includes heartbeat messages
        /// </summary>
        DebugPlus = 5,

        /// <summary>
        /// Debug information
        /// </summary>
        Debug = 10,

        /// <summary>
        /// Storage neutering (GCF checksum) logs
        /// </summary>
        StorageNeuter = 15,

        /// <summary>
        /// Informational messages
        /// </summary>
        Info = 20,

        /// <summary>
        /// Warning messages
        /// </summary>
        Warning = 30,

        /// <summary>
        /// Error messages
        /// </summary>
        Error = 40,

        /// <summary>
        /// Critical error messages
        /// </summary>
        Critical = 50
    }

    /// <summary>
    /// Extension methods for LogLevel
    /// </summary>
    public static class LogLevelExtensions
    {
        /// <summary>
        /// Get the string representation of the log level
        /// </summary>
        public static string ToString(this LogLevel level)
        {
            switch (level)
            {
                case LogLevel.DebugPlus:
                    return "DEBUGPLUS";
                case LogLevel.Debug:
                    return "DEBUG";
                case LogLevel.StorageNeuter:
                    return "STORAGENEUTER";
                case LogLevel.Info:
                    return "INFO";
                case LogLevel.Warning:
                    return "WARNING";
                case LogLevel.Error:
                    return "ERROR";
                case LogLevel.Critical:
                    return "CRITICAL";
                default:
                    return "UNKNOWN";
            }
        }

        /// <summary>
        /// Parse a string to LogLevel
        /// </summary>
        public static LogLevel Parse(string levelString)
        {
            if (string.IsNullOrEmpty(levelString))
            {
                return LogLevel.Info;
            }

            string upperLevel = levelString.ToUpper().Trim();
            
            switch (upperLevel)
            {
                case "DEBUGPLUS":
                    return LogLevel.DebugPlus;
                case "DEBUG":
                    return LogLevel.Debug;
                case "STORAGENEUTER":
                    return LogLevel.StorageNeuter;
                case "INFO":
                    return LogLevel.Info;
                case "WARNING":
                case "WARN":
                    return LogLevel.Warning;
                case "ERROR":
                    return LogLevel.Error;
                case "CRITICAL":
                case "FATAL":
                    return LogLevel.Critical;
                default:
                    return LogLevel.Info;
            }
        }
    }
}
