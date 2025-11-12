using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Hubs;
using Microsoft.AspNetCore.Hosting;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/admin")]
    public class AdminPanelController : ControllerBase
    {
        private readonly DatabaseHelper _db;
        private readonly ILogger<AdminPanelController> _logger;
        private readonly IGameRoomRepository _gameRoomRepo;
        private readonly IWebHostEnvironment _env;

        public AdminPanelController(DatabaseHelper db, ILogger<AdminPanelController> logger, IGameRoomRepository gameRoomRepo, IWebHostEnvironment env)
        {
            _db = db;
            _logger = logger;
            _gameRoomRepo = gameRoomRepo;
            _env = env;
        }

        // GET /api/admin/overview (backwards compat)
        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            try
            {
                var pokemonCount = await _db.GetPokemonCountAsync();
                var totalGames = await _db.GetTotalGamesPlayedAsync();
                var stats = await _db.GetGameStatsAsync();

                return Ok(new { pokemonCount, totalGames, stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get admin overview");
                return StatusCode(500, new { message = "Failed to retrieve overview" });
            }
        }

        // New: GET /api/admin/dashboard used by SPA
        [HttpGet("dashboard")]
        public IActionResult Dashboard()
        {
            try
            {
                // active rooms from in-memory hub snapshot
                var rooms = GameHub.GetRoomsSnapshot();
                var activeRooms = rooms.Count;
                var activePlayers = rooms.Sum(r => ((IEnumerable<object>)((dynamic)r).players).Count());

                return Ok(new { activeRooms, activePlayers, rooms });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get dashboard data");
                return StatusCode(500, new { message = "Failed to retrieve dashboard" });
            }
        }

        // GET /api/admin/users
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            try
            {
                var users = await _db.GetAllUsersDebugAsync();
                var counts = await _db.GetGamesPlayedCountsAsync();

                // merge counts into returned users
                var enriched = users.Select(u =>
                {
                    var id = u.GetType().GetProperty("id")?.GetValue(u)?.ToString() ?? string.Empty;
                    var username = u.GetType().GetProperty("username")?.GetValue(u)?.ToString() ?? string.Empty;
                    var email = u.GetType().GetProperty("email")?.GetValue(u)?.ToString() ?? string.Empty;
                    var hasPassword = u.GetType().GetProperty("hasPassword")?.GetValue(u) as bool? ?? false;
                    var gamesPlayed = 0;
                    if (!string.IsNullOrWhiteSpace(id) && counts.TryGetValue(id, out var gp)) gamesPlayed = gp;
                    return new { id, username, email, hasPassword, gamesPlayed };
                }).ToList();

                return Ok(new { users = enriched });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list users");
                return StatusCode(500, new { message = "Failed to list users" });
            }
        }

        // GET /api/admin/users/{userId}/stats
        [HttpGet("users/{userId}/stats")]
        public async Task<IActionResult> GetUserStats(string userId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userId)) return BadRequest(new { message = "Missing user id" });
                var stats = await _db.GetUserGameStatsAsync(userId);
                return Ok(new { userId, stats });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get user stats for {UserId}", userId);
                return StatusCode(500, new { message = "Failed to retrieve user stats" });
            }
        }

        // GET /api/admin/rooms
        [HttpGet("rooms")]
        public IActionResult GetRooms()
        {
            try
            {
                var rooms = GameHub.GetRoomsSnapshot();
                return Ok(new { rooms });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list rooms");
                return StatusCode(500, new { message = "Failed to list rooms" });
            }
        }

        // POST /api/admin/rooms/{code}/end
        [HttpPost("rooms/{code}/end")]
        public async Task<IActionResult> EndRoom(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return BadRequest(new { message = "Missing room code" });
            var roomCode = code.ToUpperInvariant();

            try
            {
                // remove from in-memory hub
                var removed = GameHub.TryRemoveRoom(roomCode);

                // mark ended in DB
                try
                {
                    await _gameRoomRepo.EndGameRoomAsync(roomCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to mark room {RoomCode} ended in DB", roomCode);
                }

                return Ok(new { removed });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to end room {RoomCode}", roomCode);
                return StatusCode(500, new { message = "Failed to end room" });
            }
        }

        // POST /api/admin/clear-pokemon
        [HttpPost("clear-pokemon")]
        public async Task<IActionResult> ClearPokemon()
        {
            try
            {
                var cleared = await _db.ClearAllPokemonAsync();
                return Ok(new { cleared });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear pokemon");
                return StatusCode(500, new { message = "Failed to clear pokemon" });
            }
        }

        // POST /api/admin/users/{userId}/role
        [HttpPost("users/{userId}/role")]
        public async Task<IActionResult> SetUserRole(string userId, [FromBody] RoleRequest req)
        {
            if (!_env.IsDevelopment()) return NotFound(); // protect in prod
            if (string.IsNullOrWhiteSpace(userId) || req == null || string.IsNullOrWhiteSpace(req.Role)) return BadRequest(new { message = "userId and role required" });

            try
            {
                var ok = await _db.SetUserRoleAsync(userId, req.Role);
                if (!ok) return NotFound(new { message = "user not found or update failed" });
                return Ok(new { userId, role = req.Role });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set role for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to set role" });
            }
        }

        // GET /api/admin/users/{userId}/sessions
        [HttpGet("users/{userId}/sessions")]
        public async Task<IActionResult> GetUserSessions(string userId)
        {
            if (!_env.IsDevelopment()) return NotFound();
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest(new { message = "Missing userId" });
            try
            {
                var sessions = await _db.GetSessionsByUserIdAsync(userId);
                return Ok(new { userId, sessions });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list sessions for user {UserId}", userId);
                return StatusCode(500, new { message = "Failed to list sessions" });
            }
        }

        // New: GET /api/admin/me returns info about the authenticated admin user (for SPA)
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            try
            {
                string? userId = null;
                if (HttpContext.Items.ContainsKey("UserId")) userId = HttpContext.Items["UserId"] as string;

                if (!string.IsNullOrWhiteSpace(userId))
                {
                    var info = await _db.GetUserInfoByIdAsync(userId);
                    if (!string.IsNullOrWhiteSpace(info.Id))
                    {
                        return Ok(new { id = info.Id, username = info.Username, email = info.Email, role = info.Role });
                    }
                }

                // Development fallback: try to return the seeded 'jack' admin account if present
                if (_env.IsDevelopment())
                {
                    try
                    {
                        var debug = await _db.GetUserDebugByUsernameAsync("jack");
                        if (!string.IsNullOrWhiteSpace(debug.Item1) || !string.IsNullOrWhiteSpace(debug.Item2))
                        {
                            return Ok(new { id = debug.Item1, username = debug.Item2, email = debug.Item3, role = debug.Item5 });
                        }
                    }
                    catch { }

                    // Final fallback for dev: return a default admin user
                    return Ok(new { id = "dev-admin", username = "Administrator", email = (string?)null, role = "admin" });
                }

                return Unauthorized(new { message = "Not authenticated" });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get current admin user");
                return StatusCode(500, new { message = "Failed to retrieve user info" });
            }
        }

        public class RoleRequest { public string Role { get; set; } = string.Empty; }
    }
}
