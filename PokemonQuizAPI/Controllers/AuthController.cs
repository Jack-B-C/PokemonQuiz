using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly DatabaseHelper _db;
        private readonly ILogger<AuthController> _logger;

        public AuthController(DatabaseHelper db, ILogger<AuthController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.Email))
                return BadRequest("username, email and password required");

            // Basic email validation
            if (!IsValidEmail(req.Email))
                return BadRequest(new { message = "Invalid email address" });

            // Basic password policy: min 8 chars, at least one letter and one digit
            if (!IsStrongPassword(req.Password))
                return BadRequest(new { message = "Password must be at least 8 characters and include letters and numbers" });

            var (success, userId, error) = await _db.CreateUserAsync(req.Username, req.Email, req.Password);
            if (!success) return BadRequest(new { message = error });
            return Ok(new { userId });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("username and password required");

            var userId = await _db.AuthenticateUserAsync(req.Username, req.Password);
            if (userId == null) return Unauthorized(new { message = "Invalid credentials" });

            var token = await _db.CreateSessionTokenAsync(userId);
            return Ok(new { token, userId });
        }

        // New endpoint: return current user info based on Bearer token
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            try
            {
                var auth = Request.Headers["Authorization"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer "))
                    return Unauthorized(new { message = "Missing Authorization" });

                var token = auth.Substring("Bearer ".Length).Trim();
                var userId = await _db.GetUserIdForTokenAsync(token);
                if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { message = "Invalid token" });

                var info = await _db.GetUserInfoByIdAsync(userId);

                // Return role exactly as stored in DB (do not override with default here)
                var role = info.Role;

                // Ensure email is never null in response (frontend expects a string)
                var email = info.Email ?? string.Empty;

                return Ok(new { id = info.Id, username = info.Username, email, role });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Me endpoint failed");
                return StatusCode(500, new { message = "Failed to retrieve user info" });
            }
        }

        // New endpoint: return per-game stats for current user
        [HttpGet("stats")]
        public async Task<IActionResult> Stats()
        {
            try
            {
                var auth = Request.Headers["Authorization"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer "))
                    return Unauthorized(new { message = "Missing Authorization" });

                var token = auth.Substring("Bearer ".Length).Trim();
                var userId = await _db.GetUserIdForTokenAsync(token);
                if (string.IsNullOrWhiteSpace(userId)) return Unauthorized(new { message = "Invalid token" });

                _logger.LogInformation("Stats: resolving stats for user {UserId}", userId);
                var stats = await _db.GetUserGameStatsAsync(userId);
                return Ok(new { userId, stats });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Stats endpoint failed");
                return StatusCode(500, new { message = "Failed to retrieve stats" });
            }
        }

        private static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                var _ = new MailAddress(email);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsStrongPassword(string? password)
        {
            if (string.IsNullOrEmpty(password)) return false;
            if (password.Length < 8) return false;
            // at least one letter and one digit
            var hasLetter = Regex.IsMatch(password, "[A-Za-z]");
            var hasDigit = Regex.IsMatch(password, "[0-9]");
            return hasLetter && hasDigit;
        }

        public class RegisterRequest { public string Username { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
        public class LoginRequest { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
    }
}
