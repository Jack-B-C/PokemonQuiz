using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PokemonQuizAPI.Data
{
    public class GameRoomRepository : IGameRoomRepository
    {
        private readonly DatabaseHelper _db;
        private readonly ILogger<GameRoomRepository> _logger;

        public GameRoomRepository(DatabaseHelper db, ILogger<GameRoomRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public Task<bool> InsertGameRoomAsync(string roomCode, string? hostUserId = null, string? gameId = null, CancellationToken ct = default)
            => _db.InsertGameRoomAsync(roomCode, hostUserId, gameId, ct);

        public Task<bool> GameRoomExistsAsync(string roomCode, CancellationToken ct = default)
            => _db.GameRoomExistsAsync(roomCode, ct);

        public Task<bool> GameRoomExistsAnyStatusAsync(string roomCode, CancellationToken ct = default)
            => _db.GameRoomExistsAnyStatusAsync(roomCode, ct);

        public Task<string?> GetGameIdForRoomAsync(string roomCode, CancellationToken ct = default)
            => _db.GetGameIdForRoomAsync(roomCode, ct);

        public Task<bool> UpdateGameRoomGameIdAsync(string roomCode, string gameId, CancellationToken ct = default)
            => _db.UpdateGameRoomGameIdAsync(roomCode, gameId, ct);

        public Task<bool> EndGameRoomAsync(string roomCode, CancellationToken ct = default)
            => _db.EndGameRoomAsync(roomCode, ct);

        public Task<int> GetActiveRoomCountAsync(CancellationToken ct = default)
            => _db.GetActiveRoomCountAsync(ct);
    }
}