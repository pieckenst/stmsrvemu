using System;
using System.Data;
using System.Data.OleDb;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Database
{
    /// <summary>
    /// MariaDB connection handler
    /// Handles MariaDB-specific database operations using OLE DB
    /// </summary>
    public class MariaDBConnection
    {
        private string connectionString;
        private OleDbConnection connection;
        private Logger logger;
        private bool isConnected;

        /// <summary>
        /// Constructor for MariaDBConnection
        /// </summary>
        public MariaDBConnection()
        {
            var config = ConfigManager.GetConfig();
            this.connectionString = BuildConnectionString(config);
            this.logger = Logger.GetLogger("MariaDBConnection");
            this.isConnected = false;
        }

        /// <summary>
        /// Build connection string from configuration
        /// </summary>
        private string BuildConnectionString(Dictionary<string, string> config)
        {
            string host = config["database_host"];
            string port = config["database_port"];
            string database = config["database"];
            string username = config["database_username"];
            string password = config["database_password"];

            // For .NET Framework 2.0, we'll use OleDb with MySQL provider
            // In production, you would use MySQL Connector/Net
            return string.Format("Provider=MySQLProv;Data Source={0};Port={1};Database={2};User Id={3};Password={4};",
                host, port, database, username, password);
        }

        /// <summary>
        /// Open database connection
        /// </summary>
        public void Open()
        {
            logger.LogFunctionEntry("MariaDBConnection.Open");
            
            try
            {
                if (connection == null)
                {
                    connection = new OleDbConnection(connectionString);
                }

                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                    isConnected = true;
                    logger.Info("Database connection opened successfully");
                }
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.Open", ex);
                isConnected = false;
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.Open", isConnected);
            }
        }

        /// <summary>
        /// Close database connection
        /// </summary>
        public void Close()
        {
            logger.LogFunctionEntry("MariaDBConnection.Close");
            
            try
            {
                if (connection != null && connection.State == ConnectionState.Open)
                {
                    connection.Close();
                    isConnected = false;
                    logger.Info("Database connection closed");
                }
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.Close", ex);
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.Close");
            }
        }

        /// <summary>
        /// Execute a SQL query
        /// </summary>
        public DataTable ExecuteQuery(string query)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteQuery", query);
            
            DataTable result = new DataTable();
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    using (OleDbDataAdapter adapter = new OleDbDataAdapter(command))
                    {
                        adapter.Fill(result);
                    }
                }
                
                logger.Debug("Query executed successfully, returned {0} rows", result.Rows.Count);
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteQuery", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteQuery", result.Rows.Count);
            }
            
            return result;
        }

        /// <summary>
        /// Execute a SQL query with parameters
        /// </summary>
        public DataTable ExecuteQuery(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteQuery", query, parameters.Length);
            
            DataTable result = new DataTable();
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    
                    using (OleDbDataAdapter adapter = new OleDbDataAdapter(command))
                    {
                        adapter.Fill(result);
                    }
                }
                
                logger.Debug("Parameterized query executed successfully, returned {0} rows", result.Rows.Count);
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteQuery", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteQuery", result.Rows.Count);
            }
            
            return result;
        }

        /// <summary>
        /// Execute a non-query SQL command
        /// </summary>
        public int ExecuteNonQuery(string query)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteNonQuery", query);
            
            int rowsAffected = 0;
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    rowsAffected = command.ExecuteNonQuery();
                }
                
                logger.Debug("Non-query executed successfully, affected {0} rows", rowsAffected);
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteNonQuery", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteNonQuery", rowsAffected);
            }
            
            return rowsAffected;
        }

        /// <summary>
        /// Execute a non-query SQL command with parameters
        /// </summary>
        public int ExecuteNonQuery(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteNonQuery", query, parameters.Length);
            
            int rowsAffected = 0;
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    
                    rowsAffected = command.ExecuteNonQuery();
                }
                
                logger.Debug("Parameterized non-query executed successfully, affected {0} rows", rowsAffected);
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteNonQuery", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteNonQuery", rowsAffected);
            }
            
            return rowsAffected;
        }

        /// <summary>
        /// Execute a scalar query (returns single value)
        /// </summary>
        public object ExecuteScalar(string query)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteScalar", query);
            
            object result = null;
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    result = command.ExecuteScalar();
                }
                
                logger.Debug("Scalar query executed successfully");
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteScalar", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteScalar", result);
            }
            
            return result;
        }

        /// <summary>
        /// Execute a scalar query with parameters
        /// </summary>
        public object ExecuteScalar(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteScalar", query, parameters.Length);
            
            object result = null;
            
            try
            {
                EnsureConnection();
                
                using (OleDbCommand command = new OleDbCommand(query, connection))
                {
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    
                    result = command.ExecuteScalar();
                }
                
                logger.Debug("Parameterized scalar query executed successfully");
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteScalar", ex, "Query: " + query);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteScalar", result);
            }
            
            return result;
        }

        /// <summary>
        /// Test database connection
        /// </summary>
        public bool TestConnection()
        {
            logger.LogFunctionEntry("MariaDBConnection.TestConnection");
            
            try
            {
                EnsureConnection();
                
                // Execute a simple test query
                object result = ExecuteScalar("SELECT 1");
                
                bool success = result != null && Convert.ToInt32(result) == 1;
                logger.Info("Database connection test: {0}", success ? "Success" : "Failed");
                
                logger.LogFunctionExit("MariaDBConnection.TestConnection", success);
                return success;
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.TestConnection", ex);
                logger.LogFunctionExit("MariaDBConnection.TestConnection", false);
                return false;
            }
        }

        /// <summary>
        /// Ensure connection is open
        /// </summary>
        private void EnsureConnection()
        {
            if (connection == null || connection.State != ConnectionState.Open)
            {
                Open();
            }
        }

        /// <summary>
        /// Get connection string
        /// </summary>
        public string ConnectionString
        {
            get { return connectionString; }
        }

        /// <summary>
        /// Check if connection is open
        /// </summary>
        public bool IsConnected
        {
            get { return isConnected && connection != null && connection.State == ConnectionState.Open; }
        }

        /// <summary>
        /// Dispose of connection
        /// </summary>
        public void Dispose()
        {
            Close();
            
            if (connection != null)
            {
                connection.Dispose();
                connection = null;
            }
        }
    }
}
