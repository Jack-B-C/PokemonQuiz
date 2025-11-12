using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using PokemonQuizAPI.Models;
using System.Data;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading;

namespace PokemonQuizAPI.Data
{
    /// <summary>
    /// Helper that encapsulates database operations for the API. This version
    /// </summary>
    public partial class DatabaseHelper
    {
                private readonly string _connectionString;
        private readonly ILogger<DatabaseHelper> _logger;

        /// <summary>
        /// Construct the helper and verify a MySQL connection is available.
        /// This constructor throws if no connection string is provided or the DB is unreachable.
        /// </summary>
        public DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("PokemonQuizDB") ?? throw new InvalidOperationException("Connection string 'PokemonQuizDB' is not configured.");

            // If a connection string is present, expose a small summary (do not log password)
            try
            {
                var builder = new MySqlConnectionStringBuilder(_connectionString);
                _logger.LogInformation("Resolved MySQL config - Server={Server}, Port={Port}, Database={Database}, UserId={User}", builder.Server, builder.Port, builder.Database, builder.UserID);
            }
            catch
            {
                _logger.LogInformation("MySQL connection string present but could not be parsed for debug output.");
            }

            // Verify connectivity now and fail fast if DB is not reachable
            try
            {
                using var conn = new MySqlConnection(_connectionString);
                conn.Open();
                conn.Close();
                _logger.LogInformation("Successfully connected to MySQL; using database storage.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MySQL using provided connection string. Ensure the database is available and the connection string is correct.");
                throw new InvalidOperationException("Failed to connect to MySQL using provided connection string.", ex);
            }
        }

        /// <summary>
        /// Open and return a MySQL connection using the configured connection string.
        /// </summary>
        private async Task<MySqlConnection> GetConnectionAsync(CancellationToken ct = default)
        {
            var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            return conn;
        }

        /// <summary>
        /// Small helper to centralize opening a connection and executing a DB action.
        /// Reduces duplicated try/using boilerplate across the class.
        /// </summary>
        private async Task<T> WithConnectionAsync<T>(Func<MySqlConnection, CancellationToken, Task<T>> action, CancellationToken ct = default)
        {
            try
            {
                await using var conn = await GetConnectionAsync(ct);
                return await action(conn, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database operation failed");
                throw;
            }
        }

        // Back-compat overload for callers that still pass Func<MySqlConnection, Task<T>>
        private async Task<T> WithConnectionAsync<T>(Func<MySqlConnection, Task<T>> action)
        {
            return await WithConnectionAsync(async (conn, ct) => await action(conn), CancellationToken.None);
        }

       
        
        private static PokemonData MapPokemonData(MySqlDataReader reader)
        {
            // Read id safely: database may return Guid or string
            var idOrdinal = reader.GetOrdinal("id");
            string id;
            if (reader.IsDBNull(idOrdinal))
            {
                id = string.Empty;
            }
            else
            {
                var idVal = reader.GetValue(idOrdinal);
                id = idVal?.ToString() ?? string.Empty;
            }

            return new PokemonData
            {
                Id = id,
                Name = reader.IsDBNull(reader.GetOrdinal("name")) ? string.Empty : reader.GetString("name"),
                Type1 = reader.IsDBNull(reader.GetOrdinal("type1")) ? null : reader.GetString("type1"),
                Type2 = reader.IsDBNull(reader.GetOrdinal("type2")) ? null : reader.GetString("type2"),
                Hp = reader.IsDBNull(reader.GetOrdinal("hp")) ? 0 : reader.GetInt32("hp"),
                Attack = reader.IsDBNull(reader.GetOrdinal("attack")) ? 0 : reader.GetInt32("attack"),
                Defence = reader.IsDBNull(reader.GetOrdinal("defence")) ? 0 : reader.GetInt32("defence"),
                SpecialAttack = reader.IsDBNull(reader.GetOrdinal("special_attack")) ? 0 : reader.GetInt32("special_attack"),
                SpecialDefence = reader.IsDBNull(reader.GetOrdinal("special_defence")) ? 0 : reader.GetInt32("special_defence"),
                Speed = reader.IsDBNull(reader.GetOrdinal("speed")) ? 0 : reader.GetInt32("speed"),
                ImageUrl = reader.IsDBNull(reader.GetOrdinal("image_url")) ? null : reader.GetString("image_url"),
                FetchedAt = reader.IsDBNull(reader.GetOrdinal("fetched_at")) ? DateTime.MinValue : reader.GetDateTime("fetched_at")
            };
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                await using var conn = await GetConnectionAsync();
                return conn.State == ConnectionState.Open;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database connection test failed");
                return false;
            }
        }

        

        /// <summary>
        /// Ensure database tables exist by running sql/schema.sql when using MySQL.
        /// </summary>
        public async Task EnsureDatabaseInitializedAsync()
        {
            try
            {
                var scriptPath = Path.Combine(AppContext.BaseDirectory, "sql", "schema.sql");
                await using var conn = await GetConnectionAsync();

                if (File.Exists(scriptPath))
                {
                    try
                    {
                        var script = await File.ReadAllTextAsync(scriptPath);
                        var mysqlScript = new MySqlScript(conn, script);
                        mysqlScript.Execute();
                        _logger.LogInformation("Executed schema.sql to ensure DB tables exist");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to execute schema.sql; continuing with best-effort initialization");
                    }
                }
                else
                {
                    _logger.LogInformation("No schema.sql found at {Path}, continuing with best-effort initialization", scriptPath);
                }

                // Ensure Sessions table exists (used to store simple token -> user mapping)
                try
                {
                    var createSessions = @"
                        CREATE TABLE IF NOT EXISTS Sessions (
                            token VARCHAR(255) NOT NULL PRIMARY KEY,
                            user_id CHAR(36),
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            FOREIGN KEY (user_id) REFERENCES Users(id) ON DELETE CASCADE
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                    ";
                    await using var cmd = new MySqlCommand(createSessions, conn);
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Ensured Sessions table exists");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ensure Sessions table; session persistence may be unavailable");
                }

                // Ensure Games table exists and seed known game modes so FK from GameSessions can resolve
                try
                {
                    var createGames = @"
                        CREATE TABLE IF NOT EXISTS Games (
                            id VARCHAR(100) NOT NULL PRIMARY KEY,
                            name VARCHAR(255) NOT NULL,
                            description TEXT,
                            is_active TINYINT(1) DEFAULT 1,
                            mode VARCHAR(50),
                            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                            last_updated DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                    ";
                    await using var gcmd = new MySqlCommand(createGames, conn);
                    await gcmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Ensured Games table exists");

                    // Seed common game modes idempotently
                    var seedSql = @"
                        INSERT INTO Games (id, name, description, is_active, mode, created_at, last_updated)
                        VALUES
                          ('guess-stats', 'Guess Stats', 'Guess which stat value belongs to the Pokémon (multiplayer-ready).', 1, 'guess-stats', @now, @now),
                          ('who-that-pokemon', 'Who''s That Pokémon?', 'Guess the Pokémon from its silhouette.', 1, 'silhouette', @now, @now),
                          ('guess-type', 'Guess Type', 'Guess the Pokémon type.', 1, 'guess-type', @now, @now),
                          ('higher-or-lower', 'Higher or Lower', 'Compare two Pokémon and guess which has the higher stat.', 1, 'compare-stat', @now, @now),
                          ('compare-stat', 'Compare Stat', 'Compare two Pokémon stats (alias for higher-or-lower).', 1, 'compare-stat', @now, @now)
                        ON DUPLICATE KEY UPDATE name = VALUES(name), description = VALUES(description), is_active = VALUES(is_active), mode = VALUES(mode), last_updated = VALUES(last_updated);
                    ";

                    await using var seedCmd = new MySqlCommand(seedSql, conn);
                    seedCmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    await seedCmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Seeded Games table with default game modes (including higher-or-lower)");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to ensure or seed Games table; game metadata may be missing");
                }

            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run EnsureDatabaseInitializedAsync; you may need to run SQL schema manually");
            }
        }

        // GameSessions persistence
        public async Task CreateGameSessionAsync(string sessionId, string? gameId, string? userId, string? roomId, int score, DateTime startTime)
        {
            await WithConnectionAsync(async conn =>
            {
                // If a gameId was provided, verify it exists in Games table; if not, store NULL to avoid FK errors.
                object gameIdParam = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(gameId))
                {
                    try
                    {
                        await using var chk = new MySqlCommand("SELECT COUNT(*) FROM Games WHERE id = @id LIMIT 1", conn);
                        chk.Parameters.AddWithValue("@id", gameId);
                        var cntObj = await chk.ExecuteScalarAsync();
                        var cnt = cntObj == null || cntObj == DBNull.Value ? 0 : Convert.ToInt32(cntObj);
                        if (cnt > 0)
                        {
                            gameIdParam = gameId;
                        }
                        else
                        {
                            _logger.LogInformation("CreateGameSession: supplied gameId {GameId} not found in Games table; storing NULL to avoid FK constraint", gameId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to verify game id existence for {GameId}; will store NULL to avoid FK constraint", gameId);
                        gameIdParam = DBNull.Value;
                    }
                }

                // If roomId was provided, verify it exists in GameRoom table; if not, use NULL
                object roomIdParam = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(roomId))
                {
                    try
                    {
                        await using var chkRoom = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE id = @id LIMIT 1", conn);
                        chkRoom.Parameters.AddWithValue("@id", roomId);
                        var roomCntObj = await chkRoom.ExecuteScalarAsync();
                        var roomCnt = roomCntObj == null || roomCntObj == DBNull.Value ? 0 : Convert.ToInt32(roomCntObj);
                        if (roomCnt > 0)
                        {
                            roomIdParam = roomId;
                        }
                        else
                        {
                            _logger.LogInformation("CreateGameSession: supplied roomId {RoomId} not found in GameRoom table; storing NULL to avoid FK constraint", roomId);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to verify room id existence for {RoomId}; will store NULL to avoid FK constraint", roomId);
                        roomIdParam = DBNull.Value;
                    }
                }

                await using var cmd = new MySqlCommand("INSERT INTO GameSessions (id, game_id, user_id, room_id, score, start_time) VALUES (@id, @gameId, @userId, @roomId, @score, @start)", conn);
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.Parameters.AddWithValue("@gameId", gameIdParam);
                cmd.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@roomId", roomIdParam);
                cmd.Parameters.AddWithValue("@score", score);
                cmd.Parameters.AddWithValue("@start", startTime);
                await cmd.ExecuteNonQueryAsync();

                // Log session creation with user association
                try
                {
                    if (!string.IsNullOrWhiteSpace(userId)) _logger.LogInformation("Created GameSession {SessionId} for user {UserId} (game={GameId}, room={RoomId})", sessionId, userId, gameId, roomId);
                    else _logger.LogInformation("Created anonymous GameSession {SessionId} (game={GameId}, room={RoomId})", sessionId, gameId, roomId);
                }
                catch { }

                return true;
            });
        }

        public async Task UpdateGameSessionScoreAsync(string sessionId, int newScore)
        {
            await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameSessions SET score = @score WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@score", newScore);
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();
                return true;
            });
        }

        public async Task EndGameSessionAsync(string sessionId, DateTime endTime)
        {
            await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameSessions SET end_time = @end WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@end", endTime);
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();

                // Log session end; try to include the user_id if available
                try
                {
                    await using var q = new MySqlCommand("SELECT user_id, game_id, score FROM GameSessions WHERE id = @id LIMIT 1", conn);
                    q.Parameters.AddWithValue("@id", sessionId);
                    await using var reader = (MySqlDataReader)await q.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var uid = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : reader.GetValue(reader.GetOrdinal("user_id"))?.ToString();
                        var gid = reader.IsDBNull(reader.GetOrdinal("game_id")) ? null : reader.GetString("game_id");
                        var score = reader.IsDBNull(reader.GetOrdinal("score")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("score")));
                        if (!string.IsNullOrWhiteSpace(uid)) _logger.LogInformation("Ended GameSession {SessionId} for user {UserId} (game={GameId}, score={Score})", sessionId, uid, gid, score);
                        else _logger.LogInformation("Ended anonymous GameSession {SessionId} (game={GameId}, score={Score})", sessionId, gid, score);
                    }
                }
                catch { }

                return true;
            });
        }

        // Admin helper: set role for a user
        public async Task<bool> SetUserRoleAsync(string userId, string role)
        {
            if (string.IsNullOrWhiteSpace(userId)) return false;
            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("UPDATE Users SET role = @role, last_updated = @now WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@role", role ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@id", userId);
                var rows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Set role for user {UserId} to {Role} (rows={Rows})", userId, role, rows);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SetUserRoleAsync failed for {UserId}", userId);
                return false;
            }
        }

        // UserQuestions persistence
        public async Task InsertUserQuestionAsync(string id, string gameSessionId, string? questionId, string userAnswerJson, bool isCorrect)
        {
            await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand(@"INSERT INTO UserQuestions (id, game_session_id, question_id, user_answer, is_correct) VALUES (@id, @sessionId, @qId, @answer, @isCorrect)", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@sessionId", gameSessionId);
                cmd.Parameters.AddWithValue("@qId", questionId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@answer", userAnswerJson);
                cmd.Parameters.AddWithValue("@isCorrect", isCorrect ? 1 : 0);
                await cmd.ExecuteNonQueryAsync();
                return true;
            });
        }

        // Leaderboard/upsert
        public async Task UpsertLeaderboardAsync(string? userId, string? gameId, int scoreDelta)
        {
            if (string.IsNullOrWhiteSpace(userId)) return; // can't attribute

            var userIdNonNull = userId!;

            try
            {
                await using var conn = await GetConnectionAsync();
                // Try update
                await using var upd = new MySqlCommand("UPDATE Leaderboard SET total_score = total_score + @delta, total_games = total_games + 1, last_updated = @now WHERE user_id = @userId AND game_id = @gameId", conn);
                upd.Parameters.AddWithValue("@delta", scoreDelta);
                upd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                upd.Parameters.AddWithValue("@userId", userIdNonNull);
                upd.Parameters.AddWithValue("@gameId", gameId ?? (object)DBNull.Value);
                var rows = await upd.ExecuteNonQueryAsync();
                if (rows == 0)
                {
                    await using var ins = new MySqlCommand("INSERT INTO Leaderboard (id, user_id, game_id, total_score, total_games, created_at) VALUES (@id, @userId, @gameId, @score, 1, @now)", conn);
                    ins.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    ins.Parameters.AddWithValue("@userId", userIdNonNull);
                    ins.Parameters.AddWithValue("@gameId", gameId ?? (object)DBNull.Value);
                    ins.Parameters.AddWithValue("@score", scoreDelta);
                    ins.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    await ins.ExecuteNonQueryAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to upsert leaderboard for user {UserId}", userId);
            }
        }

        // Simple counts for admin
        public async Task<int> GetPokemonCountAsyncSafe()
        {
            return await GetPokemonCountAsync();
        }

        
        public async Task<int> GetTotalGamesPlayedAsync()
        {
            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameSessions", conn);
                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count total game sessions");
                return 0;
            }
        }

        public record GameStats(string GameId, int GamesPlayed, int TotalQuestions, int CorrectAnswers, double Accuracy, double AverageScore);

        /// <summary>
        /// Returns statistics per game: games played, total questions answered, correct answers, accuracy (0-1), average score.
        /// </summary>
        public async Task<List<GameStats>> GetGameStatsAsync()
        {
            try
            {
                await using var conn = await GetConnectionAsync();

                var stats = new Dictionary<string, GameStats>();

                // Query games played and average score
                await using (var cmd = new MySqlCommand(@"SELECT IFNULL(game_id, '') AS game_id, COUNT(*) AS games_played, AVG(score) AS avg_score FROM GameSessions GROUP BY game_id", conn))
                await using (var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var gid = reader.IsDBNull(reader.GetOrdinal("game_id")) ? string.Empty : reader.GetString("game_id");
                        var played = reader.IsDBNull(reader.GetOrdinal("games_played")) ? 0 : reader.GetInt32("games_played");
                        double avg = 0;
                        try { avg = reader.IsDBNull(reader.GetOrdinal("avg_score")) ? 0.0 : reader.GetDouble("avg_score"); } catch { avg = 0.0; }

                        stats[gid] = new GameStats(gid, played, 0, 0, 0.0, avg);
                    }
                }

                // Query question totals and correct answers by joining UserQuestions -> GameSessions
                await using (var qcmd = new MySqlCommand(@"SELECT gs.game_id AS game_id, SUM(uq.is_correct) AS correct_answers, COUNT(uq.id) AS total_questions FROM UserQuestions uq JOIN GameSessions gs ON uq.game_session_id = gs.id GROUP BY gs.game_id", conn))
                await using (var qreader = (MySqlDataReader)await qcmd.ExecuteReaderAsync())
                {
                    while (await qreader.ReadAsync())
                    {
                        var gid = qreader.IsDBNull(qreader.GetOrdinal("game_id")) ? string.Empty : qreader.GetString("game_id");
                        var correct = qreader.IsDBNull(qreader.GetOrdinal("correct_answers")) ? 0 : Convert.ToInt32(qreader.GetValue(qreader.GetOrdinal("correct_answers")));
                        var total = qreader.IsDBNull(qreader.GetOrdinal("total_questions")) ? 0 : qreader.GetInt32("total_questions");

                        if (stats.TryGetValue(gid, out var s))
                        {
                            var acc = total > 0 ? (double)correct / total : 0.0;
                            stats[gid] = new GameStats(gid, s.GamesPlayed, total, correct, acc, s.AverageScore);
                        }
                        else
                        {
                            var acc = total > 0 ? (double)correct / total : 0.0;
                            stats[gid] = new GameStats(gid, 0, total, correct, acc, 0.0);
                        }
                    }
                }

                return stats.Values.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute game stats");
                return new List<GameStats>();
            }
        }

        private static string HashPassword(string password)
        {
            // PBKDF2 with 100000 iterations, 16 byte salt, 32 byte subkey
            var salt = RandomNumberGenerator.GetBytes(16);
            using var derive = new Rfc2898DeriveBytes(password, salt, 100_000, HashAlgorithmName.SHA256);
            var subkey = derive.GetBytes(32);
            var outputBytes = new byte[1 + salt.Length + subkey.Length];
            outputBytes[0] = 0x01; // format marker
            Buffer.BlockCopy(salt, 0, outputBytes, 1, salt.Length);
            Buffer.BlockCopy(subkey, 0, outputBytes, 1 + salt.Length, subkey.Length);
            return Convert.ToBase64String(outputBytes);
        }

        private static bool VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            try
            {
                var src = Convert.FromBase64String(hashedPassword);
                if (src.Length != 1 + 16 + 32 || src[0] != 0x01) return false;
                var salt = new byte[16];
                Buffer.BlockCopy(src, 1, salt, 0, 16);
                var storedSubkey = new byte[32];
                Buffer.BlockCopy(src, 1 + 16, storedSubkey, 0, 32);
                using var derive = new Rfc2898DeriveBytes(providedPassword, salt, 100_000, HashAlgorithmName.SHA256);
                var generated = derive.GetBytes(32);
                return CryptographicOperations.FixedTimeEquals(storedSubkey, generated);
            }
            catch
            {
                return false;
            }
        }

        public async Task<(bool Success, string UserId, string? Error)> CreateUserAsync(string username, string? email, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return (false, string.Empty, "Invalid input");
            username = username.Trim();
            email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

            try
            {
                await using var conn = await GetConnectionAsync();

                // Check for existing username or email first
                try
                {
                    await using var chk = new MySqlCommand("SELECT COUNT(*) FROM Users WHERE username = @username OR (email IS NOT NULL AND LOWER(email) = LOWER(@email))", conn);
                    chk.Parameters.AddWithValue("@username", username);
                    chk.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                    var cnt = Convert.ToInt32(await chk.ExecuteScalarAsync());
                    if (cnt > 0)
                    {
                        // Determine whether username or email caused the conflict
                        await using var chkUser = new MySqlCommand("SELECT COUNT(*) FROM Users WHERE username = @username", conn);
                        chkUser.Parameters.AddWithValue("@username", username);
                        var userCnt = Convert.ToInt32(await chkUser.ExecuteScalarAsync());
                        if (userCnt > 0) return (false, string.Empty, "Username already exists");

                        if (!string.IsNullOrWhiteSpace(email)) return (false, string.Empty, "Email already registered");

                        return (false, string.Empty, "User already exists");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check existing users in DB; will attempt insert and handle duplicate errors");
                }

                // Try to insert into Users table
                try
                {
                    await using var cmd = new MySqlCommand(@"INSERT INTO Users (id, username, email, password_hash, created_at) VALUES (@id, @username, @email, @hash, @now)", conn);
                    var id = Guid.NewGuid().ToString();
                    cmd.Parameters.AddWithValue("@id", id);
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.Parameters.AddWithValue("@email", email ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@hash", HashPassword(password));
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Created user {Username} with id {UserId} in database", username, id);
                    return (true, id, null);
                }
                catch (MySqlException mex)
                {
                    _logger.LogWarning(mex, "Failed to insert user into DB");
                    if (mex.Number == 1062) // duplicate entry
                    {
                        var msg = mex.Message ?? string.Empty;
                        if (msg.Contains("email", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty, "Email already registered");
                        if (msg.Contains("username", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty, "Username already exists");
                        return (false, string.Empty, "User already exists");
                    }

                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CreateUser failed");
                return (false, string.Empty, "Server error");
            }
        }

        public async Task<string?> AuthenticateUserAsync(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return null;
            username = username.Trim();

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand(@"SELECT id, password_hash FROM Users WHERE username = @username LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@username", username);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    // id column may be stored as CHAR(36) or as native GUID type; read as object then ToString()
                    var idOrdinal = reader.GetOrdinal("id");
                    string id;
                    try
                    {
                        var idVal = reader.GetValue(idOrdinal);
                        id = idVal?.ToString() ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "AuthenticateUser: failed to read id column for user {Username}", username);
                        id = string.Empty;
                    }

                    var hash = reader.IsDBNull(reader.GetOrdinal("password_hash")) ? string.Empty : reader.GetString("password_hash");
                    _logger.LogInformation("Authenticate: found user {Username} in DB with hash length {Len}", username, hash?.Length ?? 0);
                    if (string.IsNullOrEmpty(hash)) { _logger.LogInformation("Authenticate: no password hash stored for user {Username}", username); return null; }
                    var ok = VerifyHashedPassword(hash, password);
                    _logger.LogInformation("Authenticate: password verification for user {Username} in DB returned {Result}", username, ok);
                    if (!ok) return null;
                    return id;
                }
                else
                {
                    _logger.LogInformation("Authenticate: user {Username} not found in DB", username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AuthenticateUser failed");
            }

            return null;
        }

        // Development helper: return a list of users (id, username, email, hasPasswordHash)
        public async Task<List<object>> GetAllUsersDebugAsync()
        {
            return await WithConnectionAsync(async conn =>
            {
                var list = new List<object>();
                await using var cmd = new MySqlCommand(@"SELECT id, username, email, password_hash FROM Users", conn);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    // Read id robustly: DB may store as CHAR(36) or native GUID type
                    var idOrdinal = reader.GetOrdinal("id");
                    string id;
                    if (reader.IsDBNull(idOrdinal))
                    {
                        id = string.Empty;
                    }
                    else
                    {
                        var idVal = reader.GetValue(idOrdinal);
                        id = idVal?.ToString() ?? string.Empty;
                    }

                    var uname = reader.IsDBNull(reader.GetOrdinal("username")) ? string.Empty : reader.GetString("username");
                    var email = reader.IsDBNull(reader.GetOrdinal("email")) ? string.Empty : reader.GetString("email");
                    var hashLen = reader.IsDBNull(reader.GetOrdinal("password_hash")) ? 0 : (reader.GetString("password_hash")?.Length ?? 0);
                    list.Add(new { id, username = uname, email, hasPassword = hashLen > 0 });
                }
                return list;
            });
        }

        public async Task<string?> GetUserIdForTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = new MySqlCommand("SELECT user_id FROM Sessions WHERE token = @token LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@token", token);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res == null || res == DBNull.Value) return null;

                    // Convert safely to string and return null when empty
                    var s = res?.ToString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUserIdForTokenAsync failed");
                return null;
            }
        }

        public async Task<string> CreateSessionTokenAsync(string userId)
        {
            var token = Guid.NewGuid().ToString();

            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = new MySqlCommand("INSERT INTO Sessions (token, user_id, created_at) VALUES (@token, @userId, @now)", conn);
                    cmd.Parameters.AddWithValue("@token", token);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    await cmd.ExecuteNonQueryAsync();
                    return token;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CreateSessionTokenAsync failed");
                throw;
            }
        }

        public async Task<string?> GetUserRoleAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return null;
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = new MySqlCommand("SELECT role FROM Users WHERE id = @id LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@id", userId);
                    var res = await cmd.ExecuteScalarAsync();
                    if (res == null || res == DBNull.Value) return null;

                    var s = res?.ToString();
                    return string.IsNullOrWhiteSpace(s) ? null : s;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUserRoleAsync failed for user {UserId}", userId);
                return null;
            }
        }

        public async Task<(string? Id, string? Username, string? Email, string? Role)> GetUserInfoByIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return (null, null, null, null);
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = new MySqlCommand("SELECT id, username, email, role FROM Users WHERE id = @id LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@id", userId);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        // Read id robustly (could be stored as GUID or CHAR)
                        var idOrdinal = reader.GetOrdinal("id");
                        string? id = null;
                        if (!reader.IsDBNull(idOrdinal))
                        {
                            var idVal = reader.GetValue(idOrdinal);
                            id = idVal?.ToString()?.Trim();
                        }

                        string? username = null;
                        if (!reader.IsDBNull(reader.GetOrdinal("username")))
                            username = reader.GetString("username")?.Trim();

                        string? email = null;
                        if (!reader.IsDBNull(reader.GetOrdinal("email")))
                            email = reader.GetString("email")?.Trim();

                        string? role = null;
                        if (!reader.IsDBNull(reader.GetOrdinal("role")))
                            role = reader.GetString("role")?.Trim();

                        return (id, username, email, role);
                    }

                    return (null, null, null, null);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUserInfoByIdAsync failed for user {UserId}", userId);
                return (null, null, null, null);
            }
        }

        public async Task EnsureAdminUserAsync(string username, string email, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) return;
            username = username.Trim();
            email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();

            try
            {
                await using var conn = await GetConnectionAsync();

                // Check if user exists
                await using var chk = new MySqlCommand("SELECT id FROM Users WHERE username = @username LIMIT 1", conn);
                chk.Parameters.AddWithValue("@username", username);
                var existing = await chk.ExecuteScalarAsync();
                var now = DateTime.UtcNow;

                if (existing != null && existing != DBNull.Value)
                {
                    // existing may be object -> convert safely
                    var idStr = existing?.ToString()?.Trim();
                    if (string.IsNullOrWhiteSpace(idStr))
                    {
                        _logger.LogWarning("EnsureAdminUserAsync: existing user id from DB was empty for username {Username}", username);
                    }
                    else
                    {
                        var id = idStr;
                        // Update password hash and role to admin
                        await using var upd = new MySqlCommand(@"UPDATE Users SET password_hash = @hash, email = @email, role = 'admin', last_updated = @now WHERE id = @id", conn);
                        upd.Parameters.AddWithValue("@hash", HashPassword(password));
                        upd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                        upd.Parameters.AddWithValue("@now", now);
                        upd.Parameters.AddWithValue("@id", id);
                        await upd.ExecuteNonQueryAsync();
                        _logger.LogInformation("Ensured admin user exists and updated: {Username}", username);
                    }
                }
                else
                {
                    // Insert new admin user
                    await using var ins = new MySqlCommand(@"INSERT INTO Users (id, username, email, password_hash, role, created_at, last_updated) VALUES (@id, @username, @email, @hash, 'admin', @now, @now)", conn);
                    var id = Guid.NewGuid().ToString();
                    ins.Parameters.AddWithValue("@id", id);
                    ins.Parameters.AddWithValue("@username", username);
                    ins.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                    ins.Parameters.AddWithValue("@hash", HashPassword(password));
                    ins.Parameters.AddWithValue("@now", now);
                    await ins.ExecuteNonQueryAsync();
                    _logger.LogInformation("Inserted admin user: {Username}", username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "EnsureAdminUserAsync failed for {Username}", username);
            }
        }

        public async Task<(string? Id, string? Username, string? Email, string? PasswordHash, string? Role)> GetUserDebugByUsernameAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return (null, null, null, null, null);
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    await using var cmd = new MySqlCommand("SELECT id, username, email, password_hash, role FROM Users WHERE username = @username LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@username", username);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var id = reader.IsDBNull(reader.GetOrdinal("id")) ? null : reader.GetString("id");
                        var uname = reader.IsDBNull(reader.GetOrdinal("username")) ? null : reader.GetString("username");
                        var email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email");
                        var hash = reader.IsDBNull(reader.GetOrdinal("password_hash")) ? null : reader.GetString("password_hash");
                        var role = reader.IsDBNull(reader.GetOrdinal("role")) ? null : reader.GetString("role");
                        return (id, uname, email, hash, role);
                    }
                    return (null, null, null, null, null);
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUserDebugByUsernameAsync failed for {Username}", username);
                return (null, null, null, null, null);
            }
        }

        public record UserGameStats(string GameId, int GamesPlayed, int TotalQuestions, int CorrectAnswers, double Accuracy, int BestScore, double AverageScore);

        public async Task<List<UserGameStats>> GetUserGameStatsAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return new List<UserGameStats>();
            try
            {
                await using var conn = await GetConnectionAsync();

                var stats = new Dictionary<string, UserGameStats>();

                // Query games played, best and average score for this user
                await using (var cmd = new MySqlCommand(@"SELECT IFNULL(game_id, '') AS game_id, COUNT(*) AS games_played, MAX(score) AS best_score, AVG(score) AS avg_score FROM GameSessions WHERE user_id = @userId GROUP BY game_id", conn))
                {
                    cmd.Parameters.AddWithValue("@userId", userId);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var gid = reader.IsDBNull(reader.GetOrdinal("game_id")) ? string.Empty : reader.GetString("game_id");
                        var played = reader.IsDBNull(reader.GetOrdinal("games_played")) ? 0 : reader.GetInt32("games_played");
                        var best = 0;
                        try { best = reader.IsDBNull(reader.GetOrdinal("best_score")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("best_score"))); } catch { best = 0; }
                        double avg = 0;
                        try { avg = reader.IsDBNull(reader.GetOrdinal("avg_score")) ? 0.0 : reader.GetDouble("avg_score"); } catch { avg = 0.0; }

                        // For now we intentionally DO NOT include per-question/match history (userquestions) in the stats.
                        // This keeps the returned payload simple: GamesPlayed, BestScore, AverageScore. Question-level fields set to zero/null.
                        stats[gid] = new UserGameStats(gid, played, 0, 0, 0.0, best, avg);
                    }
                }

                // Previously we aggregated question totals/correct answers from UserQuestions here.
                // That logic has been removed intentionally to hide match history/details for now.

                return stats.Values.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to compute user game stats for {UserId}", userId);
                return new List<UserGameStats>();
            }
        }

        public async Task<List<object>> GetSessionsByUserIdAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return new List<object>();
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    var list = new List<object>();
                    await using var cmd = new MySqlCommand(@"SELECT id, game_id, room_id, score, start_time, end_time FROM GameSessions WHERE user_id = @userId ORDER BY start_time DESC", conn);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var id = reader.IsDBNull(reader.GetOrdinal("id")) ? string.Empty : reader.GetString("id");
                        var gid = reader.IsDBNull(reader.GetOrdinal("game_id")) ? string.Empty : reader.GetString("game_id");
                        var room = reader.IsDBNull(reader.GetOrdinal("room_id")) ? null : reader.GetString("room_id");
                        var score = reader.IsDBNull(reader.GetOrdinal("score")) ? 0 : Convert.ToInt32(reader.GetValue(reader.GetOrdinal("score")));
                        var start = reader.IsDBNull(reader.GetOrdinal("start_time")) ? (DateTime?)null : reader.GetDateTime("start_time");
                        var end = reader.IsDBNull(reader.GetOrdinal("end_time")) ? (DateTime?)null : reader.GetDateTime("end_time");
                        list.Add(new { id, gameId = gid, room = room, score, startTime = start, endTime = end });
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetSessionsByUserIdAsync failed for {UserId}", userId);
                return new List<object>();
            }
        }

        public async Task<List<object>> GetAllGamesAsync()
        {
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    var list = new List<object>();
                    await using var cmd = new MySqlCommand(@"SELECT id, name, description, is_active, mode FROM Games ORDER BY name", conn);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var id = reader.IsDBNull(reader.GetOrdinal("id")) ? string.Empty : reader.GetString("id");
                        var name = reader.IsDBNull(reader.GetOrdinal("name")) ? string.Empty : reader.GetString("name");
                        var desc = reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString("description");
                        var isActive = !reader.IsDBNull(reader.GetOrdinal("is_active")) && Convert.ToInt32(reader.GetValue(reader.GetOrdinal("is_active"))) == 1;
                        var mode = reader.IsDBNull(reader.GetOrdinal("mode")) ? string.Empty : reader.GetString("mode");
                        list.Add(new { id, name, description = desc, isActive, mode });
                    }
                    return list;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetAllGamesAsync failed");
                return new List<object>();
            }
        }

        public async Task<Dictionary<string,int>> GetGamesPlayedCountsAsync()
        {
            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    await using var cmd = new MySqlCommand(@"SELECT IFNULL(user_id, '') AS user_id, COUNT(*) AS games_played FROM GameSessions WHERE user_id IS NOT NULL GROUP BY user_id", conn);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var uid = reader.IsDBNull(reader.GetOrdinal("user_id")) ? string.Empty : reader.GetValue(reader.GetOrdinal("user_id"))?.ToString() ?? string.Empty;
                        var played = reader.IsDBNull(reader.GetOrdinal("games_played")) ? 0 : reader.GetInt32("games_played");
                        if (!string.IsNullOrWhiteSpace(uid)) dict[uid] = played;
                    }
                    return dict;
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetGamesPlayedCountsAsync failed");
                return new Dictionary<string,int>();
            }
        }
    }
}