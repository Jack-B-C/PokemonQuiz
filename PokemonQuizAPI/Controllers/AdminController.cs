using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Hubs;
using PokemonQuizAPI.Data;

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
        public IActionResult Dashboard()
        {
            // Minimal metrics: active rooms, active players, total games played (approx from DB)
            var rooms = GameHub.GetRoomsSnapshot();
            var activeRooms = rooms.Count;
            var activePlayers = rooms.SelectMany(r => ((dynamic)r).players as System.Collections.Generic.List<dynamic>).Select(p => p.ConnectionId).Distinct().Count();

            var totalGamesPlayed = 0; // placeholder - implement if you persist game results

            return Ok(new {
                activeRooms,
                activePlayers,
                totalGamesPlayed,
                rooms
            });
        }

        [HttpGet("rooms")]
        public IActionResult GetRooms()
        {
            return Ok(GameHub.GetRoomsSnapshot());
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
    }
}
