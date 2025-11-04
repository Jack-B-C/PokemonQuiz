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
