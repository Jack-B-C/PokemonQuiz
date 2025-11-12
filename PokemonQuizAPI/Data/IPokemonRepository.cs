using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using PokemonQuizAPI.Models;

namespace PokemonQuizAPI.Data
{
    public interface IPokemonRepository
    {
        Task<List<PokemonData>> GetAllPokemonAsync(CancellationToken ct = default);
        Task<PokemonData?> GetPokemonByIdAsync(string id, CancellationToken ct = default);
        Task<int> GetPokemonCountAsync(CancellationToken ct = default);
        Task<bool> InsertPokemonAsync(PokemonData pokemon, CancellationToken ct = default);
        Task<int> ClearAllPokemonAsync(CancellationToken ct = default);
    }
}