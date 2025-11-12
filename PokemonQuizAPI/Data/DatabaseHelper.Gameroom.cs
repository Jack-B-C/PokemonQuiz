using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySql.Data.MySqlClient;

namespace PokemonQuizAPI.Data
{
    public partial class DatabaseHelper
    {
        // Insert a room once; return false on duplicate key so caller can retry with a new code
        public async Task<bool> InsertGameRoomAsync(string roomCode, string? hostUserId = null, string? gameId = null, CancellationToken ct = default)
        {
            try
            {
                return await WithConnectionAsync(async (conn, ctInner) =>
                {
                    await using var cmd = new MySqlCommand(@"
                        INSERT INTO GameRoom (id, game_id, host_user_id, created_at, ended_at, room_code, is_active)
                        VALUES (@id, @game_id, @host_user_id, @created_at, @ended_at, @room_code, @is_active)", conn);

                    // typed parameters
                    cmd.Parameters.Add(new MySqlParameter("@id", MySqlDbType.VarChar) { Value = Guid.NewGuid().ToString() });
                    cmd.Parameters.Add(new MySqlParameter("@game_id", MySqlDbType.VarChar) { Value = (object?)gameId ?? DBNull.Value });
                    cmd.Parameters.Add(new MySqlParameter("@host_user_id", MySqlDbType.VarChar) { Value = (object?)hostUserId ?? DBNull.Value });
                    cmd.Parameters.Add(new MySqlParameter("@created_at", MySqlDbType.DateTime) { Value = DateTime.UtcNow });
                    cmd.Parameters.Add(new MySqlParameter("@ended_at", MySqlDbType.DateTime) { Value = DBNull.Value });
                    cmd.Parameters.Add(new MySqlParameter("@room_code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                    cmd.Parameters.Add(new MySqlParameter("@is_active", MySqlDbType.Bit) { Value = true });

                    var rowsAffected = await cmd.ExecuteNonQueryAsync(ctInner);
                    _logger.LogInformation("Inserted GameRoom {RoomCode} into database", roomCode);
                    return rowsAffected > 0;
                }, ct);
            }
            catch (MySqlException mex) when (mex.Number == 1062)
            {
                // Duplicate key - caller should generate a new code and retry
                _logger.LogWarning(mex, "InsertGameRoomAsync duplicate room_code {RoomCode}", roomCode);
                return false;
            }
        }

        public async Task<bool> GameRoomExistsAsync(string roomCode, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.Add(new MySqlParameter("@code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                var count = await cmd.ExecuteScalarAsync(ctInner);
                return Convert.ToInt32(count) > 0;
            }, ct);
        }

        public async Task<bool> GameRoomExistsAnyStatusAsync(string roomCode, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code", conn);
                cmd.Parameters.Add(new MySqlParameter("@code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                var count = await cmd.ExecuteScalarAsync(ctInner);
                return Convert.ToInt32(count) > 0;
            }, ct);
        }

        public async Task<string?> GetGameIdForRoomAsync(string roomCode, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("SELECT game_id FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1 LIMIT 1", conn);
                cmd.Parameters.Add(new MySqlParameter("@code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                var result = await cmd.ExecuteScalarAsync(ctInner);
                if (result == null || result == DBNull.Value) return null;
                return result.ToString();
            }, ct);
        }

        public async Task<bool> UpdateGameRoomGameIdAsync(string roomCode, string gameId, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET game_id = @gameId WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.Add(new MySqlParameter("@gameId", MySqlDbType.VarChar) { Value = gameId });
                cmd.Parameters.Add(new MySqlParameter("@code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                var rows = await cmd.ExecuteNonQueryAsync(ctInner);
                _logger.LogInformation("Updated GameRoom {RoomCode} with game {GameId}", roomCode, gameId);
                return rows > 0;
            }, ct);
        }

        public async Task<bool> EndGameRoomAsync(string roomCode, CancellationToken ct = default)
        {
            return await WithConnectionAsync(async (conn, ctInner) =>
            {
                await using var cmd = new MySqlCommand("UPDATE GameRoom SET is_active = 0, ended_at = @ended WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.Add(new MySqlParameter("@code", MySqlDbType.VarChar) { Value = roomCode.ToUpper().Trim() });
                cmd.Parameters.Add(new MySqlParameter("@ended", MySqlDbType.DateTime) { Value = DateTime.UtcNow });
                var rowsAffected = await cmd.ExecuteNonQueryAsync(ctInner);
                _logger.LogInformation("Marked GameRoom {RoomCode} ended in database", roomCode);
                return rowsAffected > 0;
            }, ct);
        }

        public async Task<int> GetActiveRoomCountAsync(CancellationToken ct = default)
        {
            try
            {
                return await WithConnectionAsync(async (conn, ctInner) =>
                {
                    await using var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE is_active = 1", conn);
                    var count = await cmd.ExecuteScalarAsync(ctInner);
                    return Convert.ToInt32(count);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count active rooms");
                return 0;
            }
        }
    }
}