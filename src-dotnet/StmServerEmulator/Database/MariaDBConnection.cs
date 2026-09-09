using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Database
{
    /// <summary>
    /// MariaDB connection wrapper.  It uses Connector/NET when installed and the
    /// system ODBC provider otherwise, keeping the emulator usable on .NET 2.0.
    /// </summary>
    public sealed class MariaDBConnection : IDisposable
    {
        private readonly string connectionString;
        private readonly DbProviderFactory providerFactory;
        private readonly string providerName;
        private readonly Logger logger;
        private DbConnection connection;
        private bool isConnected;

        public MariaDBConnection()
        {
            Dictionary<string, string> config = ConfigManager.GetConfig();
            logger = Logger.GetLogger("MariaDBConnection");
            providerFactory = SelectProvider(out providerName);
            connectionString = BuildConnectionString(config, providerName);
            logger.Info("Database provider selected: {0}; host={1}; database={2}", providerName, GetValue(config, "database_host"), GetValue(config, "database"));
        }

        public string ConnectionString { get { return connectionString; } }
        public string ProviderName { get { return providerName; } }
        public bool IsConnected { get { return isConnected && connection != null && connection.State == ConnectionState.Open; } }

        public void Open()
        {
            logger.LogFunctionEntry("MariaDBConnection.Open", providerName);
            try
            {
                if (connection == null)
                {
                    connection = providerFactory.CreateConnection();
                    if (connection == null)
                    {
                        throw new InvalidOperationException("The selected database provider did not create a connection");
                    }
                    connection.ConnectionString = connectionString;
                }
                if (connection.State != ConnectionState.Open)
                {
                    connection.Open();
                    isConnected = true;
                    logger.Info("MariaDB connection opened using {0}", providerName);
                }
            }
            catch (Exception ex)
            {
                isConnected = false;
                logger.LogException("MariaDBConnection.Open", ex, "provider=" + providerName);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.Open", isConnected);
            }
        }

        public void Close()
        {
            logger.LogFunctionEntry("MariaDBConnection.Close");
            try
            {
                if (connection != null && connection.State != ConnectionState.Closed)
                {
                    connection.Close();
                    logger.Info("MariaDB connection closed");
                }
                isConnected = false;
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

        public DataTable ExecuteQuery(string query) { return ExecuteQuery(query, null); }
        public DataTable ExecuteQuery(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteQuery", DescribeQuery(query), ParameterCount(parameters));
            DataTable result = new DataTable();
            try
            {
                using (DbCommand command = CreateCommand(query, parameters))
                using (DbDataAdapter adapter = providerFactory.CreateDataAdapter())
                {
                    if (adapter == null)
                    {
                        throw new InvalidOperationException("The selected database provider does not expose a data adapter");
                    }
                    adapter.SelectCommand = command;
                    adapter.Fill(result);
                }
                logger.Debug("Database query returned {0} row(s)", result.Rows.Count);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteQuery", ex, DescribeQuery(query));
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteQuery", result.Rows.Count);
            }
        }

        public int ExecuteNonQuery(string query) { return ExecuteNonQuery(query, null); }
        public int ExecuteNonQuery(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteNonQuery", DescribeQuery(query), ParameterCount(parameters));
            int affected = 0;
            try
            {
                using (DbCommand command = CreateCommand(query, parameters))
                {
                    affected = command.ExecuteNonQuery();
                }
                logger.Debug("Database non-query affected {0} row(s)", affected);
                return affected;
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteNonQuery", ex, DescribeQuery(query));
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteNonQuery", affected);
            }
        }

        public object ExecuteScalar(string query) { return ExecuteScalar(query, null); }
        public object ExecuteScalar(string query, OleDbParameter[] parameters)
        {
            logger.LogFunctionEntry("MariaDBConnection.ExecuteScalar", DescribeQuery(query), ParameterCount(parameters));
            object result = null;
            try
            {
                using (DbCommand command = CreateCommand(query, parameters))
                {
                    result = command.ExecuteScalar();
                }
                logger.Debug("Database scalar result type: {0}", result == null ? "<null>" : result.GetType().FullName);
                return result;
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.ExecuteScalar", ex, DescribeQuery(query));
                throw;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.ExecuteScalar", result == null ? "<null>" : "value");
            }
        }

        public bool TestConnection()
        {
            logger.LogFunctionEntry("MariaDBConnection.TestConnection");
            try
            {
                object result = ExecuteScalar("SELECT 1");
                bool connected = result != null && Convert.ToInt32(result) == 1;
                logger.Info("MariaDB connectivity test: {0}", connected ? "success" : "failed");
                return connected;
            }
            catch (Exception ex)
            {
                logger.LogException("MariaDBConnection.TestConnection", ex);
                return false;
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.TestConnection", IsConnected);
            }
        }

        public void Dispose()
        {
            logger.LogFunctionEntry("MariaDBConnection.Dispose");
            try
            {
                Close();
                if (connection != null)
                {
                    connection.Dispose();
                    connection = null;
                }
            }
            finally
            {
                logger.LogFunctionExit("MariaDBConnection.Dispose");
            }
        }

        private DbCommand CreateCommand(string query, OleDbParameter[] sourceParameters)
        {
            if (String.IsNullOrEmpty(query))
            {
                throw new ArgumentException("A SQL statement is required", "query");
            }
            EnsureConnection();
            DbCommand command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandType = CommandType.Text;
            AddParameters(command, sourceParameters);
            return command;
        }

        private void AddParameters(DbCommand command, OleDbParameter[] sourceParameters)
        {
            if (sourceParameters == null)
            {
                return;
            }
            for (int i = 0; i < sourceParameters.Length; i++)
            {
                OleDbParameter source = sourceParameters[i];
                DbParameter parameter = providerFactory.CreateParameter();
                if (parameter == null)
                {
                    throw new InvalidOperationException("The selected database provider did not create a parameter");
                }
                parameter.ParameterName = String.IsNullOrEmpty(source.ParameterName) ? "p" + i : source.ParameterName;
                parameter.Value = source.Value ?? DBNull.Value;
                parameter.Direction = source.Direction;
                if (source.Size > 0)
                {
                    parameter.Size = source.Size;
                }
                command.Parameters.Add(parameter);
            }
        }

        private void EnsureConnection()
        {
            if (connection == null || connection.State != ConnectionState.Open)
            {
                Open();
            }
        }

        private static DbProviderFactory SelectProvider(out string name)
        {
            try
            {
                DbProviderFactory factory = DbProviderFactories.GetFactory("MySql.Data.MySqlClient");
                name = "MySql.Data.MySqlClient";
                return factory;
            }
            catch (Exception)
            {
                name = "System.Data.Odbc";
                return DbProviderFactories.GetFactory(name);
            }
        }

        private static string BuildConnectionString(Dictionary<string, string> config, string provider)
        {
            string host = GetValue(config, "database_host");
            string port = GetValue(config, "database_port");
            string database = GetValue(config, "database");
            string username = GetValue(config, "database_username");
            string password = GetValue(config, "database_password");
            if (provider == "MySql.Data.MySqlClient")
            {
                return String.Format("Server={0};Port={1};Database={2};Uid={3};Pwd={4};Pooling=true;Connection Timeout=15;", host, port, database, username, password);
            }
            return String.Format("Driver={{MariaDB ODBC 3.1 Driver}};Server={0};Port={1};Database={2};User={3};Password={4};Option=3;", host, port, database, username, password);
        }

        private static string GetValue(Dictionary<string, string> config, string key)
        {
            string value;
            return config != null && config.TryGetValue(key, out value) ? value : String.Empty;
        }

        private static int ParameterCount(OleDbParameter[] parameters) { return parameters == null ? 0 : parameters.Length; }
        private static string DescribeQuery(string query) { return query == null ? "<null SQL>" : query.Replace('\r', ' ').Replace('\n', ' ').Trim(); }
    }
}
