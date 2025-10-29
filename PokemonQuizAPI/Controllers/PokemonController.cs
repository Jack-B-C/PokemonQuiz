using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Models;
using PokemonQuizAPI.Data;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PokemonController(DatabaseHelper db) : ControllerBase
    {
        private readonly DatabaseHelper _db = db;

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var pokemonList = await _db.GetAllPokemonAsync();
            return Ok(pokemonList);
        }
    }
}