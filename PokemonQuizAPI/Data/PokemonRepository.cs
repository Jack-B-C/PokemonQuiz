using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PokemonQuizAPI.Models;
using System.Collections.Generic;

namespace PokemonQuizAPI.Data
{
    public class PokemonRepository : IPokemonRepository
    {
        private readonly DatabaseHelper _db;
        private readonly ILogger<PokemonRepository> _logger;

        public PokemonRepository(DatabaseHelper db, ILogger<PokemonRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<List<PokemonData>> GetAllPokemonAsync(CancellationToken ct = default)
            => _db.GetAllPokemonAsync(ct);

        public Task<PokemonData?> GetPokemonByIdAsync(string id, CancellationToken ct = default)
            => _db.GetPokemonByIdAsync(id, ct);

        public Task<int> GetPokemonCountAsync(CancellationToken ct = default)
            => _db.GetPokemonCountAsync(ct);

        public Task<bool> InsertPokemonAsync(PokemonData pokemon, CancellationToken ct = default)
            => _db.InsertPokemonAsync(pokemon, ct);

        public Task<int> ClearAllPokemonAsync(CancellationToken ct = default)
            => _db.ClearAllPokemonAsync(ct);
    }
}