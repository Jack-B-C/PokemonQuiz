using MySql.Data.MySqlClient;
using PokemonQuizAPI.Models;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;

namespace PokemonQuizAPI.Data
{
    /// <summary>
    /// Helper that encapsulates database operations for the API. Supports two modes:
    /// - MySQL-backed storage when a valid connection string is provided and reachable.
    /// - Local JSON file fallback when no connection string is provided or DB is unreachable.
    ///
    /// The class exposes methods for managing Pokémon, game rooms, user accounts,
    /// sessions and simple leaderboard/game-session persistence. When running in
    /// file-fallback mode, data is stored under the application's <c>data</c>
    /// directory (AppContext.BaseDirectory + "/data").
    ///
    /// Additional notes:
    /// This class intentionally keeps a file-based fallback for local development when
    /// the MySQL connection is unavailable.
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string? _connectionString;
        private readonly ILogger<DatabaseHelper> _logger;

        private readonly bool _useFileStorage;
        private readonly string _dataDir;
        private readonly string _pokemonFile;
        private readonly string _gameRoomFile;
        private readonly string _gameSessionsFile;
        private readonly string _userQuestionsFile;
        private readonly string _leaderboardFile;
        private readonly string _usersFile;
        private readonly string _sessionsFile;
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        /// <summary>
        /// Construct the helper and decide whether to use MySQL or local JSON file storage.
        /// Attempts a short test connection to the database; on failure the instance
        /// falls back to local JSON files and logs the reason.
        /// </summary>
        public DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("PokemonQuizDB");

            // If a connection string is present, expose a small summary (do not log password)
            if (!string.IsNullOrWhiteSpace(_connectionString))
            {
                try
                {
                    var builder = new MySqlConnectionStringBuilder(_connectionString);
                    _logger.LogInformation("Resolved MySQL config - Server={Server}, Port={Port}, Database={Database}, UserId={User}", builder.Server, builder.Port, builder.Database, builder.UserID);
                }
                catch
                {
                    _logger.LogInformation("MySQL connection string present but could not be parsed for debug output.");
                }
            }

            // determine data directory for file fallback
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(_dataDir);
            _pokemonFile = Path.Combine(_dataDir, "pokemon.json");
            _gameRoomFile = Path.Combine(_dataDir, "gamerooms.json");
            _gameSessionsFile = Path.Combine(_dataDir, "gamesessions.json");
            _userQuestionsFile = Path.Combine(_dataDir, "userquestions.json");
            _leaderboardFile = Path.Combine(_dataDir, "leaderboard.json");
            _usersFile = Path.Combine(_dataDir, "users.json");
            _sessionsFile = Path.Combine(_dataDir, "sessions.json");

            // Detect whether we should use file storage: no connection string OR can't connect to MySQL
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                _logger.LogWarning("No MySQL connection string provided; falling back to local JSON storage at {DataDir}", _dataDir);
                _useFileStorage = true;
            }
            else
            {
                try
                {
                    using var conn = new MySqlConnection(_connectionString);
                    conn.Open();
                    conn.Close();
                    _useFileStorage = false;
                    _logger.LogInformation("Successfully connected to MySQL; using database storage.");
                }
                catch (Exception ex)
                {
                    // Log the exception and fall back to file storage. Include the message to help debug common issues
                    _logger.LogWarning(ex, "Failed to connect to MySQL using provided connection string; falling back to local JSON storage at {DataDir}. Error: {Message}", _dataDir, ex.Message);
                    _useFileStorage = true;
                }
            }
        }

        /// <summary>
        /// Open and return a MySQL connection using the configured connection string.
        /// Throws InvalidOperationException when running in file-fallback mode or when
        /// a connection string is missing.
        /// </summary>
        private async Task<MySqlConnection> GetConnectionAsync()
        {
            if (_useFileStorage)
                throw new InvalidOperationException("Using file storage; no MySQL connection available.");

            var conn = new MySqlConnection(_connectionString ?? throw new InvalidOperationException("No connection string provided"));
            await conn.OpenAsync();
            return conn;
        }

        /// <summary>
        /// Small helper to centralize opening a connection and executing a DB action.
        /// This reduces duplicated try/using boilerplate across the class.
        /// </summary>
        private async Task<T> WithConnectionAsync<T>(Func<MySqlConnection, Task<T>> action)
        {
            if (_useFileStorage)
                throw new InvalidOperationException("Using file storage; no MySQL connection available.");

            try
            {
                await using var conn = await GetConnectionAsync();
                return await action(conn);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Database operation failed");
                throw;
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

        private async Task<List<T>> ReadJsonListAsync<T>(string path)
        {
            await _fileLock.WaitAsync();
            try
            {
                if (!File.Exists(path)) return new List<T>();
                var txt = await File.ReadAllTextAsync(path);
                if (string.IsNullOrWhiteSpace(txt)) return new List<T>();
                return JsonSerializer.Deserialize<List<T>>(txt, _jsonOptions) ?? new List<T>();
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task WriteJsonListAsync<T>(string path, List<T> items)
        {
            await _fileLock.WaitAsync();
            try
            {
                var txt = JsonSerializer.Serialize(items, _jsonOptions);
                await File.WriteAllTextAsync(path, txt);
            }
            finally
            {
                _fileLock.Release();
            }
        }

        public async Task<List<PokemonData>> GetAllPokemonAsync()
        {
            if (_useFileStorage)
            {
                return await ReadJsonListAsync<PokemonData>(_pokemonFile);
            }

            return await WithConnectionAsync(async conn =>
            {
                var result = new List<PokemonData>();
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData ORDER BY name", conn);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(MapPokemonData(reader));
                }

                _logger.LogInformation("Successfully fetched {Count} Pokémon from database", result.Count);
                return result;
            });
        }

        public async Task<PokemonData?> GetPokemonByIdAsync(string id)
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<PokemonData>(_pokemonFile);
                return list.FirstOrDefault(p => p.Id == id);
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync()) return MapPokemonData(reader);
                return null;
            });
        }

        public async Task<int> GetPokemonCountAsync()
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<PokemonData>(_pokemonFile);
                return list.Count;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM PokemonData", conn);
                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count);
            });
        }

        public async Task<bool> InsertPokemonAsync(PokemonData pokemon)
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<PokemonData>(_pokemonFile);
                list.Add(pokemon);
                await WriteJsonListAsync(_pokemonFile, list);
                return true;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO PokemonData 
                    (id, name, type1, type2, hp, attack, defence, special_attack, special_defence, speed, image_url, fetched_at)
                    VALUES 
                    (@id, @name, @type1, @type2, @hp, @attack, @defence, @special_attack, @special_defence, @speed, @image_url, @fetched_at)", conn);

                cmd.Parameters.AddWithValue("@id", pokemon.Id);
                cmd.Parameters.AddWithValue("@name", pokemon.Name);
                cmd.Parameters.AddWithValue("@type1", pokemon.Type1 ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@type2", pokemon.Type2 ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@hp", pokemon.Hp);
                cmd.Parameters.AddWithValue("@attack", pokemon.Attack);
                cmd.Parameters.AddWithValue("@defence", pokemon.Defence);
                cmd.Parameters.AddWithValue("@special_attack", pokemon.SpecialAttack);
                cmd.Parameters.AddWithValue("@special_defence", pokemon.SpecialDefence);
                cmd.Parameters.AddWithValue("@speed", pokemon.Speed);
                cmd.Parameters.AddWithValue("@image_url", pokemon.ImageUrl ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@fetched_at", pokemon.FetchedAt);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                return rowsAffected > 0;
            });
        }

        // Internal record for file-based GameRoom storage
        private class GameRoomRecord
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
            [JsonPropertyName("gameId")]
            public string? GameId { get; set; }
            [JsonPropertyName("hostUserId")]
            public string? HostUserId { get; set; }
            [JsonPropertyName("roomCode")]
            public string RoomCode { get; set; } = string.Empty;
            [JsonPropertyName("isActive")]
            public bool IsActive { get; set; }
            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; }
            [JsonPropertyName("endedAt")]
            public DateTime? EndedAt { get; set; }
        }

        /// <summary>
        /// Insert a new game room into the GameRoom table.
        /// hostUserId and gameId are optional and will be saved as NULL when not provided.
        /// </summary>
        public async Task<bool> InsertGameRoomAsync(string roomCode, string? hostUserId = null, string? gameId = null)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                var rec = new GameRoomRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    GameId = gameId,
                    HostUserId = hostUserId,
                    RoomCode = roomCode.ToUpper(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    EndedAt = null
                };
                rooms.Add(rec);
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Inserted GameRoom {RoomCode} into local storage", roomCode);
                return true;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand(@"
                        INSERT INTO GameRoom (id, game_id, host_user_id, created_at, ended_at, room_code, is_active)
                        VALUES (@id, @game_id, @host_user_id, @created_at, @ended_at, @room_code, @is_active)", conn);

                cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                cmd.Parameters.AddWithValue("@game_id", gameId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@host_user_id", hostUserId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@ended_at", DBNull.Value);
                cmd.Parameters.AddWithValue("@room_code", roomCode.ToUpper());
                cmd.Parameters.AddWithValue("@is_active", true);

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Inserted GameRoom {RoomCode} into database", roomCode);
                return rowsAffected > 0;
            });
        }

        /// <summary>
        /// Returns true if an active GameRoom with the given room code exists (is_active = 1)
        /// </summary>
        public async Task<bool> GameRoomExistsAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                GameRoomRecord? rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                return rec != null;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());
                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            });
        }

        /// <summary>
        /// Returns true if a GameRoom with the given room code exists, regardless of its active status
        /// </summary>
        public async Task<bool> GameRoomExistsAnyStatusAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                GameRoomRecord? rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase));
                return rec != null;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());
                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            });
        }

        /// <summary>
        /// Get the game_id for a GameRoom by room code (trimmed).
        /// Returns null if not found or inactive.
        /// </summary>
        public async Task<string?> GetGameIdForRoomAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                GameRoomRecord? rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                return rec?.GameId;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("SELECT game_id FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());
                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value) return null;
                return result.ToString();
            });
        }

        /// <summary>
        /// Update the game_id stored for a GameRoom by room code (trimmed). Returns true if updated.
        /// </summary>
        public async Task<bool> UpdateGameRoomGameIdAsync(string roomCode, string gameId)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                GameRoomRecord? rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                if (rec == null) return false;
                rec.GameId = gameId;
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Updated GameRoom {RoomCode} with game {GameId} in local storage", roomCode, gameId);
                return true;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET game_id = @gameId WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());
                var rows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated GameRoom {RoomCode} with game {GameId}", roomCode, gameId);
                return rows > 0;
            });
        }

        /// <summary>
        /// Mark a GameRoom as ended by setting is_active = 0.
        /// </summary>
        public async Task<bool> EndGameRoomAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                GameRoomRecord? rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                if (rec == null) return false;
                rec.IsActive = false;
                rec.EndedAt = DateTime.UtcNow;
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Marked GameRoom {RoomCode} ended in local storage", roomCode);
                return true;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET is_active = 0, ended_at = @ended WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());
                cmd.Parameters.AddWithValue("@ended", DateTime.UtcNow);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Marked GameRoom {RoomCode} ended in database", roomCode);
                return rowsAffected > 0;
            });
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
            if (_useFileStorage) return true;
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

        public async Task<int> ClearAllPokemonAsync()
        {
            if (_useFileStorage)
            {
                await WriteJsonListAsync<PokemonData>(_pokemonFile, new List<PokemonData>());
                _logger.LogInformation("Cleared Pokémon from local storage");
                return 0;
            }

            return await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("DELETE FROM PokemonData", conn);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Cleared {Count} Pokémon from database", rowsAffected);
                return rowsAffected;
            });
        }

        /// <summary>
        /// Ensure database tables exist by running sql/schema.sql when using MySQL.
        /// </summary>
        public async Task EnsureDatabaseInitializedAsync()
        {
            if (_useFileStorage) return;

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
                    _logger.LogWarning(ex, "Failed to ensure Sessions table; session persistence may fall back to file storage");
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
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<object>(_gameSessionsFile);
                list.Add(new { id = sessionId, gameId, userId, roomId, score, startTime, endTime = (DateTime?)null });
                await WriteJsonListAsync(_gameSessionsFile, list);
                return;
            }

            await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("INSERT INTO GameSessions (id, game_id, user_id, room_id, score, start_time) VALUES (@id, @gameId, @userId, @roomId, @score, @start)", conn);
                cmd.Parameters.AddWithValue("@id", sessionId);
                cmd.Parameters.AddWithValue("@gameId", gameId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@roomId", roomId ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@score", score);
                cmd.Parameters.AddWithValue("@start", startTime);
                await cmd.ExecuteNonQueryAsync();
                return true;
            });
        }

        public async Task UpdateGameSessionScoreAsync(string sessionId, int newScore)
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<Dictionary<string, object>>(_gameSessionsFile);
                Dictionary<string, object>? rec = list.FirstOrDefault(r => r.ContainsKey("id") && (r["id"]?.ToString() == sessionId));
                if (rec != null) rec["score"] = newScore;
                await WriteJsonListAsync(_gameSessionsFile, list);
                return;
            }

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
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<Dictionary<string, object>>(_gameSessionsFile);
                Dictionary<string, object>? rec = list.FirstOrDefault(r => r.ContainsKey("id") && (r["id"]?.ToString() == sessionId));
                if (rec != null) rec["endTime"] = endTime;
                await WriteJsonListAsync(_gameSessionsFile, list);
                return;
            }

            await WithConnectionAsync(async conn =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameSessions SET end_time = @end WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@end", endTime);
                cmd.Parameters.AddWithValue("@id", sessionId);
                await cmd.ExecuteNonQueryAsync();
                return true;
            });
        }

        // UserQuestions persistence
        public async Task InsertUserQuestionAsync(string id, string gameSessionId, string? questionId, string userAnswerJson, bool isCorrect)
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<object>(_userQuestionsFile);
                list.Add(new { id, gameSessionId, questionId, userAnswer = userAnswerJson, isCorrect, createdAt = DateTime.UtcNow });
                await WriteJsonListAsync(_userQuestionsFile, list);
                return;
            }

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

            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<Dictionary<string, object?>>(_leaderboardFile);
                Dictionary<string, object?>? rec = list.FirstOrDefault(r => r.ContainsKey("userId") && (r["userId"]?.ToString() == userIdNonNull));
                if (rec == null)
                {
                    list.Add(new Dictionary<string, object?>
                    {
                        ["id"] = Guid.NewGuid().ToString(),
                        ["userId"] = userIdNonNull,
                        ["gameId"] = gameId ?? string.Empty,
                        ["totalScore"] = scoreDelta,
                        ["totalGames"] = 1,
                        ["createdAt"] = DateTime.UtcNow
                    });
                }
                else
                {
                    var oldObj = rec.GetValueOrDefault("totalScore", 0);
                    var old = Convert.ToInt32(oldObj ?? 0);
                    rec["totalScore"] = old + scoreDelta;
                    var gamesObj = rec.GetValueOrDefault("totalGames", 0);
                    rec["totalGames"] = Convert.ToInt32(gamesObj ?? 0) + 1;
                }
                await WriteJsonListAsync(_leaderboardFile, list);
                return;
            }

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

        public async Task<int> GetActiveRoomCountAsync()
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                return rooms.Count(r => r.IsActive);
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE is_active = 1", conn);
                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count active rooms");
                return 0;
            }
        }

        public async Task<int> GetTotalGamesPlayedAsync()
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<object>(_gameSessionsFile);
                return list.Count;
            }

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
        /// Works against MySQL or file storage fallback.
        /// </summary>
        public async Task<List<GameStats>> GetGameStatsAsync()
        {
            if (_useFileStorage)
            {
                // Read sessions and user questions from files
                var sessions = await ReadJsonListAsync<Dictionary<string, object?>>(_gameSessionsFile);
                var questions = await ReadJsonListAsync<Dictionary<string, object?>>(_userQuestionsFile);

                // Build sessionId -> gameId map
                var sessionGame = new Dictionary<string, string>();
                var gameCounts = new Dictionary<string, (int gamesPlayed, double totalScore, int scoreCount)>();

                foreach (var s in sessions)
                {
                    if (s == null) continue;
                    var id = s.GetValueOrDefault("id")?.ToString() ?? string.Empty;
                    var gameId = s.GetValueOrDefault("gameId")?.ToString() ?? string.Empty;
                    var scoreObj = s.GetValueOrDefault("score");
                    double score = 0;
                    if (scoreObj != null && double.TryParse(scoreObj.ToString(), out var sc)) score = sc;

                    sessionGame[id] = gameId;

                    if (!gameCounts.TryGetValue(gameId, out var g)) g = (0, 0, 0);
                    g.gamesPlayed++;
                    g.totalScore += score;
                    g.scoreCount++;
                    gameCounts[gameId] = g;
                }

                var questionCounts = new Dictionary<string, (int total, int correct)>();
                foreach (var q in questions)
                {
                    if (q == null) continue;
                    var sessionId = q.GetValueOrDefault("gameSessionId")?.ToString() ?? q.GetValueOrDefault("game_session_id")?.ToString() ?? string.Empty;
                    if (string.IsNullOrEmpty(sessionId)) continue;
                    if (!sessionGame.TryGetValue(sessionId, out var gameId)) gameId = string.Empty;

                    var isCorrectObj = q.GetValueOrDefault("isCorrect") ?? q.GetValueOrDefault("is_correct");
                    var isCorrect = false;
                    if (isCorrectObj != null)
                    {
                        if (isCorrectObj is bool b) isCorrect = b;
                        else if (int.TryParse(isCorrectObj.ToString(), out var iv)) isCorrect = iv != 0;
                        else _ = bool.TryParse(isCorrectObj.ToString(), out isCorrect);
                    }

                    if (!questionCounts.TryGetValue(gameId, out var qc)) qc = (0, 0);
                    qc.total++;
                    if (isCorrect) qc.correct++;
                    questionCounts[gameId] = qc;
                }

                var result = new List<GameStats>();
                var allGameIds = new HashSet<string>(gameCounts.Keys.Concat(questionCounts.Keys));
                foreach (var gid in allGameIds)
                {
                    gameCounts.TryGetValue(gid, out var g);
                    questionCounts.TryGetValue(gid, out var q);
                    var avg = g.scoreCount > 0 ? (g.totalScore / g.scoreCount) : 0.0;
                    var accuracy = q.total > 0 ? (double)q.correct / q.total : 0.0;
                    result.Add(new GameStats(gid, g.gamesPlayed, q.total, q.correct, accuracy, avg));
                }

                return result;
            }

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

        // Simple user record for file storage
        public class UserRecord
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;
            [JsonPropertyName("username")]
            public string Username { get; set; } = string.Empty;
            [JsonPropertyName("email")]
            public string? Email { get; set; }
            [JsonPropertyName("passwordHash")]
            public string PasswordHash { get; set; } = string.Empty;
            [JsonPropertyName("createdAt")]
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        // Session token mapping for simple auth: token -> userId
        public class SessionRecord
        {
            [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
            [JsonPropertyName("userId")] public string UserId { get; set; } = string.Empty;
            [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
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

            if (_useFileStorage)
            {
                var users = await ReadJsonListAsync<UserRecord>(_usersFile);
                if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, string.Empty, "Username already exists");
                }

                if (!string.IsNullOrWhiteSpace(email) && users.Any(u => !string.IsNullOrWhiteSpace(u.Email) && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, string.Empty, "Email already registered");
                }

                var rec = new UserRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = username,
                    Email = email,
                    PasswordHash = HashPassword(password),
                    CreatedAt = DateTime.UtcNow
                };
                users.Add(rec);
                await WriteJsonListAsync(_usersFile, users);
                _logger.LogInformation("Created user {Username} with id {UserId} in file storage", username, rec.Id);
                return (true, rec.Id, null);
            }

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
                    // Handle common duplicate key constraint errors
                    _logger.LogWarning(mex, "Failed to insert user into DB; falling back to file storage");
                    if (mex.Number == 1062) // duplicate entry
                    {
                        var msg = mex.Message ?? string.Empty;
                        if (msg.Contains("email", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty, "Email already registered");
                        if (msg.Contains("username", StringComparison.OrdinalIgnoreCase)) return (false, string.Empty, "Username already exists");
                        return (false, string.Empty, "User already exists");
                    }

                    // fallback to file storage below
                }

                // If we reach here, fall back to file storage behavior
                var usersFallback = await ReadJsonListAsync<UserRecord>(_usersFile);
                if (usersFallback.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, string.Empty, "Username already exists");
                }
                if (!string.IsNullOrWhiteSpace(email) && usersFallback.Any(u => !string.IsNullOrWhiteSpace(u.Email) && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase)))
                {
                    return (false, string.Empty, "Email already registered");
                }
                var recFallback = new UserRecord
                {
                    Id = Guid.NewGuid().ToString(),
                    Username = username,
                    Email = email,
                    PasswordHash = HashPassword(password),
                    CreatedAt = DateTime.UtcNow
                };
                usersFallback.Add(recFallback);
                await WriteJsonListAsync(_usersFile, usersFallback);
                _logger.LogInformation("Created user {Username} with id {UserId} in fallback file storage", username, recFallback.Id);
                return (true, recFallback.Id, null);
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

            if (_useFileStorage)
            {
                var users = await ReadJsonListAsync<UserRecord>(_usersFile);
                var rec = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                if (rec == null)
                {
                    _logger.LogInformation("Authenticate: user {Username} not found in file storage", username);
                    return null;
                }
                _logger.LogInformation("Authenticate: found user {Username} in file storage with hash length {Len}", username, rec.PasswordHash?.Length ?? 0);
                if (string.IsNullOrEmpty(rec.PasswordHash))
                {
                    _logger.LogInformation("Authenticate: user {Username} has no password hash in file storage", username);
                    return null;
                }
                var okFile = VerifyHashedPassword(rec.PasswordHash, password);
                _logger.LogInformation("Authenticate: password verification for user {Username} in file storage returned {Result}", username, okFile);
                if (!okFile) return null;
                return rec.Id;
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                try
                {
                    await using var cmd = new MySqlCommand(@"SELECT id, password_hash FROM Users WHERE username = @username LIMIT 1", conn);
                    cmd.Parameters.AddWithValue("@username", username);
                    await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        var id = reader.GetString("id");
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
                    _logger.LogWarning(ex, "DB user lookup failed; falling back to file storage");
                    var users = await ReadJsonListAsync<UserRecord>(_usersFile);
                    var rec = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                    if (rec == null)
                    {
                        _logger.LogInformation("Authenticate fallback: user {Username} not found in file storage", username);
                        return null;
                    }
                    _logger.LogInformation("Authenticate fallback: found user {Username} in file storage with hash length {Len}", username, rec.PasswordHash?.Length ?? 0);
                    if (string.IsNullOrEmpty(rec.PasswordHash))
                    {
                        _logger.LogInformation("Authenticate fallback: user {Username} has no password hash in file storage", username);
                        return null;
                    }
                    var ok = VerifyHashedPassword(rec.PasswordHash, password);
                    _logger.LogInformation("Authenticate fallback: password verification for user {Username} in file storage returned {Result}", username, ok);
                    if (!ok) return null;
                    return rec.Id;
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
            var result = new List<object>();
            if (_useFileStorage)
            {
                var users = await ReadJsonListAsync<UserRecord>(_usersFile);
                foreach (var u in users)
                {
                    result.Add(new { id = u.Id, username = u.Username, email = u.Email, hasPassword = !string.IsNullOrEmpty(u.PasswordHash) });
                }
                return result;
            }

            return await WithConnectionAsync(async conn =>
            {
                var list = new List<object>();
                await using var cmd = new MySqlCommand(@"SELECT id, username, email, password_hash FROM Users", conn);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var id = reader.IsDBNull(reader.GetOrdinal("id")) ? string.Empty : reader.GetString("id");
                    var uname = reader.IsDBNull(reader.GetOrdinal("username")) ? string.Empty : reader.GetString("username");
                    var email = reader.IsDBNull(reader.GetOrdinal("email")) ? string.Empty : reader.GetString("email");
                    var hashLen = reader.IsDBNull(reader.GetOrdinal("password_hash")) ? 0 : reader.GetString("password_hash").Length;
                    list.Add(new { id, username = uname, email, hasPassword = hashLen > 0 });
                }
                return list;
            });
        }

        public async Task<string?> GetUserIdForTokenAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;

            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                return list.FirstOrDefault(s => s.Token == token)?.UserId;
            }

            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    try
                    {
                        await using var cmd = new MySqlCommand("SELECT user_id FROM Sessions WHERE token = @token LIMIT 1", conn);
                        cmd.Parameters.AddWithValue("@token", token);
                        var res = await cmd.ExecuteScalarAsync();
                        if (res == null || res == DBNull.Value) return null;
                        return res.ToString();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to lookup session token in DB; falling back to file");
                        var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                        return list.FirstOrDefault(s => s.Token == token)?.UserId;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetUserIdForTokenAsync failed");
                var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                return list.FirstOrDefault(s => s.Token == token)?.UserId;
            }
        }

        public async Task<string> CreateSessionTokenAsync(string userId)
        {
            var token = Guid.NewGuid().ToString();
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                list.Add(new SessionRecord { Token = token, UserId = userId, CreatedAt = DateTime.UtcNow });
                await WriteJsonListAsync(_sessionsFile, list);
                return token;
            }

            try
            {
                return await WithConnectionAsync(async conn =>
                {
                    try
                    {
                        await using var cmd = new MySqlCommand("INSERT INTO Sessions (token, user_id, created_at) VALUES (@token, @userId, @now)", conn);
                        cmd.Parameters.AddWithValue("@token", token);
                        cmd.Parameters.AddWithValue("@userId", userId);
                        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                        await cmd.ExecuteNonQueryAsync();
                        return token;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to persist session token to DB; using file storage");
                        var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                        list.Add(new SessionRecord { Token = token, UserId = userId, CreatedAt = DateTime.UtcNow });
                        await WriteJsonListAsync(_sessionsFile, list);
                        return token;
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CreateSessionTokenAsync failed; using file storage");
                var list = await ReadJsonListAsync<SessionRecord>(_sessionsFile);
                list.Add(new SessionRecord { Token = token, UserId = userId, CreatedAt = DateTime.UtcNow });
                await WriteJsonListAsync(_sessionsFile, list);
                return token;
            }
        }
    }
}