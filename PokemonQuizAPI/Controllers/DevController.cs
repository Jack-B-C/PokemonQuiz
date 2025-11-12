using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using Microsoft.AspNetCore.Hosting;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/dev")]
    public class DevController : ControllerBase
    {
        private readonly DatabaseHelper _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<DevController> _logger;

        public DevController(DatabaseHelper db, IWebHostEnvironment env, ILogger<DevController> logger)
        {
            _db = db;
            _env = env;
            _logger = logger;
        }

        // Create or update the default admin user (development only)
        [HttpPost("ensure-admin")]
        public async Task<IActionResult> EnsureAdmin()
        {
            if (!_env.IsDevelopment())
                return NotFound();

            try
            {
                await _db.EnsureAdminUserAsync("jack", "jackaroonie636@gmail.com", "test");
                return Ok(new { message = "Admin ensured" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure admin");
                return StatusCode(500, new { message = "Failed to ensure admin" });
            }
        }

        // List users for debugging (development only)
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            if (!_env.IsDevelopment())
                return NotFound();

            try
            {
                var users = await _db.GetAllUsersDebugAsync();
                return Ok(new { users });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list users");
                return StatusCode(500, new { message = "Failed to list users" });
            }
        }

        // Dev: check credentials for a username/password
        [HttpPost("check-login")]
        public async Task<IActionResult> CheckLogin([FromBody] LoginCheck req)
        {
            if (!_env.IsDevelopment())
                return NotFound();

            if (req == null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest(new { message = "username and password required" });

            try
            {
                var userId = await _db.AuthenticateUserAsync(req.Username, req.Password);
                return Ok(new { success = userId != null, userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckLogin failed");
                return StatusCode(500, new { message = "Error checking credentials" });
            }
        }

        // Dev: get stored password hash for a username (debug only)
        [HttpGet("user-hash/{username}")]
        public async Task<IActionResult> GetUserHash(string username)
        {
            if (!_env.IsDevelopment())
                return NotFound();

            if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "username required" });

            try
            {
                var u = await _db.GetUserDebugByUsernameAsync(username);
                if (u.Id == null) return NotFound(new { message = "user not found" });
                return Ok(new { id = u.Id, username = u.Username, email = u.Email, hash = u.PasswordHash, role = u.Role });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetUserHash failed");
                return StatusCode(500, new { message = "Error reading user hash" });
            }
        }

        public class LoginCheck { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
    }
}
