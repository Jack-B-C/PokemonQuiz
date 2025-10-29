using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Models;
using System.Text.Json;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SeedController(DatabaseHelper db, ILogger<SeedController> logger) : ControllerBase
    {
        private readonly DatabaseHelper _db = db;
        private readonly ILogger<SeedController> _logger = logger;
        private readonly HttpClient _httpClient = new();

        [HttpPost("pokemon")]
        [HttpGet("pokemon")]  // Allow GET for easy browser testing
        public async Task<IActionResult> SeedPokemon([FromQuery] int count = 151)
        {
            try
            {
                var existingCount = await _db.GetPokemonCountAsync();
                if (existingCount > 0)
                {
                    return BadRequest(new
                    {
                        message = $"Database already has {existingCount} Pokémon. Clear it first or use a different endpoint."
                    });
                }

                var seededCount = 0;
                var failedCount = 0;

                _logger.LogInformation("Starting to seed {Count} Pokémon from PokeAPI", count);

                for (int i = 1; i <= count; i++)
                {
                    try
                    {
                        var pokemon = await FetchPokemonFromPokeApi(i);
                        if (pokemon != null)
                        {
                            await _db.InsertPokemonAsync(pokemon);
                            seededCount++;
                            _logger.LogInformation("Seeded Pokémon {Number}/{Total}: {Name}", i, count, pokemon.Name);
                        }
                        else
                        {
                            failedCount++;
                        }

                        // Add small delay to avoid rate limiting
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to seed Pokémon #{Number}", i);
                        failedCount++;
                    }
                }

                return Ok(new
                {
                    message = "Seeding completed",
                    seeded = seededCount,
                    failed = failedCount,
                    total = count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error seeding Pokémon database");
                return StatusCode(500, new { message = "Seeding failed", error = ex.Message });
            }
        }

        private async Task<PokemonData?> FetchPokemonFromPokeApi(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"https://pokeapi.co/api/v2/pokemon/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch Pokémon #{Id} from PokeAPI", id);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var pokeApiData = JsonSerializer.Deserialize<JsonElement>(json);

                // Extract stats
                var stats = pokeApiData.GetProperty("stats");
                var hp = GetStatValue(stats, "hp");
                var attack = GetStatValue(stats, "attack");
                var defense = GetStatValue(stats, "defense");
                var specialAttack = GetStatValue(stats, "special-attack");
                var specialDefense = GetStatValue(stats, "special-defense");
                var speed = GetStatValue(stats, "speed");

                // Extract types
                var types = pokeApiData.GetProperty("types");
                var typeArray = types.EnumerateArray().ToList();
                var type1 = typeArray.Count > 0
                    ? typeArray[0].GetProperty("type").GetProperty("name").GetString()
                    : null;
                var type2 = typeArray.Count > 1
                    ? typeArray[1].GetProperty("type").GetProperty("name").GetString()
                    : null;

                // Get official artwork
                var imageUrl = pokeApiData
                    .GetProperty("sprites")
                    .GetProperty("other")
                    .GetProperty("official-artwork")
                    .GetProperty("front_default")
                    .GetString();

                var name = pokeApiData.GetProperty("name").GetString() ?? "Unknown";
                // Capitalize first letter
                if (name.Length > 0)
                {
                    name = char.ToUpper(name[0]) + name[1..];
                }

                return new PokemonData
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    Type1 = type1,
                    Type2 = type2,
                    Hp = hp,
                    Attack = attack,
                    Defence = defense,
                    SpecialAttack = specialAttack,
                    SpecialDefence = specialDefense,
                    Speed = speed,
                    ImageUrl = imageUrl,
                    FetchedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Pokémon #{Id} from PokeAPI", id);
                return null;
            }
        }

        private static int GetStatValue(JsonElement stats, string statName)
        {
            foreach (var stat in stats.EnumerateArray())
            {
                var name = stat.GetProperty("stat").GetProperty("name").GetString();
                if (name == statName)
                {
                    return stat.GetProperty("base_stat").GetInt32();
                }
            }
            return 0;
        }

        [HttpDelete("pokemon")]
        public IActionResult ClearPokemon()
        {
            try
            {
                // This would need a new method in DatabaseHelper
                // For now, just return instructions
                return Ok(new
                {
                    message = "To clear the database, run this SQL in HeidiSQL:",
                    sql = "DELETE FROM PokemonData;"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing Pokémon database");
                return StatusCode(500, new { message = "Clear failed", error = ex.Message });
            }
        }
    }
}