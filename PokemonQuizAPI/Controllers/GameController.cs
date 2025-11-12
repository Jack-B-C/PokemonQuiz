using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Models;
using PokemonQuizAPI.Data;

namespace PokemonQuizAPI.Controllers
{
    public class StatOption
    {
        public string Stat { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class GameResponse
    {
        public string PokemonName { get; set; } = string.Empty;
        public string Image_Url { get; set; } = string.Empty;
        public string StatToGuess { get; set; } = string.Empty;
        public int CorrectValue { get; set; }
        public List<StatOption> OtherValues { get; set; } = new();
    }

    public class GameModeInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Multiplayer { get; set; }
    }

    [ApiController]
    [Route("api/[controller]")]
    public class GameController(DatabaseHelper db, ILogger<GameController> logger) : ControllerBase
    {
        private readonly DatabaseHelper _db = db;
        private readonly ILogger<GameController> _logger = logger;

        [HttpGet("modes")]
        public IActionResult GetModes()
        {
            var modes = new List<GameModeInfo>
            {
                new GameModeInfo
                {
                    Id = "guess-stats",
                    Name = "Guess Stats",
                    Description = "Guess which stat value belongs to the Pokémon (multiplayer-ready).",
                    Multiplayer = true
                },
                new GameModeInfo
                {
                    Id = "silhouette",
                    Name = "Silhouette Guess",
                    Description = "Guess the Pokémon from its silhouette (singleplayer or multiplayer).",
                    Multiplayer = true
                }
            };

            return Ok(modes);
        }

        [HttpGet("random")]
        public async Task<IActionResult> GetRandomPokemon()
        {
            try
            {
                var allPokemon = await _db.GetAllPokemonAsync();

                if (allPokemon.Count == 0)
                {
                    return NotFound(new { message = "No Pokémon data available. Please seed the database first at POST /api/seed/pokemon" });
                }

                var random = new Random();
                var selectedPokemon = allPokemon[random.Next(allPokemon.Count)];

                // Pick a random stat to guess
                var stats = new Dictionary<string, int>
                {
                    ["HP"] = selectedPokemon.Hp,
                    ["Attack"] = selectedPokemon.Attack,
                    ["Defence"] = selectedPokemon.Defence,
                    ["Special Attack"] = selectedPokemon.SpecialAttack,
                    ["Special Defence"] = selectedPokemon.SpecialDefence,
                    ["Speed"] = selectedPokemon.Speed
                };

                var statToGuess = stats.ElementAt(random.Next(stats.Count));
                var correctValue = statToGuess.Value;

                // Generate 3 fake values in multiples of 10, 20, 30, 40, 50
                var otherValues = new List<StatOption>();
                var usedValues = new HashSet<int> { correctValue };

                var intervals = new[] { 10, 20, 30, 40, 50 };

                while (otherValues.Count < 3)
                {
                    var offset = intervals[random.Next(intervals.Length)];
                    if (random.Next(2) == 0) offset = -offset; // randomly above or below

                    var fakeValue = correctValue + offset;

                    // Ensure minimum stat of 5 and max 255
                    fakeValue = Math.Max(5, Math.Min(255, fakeValue));

                    if (usedValues.Add(fakeValue))
                    {
                        otherValues.Add(new StatOption
                        {
                            Stat = statToGuess.Key,
                            Value = fakeValue
                        });
                    }
                }

                var response = new GameResponse
                {
                    PokemonName = selectedPokemon.Name,
                    Image_Url = selectedPokemon.ImageUrl ?? string.Empty,
                    StatToGuess = statToGuess.Key,
                    CorrectValue = correctValue,
                    OtherValues = otherValues
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching random Pokémon");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpGet("pokemon/{id}")]
        public async Task<IActionResult> GetPokemonById(string id)
        {
            try
            {
                var pokemon = await _db.GetPokemonByIdAsync(id);

                if (pokemon == null)
                {
                    return NotFound(new { message = "Pokémon not found" });
                }

                return Ok(pokemon);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Pokémon by ID");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        [HttpGet("pokemon")]
        public async Task<IActionResult> GetAllPokemon([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            try
            {
                var allPokemon = await _db.GetAllPokemonAsync();
                var totalCount = allPokemon.Count;

                var paginatedPokemon = allPokemon
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();

                return Ok(new
                {
                    data = paginatedPokemon,
                    page,
                    pageSize,
                    totalCount,
                    totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all Pokémon");
                return StatusCode(500, new { message = "Internal server error" });
            }
        }

        public class SubmitResultRequest
        {
            public string? GameId { get; set; }
            public int Score { get; set; }
            public int TotalQuestions { get; set; }
            public int CorrectAnswers { get; set; }
        }

        // POST /api/game/submit-result
        [HttpPost("submit-result")]
        public async Task<IActionResult> SubmitResult([FromBody] SubmitResultRequest req)
        {
            if (req == null) return BadRequest(new { message = "Missing payload" });

            try
            {
                // Resolve user id from bearer token if present
                var auth = Request.Headers["Authorization"].FirstOrDefault();
                string? userId = null;
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer "))
                {
                    var token = auth.Substring("Bearer ".Length).Trim();
                    userId = await _db.GetUserIdForTokenAsync(token);
                }

                // create a new GameSession record
                var sessionId = Guid.NewGuid().ToString();
                try
                {
                    await _db.CreateGameSessionAsync(sessionId, req.GameId, userId, null, req.Score, DateTime.UtcNow);
                    await _db.EndGameSessionAsync(sessionId, DateTime.UtcNow);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist submitted result for user {UserId}", userId);
                }

                return Ok(new { sessionId, userId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SubmitResult failed");
                return StatusCode(500, new { message = "Failed to submit result" });
            }
        }
    }
}
