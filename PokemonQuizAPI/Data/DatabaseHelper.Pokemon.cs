using MySql.Data.MySqlClient;
using PokemonQuizAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PokemonQuizAPI.Data
{
    public partial class DatabaseHelper
    {

        // Returns all rows from PokemonData, ordered by name
        public async Task<List<PokemonData>> GetAllPokemonAsync(CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                var result = new List<PokemonData>();
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData ORDER BY name", conn);
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync(ctInner);

                while (await reader.ReadAsync(ctInner))
                {
                    result.Add(MapPokemonData(reader));
                }
                _logger.LogInformation("Fetched {Count} Pokémon", result.Count);
                return result;
            }, ct);
        }

        // Returns a single pokemon by its id, or null if not found
        public async Task<PokemonData?> GetPokemonByIdAsync(string id, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("SELECT * FROM PokemonData WHERE id = @id", conn);
                cmd.Parameters.Add(new MySqlParameter("@id", MySqlDbType.VarChar) { Value = id ?? string.Empty });
                await using var reader = (MySqlDataReader)await cmd.ExecuteReaderAsync(ctInner);
                if (await reader.ReadAsync(ctInner)) return MapPokemonData(reader);
                return null;
            }, ct);
        }

        // returns the total count of pokemon in the database
        public async Task<int> GetPokemonCountAsync(CancellationToken cancellationToken = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM PokemonData", conn);
                var result = await cmd.ExecuteScalarAsync(ctInner);
                if (result == null || result == DBNull.Value) return 0;
                try
                {
                    return Convert.ToInt32(result);
                }
                catch (OverflowException)
                {
                    return int.MaxValue;
                }
            }, cancellationToken);
        }

        // insert pokemon into database (uses typed parameters)
        public async Task<bool> InsertPokemonAsync(PokemonData pokemon, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand(@"
                    INSERT INTO PokemonData 
                    (id, name, type1, type2, hp, attack, defence, special_attack, special_defence, speed, image_url, fetched_at)
                    VALUES 
                    (@id, @name, @type1, @type2, @hp, @attack, @defence, @special_attack, @special_defence, @speed, @image_url, @fetched_at)", conn);

                var p = cmd.Parameters;
                p.Add(new MySqlParameter("@id", MySqlDbType.VarChar) { Value = pokemon.Id ?? string.Empty });
                p.Add(new MySqlParameter("@name", MySqlDbType.VarChar) { Value = pokemon.Name ?? string.Empty });
                p.Add(new MySqlParameter("@type1", MySqlDbType.VarChar) { Value = (object?)pokemon.Type1 ?? DBNull.Value });
                p.Add(new MySqlParameter("@type2", MySqlDbType.VarChar) { Value = (object?)pokemon.Type2 ?? DBNull.Value });
                p.Add(new MySqlParameter("@hp", MySqlDbType.Int32) { Value = pokemon.Hp });
                p.Add(new MySqlParameter("@attack", MySqlDbType.Int32) { Value = pokemon.Attack });
                p.Add(new MySqlParameter("@defence", MySqlDbType.Int32) { Value = pokemon.Defence });
                p.Add(new MySqlParameter("@special_attack", MySqlDbType.Int32) { Value = pokemon.SpecialAttack });
                p.Add(new MySqlParameter("@special_defence", MySqlDbType.Int32) { Value = pokemon.SpecialDefence });
                p.Add(new MySqlParameter("@speed", MySqlDbType.Int32) { Value = pokemon.Speed });
                p.Add(new MySqlParameter("@image_url", MySqlDbType.VarChar) { Value = (object?)pokemon.ImageUrl ?? DBNull.Value });
                p.Add(new MySqlParameter("@fetched_at", MySqlDbType.DateTime) { Value = pokemon.FetchedAt });

                var rowsAffected = await cmd.ExecuteNonQueryAsync(ctInner);
                return rowsAffected > 0;
            }, ct);
        }

        // deletes all pokemon from database, returns number of rows affected, should be admin only
        public async Task<int> ClearAllPokemonAsync(CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("DELETE FROM PokemonData", conn);
                var rowsAffected = await cmd.ExecuteNonQueryAsync(ctInner);
                _logger.LogInformation("Cleared {Count} Pokémon from database", rowsAffected);
                return rowsAffected;
            }, ct);
        }

    }
}