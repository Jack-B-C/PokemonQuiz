using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PokemonQuizAPI.Data
{
    public interface IGameRoomRepository
    {
        Task<bool> InsertGameRoomAsync(string roomCode, string? hostUserId = null, string? gameId = null, CancellationToken ct = default);
        Task<bool> GameRoomExistsAsync(string roomCode, CancellationToken ct = default);
        Task<bool> GameRoomExistsAnyStatusAsync(string roomCode, CancellationToken ct = default);
        Task<string?> GetGameIdForRoomAsync(string roomCode, CancellationToken ct = default);
        Task<bool> UpdateGameRoomGameIdAsync(string roomCode, string gameId, CancellationToken ct = default);
        Task<bool> EndGameRoomAsync(string roomCode, CancellationToken ct = default);
        Task<int> GetActiveRoomCountAsync(CancellationToken ct = default);
    }
}