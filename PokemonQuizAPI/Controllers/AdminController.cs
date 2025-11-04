using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;
using System.Reflection;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminController : ControllerBase
    {
        private readonly DatabaseHelper _db;
        private readonly ILogger<AdminController> _logger;

        public AdminController(DatabaseHelper db, ILogger<AdminController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            // Minimal metrics: active rooms, active players, total games played (from DB)
            var rooms = GameHub.GetRoomsSnapshot();
            var activeRooms = rooms.Count;

            // Collect distinct connection ids from rooms in a null-safe way
            var connectionIds = new HashSet<string>();
            foreach (var roomObj in rooms)
            {
                if (roomObj == null) continue;
                var t = roomObj.GetType();
                var playersProp = t.GetProperty("players") ?? t.GetProperty("Players");
                if (playersProp == null) continue;
                var playersVal = playersProp.GetValue(roomObj) as System.Collections.IEnumerable;
                if (playersVal == null) continue;

                foreach (var p in playersVal)
                {
                    if (p == null) continue;
                    var pt = p.GetType();
                    var connProp = pt.GetProperty("ConnectionId") ?? pt.GetProperty("connectionId") ?? pt.GetProperty("ConnectionID");
                    if (connProp == null) continue;
                    var connVal = connProp.GetValue(p)?.ToString();
                    if (!string.IsNullOrWhiteSpace(connVal)) connectionIds.Add(connVal);
                }
            }

            var activePlayers = connectionIds.Count;

            // Get per-game stats from DB helper
            var stats = await _db.GetGameStatsAsync();
            var totalGamesPlayed = stats.Sum(s => s.GamesPlayed);

            return Ok(new {
                activeRooms,
                activePlayers,
                totalGamesPlayed,
                rooms,
                gameStats = stats
            });
        }

        [HttpGet("rooms")]
        public IActionResult GetRooms()
        {
            return Ok(GameHub.GetRoomsSnapshot());
        }

        [HttpGet("stats/games")]
        public async Task<IActionResult> GetGameStats()
        {
            var stats = await _db.GetGameStatsAsync();
            return Ok(stats);
        }

        [HttpPost("rooms/{code}/end")]
        public IActionResult EndRoom(string code)
        {
            if (GameHub.TryRemoveRoom(code))
            {
                _logger.LogInformation("Admin removed room {RoomCode}", code);
                return Ok(new { success = true });
            }
            return NotFound(new { success = false, message = "Room not found" });
        }

        [HttpGet("stats/users")] // later: implement user stats
        public IActionResult UserStats()
        {
            // Placeholder
            return Ok(new { users = new string[] { } });
        }

        [HttpGet("debug/users")]
        public async Task<IActionResult> GetAllUsersDebug()
        {
            // Require admin auth: middleware sets HttpContext.Items["UserId"] when admin token provided
            if (!HttpContext.Items.TryGetValue("UserId", out var userIdObj) || userIdObj == null)
            {
                return Unauthorized();
            }

            var list = await _db.GetAllUsersDebugAsync();
            return Ok(list);
        }
    }
}
