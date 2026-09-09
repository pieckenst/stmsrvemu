using System;
using System.Text;

namespace StmServerEmulator.Logging
{
    /// <summary>
    /// Formatter for log messages with color support (for console output)
    /// </summary>
    public class LogFormatter
    {
        /// <summary>
        /// Console color codes for different log levels
        /// </summary>
        public enum ConsoleColorCode
        {
            Reset = 0,
            Grey = 90,
            Red = 91,
            Green = 92,
            Yellow = 93,
            Blue = 94,
            Magenta = 95,
            Cyan = 96,
            White = 97
        }

        /// <summary>
        /// Format a log message with color codes for console output
        /// </summary>
        public static string FormatColoredMessage(LogLevel level, string name, string message)
        {
            ConsoleColorCode colorCode = GetColorForLevel(level);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string levelString = level.ToString();

            return string.Format("\x1b[{0}m{1} {2,-14} {3,-9} {4}\x1b[0m",
                (int)colorCode, timestamp, name, levelString, message);
        }

        /// <summary>
        /// Get the console color code for a log level
        /// </summary>
        private static ConsoleColorCode GetColorForLevel(LogLevel level)
        {
            switch (level)
            {
                case LogLevel.DebugPlus:
                    return ConsoleColorCode.Magenta;
                case LogLevel.Debug:
                    return ConsoleColorCode.Blue;
                case LogLevel.StorageNeuter:
                    return ConsoleColorCode.Cyan;
                case LogLevel.Info:
                    return ConsoleColorCode.Green;
                case LogLevel.Warning:
                    return ConsoleColorCode.Yellow;
                case LogLevel.Error:
                    return ConsoleColorCode.Red;
                case LogLevel.Critical:
                    return ConsoleColorCode.Red;
                default:
                    return ConsoleColorCode.Grey;
            }
        }

        /// <summary>
        /// Format a log message without color codes (for file output)
        /// </summary>
        public static string FormatPlainMessage(LogLevel level, string name, string message)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string levelString = level.ToString();

            return string.Format("{0} {1,-14} {2,-9} {3}",
                timestamp, name, levelString, message);
        }

        /// <summary>
        /// Format an exception message
        /// </summary>
        public static string FormatException(Exception ex, string message = "")
        {
            StringBuilder sb = new StringBuilder();
            
            if (!string.IsNullOrEmpty(message))
            {
                sb.AppendLine(message);
            }

            sb.AppendLine("Exception: " + ex.GetType().Name);
            sb.AppendLine("Message: " + ex.Message);
            
            if (ex.StackTrace != null)
            {
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(ex.StackTrace);
            }

            if (ex.InnerException != null)
            {
                sb.AppendLine("Inner Exception:");
                sb.AppendLine(FormatException(ex.InnerException));
            }

            return sb.ToString();
        }
    }
}
