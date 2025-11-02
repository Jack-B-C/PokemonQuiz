using MySql.Data.MySqlClient;
using PokemonQuizAPI.Models;
using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.IO;
using System.Linq;

namespace PokemonQuizAPI.Data
{
    public class DatabaseHelper
    {
        private readonly string? _connectionString;
        private readonly ILogger<DatabaseHelper> _logger;

        private readonly bool _useFileStorage;
        private readonly string _dataDir;
        private readonly string _pokemonFile;
        private readonly string _gameRoomFile;
        private readonly SemaphoreSlim _fileLock = new(1, 1);

        public DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _connectionString = configuration.GetConnectionString("PokemonQuizDB");

            // determine data directory for file fallback
            _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
            Directory.CreateDirectory(_dataDir);
            _pokemonFile = Path.Combine(_dataDir, "pokemon.json");
            _gameRoomFile = Path.Combine(_dataDir, "gamerooms.json");

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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to MySQL using provided connection string; falling back to local JSON storage at {DataDir}", _dataDir);
                    _useFileStorage = true;
                }
            }
        }

        private async Task<MySqlConnection> GetConnectionAsync()
        {
            if (_useFileStorage)
                throw new InvalidOperationException("Using file storage; no MySQL connection available.");

            var conn = new MySqlConnection(_connectionString!);
            await conn.OpenAsync();
            return conn;
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

            var result = new List<PokemonData>();

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData ORDER BY name", conn);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    result.Add(MapPokemonData(reader));
                }

                _logger.LogInformation("Successfully fetched {Count} Pokémon from database", result.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all Pokémon from database");
                throw;
            }

            return result;
        }

        public async Task<PokemonData?> GetPokemonByIdAsync(string id)
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<PokemonData>(_pokemonFile);
                return list.FirstOrDefault(p => p.Id == id);
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    return MapPokemonData(reader);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Pokémon with ID: {Id}", id);
                throw;
            }
        }

        public async Task<int> GetPokemonCountAsync()
        {
            if (_useFileStorage)
            {
                var list = await ReadJsonListAsync<PokemonData>(_pokemonFile);
                return list.Count;
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM PokemonData", conn);

                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Pokémon count");
                throw;
            }
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

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO PokemonData 
                    (id, name, type1, type2, hp, attack, defence, special_attack, special_defence, speed, image_url, fetched_at)
                    VALUES 
                    (@id, @name, @type1, @type2, @hp, @attack, @defence, @special_attack, @special_defence, @speed, @image_url, @fetched_at)",
                    conn);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting Pokémon: {Name}", pokemon.Name);
                throw;
            }
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
                    CreatedAt = DateTime.UtcNow
                };
                rooms.Add(rec);
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Inserted GameRoom {RoomCode} into local storage", roomCode);
                return true;
            }
            else
            {
                try
                {
                    await using var conn = await GetConnectionAsync();
                    await using var cmd = new MySqlCommand(@"
                    INSERT INTO GameRoom (id, game_id, host_user_id, room_code, is_active, created_at)
                    VALUES (@id, @game_id, @host_user_id, @room_code, @is_active, @created_at)", conn);

                    cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                    cmd.Parameters.AddWithValue("@game_id", gameId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@host_user_id", hostUserId ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@room_code", roomCode.ToUpper());
                    cmd.Parameters.AddWithValue("@is_active", true);
                    cmd.Parameters.AddWithValue("@created_at", DateTime.UtcNow);

                    var rowsAffected = await cmd.ExecuteNonQueryAsync();
                    _logger.LogInformation("Inserted GameRoom {RoomCode} into database", roomCode);
                    return rowsAffected > 0;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error inserting GameRoom: {RoomCode}", roomCode);
                    throw;
                }
            }
        }

        /// <summary>
        /// Returns true if an active GameRoom with the given room code exists (is_active = 1)
        /// </summary>
        public async Task<bool> GameRoomExistsAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                return rooms.Any(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());

                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of GameRoom: {RoomCode}", roomCode);
                throw;
            }
        }

        /// <summary>
        /// Returns true if a GameRoom with the given room code exists, regardless of its active status
        /// </summary>
        public async Task<bool> GameRoomExistsAnyStatusAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                return rooms.Any(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase));
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());

                var count = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(count) > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking existence of GameRoom (any status): {RoomCode}", roomCode);
                throw;
            }
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
                var rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                return rec?.GameId;
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("SELECT game_id FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1 LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());

                var result = await cmd.ExecuteScalarAsync();
                if (result == null || result == DBNull.Value) return null;
                return result.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching game id for room {RoomCode}", roomCode);
                throw;
            }
        }

        /// <summary>
        /// Update the game_id stored for a GameRoom by room code (trimmed). Returns true if updated.
        /// </summary>
        public async Task<bool> UpdateGameRoomGameIdAsync(string roomCode, string gameId)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                var rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                if (rec == null) return false;
                rec.GameId = gameId;
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Updated GameRoom {RoomCode} with game {GameId} in local storage", roomCode, gameId);
                return true;
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET game_id = @gameId WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@gameId", gameId);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());

                var rows = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Updated GameRoom {RoomCode} with game {GameId}", roomCode, gameId);
                return rows > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating game id for GameRoom: {RoomCode}", roomCode);
                throw;
            }
        }

        /// <summary>
        /// Mark a GameRoom as ended by setting is_active = 0.
        /// </summary>
        public async Task<bool> EndGameRoomAsync(string roomCode)
        {
            if (_useFileStorage)
            {
                var rooms = await ReadJsonListAsync<GameRoomRecord>(_gameRoomFile);
                var rec = rooms.FirstOrDefault(r => string.Equals(r.RoomCode, roomCode, StringComparison.OrdinalIgnoreCase) && r.IsActive);
                if (rec == null) return false;
                rec.IsActive = false;
                await WriteJsonListAsync(_gameRoomFile, rooms);
                _logger.LogInformation("Marked GameRoom {RoomCode} ended in local storage", roomCode);
                return true;
            }

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET is_active = 0 WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@code", roomCode.ToUpper().Trim());

                var rowsAffected = await cmd.ExecuteNonQueryAsync();
                _logger.LogInformation("Marked GameRoom {RoomCode} ended in database", roomCode);
                return rowsAffected > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending GameRoom: {RoomCode}", roomCode);
                throw;
            }
        }

        private static PokemonData MapPokemonData(MySqlDataReader reader)
        {
            return new PokemonData
            {
                Id = reader.GetGuid("id").ToString(),
                Name = reader.GetString("name"),
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

            try
            {
                await using var conn = await GetConnectionAsync();
                await using var cmd = new MySqlCommand("DELETE FROM PokemonData", conn);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                _logger.LogInformation("Cleared {Count} Pokémon from database", rowsAffected);
                return rowsAffected;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing Pokémon from database");
                throw;
            }
        }
    }
}