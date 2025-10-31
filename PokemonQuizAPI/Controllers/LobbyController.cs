using Microsoft.AspNetCore.Mvc;
using MySql.Data.MySqlClient;
using Microsoft.AspNetCore.SignalR;
using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LobbyController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IHubContext<GameHub> _hubContext;
        private readonly DatabaseHelper _db;
        private readonly ILogger<LobbyController> _logger;

        public LobbyController(IConfiguration config, IHubContext<GameHub> hubContext, DatabaseHelper db, ILogger<LobbyController> logger)
        {
            _config = config;
            _hubContext = hubContext;
            _db = db;
            _logger = logger;
        }

        [HttpGet("validate")]
        public IActionResult ValidateRoomCode([FromQuery] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { valid = false, message = "Empty code" });

            // Use the same connection string key as DatabaseHelper
            var connString = _config.GetConnectionString("PokemonQuizDB");
            if (string.IsNullOrWhiteSpace(connString))
                return StatusCode(500, new { valid = false, message = "Database connection not configured" });

            try
            {
                using var conn = new MySqlConnection(connString);
                conn.Open();

                // Trim room_code to handle CHAR(n) padding
                var cmd = new MySqlCommand("SELECT COUNT(*) FROM GameRoom WHERE TRIM(room_code) = @code AND is_active = 1", conn);
                cmd.Parameters.AddWithValue("@code", code.ToUpper().Trim());

                var count = Convert.ToInt32(cmd.ExecuteScalar());
                return Ok(new { valid = count > 0 });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating room code {Code}", code);
                return StatusCode(500, new { valid = false, message = "Database error" });
            }
        }

        [HttpPost("select")]
        public async Task<IActionResult> SelectGame([FromQuery] string code, [FromQuery] string gameId, [FromQuery] string? hostName = null)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(gameId))
                return BadRequest(new { success = false, message = "Invalid parameters" });

            var normalized = code.ToUpper().Trim();

            bool dbUpdated = false;
            try
            {
                dbUpdated = await _db.UpdateGameRoomGameIdAsync(normalized, gameId);
                if (!dbUpdated)
                {
                    _logger.LogWarning("SelectGame: DB update reported no rows for {Code}", normalized);
                }
            }
            catch (Exception ex)
            {
                // Log DB error but continue to broadcast so UI remains responsive
                _logger.LogWarning(ex, "SelectGame: failed to update DB for room {Code}", normalized);
            }

            try
            {
                await _hubContext.Clients.Group(normalized).SendAsync("GameSelected", gameId);
                _logger.LogInformation("Game {GameId} selected for room {RoomCode} by {Host}", gameId, normalized, hostName ?? "(unknown)");
                return Ok(new { success = true, dbUpdated });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting GameSelected for room {Code}", normalized);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }
}
