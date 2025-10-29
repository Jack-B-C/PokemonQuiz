using MySql.Data.MySqlClient;
using PokemonQuizAPI.Models;
using System.Data;

namespace PokemonQuizAPI.Data
{
    public class DatabaseHelper(IConfiguration configuration, ILogger<DatabaseHelper> logger)
    {
        private readonly string _connectionString = configuration.GetConnectionString("PokemonQuizDB")
            ?? throw new ArgumentNullException(nameof(configuration), "Connection string 'PokemonQuizDB' not found");
        private readonly ILogger<DatabaseHelper> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        private async Task<MySqlConnection> GetConnectionAsync()
        {
            var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync();
            return conn;
        }

        public async Task<List<PokemonData>> GetAllPokemonAsync()
        {
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