using System;
using System.Data.OleDb;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Database
{
    /// <summary>Durable raw and decoded CSER telemetry archive.</summary>
    internal sealed class ClientStatisticsDatabase : IDisposable
    {
        private readonly MariaDBConnection connection;
        private readonly Logger logger;

        internal ClientStatisticsDatabase()
        {
            logger = Logger.GetLogger("ClientStatisticsDatabase"); logger.LogFunctionEntry("ClientStatisticsDatabase.ctor");
            try { connection = new MariaDBConnection(); connection.Open(); connection.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS client_statistics_events (event_id BIGINT AUTO_INCREMENT PRIMARY KEY, event_type VARCHAR(48) NOT NULL, source_ip VARCHAR(45) NOT NULL, source_port INT NOT NULL, payload LONGBLOB NOT NULL, decoded_data LONGTEXT NOT NULL, received_at DATETIME NOT NULL, INDEX idx_client_statistics_type (event_type), INDEX idx_client_statistics_time (received_at))"); }
            finally { logger.LogFunctionExit("ClientStatisticsDatabase.ctor"); }
        }

        internal void Store(string eventType, string address, int port, byte[] payload, string decodedData)
        {
            logger.LogFunctionEntry("ClientStatisticsDatabase.Store", eventType, address, payload == null ? 0 : payload.Length);
            try { connection.ExecuteNonQuery("INSERT INTO client_statistics_events (event_type,source_ip,source_port,payload,decoded_data,received_at) VALUES (?,?,?,?,?,NOW())", new OleDbParameter[] { new OleDbParameter("event_type",eventType), new OleDbParameter("source_ip",address), new OleDbParameter("source_port",port), new OleDbParameter("payload",payload ?? new byte[0]), new OleDbParameter("decoded_data",decodedData ?? String.Empty) }); }
            finally { logger.LogFunctionExit("ClientStatisticsDatabase.Store"); }
        }
        public void Dispose() { logger.LogFunctionEntry("ClientStatisticsDatabase.Dispose"); try { connection.Dispose(); } finally { logger.LogFunctionExit("ClientStatisticsDatabase.Dispose"); } }
    }
}
