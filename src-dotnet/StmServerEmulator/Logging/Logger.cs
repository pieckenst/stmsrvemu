using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace StmServerEmulator.Logging
{
    /// <summary>
    /// Logger for the Steam Server Emulator
    /// Provides multi-level logging with file and console output
    /// </summary>
    public class Logger
    {
        private static readonly object lockObject = new object();
        private static Dictionary<string, Logger> loggers = new Dictionary<string, Logger>();
        private static LogLevel globalLogLevel = LogLevel.Info;
        private static bool logToFile = true;
        private static bool loggingEnabled = true;
        private static string logDirectory = "logs";
        private static StreamWriter debugWriter;
        private static StreamWriter infoWriter;
        private static StreamWriter errorWriter;
        private static string startupTimestamp;

        private string name;
        private LogLevel minLevel;

        /// <summary>
        /// Get or create a logger with the specified name
        /// </summary>
        public static Logger GetLogger(string name)
        {
            lock (lockObject)
            {
                if (!loggers.ContainsKey(name))
                {
                    loggers[name] = new Logger(name);
                }
                return loggers[name];
            }
        }

        /// <summary>
        /// Initialize the logging system
        /// </summary>
        public static void Initialize()
        {
            Initialize(LogLevel.Info, true, true, "logs");
        }

        /// <summary>
        /// Initialize the logging system with custom settings
        /// </summary>
        public static void Initialize(LogLevel level, bool logToConsole, bool logToFiles, string logDir)
        {
            lock (lockObject)
            {
                globalLogLevel = level;
                logToFile = logToFiles;
                loggingEnabled = logToConsole;
                logDirectory = logDir;

                if (!loggingEnabled && !logToFile)
                {
                    return;
                }

                try
                {
                    // Create log directory if it doesn't exist
                    if (!Directory.Exists(logDirectory))
                    {
                        Directory.CreateDirectory(logDirectory);
                    }

                    // Generate timestamp for log filenames
                    startupTimestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

                    if (logToFile)
                    {
                        // Open file writers
                        string debugPath = Path.Combine(logDirectory, $"emulator_debug_{startupTimestamp}.log");
                        string infoPath = Path.Combine(logDirectory, $"emulator_info_{startupTimestamp}.log");
                        string errorPath = Path.Combine(logDirectory, $"emulator_error_{startupTimestamp}.log");

                        debugWriter = new StreamWriter(debugPath, true);
                        infoWriter = new StreamWriter(infoPath, true);
                        errorWriter = new StreamWriter(errorPath, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Failed to initialize logging: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Shutdown the logging system and flush all writers
        /// </summary>
        public static void Shutdown()
        {
            lock (lockObject)
            {
                try
                {
                    if (debugWriter != null)
                    {
                        debugWriter.Flush();
                        debugWriter.Close();
                        debugWriter = null;
                    }

                    if (infoWriter != null)
                    {
                        infoWriter.Flush();
                        infoWriter.Close();
                        infoWriter = null;
                    }

                    if (errorWriter != null)
                    {
                        errorWriter.Flush();
                        errorWriter.Close();
                        errorWriter = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error during logging shutdown: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Private constructor for Logger
        /// </summary>
        private Logger(string name)
        {
            this.name = name;
            this.minLevel = globalLogLevel;
        }

        /// <summary>
        /// Set the minimum log level for this logger
        /// </summary>
        public void SetLevel(LogLevel level)
        {
            this.minLevel = level;
        }

        /// <summary>
        /// Log a message at the specified level
        /// </summary>
        public void Log(LogLevel level, string message, params object[] args)
        {
            if (!loggingEnabled && !logToFile)
            {
                return;
            }

            if (level < minLevel)
            {
                return;
            }

            string formattedMessage = string.Format(message, args);
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logMessage = string.Format("{0} {1,-14} {2,-9} {3}", 
                timestamp, name, level.ToString(), formattedMessage);

            lock (lockObject)
            {
                // Write to console
                if (loggingEnabled)
                {
                    Console.WriteLine(logMessage);
                }

                // Write to files
                if (logToFile)
                {
                    try
                    {
                        if (debugWriter != null && level >= LogLevel.DebugPlus)
                        {
                            debugWriter.WriteLine(logMessage);
                            debugWriter.Flush();
                        }

                        if (infoWriter != null && level >= LogLevel.Info)
                        {
                            infoWriter.WriteLine(logMessage);
                            infoWriter.Flush();
                        }

                        if (errorWriter != null && level >= LogLevel.Warning)
                        {
                            errorWriter.WriteLine(logMessage);
                            errorWriter.Flush();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error writing to log file: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Log raw packet data (hex dump)
        /// </summary>
        public void LogPacket(LogLevel level, string direction, byte[] data, string description = "")
        {
            if (!loggingEnabled && !logToFile)
            {
                return;
            }

            if (level < minLevel)
            {
                return;
            }

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logMessage = string.Format("{0} {1,-14} {2,-9} [{3}] {4} ({5} bytes)", 
                timestamp, name, level.ToString(), direction, description, data.Length);

            // Add hex dump
            string hexDump = BitConverter.ToString(data).Replace("-", " ");
            logMessage += "\nHEX: " + hexDump;

            // Add ASCII representation
            string ascii = System.Text.Encoding.ASCII.GetString(data);
            for (int i = 0; i < ascii.Length; i++)
            {
                if (ascii[i] < 32 || ascii[i] > 126)
                {
                    ascii = ascii.Substring(0, i) + "." + ascii.Substring(i + 1);
                }
            }
            logMessage += "\nASCII: " + ascii;

            lock (lockObject)
            {
                if (loggingEnabled)
                {
                    Console.WriteLine(logMessage);
                }

                if (logToFile)
                {
                    try
                    {
                        if (debugWriter != null && level >= LogLevel.DebugPlus)
                        {
                            debugWriter.WriteLine(logMessage);
                            debugWriter.Flush();
                        }

                        if (infoWriter != null && level >= LogLevel.Info)
                        {
                            infoWriter.WriteLine(logMessage);
                            infoWriter.Flush();
                        }

                        if (errorWriter != null && level >= LogLevel.Warning)
                        {
                            errorWriter.WriteLine(logMessage);
                            errorWriter.Flush();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error writing packet log: " + ex.Message);
                    }
                }
            }
        }

        /// <summary>
        /// Log function entry
        /// </summary>
        public void LogFunctionEntry(string functionName, params object[] args)
        {
            string argsStr = args.Length > 0 ? string.Join(", ", args) : "";
            Log(LogLevel.DebugPlus, "ENTER: {0}({1})", functionName, argsStr);
        }

        /// <summary>
        /// Log function exit
        /// </summary>
        public void LogFunctionExit(string functionName, object result = null)
        {
            string resultStr = result != null ? " -> " + result.ToString() : "";
            Log(LogLevel.DebugPlus, "EXIT: {0}{1}", functionName, resultStr);
        }

        /// <summary>
        /// Log exception with full details
        /// </summary>
        public void LogException(string functionName, Exception ex, string additionalInfo = "")
        {
            string errorMessage = string.Format("EXCEPTION in {0}: {1}", functionName, ex.Message);
            if (!string.IsNullOrEmpty(additionalInfo))
            {
                errorMessage += " - " + additionalInfo;
            }
            
            Log(LogLevel.Error, errorMessage);
            Log(LogLevel.Error, "Stack Trace: {0}", ex.StackTrace);
            
            if (ex.InnerException != null)
            {
                Log(LogLevel.Error, "Inner Exception: {0}", ex.InnerException.Message);
                Log(LogLevel.Error, "Inner Stack Trace: {0}", ex.InnerException.StackTrace);
            }
        }

        /// <summary>
        /// Log a debugplus message
        /// </summary>
        public void DebugPlus(string message, params object[] args)
        {
            Log(LogLevel.DebugPlus, message, args);
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        public void Debug(string message, params object[] args)
        {
            Log(LogLevel.Debug, message, args);
        }

        /// <summary>
        /// Log a storage neuter message
        /// </summary>
        public void StorageNeuter(string message, params object[] args)
        {
            Log(LogLevel.StorageNeuter, message, args);
        }

        /// <summary>
        /// Log an info message
        /// </summary>
        public void Info(string message, params object[] args)
        {
            Log(LogLevel.Info, message, args);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        public void Warning(string message, params object[] args)
        {
            Log(LogLevel.Warning, message, args);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        public void Error(string message, params object[] args)
        {
            Log(LogLevel.Error, message, args);
        }

        /// <summary>
        /// Log a critical message
        /// </summary>
        public void Critical(string message, params object[] args)
        {
            Log(LogLevel.Critical, message, args);
        }

        /// <summary>
        /// Log an exception
        /// </summary>
        public void Exception(Exception ex, string message = "")
        {
            string exceptionMessage = string.IsNullOrEmpty(message) 
                ? ex.ToString() 
                : message + " - " + ex.ToString();
            
            Log(LogLevel.Error, exceptionMessage);
            Log(LogLevel.Error, "Stack Trace: {0}", ex.StackTrace);
            
            if (ex.InnerException != null)
            {
                Log(LogLevel.Error, "Inner Exception: {0}", ex.InnerException.Message);
                Log(LogLevel.Error, "Inner Stack Trace: {0}", ex.InnerException.StackTrace);
            }
        }

        /// <summary>
        /// Get the global log level
        /// </summary>
        public static LogLevel GlobalLogLevel
        {
            get { return globalLogLevel; }
            set 
            { 
                lock (lockObject)
                {
                    globalLogLevel = value;
                    // Update all existing loggers
                    foreach (var logger in loggers.Values)
                    {
                        logger.minLevel = value;
                    }
                }
            }
        }

        /// <summary>
        /// Get or set whether logging is enabled
        /// </summary>
        public static bool LoggingEnabled
        {
            get { return loggingEnabled; }
            set { loggingEnabled = value; }
        }

        /// <summary>
        /// Get or set whether to log to file
        /// </summary>
        public static bool LogToFile
        {
            get { return logToFile; }
            set { logToFile = value; }
        }
    }
}
