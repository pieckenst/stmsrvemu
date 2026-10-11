using System;
using System.Collections.Generic;
using System.Data;
using System.Data.OleDb;
using StmServerEmulator.Logging;

namespace StmServerEmulator.Database
{
    internal sealed class TrackerProfile
    {
        internal uint AccountId; internal string Email; internal string UserName; internal string FirstName; internal string LastName;
    }
    internal sealed class TrackerOfflineMessage
    {
        internal long MessageId; internal uint SenderId; internal string UserName; internal uint Status; internal string Text;
    }

    /// <summary>Persistent Friends/Tracker repository, using the same directional relationship model as the Python tracker.</summary>
    internal sealed class TrackerDatabase : IDisposable
    {
        private readonly MariaDBConnection connection;
        private readonly Logger logger;
        internal TrackerDatabase()
        {
            logger = Logger.GetLogger("TrackerDatabase"); logger.LogFunctionEntry("TrackerDatabase.ctor");
            try { connection = new MariaDBConnection(); connection.Open(); InitializeSchema(); }
            finally { logger.LogFunctionExit("TrackerDatabase.ctor"); }
        }
        private void InitializeSchema()
        {
            logger.LogFunctionEntry("TrackerDatabase.InitializeSchema");
            try
            {
                connection.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS friends_registry (accountID INT NOT NULL PRIMARY KEY, nickname VARCHAR(64) NOT NULL, first_name VARCHAR(64) NOT NULL, last_name VARCHAR(64) NOT NULL, last_login DATETIME, INDEX idx_friends_nickname (nickname))");
                connection.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS friends_friendslist (friendRegistryID INT NOT NULL, friendsaccountID INT NOT NULL, relationship TINYINT NOT NULL, created_at DATETIME NOT NULL, updated_at DATETIME NOT NULL, PRIMARY KEY (friendRegistryID, friendsaccountID), INDEX idx_friends_target (friendsaccountID))");
                connection.ExecuteNonQuery("CREATE TABLE IF NOT EXISTS tracker_offline_messages (message_id BIGINT AUTO_INCREMENT PRIMARY KEY, sender_id INT NOT NULL, target_id INT NOT NULL, user_name VARCHAR(64) NOT NULL, status INT NOT NULL, message_text MEDIUMTEXT NOT NULL, created_at DATETIME NOT NULL, delivered_at DATETIME NULL, INDEX idx_tracker_message_target (target_id, delivered_at))");
            }
            finally { logger.LogFunctionExit("TrackerDatabase.InitializeSchema"); }
        }
        internal TrackerProfile FindProfile(uint accountId)
        {
            logger.LogFunctionEntry("TrackerDatabase.FindProfile", accountId);
            try
            {
                DataTable table = connection.ExecuteQuery("SELECT u.account_id, u.email, f.nickname, f.first_name, f.last_name FROM users u INNER JOIN friends_registry f ON f.accountID=u.account_id WHERE u.account_id=?", P("account_id", accountId));
                return table.Rows.Count == 0 ? null : Profile(table.Rows[0]);
            }
            finally { logger.LogFunctionExit("TrackerDatabase.FindProfile"); }
        }
        internal TrackerProfile FindOrCreateProfile(uint accountId, string email, string userName)
        {
            logger.LogFunctionEntry("TrackerDatabase.FindOrCreateProfile", accountId, email);
            try
            {
                TrackerProfile profile = FindProfile(accountId); if (profile != null) return profile;
                DataTable users = connection.ExecuteQuery("SELECT account_id, email, username FROM users WHERE account_id=? OR email=? OR username=? ORDER BY account_id LIMIT 1", new OleDbParameter[] { new OleDbParameter("account_id", accountId), new OleDbParameter("email", email ?? String.Empty), new OleDbParameter("username", userName ?? String.Empty) });
                if (users.Rows.Count == 0) return null;
                DataRow row = users.Rows[0]; uint actualId = Convert.ToUInt32(row["account_id"]); string actualEmail = Convert.ToString(row["email"]); string name = String.IsNullOrEmpty(userName) ? Convert.ToString(row["username"]) : userName;
                connection.ExecuteNonQuery("INSERT INTO friends_registry (accountID,nickname,first_name,last_name,last_login) VALUES (?,?,?,?,NOW()) ON DUPLICATE KEY UPDATE last_login=NOW()", new OleDbParameter[] { new OleDbParameter("accountID", actualId), new OleDbParameter("nickname", name), new OleDbParameter("first_name", String.Empty), new OleDbParameter("last_name", String.Empty) });
                return FindProfile(actualId);
            }
            finally { logger.LogFunctionExit("TrackerDatabase.FindOrCreateProfile"); }
        }
        internal IList<TrackerProfile> SearchProfiles(uint excludeId)
        {
            logger.LogFunctionEntry("TrackerDatabase.SearchProfiles", excludeId); List<TrackerProfile> result = new List<TrackerProfile>();
            try { DataTable table = connection.ExecuteQuery("SELECT u.account_id,u.email,f.nickname,f.first_name,f.last_name FROM users u INNER JOIN friends_registry f ON f.accountID=u.account_id WHERE u.account_id<>? ORDER BY f.nickname", P("account_id", excludeId)); foreach (DataRow row in table.Rows) result.Add(Profile(row)); return result; }
            finally { logger.LogFunctionExit("TrackerDatabase.SearchProfiles", result.Count); }
        }
        internal IList<uint> GetFriends(uint accountId)
        {
            logger.LogFunctionEntry("TrackerDatabase.GetFriends", accountId); List<uint> result = new List<uint>();
            try { DataTable table = connection.ExecuteQuery("SELECT friendsaccountID FROM friends_friendslist WHERE friendRegistryID=? AND relationship=3", P("account_id", accountId)); foreach(DataRow row in table.Rows) result.Add(Convert.ToUInt32(row[0])); return result; }
            finally { logger.LogFunctionExit("TrackerDatabase.GetFriends", result.Count); }
        }
        internal IList<uint> GetPending(uint accountId)
        {
            logger.LogFunctionEntry("TrackerDatabase.GetPending", accountId); List<uint> result = new List<uint>();
            try { DataTable table = connection.ExecuteQuery("SELECT friendsaccountID FROM friends_friendslist WHERE friendRegistryID=? AND relationship=2", P("account_id", accountId)); foreach(DataRow row in table.Rows) result.Add(Convert.ToUInt32(row[0])); return result; }
            finally { logger.LogFunctionExit("TrackerDatabase.GetPending", result.Count); }
        }
        internal bool RequestFriend(uint source, uint target)
        {
            logger.LogFunctionEntry("TrackerDatabase.RequestFriend", source, target);
            try { if (source == target || FindProfile(target) == null) return false; connection.ExecuteNonQuery("INSERT INTO friends_friendslist (friendRegistryID,friendsaccountID,relationship,created_at,updated_at) VALUES (?,?,4,NOW(),NOW()) ON DUPLICATE KEY UPDATE relationship=4,updated_at=NOW()", P2(source,target)); connection.ExecuteNonQuery("INSERT INTO friends_friendslist (friendRegistryID,friendsaccountID,relationship,created_at,updated_at) VALUES (?,?,2,NOW(),NOW()) ON DUPLICATE KEY UPDATE relationship=2,updated_at=NOW()", P2(target,source)); return true; }
            finally { logger.LogFunctionExit("TrackerDatabase.RequestFriend"); }
        }
        internal bool ResolveFriend(uint accountId, uint friendId, bool accepted)
        {
            logger.LogFunctionEntry("TrackerDatabase.ResolveFriend", accountId, friendId, accepted);
            try { if (accountId == friendId) return false; if (!accepted) { connection.ExecuteNonQuery("DELETE FROM friends_friendslist WHERE (friendRegistryID=? AND friendsaccountID=?) OR (friendRegistryID=? AND friendsaccountID=?)", new OleDbParameter[] { new OleDbParameter("a",accountId),new OleDbParameter("b",friendId),new OleDbParameter("c",friendId),new OleDbParameter("d",accountId) }); return true; } int a=connection.ExecuteNonQuery("UPDATE friends_friendslist SET relationship=3,updated_at=NOW() WHERE friendRegistryID=? AND friendsaccountID=?",P2(accountId,friendId)); int b=connection.ExecuteNonQuery("UPDATE friends_friendslist SET relationship=3,updated_at=NOW() WHERE friendRegistryID=? AND friendsaccountID=?",P2(friendId,accountId)); return a==1 && b==1; }
            finally { logger.LogFunctionExit("TrackerDatabase.ResolveFriend"); }
        }
        internal void UpdateProfile(uint accountId, string name, string firstName, string lastName) { logger.LogFunctionEntry("TrackerDatabase.UpdateProfile",accountId); try { connection.ExecuteNonQuery("UPDATE friends_registry SET nickname=?,first_name=?,last_name=? WHERE accountID=?",new OleDbParameter[] {new OleDbParameter("name",name),new OleDbParameter("first",firstName),new OleDbParameter("last",lastName),new OleDbParameter("id",accountId)}); } finally { logger.LogFunctionExit("TrackerDatabase.UpdateProfile"); } }
        internal void StoreOfflineMessage(uint sender, uint target, string userName, uint status, string text) { logger.LogFunctionEntry("TrackerDatabase.StoreOfflineMessage",sender,target); try { connection.ExecuteNonQuery("INSERT INTO tracker_offline_messages (sender_id,target_id,user_name,status,message_text,created_at) VALUES (?,?,?,?,?,NOW())",new OleDbParameter[] {new OleDbParameter("sender",sender),new OleDbParameter("target",target),new OleDbParameter("name",userName),new OleDbParameter("status",status),new OleDbParameter("text",text)}); } finally { logger.LogFunctionExit("TrackerDatabase.StoreOfflineMessage"); } }
        internal IList<TrackerOfflineMessage> GetUndeliveredMessages(uint target)
        {
            logger.LogFunctionEntry("TrackerDatabase.GetUndeliveredMessages", target); List<TrackerOfflineMessage> result = new List<TrackerOfflineMessage>();
            try { DataTable table=connection.ExecuteQuery("SELECT message_id,sender_id,user_name,status,message_text FROM tracker_offline_messages WHERE target_id=? AND delivered_at IS NULL ORDER BY message_id",P("target",target)); foreach(DataRow row in table.Rows) { TrackerOfflineMessage message=new TrackerOfflineMessage(); message.MessageId=Convert.ToInt64(row["message_id"]); message.SenderId=Convert.ToUInt32(row["sender_id"]); message.UserName=Convert.ToString(row["user_name"]); message.Status=Convert.ToUInt32(row["status"]); message.Text=Convert.ToString(row["message_text"]); result.Add(message); } return result; }
            finally { logger.LogFunctionExit("TrackerDatabase.GetUndeliveredMessages", result.Count); }
        }
        internal void MarkMessageDelivered(long messageId) { logger.LogFunctionEntry("TrackerDatabase.MarkMessageDelivered",messageId); try { connection.ExecuteNonQuery("UPDATE tracker_offline_messages SET delivered_at=NOW() WHERE message_id=? AND delivered_at IS NULL",P("message_id",messageId)); } finally { logger.LogFunctionExit("TrackerDatabase.MarkMessageDelivered"); } }
        private static TrackerProfile Profile(DataRow row) { TrackerProfile profile = new TrackerProfile(); profile.AccountId=Convert.ToUInt32(row["account_id"]); profile.Email=Convert.ToString(row["email"]); profile.UserName=Convert.ToString(row["nickname"]); profile.FirstName=Convert.ToString(row["first_name"]); profile.LastName=Convert.ToString(row["last_name"]); return profile; }
        private static OleDbParameter[] P(string name, object value) { return new OleDbParameter[] { new OleDbParameter(name,value) }; }
        private static OleDbParameter[] P2(uint first,uint second) { return new OleDbParameter[] { new OleDbParameter("first",first),new OleDbParameter("second",second) }; }
        public void Dispose() { logger.LogFunctionEntry("TrackerDatabase.Dispose"); try { connection.Dispose(); } finally { logger.LogFunctionExit("TrackerDatabase.Dispose"); } }
    }
}
