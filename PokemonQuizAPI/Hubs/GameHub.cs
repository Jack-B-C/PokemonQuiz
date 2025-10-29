using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace PokemonQuizAPI.Hubs
{
    public class GameHub(ILogger<GameHub> logger) : Hub
    {
        // In-memory store for players per room
        private static readonly ConcurrentDictionary<string, RoomData> Rooms = new();
        private readonly ILogger<GameHub> _logger = logger;

        public class Player
        {
            public string Name { get; set; } = string.Empty;
            public bool IsHost { get; set; }
            public string ConnectionId { get; set; } = string.Empty;
            public int Score { get; set; }
            public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        }

        public class RoomData
        {
            public string RoomCode { get; set; } = string.Empty;
            public List<Player> Players { get; set; } = [];
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public bool GameStarted { get; set; }
        }

        // Host creates room
        public async Task CreateRoom(string hostName)
        {
            try
            {
                // Generate a unique 4-character room code
                string roomCode;
                do
                {
                    roomCode = GenerateRoomCode();
                } while (Rooms.ContainsKey(roomCode));

                var hostPlayer = new Player
                {
                    Name = hostName,
                    IsHost = true,
                    ConnectionId = Context.ConnectionId,
                    Score = 0
                };

                var roomData = new RoomData
                {
                    RoomCode = roomCode,
                    Players = [hostPlayer],
                    GameStarted = false
                };

                Rooms[roomCode] = roomData;
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Room {RoomCode} created by {HostName}", roomCode, hostName);
                await Clients.Caller.SendAsync("RoomCreated", roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating room");
                await Clients.Caller.SendAsync("Error", "Failed to create room");
            }
        }

        // Player joins existing room
        public async Task JoinRoom(string roomCode, string playerName)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room does not exist");
                    return;
                }

                // Check if name is already taken
                if (roomData.Players.Count > 0 && roomData.Players.Exists(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                {
                    await Clients.Caller.SendAsync("Error", "Name already taken in this room");
                    return;
                }

                // Check if game already started
                if (roomData.GameStarted)
                {
                    await Clients.Caller.SendAsync("Error", "Game has already started");
                    return;
                }

                var newPlayer = new Player
                {
                    Name = playerName,
                    IsHost = false,
                    ConnectionId = Context.ConnectionId,
                    Score = 0
                };

                roomData.Players.Add(newPlayer);
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Player {PlayerName} joined room {RoomCode}", playerName, roomCode);

                // Send updated player list to all players in room
                await Clients.Group(roomCode).SendAsync("PlayerJoined", new
                {
                    playerName = newPlayer.Name,
                    players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                });

                // Send room info to the new player
                await Clients.Caller.SendAsync("RoomJoined", new
                {
                    roomCode,
                    players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to join room");
            }
        }

        // Player leaves room
        public async Task LeaveRoom(string roomCode)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    return;
                }

                var player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player == null)
                {
                    return;
                }

                roomData.Players.Remove(player);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Player {PlayerName} left room {RoomCode}", player.Name, roomCode);

                // If room is empty or host left, clean up room
                if (roomData.Players.Count == 0 || player.IsHost)
                {
                    Rooms.TryRemove(roomCode, out _);
                    await Clients.Group(roomCode).SendAsync("RoomClosed", "Host has left the room");
                    _logger.LogInformation("Room {RoomCode} closed", roomCode);
                }
                else
                {
                    // Notify remaining players
                    await Clients.Group(roomCode).SendAsync("PlayerLeft", new
                    {
                        playerName = player.Name,
                        players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error leaving room {RoomCode}", roomCode);
            }
        }

        // Start game (host only)
        public async Task StartGame(string roomCode)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room not found");
                    return;
                }

                var host = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId && p.IsHost);
                if (host == null)
                {
                    await Clients.Caller.SendAsync("Error", "Only host can start the game");
                    return;
                }

                roomData.GameStarted = true;
                _logger.LogInformation("Game started in room {RoomCode} by {HostName}", roomCode, host.Name);

                await Clients.Group(roomCode).SendAsync("GameStarted");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting game in room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to start game");
            }
        }

        // Update player score
        public async Task UpdateScore(string roomCode, int scoreChange)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    return;
                }

                var player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    player.Score += scoreChange;

                    await Clients.Group(roomCode).SendAsync("ScoreUpdated", new
                    {
                        playerName = player.Name,
                        score = player.Score,
                        players = roomData.Players.Select(p => new { p.Name, p.Score }).OrderByDescending(p => p.Score)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating score in room {RoomCode}", roomCode);
            }
        }

        // Handle disconnection
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                // Find and remove player from any room
                foreach (var room in Rooms.Values)
                {
                    var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                    if (player != null)
                    {
                        await LeaveRoom(room.RoomCode);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling disconnection");
            }

            await base.OnDisconnectedAsync(exception);
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Removed confusing characters
            var random = new Random();
            return new string([.. Enumerable.Range(0, 4).Select(_ => chars[random.Next(chars.Length)])]);
        }
    }
}