using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using StmServerEmulator.Config;
using StmServerEmulator.Logging;
using StmServerEmulator.Utilities;

namespace StmServerEmulator.Database
{
    /// <summary>
    /// Authentication database driver
    /// Handles all authentication-related database operations
    /// </summary>
    public class AuthDatabase
    {
        private MariaDBConnection connection;
        private Logger logger;
        private Dictionary<string, string> config;

        /// <summary>
        /// Constructor for AuthDatabase
        /// </summary>
        public AuthDatabase(Dictionary<string, string> config)
        {
            this.config = config;
            this.connection = new MariaDBConnection();
            this.logger = Logger.GetLogger("AuthDatabase");
            
            InitializeDatabase();
        }

        /// <summary>
        /// Initialize database tables
        /// </summary>
        private void InitializeDatabase()
        {
            logger.LogFunctionEntry("AuthDatabase.InitializeDatabase");
            
            try
            {
                connection.Open();
                
                // Create users table
                string createUsersTable = @"
                    CREATE TABLE IF NOT EXISTS users (
                        account_id INT AUTO_INCREMENT PRIMARY KEY,
                        username VARCHAR(64) UNIQUE NOT NULL,
                        password_hash VARCHAR(128) NOT NULL,
                        email VARCHAR(255) UNIQUE NOT NULL,
                        question VARCHAR(255),
                        answer VARCHAR(255),
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        last_login DATETIME,
                        email_verified BOOLEAN DEFAULT FALSE,
                        verification_code VARCHAR(32),
                        ip_address VARCHAR(45)
                    )";
                
                connection.ExecuteNonQuery(createUsersTable);
                logger.Debug("Users table created/verified");

                // Create subscriptions table
                string createSubscriptionsTable = @"
                    CREATE TABLE IF NOT EXISTS subscriptions (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        account_id INT NOT NULL,
                        subscription_id INT NOT NULL,
                        receipt_id INT NOT NULL,
                        purchased_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        expires_at DATETIME,
                        status VARCHAR(32) DEFAULT 'active',
                        FOREIGN KEY (account_id) REFERENCES users(account_id)
                    )";
                
                connection.ExecuteNonQuery(createSubscriptionsTable);
                logger.Debug("Subscriptions table created/verified");

                // Create auth_tickets table
                string createTicketsTable = @"
                    CREATE TABLE IF NOT EXISTS auth_tickets (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        account_id INT NOT NULL,
                        ticket_hash VARCHAR(64) NOT NULL,
                        ticket_data BLOB NOT NULL,
                        created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        expires_at DATETIME NOT NULL,
                        ip_address VARCHAR(45),
                        valid BOOLEAN DEFAULT TRUE,
                        FOREIGN KEY (account_id) REFERENCES users(account_id)
                    )";
                
                connection.ExecuteNonQuery(createTicketsTable);
                logger.Debug("Auth tickets table created/verified");

                // Create user_registry table
                string createRegistryTable = @"
                    CREATE TABLE IF NOT EXISTS user_registry (
                        account_id INT PRIMARY KEY,
                        registry_data BLOB NOT NULL,
                        updated_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (account_id) REFERENCES users(account_id)
                    )";
                
                connection.ExecuteNonQuery(createRegistryTable);
                logger.Debug("User registry table created/verified");

                // Create login_attempts table
                string createLoginAttemptsTable = @"
                    CREATE TABLE IF NOT EXISTS login_attempts (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        username VARCHAR(64) NOT NULL,
                        ip_address VARCHAR(45) NOT NULL,
                        attempt_time DATETIME DEFAULT CURRENT_TIMESTAMP,
                        success BOOLEAN DEFAULT FALSE
                    )";
                
                connection.ExecuteNonQuery(createLoginAttemptsTable);
                logger.Debug("Login attempts table created/verified");

                logger.Info("Database initialization completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.InitializeDatabase", ex);
                throw;
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.InitializeDatabase");
            }
        }

        /// <summary>
        /// Check if username exists
        /// </summary>
        public bool CheckUsernameExists(string username)
        {
            logger.LogFunctionEntry("AuthDatabase.CheckUsernameExists", username);
            
            try
            {
                string query = "SELECT COUNT(*) FROM users WHERE username = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                int count = Convert.ToInt32(result);
                
                logger.Debug("Username exists check: {0} -> {1}", username, count > 0);
                logger.LogFunctionExit("AuthDatabase.CheckUsernameExists", count > 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.CheckUsernameExists", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.CheckUsernameExists", false);
                return false;
            }
        }

        /// <summary>
        /// Check if email exists
        /// </summary>
        public bool CheckEmailExists(string email)
        {
            logger.LogFunctionEntry("AuthDatabase.CheckEmailExists", email);
            
            try
            {
                string query = "SELECT COUNT(*) FROM users WHERE email = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("email", email)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                int count = Convert.ToInt32(result);
                
                logger.Debug("Email exists check: {0} -> {1}", email, count > 0);
                logger.LogFunctionExit("AuthDatabase.CheckEmailExists", count > 0);
                return count > 0;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.CheckEmailExists", ex, "Email: " + email);
                logger.LogFunctionExit("AuthDatabase.CheckEmailExists", false);
                return false;
            }
        }

        /// <summary>
        /// Check if email is verified
        /// </summary>
        public bool CheckEmailVerified(string username)
        {
            logger.LogFunctionEntry("AuthDatabase.CheckEmailVerified", username);
            
            try
            {
                string query = "SELECT email_verified FROM users WHERE username = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                bool verified = result != null && Convert.ToBoolean(result);
                
                logger.Debug("Email verified check: {0} -> {1}", username, verified);
                logger.LogFunctionExit("AuthDatabase.CheckEmailVerified", verified);
                return verified;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.CheckEmailVerified", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.CheckEmailVerified", false);
                return false;
            }
        }

        /// <summary>
        /// Validate user credentials
        /// </summary>
        public bool ValidateCredentials(string username, string password)
        {
            logger.LogFunctionEntry("AuthDatabase.ValidateCredentials", username);
            
            try
            {
                string query = "SELECT password_hash FROM users WHERE username = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                
                if (result == null || result == DBNull.Value)
                {
                    logger.Debug("User not found: {0}", username);
                    logger.LogFunctionExit("AuthDatabase.ValidateCredentials", false);
                    return false;
                }

                string storedHash = result.ToString();
                string inputHash = StringHelper.CalculateSha256(username + ":" + password);
                
                bool valid = storedHash == inputHash;
                logger.Debug("Password validation: {0} -> {1}", username, valid);
                
                if (valid)
                {
                    // Update last login
                    UpdateLastLogin(username);
                }
                
                logger.LogFunctionExit("AuthDatabase.ValidateCredentials", valid);
                return valid;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.ValidateCredentials", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.ValidateCredentials", false);
                return false;
            }
        }

        /// <summary>
        /// Create a new user
        /// </summary>
        public uint CreateUser(string username, string password, string email, string question, string answer, string ipAddress)
        {
            logger.LogFunctionEntry("AuthDatabase.CreateUser", username, email);
            
            try
            {
                string passwordHash = StringHelper.CalculateSha256(username + ":" + password);
                string verificationCode = StringHelper.GenerateRandomAlphanumeric(32);
                
                string query = @"
                    INSERT INTO users (username, password_hash, email, question, answer, verification_code, ip_address)
                    VALUES (?, ?, ?, ?, ?, ?, ?)";
                
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username),
                    new OleDbParameter("password_hash", passwordHash),
                    new OleDbParameter("email", email),
                    new OleDbParameter("question", question),
                    new OleDbParameter("answer", answer),
                    new OleDbParameter("verification_code", verificationCode),
                    new OleDbParameter("ip_address", ipAddress)
                };
                
                connection.ExecuteNonQuery(query, parameters);
                
                // Get the generated account ID
                object accountId = connection.ExecuteScalar("SELECT LAST_INSERT_ID()");
                uint accountIdUint = Convert.ToUInt32(accountId);
                
                logger.Info("User created: {0} (Account ID: {1})", username, accountIdUint);
                logger.LogFunctionExit("AuthDatabase.CreateUser", accountIdUint);
                return accountIdUint;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.CreateUser", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.CreateUser", 0);
                throw;
            }
        }

        /// <summary>
        /// Get account ID for username
        /// </summary>
        public uint GetAccountId(string username)
        {
            logger.LogFunctionEntry("AuthDatabase.GetAccountId", username);
            
            try
            {
                string query = "SELECT account_id FROM users WHERE username = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                
                if (result == null || result == DBNull.Value)
                {
                    logger.Debug("Account ID not found for username: {0}", username);
                    logger.LogFunctionExit("AuthDatabase.GetAccountId", 0);
                    return 0;
                }

                uint accountId = Convert.ToUInt32(result);
                logger.Debug("Account ID for {0}: {1}", username, accountId);
                logger.LogFunctionExit("AuthDatabase.GetAccountId", accountId);
                return accountId;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.GetAccountId", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.GetAccountId", 0);
                return 0;
            }
        }

        /// <summary>
        /// Store authentication ticket
        /// </summary>
        public void StoreAuthTicket(uint accountId, byte[] ticket, string ipAddress, DateTime createdAt, DateTime expiresAt)
        {
            logger.LogFunctionEntry("AuthDatabase.StoreAuthTicket", accountId, ipAddress);
            
            try
            {
                string ticketHash = StringHelper.CalculateSha256(ticket);
                
                string query = @"
                    INSERT INTO auth_tickets (account_id, ticket_hash, ticket_data, created_at, expires_at, ip_address)
                    VALUES (?, ?, ?, ?, ?, ?)";
                
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("account_id", accountId),
                    new OleDbParameter("ticket_hash", ticketHash),
                    new OleDbParameter("ticket_data", ticket),
                    new OleDbParameter("created_at", createdAt),
                    new OleDbParameter("expires_at", expiresAt),
                    new OleDbParameter("ip_address", ipAddress)
                };
                
                connection.ExecuteNonQuery(query, parameters);
                logger.Debug("Auth ticket stored for account {0}", accountId);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.StoreAuthTicket", ex, "Account ID: " + accountId);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.StoreAuthTicket");
            }
        }

        /// <summary>
        /// Invalidate tickets for account
        /// </summary>
        public void InvalidateTickets(uint accountId)
        {
            logger.LogFunctionEntry("AuthDatabase.InvalidateTickets", accountId);
            
            try
            {
                string query = "UPDATE auth_tickets SET valid = FALSE WHERE account_id = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("account_id", accountId)
                };
                
                int rowsAffected = connection.ExecuteNonQuery(query, parameters);
                logger.Debug("Invalidated {0} tickets for account {1}", rowsAffected, accountId);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.InvalidateTickets", ex, "Account ID: " + accountId);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.InvalidateTickets");
            }
        }

        /// <summary>
        /// Expire expired tickets
        /// </summary>
        public void ExpireExpiredTickets()
        {
            logger.LogFunctionEntry("AuthDatabase.ExpireExpiredTickets");
            
            try
            {
                string query = "UPDATE auth_tickets SET valid = FALSE WHERE expires_at < NOW()";
                int rowsAffected = connection.ExecuteNonQuery(query);
                logger.Debug("Expired {0} tickets", rowsAffected);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.ExpireExpiredTickets", ex);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.ExpireExpiredTickets");
            }
        }

        /// <summary>
        /// Get user registry dictionary
        /// </summary>
        public Dictionary<byte[], byte[]> GetUserRegistryDict(string username, string pktVersion, string ipAddress)
        {
            logger.LogFunctionEntry("AuthDatabase.GetUserRegistryDict", username, pktVersion);
            
            try
            {
                uint accountId = GetAccountId(username);
                if (accountId == 0)
                {
                    logger.Warning("User not found: {0}", username);
                    logger.LogFunctionExit("AuthDatabase.GetUserRegistryDict", null);
                    return null;
                }

                // Check if registry exists
                string query = "SELECT registry_data FROM user_registry WHERE account_id = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("account_id", accountId)
                };
                
                object result = connection.ExecuteScalar(query, parameters);
                
                Dictionary<byte[], byte[]> registry;
                
                if (result == null || result == DBNull.Value)
                {
                    // Create new registry
                    registry = CreateDefaultRegistry(accountId, username, pktVersion);
                    SaveUserRegistry(accountId, registry);
                }
                else
                {
                    byte[] registryData = (byte[])result;
                    registry = DeserializeRegistry(registryData);
                }

                logger.Debug("Retrieved registry for user {0} with {1} entries", username, registry.Count);
                logger.LogFunctionExit("AuthDatabase.GetUserRegistryDict", registry.Count);
                return registry;
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.GetUserRegistryDict", ex, "Username: " + username);
                logger.LogFunctionExit("AuthDatabase.GetUserRegistryDict", null);
                return null;
            }
        }

        /// <summary>
        /// Save user registry
        /// </summary>
        private void SaveUserRegistry(uint accountId, Dictionary<byte[], byte[]> registry)
        {
            logger.LogFunctionEntry("AuthDatabase.SaveUserRegistry", accountId);
            
            try
            {
                byte[] registryData = SerializeRegistry(registry);
                
                string query = @"
                    INSERT INTO user_registry (account_id, registry_data)
                    VALUES (?, ?)
                    ON DUPLICATE KEY UPDATE registry_data = ?";
                
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("account_id", accountId),
                    new OleDbParameter("registry_data", registryData),
                    new OleDbParameter("registry_data_update", registryData)
                };
                
                connection.ExecuteNonQuery(query, parameters);
                logger.Debug("Saved registry for account {0}", accountId);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.SaveUserRegistry", ex, "Account ID: " + accountId);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.SaveUserRegistry");
            }
        }

        /// <summary>
        /// Create default user registry
        /// </summary>
        private Dictionary<byte[], byte[]> CreateDefaultRegistry(uint accountId, string username, string pktVersion)
        {
            logger.LogFunctionEntry("AuthDatabase.CreateDefaultRegistry", accountId, username);
            
            Dictionary<byte[], byte[]> registry = new Dictionary<byte[], byte[]>(new ByteArrayComparer());
            
            // Add basic registry entries
            registry[new byte[] { 0x01, 0x00, 0x00, 0x00 }] = BitConverter.GetBytes(accountId);
            registry[new byte[] { 0x02, 0x00, 0x00, 0x00 }] = Encoding.ASCII.GetBytes(username + "\0");
            
            // Add version-specific entries
            if (pktVersion == "r2004" || pktVersion == "r2007")
            {
                registry[new byte[] { 0x03, 0x00, 0x00, 0x00 }] = Encoding.ASCII.GetBytes(pktVersion + "\0");
            }

            logger.Debug("Created default registry with {0} entries", registry.Count);
            logger.LogFunctionExit("AuthDatabase.CreateDefaultRegistry", registry.Count);
            return registry;
        }

        /// <summary>
        /// Serialize registry dictionary to byte array
        /// </summary>
        private byte[] SerializeRegistry(Dictionary<byte[], byte[]> registry)
        {
            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                foreach (var kvp in registry)
                {
                    // Write key length (2 bytes)
                    byte[] keyLength = BitConverter.GetBytes((ushort)kvp.Key.Length);
                    ms.Write(keyLength, 0, 2);

                    // Write key
                    ms.Write(kvp.Key, 0, kvp.Key.Length);

                    // Write value length (4 bytes)
                    byte[] valueLength = BitConverter.GetBytes(kvp.Value.Length);
                    ms.Write(valueLength, 0, 4);

                    // Write value
                    ms.Write(kvp.Value, 0, kvp.Value.Length);
                }
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserialize byte array to registry dictionary
        /// </summary>
        private Dictionary<byte[], byte[]> DeserializeRegistry(byte[] data)
        {
            Dictionary<byte[], byte[]> registry = new Dictionary<byte[], byte[]>(new ByteArrayComparer());
            int offset = 0;

            while (offset < data.Length)
            {
                // Read key length
                ushort keyLength = BitConverter.ToUInt16(data, offset);
                offset += 2;

                // Read key
                byte[] key = new byte[keyLength];
                Array.Copy(data, offset, key, 0, keyLength);
                offset += keyLength;

                // Read value length
                uint valueLength = BitConverter.ToUInt32(data, offset);
                offset += 4;

                // Read value
                byte[] value = new byte[valueLength];
                Array.Copy(data, offset, value, 0, (int)valueLength);
                offset += (int)valueLength;

                registry[key] = value;
            }

            return registry;
        }

        /// <summary>
        /// Insert subscription
        /// </summary>
        public void InsertSubscription(string username, uint subscriptionId, uint receiptId, string ipAddress, object paymentData, string pktVersion)
        {
            logger.LogFunctionEntry("AuthDatabase.InsertSubscription", username, subscriptionId);
            
            try
            {
                uint accountId = GetAccountId(username);
                
                string query = @"
                    INSERT INTO subscriptions (account_id, subscription_id, receipt_id)
                    VALUES (?, ?, ?)";
                
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("account_id", accountId),
                    new OleDbParameter("subscription_id", subscriptionId),
                    new OleDbParameter("receipt_id", receiptId)
                };
                
                connection.ExecuteNonQuery(query, parameters);
                logger.Info("Subscription inserted for user {0}: {1}", username, subscriptionId);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.InsertSubscription", ex, "Username: " + username);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.InsertSubscription");
            }
        }

        /// <summary>
        /// Update subscription status flags
        /// </summary>
        public void UpdateSubscriptionStatusFlags(string username)
        {
            logger.LogFunctionEntry("AuthDatabase.UpdateSubscriptionStatusFlags", username);
            
            try
            {
                // This would update subscription status flags in the user registry
                // For now, just log the operation
                logger.Debug("Subscription status flags updated for user {0}", username);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.UpdateSubscriptionStatusFlags", ex, "Username: " + username);
            }
            finally
            {
                logger.LogFunctionExit("AuthDatabase.UpdateSubscriptionStatusFlags");
            }
        }

        /// <summary>
        /// Update last login time
        /// </summary>
        private void UpdateLastLogin(string username)
        {
            try
            {
                string query = "UPDATE users SET last_login = NOW() WHERE username = ?";
                OleDbParameter[] parameters = new OleDbParameter[]
                {
                    new OleDbParameter("username", username)
                };
                
                connection.ExecuteNonQuery(query, parameters);
            }
            catch (Exception ex)
            {
                logger.LogException("AuthDatabase.UpdateLastLogin", ex, "Username: " + username);
            }
        }

        /// <summary>
        /// Generate suggested usernames
        /// </summary>
        public string[] GenerateSuggestedNames(int count)
        {
            logger.LogFunctionEntry("AuthDatabase.GenerateSuggestedNames", count);
            
            string[] names = new string[count];
            Random random = new Random();
            string[] prefixes = new string[] { "cool", "awesome", "super", "mega", "ultra", "hyper", "cyber", "digital", "virtual", "retro" };
            string[] suffixes = new string[] { "gamer", "player", "user", "master", "ninja", "warrior", "hero", "legend", "champion", "star" };

            for (int i = 0; i < count; i++)
            {
                string prefix = prefixes[random.Next(prefixes.Length)];
                string suffix = suffixes[random.Next(suffixes.Length)];
                int number = random.Next(1000, 9999);
                names[i] = prefix + suffix + number.ToString();
            }

            logger.LogFunctionExit("AuthDatabase.GenerateSuggestedNames", names.Length);
            return names;
        }

        /// <summary>
        /// Dispose of database resources
        /// </summary>
        public void Dispose()
        {
            if (connection != null)
            {
                connection.Dispose();
                connection = null;
            }
        }
    }
}