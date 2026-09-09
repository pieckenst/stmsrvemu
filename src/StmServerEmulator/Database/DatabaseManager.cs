using System;
using System.Collections.Generic;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Database
{
    /// <summary>Owns the process-wide database connectivity check and shutdown path.</summary>
    public static class DatabaseManager
    {
        private static readonly object sync = new object();
        private static Dictionary<string, string> config;
        private static Logger logger;
        private static MariaDBConnection connection;
        private static bool initialized;

        public static bool IsInitialized { get { return initialized; } }

        public static void Initialize()
        {
            lock (sync)
            {
                if (initialized)
                {
                    return;
                }
                logger = Logger.GetLogger("DatabaseManager");
                logger.LogFunctionEntry("DatabaseManager.Initialize");
                try
                {
                    config = ConfigManager.GetConfig();
                    connection = new MariaDBConnection();
                    connection.Open();
                    if (!connection.TestConnection())
                    {
                        throw new InvalidOperationException("MariaDB did not return SELECT 1 during startup verification");
                    }
                    initialized = true;
                    logger.Info("Database ready using {0} for {1}@{2}:{3}", connection.ProviderName, config["database"], config["database_host"], config["database_port"]);
                }
                catch (Exception ex)
                {
                    initialized = false;
                    logger.LogException("DatabaseManager.Initialize", ex, "Database startup failed");
                    if (connection != null)
                    {
                        connection.Dispose();
                        connection = null;
                    }
                    throw;
                }
                finally
                {
                    logger.LogFunctionExit("DatabaseManager.Initialize", initialized);
                }
            }
        }

        public static void Shutdown()
        {
            lock (sync)
            {
                if (!initialized && connection == null)
                {
                    return;
                }
                logger.LogFunctionEntry("DatabaseManager.Shutdown");
                try
                {
                    if (connection != null)
                    {
                        connection.Dispose();
                        connection = null;
                    }
                    initialized = false;
                    logger.Info("Database connections shut down");
                }
                catch (Exception ex)
                {
                    logger.LogException("DatabaseManager.Shutdown", ex);
                }
                finally
                {
                    logger.LogFunctionExit("DatabaseManager.Shutdown");
                }
            }
        }
    }
}
