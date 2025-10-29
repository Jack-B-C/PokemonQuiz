using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController(DatabaseHelper db, ILogger<HealthController> logger) : ControllerBase
    {
        private readonly DatabaseHelper _db = db;
        private readonly ILogger<HealthController> _logger = logger;

        [HttpGet]
        public IActionResult GetHealth()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow,
                service = "PokemonQuizAPI"
            });
        }

        [HttpGet("database")]
        public async Task<IActionResult> CheckDatabase()
        {
            try
            {
                var isConnected = await _db.TestConnectionAsync();
                var pokemonCount = isConnected ? await _db.GetPokemonCountAsync() : 0;

                return Ok(new
                {
                    database = isConnected ? "connected" : "disconnected",
                    pokemonCount,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                return StatusCode(500, new
                {
                    database = "error",
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
            }
        }
    }
}