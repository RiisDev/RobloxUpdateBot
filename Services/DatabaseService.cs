using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace RobloxUpdateBot.Services
{
    [SuppressMessage("ReSharper", "InvertIf")]
    public class DatabaseService
    {
        private const string DbFolder = "data";
        private const string DbFileName = "botdata.db";
        private static readonly string DbPath = Path.Combine(DbFolder, DbFileName);
        private static readonly string ConnectionString = $"Data Source={DbPath}";
        private static readonly SqliteConnection SharedConnection = new (ConnectionString);

        public DatabaseService()
        {
            Directory.CreateDirectory(DbFolder);
            SharedConnection.Open();
            Initialize();
        }


        private void ExecuteNonQuery(string sql, Dictionary<string, object>? commands = null)
        {
            using SqliteCommand command = SharedConnection.CreateCommand();

            command.CommandText = sql;

            if (commands is not null)
                foreach (KeyValuePair<string, object> commandPair in commands) 
                    command.Parameters.AddWithValue(commandPair.Key, commandPair.Value);

            command.ExecuteNonQuery();
        }

        private object? ExecuteScalar(string sql, Dictionary<string, object>? commands = null)
        {
            using SqliteCommand command = SharedConnection.CreateCommand();
            command.CommandText = sql;

            if (commands is not null)
                foreach (KeyValuePair<string, object> commandPair in commands)
                    command.Parameters.AddWithValue(commandPair.Key, commandPair.Value);

            return command.ExecuteScalar();
        }

        private void Initialize() => ExecuteNonQuery("""
                                                      CREATE TABLE IF NOT EXISTS Channel (
                                                          ChannelId INTEGER PRIMARY KEY,
                                                          ChannelUpdatedTrueText TEXT NOT NULL,
                                                          ChannelUpdatedFalseText TEXT NOT NULL
                                                      );
                                                      CREATE TABLE IF NOT EXISTS Status (
                                                          Client TEXT PRIMARY KEY,
                                                          Version TEXT NOT NULL,
                                                          ChannelId INTEGER NOT NULL,
                                                          Updated INTEGER NOT NULL,
                                                          FOREIGN KEY(ChannelId) REFERENCES Channel(ChannelId)
                                                      );
                                                      CREATE TABLE IF NOT EXISTS History (
                                                          Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                                          Client TEXT NOT NULL,
                                                          Version TEXT NOT NULL,
                                                          Date TEXT NOT NULL
                                                      );
                                                      CREATE TABLE IF NOT EXISTS VerifiedUsers (
                                                          DiscordId INTEGER PRIMARY KEY
                                                      );
                                                      CREATE TABLE IF NOT EXISTS VerifiedRoles (
                                                          RoleId INTEGER PRIMARY KEY
                                                      );
                                                      CREATE TABLE IF NOT EXISTS LogChannel (
                                                          ChannelId INTEGER PRIMARY KEY
                                                      );
                                                      """);

        public void SetLog(ulong id)
        {
            ExecuteNonQuery("INSERT INTO LogChannel (ChannelId) VALUES (@channel) ON CONFLICT(ChannelId) DO NOTHING", new Dictionary<string, object>{{"@channel", id}});
        }

        public ulong GetLog()
        {
            object? result = ExecuteScalar("SELECT ChannelId FROM LogChannel");
            return Convert.ToUInt64(result ?? 0);
        }

        public void UpdateChannel(Channel channel)
        {
            ExecuteNonQuery("""
                            INSERT INTO Channel (ChannelId, ChannelUpdatedTrueText, ChannelUpdatedFalseText)
                            VALUES (@id, @trueText, @falseText)
                            ON CONFLICT(ChannelId) DO UPDATE SET 
                            ChannelUpdatedTrueText = excluded.ChannelUpdatedTrueText, 
                            ChannelUpdatedFalseText = excluded.ChannelUpdatedFalseText;
                            """, new Dictionary<string, object> {
                                {"@id", channel.ChannelId},
                                {"@trueText", channel.ChannelUpdatedTrueText},
                                {"@falseText", channel.ChannelUpdatedFalseText},
                            });
        }

        public Channel GetChannel(ulong channelId)
        {
            Channel channel = null!;

            SqliteCommand command = SharedConnection.CreateCommand();
            command.CommandText = """
                                  SELECT ChannelId, ChannelUpdatedTrueText, ChannelUpdatedFalseText
                                  FROM Channel
                                  WHERE ChannelId = @id
                                  """;
            command.Parameters.AddWithValue("@id", channelId);
            using SqliteDataReader reader = command.ExecuteReader();
            
            if (reader.Read())
            {
                channel = new Channel
                (
                    ChannelId: Convert.ToUInt64(reader["ChannelId"]),
                    ChannelUpdatedTrueText: reader["ChannelUpdatedTrueText"].ToString() ?? "",
                    ChannelUpdatedFalseText: reader["ChannelUpdatedFalseText"].ToString() ?? ""
                );
            }

            return channel;
        }


        public void UpdateStatus(Status status)
        {
            ExecuteNonQuery("""
                            INSERT INTO Status (Client, Version, ChannelId, Updated)
                            VALUES (@client, @version, @channelId, @updated)
                            ON CONFLICT(Client) DO UPDATE SET
                                Version = excluded.Version,
                                ChannelId = excluded.ChannelId,
                                Updated = excluded.Updated;
                            """, new Dictionary<string, object> {
                            {"@client", status.Client},
                            {"@version", status.Version},
                            {"@channelId", status.ChannelId},
                            {"@updated", status.Updated ? 1 : 0}
                            });
        }

        public Status GetStatus(string clientName)
        {
            Status status = null!;
            using SqliteCommand cmd = SharedConnection.CreateCommand();
            cmd.CommandText = "SELECT Client, Version, ChannelId, Updated FROM Status WHERE Client = @client";
            cmd.Parameters.AddWithValue("@client", clientName);

            using SqliteDataReader reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                string client = reader.GetString(0);
                string version = reader.GetString(1);
                ulong channelId = (ulong)(long)reader["ChannelId"];
                bool updated = Convert.ToBoolean((long)reader["Updated"]);

                status = new Status(client, version, channelId, updated);
            }

            return status;
        }
        
        public void AddHistory(History history)
        {
            ExecuteNonQuery("INSERT INTO History (Client, Version, Date) VALUES (@client, @version, @date)", new Dictionary<string, object>
            {
                {"@client", history.Client},
                {"@version", history.Version},
                {"@date", DateTime.UtcNow.ToString("o")}
            });
        }

        public void AddVerifiedUser(VerifiedUsers user)
        {
            ExecuteNonQuery("INSERT INTO VerifiedUsers (DiscordId) VALUES (@discordId) ON CONFLICT(discordId) DO NOTHING", new Dictionary<string, object>
            {
                {"@discordId", user.DiscordId}
            });
        }

        public void RemoveVerifiedUser(VerifiedUsers user)
        {
            ExecuteNonQuery("DELETE FROM VerifiedUsers WHERE DiscordId = @discordId", new Dictionary<string, object>
            {
                {"@discordId", user.DiscordId}
            });
        }

        public void AddVerifiedRole(VerifiedRoles role)
        {
            ExecuteNonQuery("INSERT INTO VerifiedRoles (RoleId) VALUES (@roleId) ON CONFLICT(roleId) DO NOTHING", new Dictionary<string, object>
            {
                {"@roleId", role.RoleId}
            });
        }

        public void RemoveVerifiedRole(VerifiedRoles role)
        {
            ExecuteNonQuery("DELETE FROM VerifiedRoles WHERE RoleId = @roleId", new Dictionary<string, object>
            {
                {"@roleId", role.RoleId}
            });
        }

        public bool IsVerifiedUser(ulong userId)
        {
            object? result = ExecuteScalar("SELECT 1 FROM VerifiedUsers WHERE DiscordId = @userId", new Dictionary<string, object>{{ "@userId", userId}});
            return Convert.ToInt32(result ?? -1) > 0;
        }

        public List<ulong> GetVerifiedRoles()
        {
            List<ulong> verifiedRoles = []; 
            using SqliteCommand cmd = SharedConnection.CreateCommand();
            using SqliteDataReader results = cmd.ExecuteReader();

            foreach (object? row in results)
                if (row is Dictionary<string, object> dict && dict["RoleId"] is ulong roleId)
                    verifiedRoles.Add(roleId);

            return verifiedRoles;
        }


        public List<History> GetHistory(string client)
        {
            SqliteCommand command = SharedConnection.CreateCommand();
            command.CommandText = "SELECT Version, Date FROM History WHERE Client = @client ORDER BY Date DESC";
            command.Parameters.AddWithValue("@client", client);
            using SqliteDataReader reader = command.ExecuteReader();
            List<History> historyList = [];
            while (reader.Read())
            {
                historyList.Add(new History(client, reader.GetString(0), DateTime.Parse(reader.GetString(1))));
            }
            return historyList;
        }
        
    }
}
