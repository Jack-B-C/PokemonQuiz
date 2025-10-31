using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using PokemonQuizAPI.Data;
using Newtonsoft.Json.Linq;

namespace PokemonQuizAPI.Hubs
{
    public class GameHub : Hub
    {
        private static readonly ConcurrentDictionary<string, RoomData> Rooms = new();
        private readonly ILogger<GameHub> _logger;
        private readonly DatabaseHelper _db;

        public GameHub(ILogger<GameHub> logger, DatabaseHelper db)
        {
            _logger = logger;
            _db = db;
        }

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
            public List<Player> Players { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public bool GameStarted { get; set; }
            public string? SelectedGameId { get; set; }

            // Multiplayer game state
            public object? CurrentQuestion { get; set; }
            public DateTime? QuestionStartedAt { get; set; }

            // Track submissions for current question (ConnectionId -> payload)
            public Dictionary<string, object> Submissions { get; set; } = new();
        }

        // Helper: normalize an incoming question JObject to a consistent outgoing shape
        private JObject BuildNormalizedQuestion(JObject source)
        {
            // Try multiple token names for compatibility
            var pokemonName = source.SelectToken("pokemonName")?.ToString()
                ?? source.SelectToken("PokemonName")?.ToString()
                ?? source.SelectToken("name")?.ToString()
                ?? string.Empty;

            var imageUrl = source.SelectToken("image_Url")?.ToString()
                ?? source.SelectToken("Image_Url")?.ToString()
                ?? source.SelectToken("imageUrl")?.ToString()
                ?? source.SelectToken("ImageUrl")?.ToString()
                ?? string.Empty;

            var statToGuess = source.SelectToken("statToGuess")?.ToString()
                ?? source.SelectToken("StattoGuess")?.ToString()
                ?? source.SelectToken("stat")?.ToString()
                ?? string.Empty;

            // correctValue may be number or string
            int correctValue = 0;
            var correctToken = source.SelectToken("correctValue") ?? source.SelectToken("CorrectValue") ?? source.SelectToken("correct_value");
            if (correctToken != null)
            {
                if (correctToken.Type == JTokenType.Integer || correctToken.Type == JTokenType.Float)
                {
                    correctValue = correctToken.ToObject<int>();
                }
                else
                {
                    int.TryParse(correctToken.ToString(), out correctValue);
                }
            }

            // Build otherValues array
            var otherValues = new JArray();
            var ovToken = source.SelectToken("otherValues") ?? source.SelectToken("OtherValues") ?? source.SelectToken("other_values");
            if (ovToken is JArray arr)
            {
                foreach (var item in arr)
                {
                    var stat = item.SelectToken("stat")?.ToString() ?? item.SelectToken("Stat")?.ToString() ?? statToGuess ?? string.Empty;
                    var valToken = item.SelectToken("value") ?? item.SelectToken("Value");
                    int val = 0;
                    if (valToken != null)
                    {
                        if (valToken.Type == JTokenType.Integer || valToken.Type == JTokenType.Float) val = valToken.ToObject<int>();
                        else int.TryParse(valToken.ToString(), out val);
                    }

                    otherValues.Add(new JObject { ["stat"] = stat, ["value"] = val });
                }
            }

            // Ensure we have 3 other values (server-side generation ensures this, client-sent may vary)
            var normalized = new JObject
            {
                ["pokemonName"] = pokemonName,
                ["image_Url"] = imageUrl,
                ["statToGuess"] = statToGuess,
                ["correctValue"] = correctValue,
                ["otherValues"] = otherValues
            };

            return normalized;
        }

        // ------------------ Create Room ------------------
        public async Task CreateRoom(string hostName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hostName))
                {
                    await Clients.Caller.SendAsync("Error", "Host name cannot be empty.");
                    return;
                }

                string roomCode;
                do
                {
                    roomCode = GenerateRoomCode();
                } while (Rooms.ContainsKey(roomCode));

                var hostPlayer = new Player
                {
                    Name = hostName.Trim(),
                    IsHost = true,
                    ConnectionId = Context.ConnectionId,
                    Score = 0
                };

                var roomData = new RoomData
                {
                    RoomCode = roomCode,
                    Players = new List<Player> { hostPlayer },
                    GameStarted = false,
                    SelectedGameId = null
                };

                // Persist room to database
                try
                {
                    await _db.InsertGameRoomAsync(roomCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to persist room {RoomCode} to database", roomCode);
                    await Clients.Caller.SendAsync("Error", "Failed to create room on server.");
                    return;
                }

                Rooms[roomCode] = roomData;
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Room {RoomCode} created by {HostName}", roomCode, hostName);

                // Send back room code and host info
                await Clients.Caller.SendAsync("RoomCreated", new
                {
                    roomCode,
                    players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating room");
                await Clients.Caller.SendAsync("Error", "Failed to create room.");
            }
        }

        // ------------------ Select Game (host only) ------------------
        public async Task SelectGame(string roomCode, string gameId, string? hostName = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(gameId))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid room or game selection.");
                    return;
                }

                roomCode = roomCode.ToUpper();
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room not found.");
                    return;
                }

                // Authorize: either caller connection is host, or provided hostName matches the host player name
                var hostByConnection = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId && p.IsHost);
                Player? hostByName = null;
                if (!string.IsNullOrWhiteSpace(hostName))
                {
                    hostByName = roomData.Players.FirstOrDefault(p => p.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase) && p.IsHost);
                }

                if (hostByConnection == null && hostByName == null)
                {
                    await Clients.Caller.SendAsync("Error", "Only the host can select a game.");
                    return;
                }

                roomData.SelectedGameId = gameId;

                _logger.LogInformation("Host selected game {GameId} for room {RoomCode}", gameId, roomCode);

                // Broadcast selected game to all clients in room
                await Clients.Group(roomCode).SendAsync("GameSelected", gameId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error selecting game for room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to select game.");
            }
        }

        // ------------------ Join Room ------------------
        public async Task JoinRoom(string roomCode, string playerName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(playerName))
                {
                    await Clients.Caller.SendAsync("Error", "Player name cannot be empty.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(roomCode))
                {
                    await Clients.Caller.SendAsync("Error", "Room code cannot be empty.");
                    return;
                }

                roomCode = roomCode.ToUpper();

                // If room not in memory, check DB and rehydrate minimal room if it exists
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    try
                    {
                        var exists = await _db.GameRoomExistsAsync(roomCode);
                        if (exists)
                        {
                            roomData = new RoomData
                            {
                                RoomCode = roomCode,
                                Players = new List<Player>(),
                                GameStarted = false,
                                CreatedAt = DateTime.UtcNow
                            };

                            // Populate selected game from DB if set
                            try
                            {
                                var gameId = await _db.GetGameIdForRoomAsync(roomCode);
                                if (!string.IsNullOrWhiteSpace(gameId))
                                {
                                    roomData.SelectedGameId = gameId;
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to fetch game id for room {RoomCode}", roomCode);
                            }

                            Rooms[roomCode] = roomData;
                            _logger.LogInformation("Rehydrated room {RoomCode} from database into memory", roomCode);
                        }
                        else
                        {
                            await Clients.Caller.SendAsync("Error", "Invalid code — room not found.");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error checking database for room {RoomCode}", roomCode);
                        await Clients.Caller.SendAsync("Error", "Failed to join room (server error).");
                        return;
                    }
                }

                if (roomData.GameStarted)
                {
                    await Clients.Caller.SendAsync("Error", "Game already started.");
                    return;
                }

                if (roomData.Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                {
                    await Clients.Caller.SendAsync("Error", "Name already taken in this room.");
                    return;
                }

                var newPlayer = new Player
                {
                    Name = playerName.Trim(),
                    IsHost = false,
                    ConnectionId = Context.ConnectionId,
                    Score = 0
                };

                roomData.Players.Add(newPlayer);
                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Player {PlayerName} joined room {RoomCode}", playerName, roomCode);

                // Notify all players in the room
                await Clients.Group(roomCode).SendAsync("PlayerJoined", new
                {
                    playerName = newPlayer.Name,
                    players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                });

                // Send joined confirmation to caller, include current question if present
                object? currentQuestion = null;
                string? questionStartedAt = null;
                if (roomData.CurrentQuestion != null)
                {
                    try
                    {
                        currentQuestion = roomData.CurrentQuestion is JObject j ? j : JObject.FromObject(roomData.CurrentQuestion);
                    }
                    catch
                    {
                        currentQuestion = roomData.CurrentQuestion;
                    }

                    if (roomData.QuestionStartedAt.HasValue)
                    {
                        questionStartedAt = roomData.QuestionStartedAt.Value.ToString("o");
                    }
                }

                await Clients.Caller.SendAsync("RoomJoined", new
                {
                    roomCode,
                    players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score }),
                    selectedGame = roomData.SelectedGameId,
                    currentQuestion,
                    questionStartedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error joining room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to join room.");
            }
        }

        // ------------------ Leave Room ------------------
        public async Task LeaveRoom(string roomCode)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                    return;

                var player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player == null)
                    return;

                roomData.Players.Remove(player);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);

                if (player.IsHost || roomData.Players.Count == 0)
                {
                    Rooms.TryRemove(roomCode, out _);
                    // Mark room ended in database
                    try
                    {
                        // If you want to mark ended, implement Update to set ended_at (not implemented in DB helper yet)
                        // await _db.EndGameRoomAsync(roomCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to mark room {RoomCode} ended in database", roomCode);
                    }

                    await Clients.Group(roomCode).SendAsync("RoomClosed", "Host left the room — room closed.");
                    _logger.LogInformation("Room {RoomCode} closed (host left)", roomCode);
                }
                else
                {
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

        // ------------------ Start Game ------------------
        public async Task StartGame(string roomCode)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room not found.");
                    return;
                }

                var host = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId && p.IsHost);
                if (host == null)
                {
                    await Clients.Caller.SendAsync("Error", "Only the host can start the game.");
                    return;
                }

                // Try to generate the first question before marking the game as started
                JObject? questionObj = null;
                try
                {
                    questionObj = await GenerateRandomQuestionAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to generate first question for room {RoomCode}", roomCode);
                }

                if (questionObj == null)
                {
                    // No Pokemon data available — inform the caller and do not start the game
                    await Clients.Caller.SendAsync("Error", "No Pokémon data available. Please seed the database first at POST /api/seed/pokemon");
                    return;
                }

                // We have a valid question — start the game and broadcast
                roomData.GameStarted = true;

                _logger.LogInformation("Game started in room {RoomCode} by {HostName}", roomCode, host.Name);

                // include selected game id so clients know which game to navigate to
                await Clients.Group(roomCode).SendAsync("GameStarted", roomData.SelectedGameId);

                try
                {
                    // Normalize and store the generated question, then broadcast
                    var normalized = BuildNormalizedQuestion(questionObj);
                    roomData.CurrentQuestion = normalized;
                    roomData.QuestionStartedAt = DateTime.UtcNow;
                    roomData.Submissions = new Dictionary<string, object>();

                    // convert to simple serializable object
                    var plain = ConvertNormalizedToPlainObject(normalized);
                    _logger.LogDebug("Broadcasting Question for room {RoomCode}: {Question}", roomCode, normalized.ToString());

                    await Clients.Group(roomCode).SendAsync("Question", plain);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send first question for room {RoomCode}", roomCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting game in room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to start game.");
            }
        }

        private async Task<JObject?> GenerateRandomQuestionAsync()
        {
            try
            {
                var allPokemon = await _db.GetAllPokemonAsync();
                if (allPokemon == null || allPokemon.Count == 0) return null;

                var random = new Random();
                var selected = allPokemon[random.Next(allPokemon.Count)];

                var stats = new Dictionary<string, int>
                {
                    ["HP"] = selected.Hp,
                    ["Attack"] = selected.Attack,
                    ["Defence"] = selected.Defence,
                    ["Special Attack"] = selected.SpecialAttack,
                    ["Special Defence"] = selected.SpecialDefence,
                    ["Speed"] = selected.Speed
                };

                var statEntry = stats.ElementAt(random.Next(stats.Count));
                var correctValue = statEntry.Value;

                var otherValues = new JArray();
                var used = new HashSet<int> { correctValue };
                var intervals = new[] { 10, 20, 30, 40, 50 };

                while (otherValues.Count < 3)
                {
                    var offset = intervals[random.Next(intervals.Length)];
                    if (random.Next(2) == 0) offset = -offset;
                    var fake = correctValue + offset;
                    fake = Math.Max(5, Math.Min(255, fake));
                    if (used.Add(fake))
                    {
                        otherValues.Add(new JObject { ["stat"] = statEntry.Key, ["value"] = fake });
                    }
                }

                var obj = new JObject
                {
                    ["pokemonName"] = selected.Name,
                    ["image_Url"] = selected.ImageUrl ?? string.Empty,
                    ["statToGuess"] = statEntry.Key,
                    ["correctValue"] = correctValue,
                    ["otherValues"] = otherValues
                };

                return obj;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating random question");
                return null;
            }
        }

        // ------------------ Send Question (host only) ------------------
        public async Task SendQuestionToRoom(string roomCode, object questionData)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(roomCode) || questionData == null)
                {
                    await Clients.Caller.SendAsync("Error", "Invalid room or question data.");
                    return;
                }

                roomCode = roomCode.ToUpper();
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room not found.");
                    return;
                }

                var host = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId && p.IsHost);
                if (host == null)
                {
                    await Clients.Caller.SendAsync("Error", "Only the host can send questions.");
                    return;
                }

                JObject? questionObj = null;
                try
                {
                    if (questionData is JObject j) questionObj = j;
                    else questionObj = JObject.FromObject(questionData);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to convert questionData to JObject for room {RoomCode}", roomCode);
                    await Clients.Caller.SendAsync("Error", "Invalid question payload.");
                    return;
                }

                // Normalize outgoing shape to ensure clients receive consistent fields
                var normalized = BuildNormalizedQuestion(questionObj);

                roomData.CurrentQuestion = normalized;
                roomData.QuestionStartedAt = DateTime.UtcNow;
                roomData.Submissions = new Dictionary<string, object>();

                _logger.LogDebug("SendQuestionToRoom broadcasting for {RoomCode}: {Question}", roomCode, normalized.ToString());

                // convert to simple serializable object
                var plainOut = ConvertNormalizedToPlainObject(normalized);
                await Clients.Group(roomCode).SendAsync("Question", plainOut);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending question to room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to send question.");
            }
        }

        // ------------------ Submit Answer (clients) ------------------
        public async Task SubmitAnswer(string roomCode, int selectedValue, int timeTakenMs)
        {
            try
            {
                roomCode = roomCode.ToUpper();
                if (!Rooms.TryGetValue(roomCode, out var roomData) || roomData.CurrentQuestion == null)
                    return;

                var player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player == null)
                    return;

                JObject? question = null;
                try
                {
                    if (roomData.CurrentQuestion is JObject jq) question = jq;
                    else question = roomData.CurrentQuestion is string s ? JObject.Parse(s) : JObject.FromObject(roomData.CurrentQuestion);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse CurrentQuestion for room {RoomCode}", roomCode);
                    await Clients.Caller.SendAsync("Error", "Server error processing question data.");
                    return;
                }

                if (question == null)
                {
                    _logger.LogWarning("CurrentQuestion was null after conversion for room {RoomCode}", roomCode);
                    await Clients.Caller.SendAsync("Error", "Server missing question data.");
                    return;
                }

                JToken? correctToken = question.SelectToken("correctValue") ?? question.SelectToken("CorrectValue") ?? question.SelectToken("correct_value");
                if (correctToken == null)
                {
                    _logger.LogWarning("Could not find correctValue token in question for room {RoomCode}: {Question}", roomCode, question.ToString());
                    await Clients.Caller.SendAsync("Error", "Question data missing correct value.");
                    return;
                }

                int correctValue;
                try
                {
                    if (correctToken.Type == JTokenType.Integer || correctToken.Type == JTokenType.Float)
                        correctValue = correctToken.ToObject<int>();
                    else correctValue = int.Parse(correctToken.ToString());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse correctValue for room {RoomCode}", roomCode);
                    await Clients.Caller.SendAsync("Error", "Invalid question correct value.");
                    return;
                }

                var correct = selectedValue == correctValue;

                int points = 0;
                if (correct)
                {
                    points = 100;
                    var bonus = Math.Max(0, 500 - timeTakenMs) / 5;
                    points += bonus;
                }

                player.Score += points;

                // store playerName with submission so clients can display who answered what
                roomData.Submissions[Context.ConnectionId] = new { selectedValue, timeTakenMs, correct, playerName = player.Name };

                _logger.LogInformation("Player {Player} submitted {Value} (correct={Correct}) in room {RoomCode}", player.Name, selectedValue, correct, roomCode);

                try
                {
                    await Clients.Group(roomCode).SendAsync("ScoreUpdated", new
                    {
                        playerName = player.Name,
                        score = player.Score,
                        players = roomData.Players.Select(p => new { p.Name, p.Score }).OrderByDescending(p => p.Score)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send ScoreUpdated for room {RoomCode}", roomCode);
                }

                if (roomData.Submissions.Count >= roomData.Players.Count)
                {
                    try
                    {
                        var leaderboard = roomData.Players
                            .Select(p => new { p.Name, p.Score })
                            .OrderByDescending(p => p.Score)
                            .ToList();

                        _logger.LogInformation("All players answered in room {RoomCode}, broadcasting AllAnswered", roomCode);

                        await Clients.Group(roomCode).SendAsync("AllAnswered", new
                        {
                            message = "All players have answered",
                            submissions = roomData.Submissions.Select(kv => new { connectionId = kv.Key, data = kv.Value }),
                            leaderboard
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send AllAnswered for room {RoomCode}", roomCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting answer in room {RoomCode}", roomCode);
                try { await Clients.Caller.SendAsync("Error", "Server error submitting answer."); } catch { }
            }
        }

        // ------------------ End Game ------------------
        public async Task EndGame(string roomCode)
        {
            try
            {
                roomCode = roomCode.ToUpper();
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                    return;

                // Create final leaderboard
                var leaderboard = roomData.Players
                    .Select(p => new { p.Name, p.Score })
                    .OrderByDescending(p => p.Score)
                    .ToList();

                // Broadcast game over with leaderboard
                await Clients.Group(roomCode).SendAsync("GameOver", leaderboard);

                // Optionally mark room ended in DB (not implemented)
                roomData.GameStarted = false;
                roomData.CurrentQuestion = null;
                roomData.QuestionStartedAt = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending game in room {RoomCode}", roomCode);
            }
        }

        // ------------------ Update Score ------------------
        public async Task UpdateScore(string roomCode, int scoreChange)
        {
            try
            {
                roomCode = roomCode.ToUpper();

                if (!Rooms.TryGetValue(roomCode, out var roomData))
                    return;

                var player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player == null)
                    return;

                player.Score += scoreChange;

                await Clients.Group(roomCode).SendAsync("ScoreUpdated", new
                {
                    playerName = player.Name,
                    score = player.Score,
                    players = roomData.Players.Select(p => new { p.Name, p.Score })
                        .OrderByDescending(p => p.Score)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating score in room {RoomCode}", roomCode);
            }
        }

        // ------------------ On Disconnect ------------------
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            foreach (var room in Rooms.Values)
            {
                var player = room.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                if (player != null)
                {
                    await LeaveRoom(room.RoomCode);
                    break;
                }
            }

            await base.OnDisconnectedAsync(exception);
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = new Random();
            return new string(Enumerable.Range(0, 4).Select(_ => chars[random.Next(chars.Length)]).ToArray());
        }

        // ------------------ Get Room Info ------------------
        public Task<object?> GetRoomInfo(string roomCode)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return Task.FromResult<object?>(null);
            roomCode = roomCode.ToUpper();
            if (!Rooms.TryGetValue(roomCode, out var roomData))
                return Task.FromResult<object?>(null);

            object? currentQuestion = null;
            string? questionStartedAt = null;
            if (roomData.CurrentQuestion != null)
            {
                // attempt to serialize to a JObject so JS clients can consume fields like correctValue
                try
                {
                    var normalizedQ = roomData.CurrentQuestion is JObject j ? j : JObject.FromObject(roomData.CurrentQuestion);
                    currentQuestion = ConvertNormalizedToPlainObject((JObject)normalizedQ);
                }
                catch
                {
                    currentQuestion = roomData.CurrentQuestion;
                }

                if (roomData.QuestionStartedAt.HasValue)
                {
                    questionStartedAt = roomData.QuestionStartedAt.Value.ToString("o");
                }
            }

            var info = new
            {
                roomCode = roomData.RoomCode,
                players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score }).ToList(),
                selectedGame = roomData.SelectedGameId,
                currentQuestion,
                questionStartedAt
            };

            return Task.FromResult<object?>(info);
        }

        // ------------------ Claim Host (reconnect support) ------------------
        public async Task ClaimHost(string roomCode, string hostName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(hostName))
                {
                    await Clients.Caller.SendAsync("Error", "Invalid room or host name.");
                    return;
                }

                roomCode = roomCode.ToUpper();
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    await Clients.Caller.SendAsync("Error", "Room not found.");
                    return;
                }

                // Find existing player by name (case-insensitive)
                var existing = roomData.Players.FirstOrDefault(p => p.Name.Equals(hostName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    // Clear previous host flag from any other player
                    foreach (var p in roomData.Players) p.IsHost = false;

                    existing.IsHost = true;
                    existing.ConnectionId = Context.ConnectionId;

                    // Ensure caller is in the group
                    await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                    _logger.LogInformation("Player {HostName} reclaimed host for room {RoomCode}", hostName, roomCode);

                    // Notify room
                    await Clients.Group(roomCode).SendAsync("NewHost", existing.Name);

                    // Send room state to caller
                    await Clients.Caller.SendAsync("RoomJoined", new
                    {
                        roomCode = roomData.RoomCode,
                        players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score }),
                        selectedGame = roomData.SelectedGameId
                    });
                }
                else
                {
                    // If no existing player with that name, add as host
                    var hostPlayer = new Player
                    {
                        Name = hostName.Trim(),
                        IsHost = true,
                        ConnectionId = Context.ConnectionId,
                        Score = 0
                    };

                    roomData.Players.Add(hostPlayer);
                    await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                    _logger.LogInformation("Player {HostName} joined as host for room {RoomCode}", hostName, roomCode);

                    await Clients.Group(roomCode).SendAsync("PlayerJoined", new
                    {
                        playerName = hostPlayer.Name,
                        players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score })
                    });

                    await Clients.Caller.SendAsync("RoomJoined", new
                    {
                        roomCode = roomData.RoomCode,
                        players = roomData.Players.Select(p => new { p.Name, p.IsHost, p.Score }),
                        selectedGame = roomData.SelectedGameId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error claiming host for room {RoomCode}", roomCode);
                await Clients.Caller.SendAsync("Error", "Failed to claim host.");
            }
        }

        // Helper to convert normalized JObject into a plain serializable object
        private object ConvertNormalizedToPlainObject(JObject normalized)
        {
            var pokemonName = normalized.SelectToken("pokemonName")?.ToString() ?? string.Empty;
            var imageUrl = normalized.SelectToken("image_Url")?.ToString() ?? string.Empty;
            var statToGuess = normalized.SelectToken("statToGuess")?.ToString() ?? string.Empty;
            var correctToken = normalized.SelectToken("correctValue");
            int correctValue = 0;
            if (correctToken != null)
            {
                if (correctToken.Type == JTokenType.Integer || correctToken.Type == JTokenType.Float)
                    correctValue = correctToken.ToObject<int>();
                else int.TryParse(correctToken.ToString(), out correctValue);
            }

            var other = new List<object>();
            var ovToken = normalized.SelectToken("otherValues");
            if (ovToken is JArray arr)
            {
                foreach (var item in arr)
                {
                    var stat = item.SelectToken("stat")?.ToString() ?? string.Empty;
                    var valToken = item.SelectToken("value") ?? item.SelectToken("Value");
                    int val = 0;
                    if (valToken != null)
                    {
                        if (valToken.Type == JTokenType.Integer || valToken.Type == JTokenType.Float) val = valToken.ToObject<int>();
                        else int.TryParse(valToken.ToString(), out val);
                    }
                    other.Add(new { stat, value = val });
                }
            }

            return new
            {
                pokemonName,
                image_Url = imageUrl,
                statToGuess,
                correctValue,
                otherValues = other
            };
        }
    }
}
