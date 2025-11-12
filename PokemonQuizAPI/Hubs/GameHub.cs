using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using PokemonQuizAPI.Data;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Text.Json;
using PokemonQuizAPI.Models;

namespace PokemonQuizAPI.Hubs
{
    public class GameHub : Hub
    {
        private static readonly ConcurrentDictionary<string, RoomData> Rooms = new();
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> ShutdownTokens = new();
        private readonly ILogger<GameHub> _logger;
        private readonly DatabaseHelper _db;
        private readonly IGameRoomRepository _gameRoomRepo;
        private const int MaxNameLength = 24;
        private const int QuestionTimeoutMs = 20000; // 20s used for scoring
        private const int MAX_QUESTIONS_PER_GAME = 10;

        public GameHub(ILogger<GameHub> logger, DatabaseHelper db, IGameRoomRepository gameRoomRepo)
        {
            _logger = logger;
            _db = db;
            _gameRoomRepo = gameRoomRepo;
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

            // Current persistent game session id (DB or file-backed)
            public string? CurrentSessionId { get; set; }

            // Number of questions sent in this session
            public int QuestionsSent { get; set; } = 0;

            // Sync object for thread-safety
            public object SyncRoot { get; } = new();
        }

        // Helper: normalize an incoming question JObject to a consistent outgoing shape
        private static JObject BuildNormalizedQuestion(JObject source)
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
                    _ = int.TryParse(correctToken.ToString(), out correctValue);
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
                        else _ = int.TryParse(valToken.ToString(), out val);
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

        // Helper to convert normalized JObject into a plain serializable object
        private static object ConvertNormalizedToPlainObject(JObject normalized)
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
                else _ = int.TryParse(correctToken.ToString(), out correctValue);
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
                        else _ = int.TryParse(valToken.ToString(), out val);
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

                hostName = hostName.Trim();
                if (hostName.Length > MaxNameLength)
                {
                    await Clients.Caller.SendAsync("Error", $"Host name too long. Maximum {MaxNameLength} characters allowed.");
                    return;
                }

                // Try to generate a unique room code and persist it in the DB.
                const int maxAttempts = 6;
                string roomCode = string.Empty;
                var inserted = false;

                for (int attempt = 0; attempt < maxAttempts; attempt++)
                {
                    var candidate = GenerateRoomCode();

                    // Avoid obvious in-memory collision
                    if (Rooms.ContainsKey(candidate)) continue;

                    try
                    {
                        // Attempt to insert; InsertGameRoomAsync returns false on duplicate key
                        if (await _gameRoomRepo.InsertGameRoomAsync(candidate, null, null, Context.ConnectionAborted))
                        {
                            roomCode = candidate;
                            inserted = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Insert attempt failed for room candidate {Candidate}", candidate);
                    }

                    // small backoff before retrying
                    try { await Task.Delay(50, Context.ConnectionAborted); } catch { }
                }

                if (!inserted)
                {
                    await Clients.Caller.SendAsync("Error", "Failed to create room after multiple attempts. Try again.");
                    return;
                }

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
                    Players = new List<Player> { hostPlayer },
                    GameStarted = false,
                    SelectedGameId = null
                };

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

                // Persist selection to DB if available
                try
                {
                    await _gameRoomRepo.UpdateGameRoomGameIdAsync(roomCode, gameId, Context.ConnectionAborted);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist selected game id {GameId} for room {RoomCode}", gameId, roomCode);
                }

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

                playerName = playerName.Trim();
                if (playerName.Length > MaxNameLength)
                {
                    await Clients.Caller.SendAsync("Error", $"Player name too long. Maximum {MaxNameLength} characters allowed.");
                    return;
                }

                roomCode = roomCode.ToUpper();

                // If room not in memory, check DB and rehydrate minimal room if it exists (allow inactive rooms to be rejoined)
                if (!Rooms.TryGetValue(roomCode, out var roomData))
                {
                    try
                    {
                        // Check DB for room presence regardless of is_active
                        var existsAny = await _gameRoomRepo.GameRoomExistsAnyStatusAsync(roomCode, Context.ConnectionAborted);
                        if (existsAny)
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
                                var gameId = await _gameRoomRepo.GetGameIdForRoomAsync(roomCode, Context.ConnectionAborted);
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
                            _logger.LogInformation("Rehydrated room {RoomCode} from database into memory (any-status)", roomCode);
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

                // If a shutdown was scheduled, cancel it because a player rejoined
                if (ShutdownTokens.TryRemove(roomCode, out var tokenSource))
                {
                    try { tokenSource.Cancel(); } catch { }
                    try { tokenSource.Dispose(); } catch { }
                    _logger.LogInformation("Cancelled scheduled shutdown for room {RoomCode} because a player rejoined", roomCode);
                }

                if (roomData.GameStarted)
                {
                    // allow join even if game started in rehydrate scenario, but callers may handle error
                    // do not reject here; allow caller to rehydrate state
                    // we won't send the "Game already started" error here to allow reconnects
                }

                lock (roomData.SyncRoot)
                {
                    if (roomData.Players.Any(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Clients.Caller.SendAsync("Error", "Name already taken in this room.");
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
                }

                await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

                _logger.LogInformation("Player {PlayerName} joined room {RoomCode}", playerName, roomCode);

                // Notify all players in the room
                await Clients.Group(roomCode).SendAsync("PlayerJoined", new
                {
                    playerName = playerName,
                    players = Rooms[roomCode].Players.Select(p => new { p.Name, p.IsHost, p.Score })
                });

                // Send joined confirmation to caller, include current question if present
                object? currentQuestion = null;
                string? questionStartedAt = null;
                if (roomData.CurrentQuestion != null)
                {
                    try
                    {
                        var normalizedQ = roomData.CurrentQuestion is JObject j ? j : JObject.FromObject(roomData.CurrentQuestion);
                        // If this is a compare-stat question, return it as plain object so clients receive expected fields
                        if (normalizedQ is JObject nj && IsCompareQuestion(nj))
                        {
                            currentQuestion = nj;
                        }
                        else
                        {
                            currentQuestion = ConvertNormalizedToPlainObject((JObject)normalizedQ);
                        }
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

                Player? player = null;
                lock (roomData.SyncRoot)
                {
                    player = roomData.Players.FirstOrDefault(p => p.ConnectionId == Context.ConnectionId);
                    if (player == null)
                        return;

                    roomData.Players.Remove(player);
                }

                await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomCode);

                if (roomData.Players.Count == 0)
                {
                    // schedule shutdown after grace period
                    var cts = new CancellationTokenSource();
                    if (!ShutdownTokens.TryAdd(roomCode, cts))
                    {
                        try { cts.Dispose(); } catch { }
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var delay = TimeSpan.FromMinutes(2); // 2 minute grace
                            _logger.LogInformation("Scheduled shutdown for room {RoomCode} in {Delay}", roomCode, delay);
                            await Task.Delay(delay, cts.Token);

                            // perform shutdown
                            Rooms.TryRemove(roomCode, out _);
                            try
                            {
                                await _gameRoomRepo.EndGameRoomAsync(roomCode, cts.Token);
                                _logger.LogInformation("Marked room {RoomCode} inactive in DB after grace period", roomCode);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to mark room {RoomCode} inactive in DB", roomCode);
                            }

                            try
                            {
                                await Clients.Group(roomCode).SendAsync("RoomClosed", "Room closed due to inactivity.");
                            }
                            catch { }
                        }
                        catch (TaskCanceledException)
                        {
                            _logger.LogInformation("Shutdown for room {RoomCode} canceled because a player rejoined", roomCode);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during scheduled shutdown for room {RoomCode}", roomCode);
                        }
                        finally
                        {
                            if (ShutdownTokens.TryRemove(roomCode, out var removed))
                            {
                                try { removed.Dispose(); } catch { }
                            }
                        }
                    });

                    _logger.LogInformation("Room {RoomCode} has no players; scheduled shutdown", roomCode);
                }
                else
                {
                    // If host left but players remain, assign new host
                    if (player.IsHost && roomData.Players.Count > 0)
                    {
                        lock (roomData.SyncRoot)
                        {
                            var newHost = roomData.Players[0];
                            newHost.IsHost = true;
                            _logger.LogInformation("Host left room {RoomCode}; new host is {NewHost}", roomCode, newHost.Name);
                        }

                        await Clients.Group(roomCode).SendAsync("NewHost", roomData.Players[0].Name);
                    }

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

                // reset players' scores when starting a new game
                lock (roomData.SyncRoot)
                {
                    foreach (var p in roomData.Players)
                    {
                        p.Score = 0;
                    }
                }

                // Try to generate the first question before marking the game as started
                JObject? questionObj = null;
                try
                {
                    // generate based on selected game id
                    var selectedGame = roomData.SelectedGameId ?? string.Empty;
                    if (selectedGame.Equals("higher-or-lower", StringComparison.OrdinalIgnoreCase) || selectedGame.Equals("compare-stat", StringComparison.OrdinalIgnoreCase))
                    {
                        questionObj = await GenerateRandomCompareQuestionAsync();
                    }
                    else
                    {
                        questionObj = await GenerateRandomQuestionAsync();
                    }
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

                // We have a valid question — start the game
                roomData.GameStarted = true;

                _logger.LogInformation("Game started in room {RoomCode} by {HostName}", roomCode, host.Name);

                try
                {
                    // Create and persist a GameSession for this room/game
                    try
                    {
                        var sessionId = Guid.NewGuid().ToString();
                        roomData.CurrentSessionId = sessionId;
                        // store session with initial score 0; userId null for room session
                        await _db.CreateGameSessionAsync(sessionId, roomData.SelectedGameId, null, roomCode, 0, DateTime.UtcNow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to create persistent GameSession for room {RoomCode}", roomCode);
                    }

                    // For compare-stat/higher-or-lower we prefer to build and store the plain compare object
                    if ((roomData.SelectedGameId ?? string.Empty).Equals("higher-or-lower", StringComparison.OrdinalIgnoreCase) || (roomData.SelectedGameId ?? string.Empty).Equals("compare-stat", StringComparison.OrdinalIgnoreCase))
                    {
                        var aName = questionObj.SelectToken("pokemonAName")?.ToString() ?? questionObj.SelectToken("PokemonAName")?.ToString() ?? string.Empty;
                        var aImg = questionObj.SelectToken("pokemonAImageUrl")?.ToString() ?? questionObj.SelectToken("PokemonAImageUrl")?.ToString() ?? string.Empty;
                        var aVal = questionObj.SelectToken("pokemonAValue")?.ToObject<int?>();

                        var bName = questionObj.SelectToken("pokemonBName")?.ToString() ?? questionObj.SelectToken("PokemonBName")?.ToString() ?? string.Empty;
                        var bImg = questionObj.SelectToken("pokemonBImageUrl")?.ToString() ?? questionObj.SelectToken("PokemonBImageUrl")?.ToString() ?? string.Empty;
                        var bVal = questionObj.SelectToken("pokemonBValue")?.ToObject<int?>();

                        var stat = questionObj.SelectToken("statToCompare")?.ToString() ?? string.Empty;

                        var plainOut = new {
                            pokemonAId = questionObj.SelectToken("pokemonAId")?.ToString() ?? questionObj.SelectToken("PokemonAId")?.ToString() ?? string.Empty,
                            pokemonAName = aName,
                            pokemonAImageUrl = aImg,
                            pokemonAValue = aVal,
                            pokemonBId = questionObj.SelectToken("pokemonBId")?.ToString() ?? questionObj.SelectToken("PokemonBId")?.ToString() ?? string.Empty,
                            pokemonBName = bName,
                            pokemonBImageUrl = bImg,
                            pokemonBValue = bVal,
                            statToCompare = stat
                        };

                        lock (roomData.SyncRoot)
                        {
                            roomData.CurrentQuestion = JObject.FromObject(plainOut);
                            roomData.QuestionStartedAt = DateTime.UtcNow;
                            roomData.Submissions = new Dictionary<string, object>();
                            roomData.QuestionsSent = 1;
                        }

                        // Send GameStarted with full payload so clients navigate and receive question
                        var gsPayload = new {
                            gameId = roomData.SelectedGameId,
                            currentQuestion = plainOut,
                            questionStartedAt = roomData.QuestionStartedAt.HasValue ? roomData.QuestionStartedAt.Value.ToString("o") : null
                        };
                        await Clients.Group(roomCode).SendAsync("GameStarted", gsPayload);
                        _logger.LogInformation("Sent GameStarted with currentQuestion for room {RoomCode}", roomCode);

                        // ALSO broadcast Question for any clients that missed GameStarted or use old handler
                        _logger.LogInformation("Broadcasting Question for room {RoomCode}: {Question}", roomCode, JObject.FromObject(plainOut).ToString());
                        await Clients.Group(roomCode).SendAsync("Question", plainOut);

                        // If this was the final question, schedule EndGame after the question timeout to ensure game ends even if players don't submit
                        if (roomData.QuestionsSent >= MAX_QUESTIONS_PER_GAME)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var delay = TimeSpan.FromMilliseconds(QuestionTimeoutMs + 1500);
                                    _logger.LogInformation("Scheduled EndGame for room {RoomCode} in {Delay} because max questions reached", roomCode, delay);
                                    await Task.Delay(delay);
                                    await EndGame(roomCode);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Scheduled EndGame failed for room {RoomCode}", roomCode);
                                }
                            });
                        }
                    }
                    else
                    {
                        var normalized = BuildNormalizedQuestion(questionObj);
                        var plain = ConvertNormalizedToPlainObject(normalized);

                        lock (roomData.SyncRoot)
                        {
                            roomData.CurrentQuestion = normalized;
                            roomData.QuestionStartedAt = DateTime.UtcNow;
                            roomData.Submissions = new Dictionary<string, object>();
                            roomData.QuestionsSent = 1;
                        }

                        // Send GameStarted with full payload so clients navigate and receive question
                        var gsPayload2 = new {
                            gameId = roomData.SelectedGameId,
                            currentQuestion = plain,
                            questionStartedAt = roomData.QuestionStartedAt.HasValue ? roomData.QuestionStartedAt.Value.ToString("o") : null
                        };
                        await Clients.Group(roomCode).SendAsync("GameStarted", gsPayload2);
                        _logger.LogInformation("Sent GameStarted with currentQuestion for room {RoomCode}", roomCode);

                        // ALSO broadcast Question for any clients that missed GameStarted or use old handler
                        _logger.LogInformation("Broadcasting Question for room {RoomCode}: {Question}", roomCode, JObject.FromObject(plain).ToString());
                        await Clients.Group(roomCode).SendAsync("Question", plain);

                        // If this was the final question, schedule EndGame after the question timeout to ensure game ends even if players don't submit
                        if (roomData.QuestionsSent >= MAX_QUESTIONS_PER_GAME)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var delay = TimeSpan.FromMilliseconds(QuestionTimeoutMs + 1500);
                                    _logger.LogInformation("Scheduled EndGame for room {RoomCode} in {Delay} because max questions reached", roomCode, delay);
                                    await Task.Delay(delay);
                                    await EndGame(roomCode);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Scheduled EndGame failed for room {RoomCode}", roomCode);
                                }
                            });
                        }
                    }
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

                var random = Random.Shared;
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

        private async Task<JObject?> GenerateRandomCompareQuestionAsync()
        {
            try
            {
                var all = await _db.GetAllPokemonAsync();
                if (all == null || all.Count == 0) return null;

                var rnd = Random.Shared;
                var a = all[rnd.Next(all.Count)];

                int aTotal = a.Hp + a.Attack + a.Defence + a.SpecialAttack + a.SpecialDefence + a.Speed;

                var candidates = all
                    .Where(p => p.Id != a.Id)
                    .OrderBy(p => Math.Abs((p.Hp + p.Attack + p.Defence + p.SpecialAttack + p.SpecialDefence + p.Speed) - aTotal))
                    .Take(12)
                    .ToList();

                PokemonData b;
                if (candidates.Count == 0)
                {
                    b = all[rnd.Next(all.Count)];
                    if (b.Id == a.Id) b = all[(all.IndexOf(a) + 1) % all.Count];
                }
                else
                {
                    b = candidates[rnd.Next(candidates.Count)];
                }

                var statKeys = new[] { "HP", "Attack", "Defence", "Special Attack", "Special Defence", "Speed" };
                var statName = statKeys[rnd.Next(statKeys.Length)];
                int aVal = GetStatValueByName(a, statName);
                int bVal = GetStatValueByName(b, statName);

                var obj = new JObject
                {
                    ["pokemonAId"] = a.Id,
                    ["pokemonAName"] = a.Name,
                    ["pokemonAImageUrl"] = a.ImageUrl ?? string.Empty,
                    ["pokemonAValue"] = aVal,
                    ["pokemonBId"] = b.Id,
                    ["pokemonBName"] = b.Name,
                    ["pokemonBImageUrl"] = b.ImageUrl ?? string.Empty,
                    ["pokemonBValue"] = bVal,
                    ["statToCompare"] = statName
                };

                return obj;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating compare-stat question");
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

                // Disallow sending additional questions once max reached. Do not await inside lock.
                bool maxReached;
                lock (roomData.SyncRoot)
                {
                    maxReached = roomData.QuestionsSent >= MAX_QUESTIONS_PER_GAME;
                }

                if (maxReached)
                {
                    _logger.LogInformation("Host attempted to send question but max questions reached for room {RoomCode}", roomCode);
                    try { await Clients.Caller.SendAsync("Error", "Maximum questions reached. Ending game."); } catch { }
                    // End game (fire-and-forget to avoid deadlocks)
                    _ = Task.Run(async () =>
                    {
                        try { await EndGame(roomCode); } catch (Exception ex) { _logger.LogWarning(ex, "EndGame failed for room {RoomCode}", roomCode); }
                    });
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
                    // continue and try to generate server-side question below
                    questionObj = null;
                }

                // If incoming payload is empty or malformed, generate server-side question as a fallback
                if (questionObj == null ||
                    (questionObj.SelectToken("pokemonName") == null && questionObj.SelectToken("PokemonName") == null && questionObj.SelectToken("name") == null) ||
                    (questionObj.SelectToken("correctValue") == null && questionObj.SelectToken("CorrectValue") == null && questionObj.SelectToken("correct_value") == null))
                {
                    _logger.LogInformation("Incoming question payload was empty or malformed for room {RoomCode}, generating server-side question as fallback", roomCode);

                    if ((roomData.SelectedGameId ?? string.Empty).Equals("higher-or-lower", StringComparison.OrdinalIgnoreCase) || (roomData.SelectedGameId ?? string.Empty).Equals("compare-stat", StringComparison.OrdinalIgnoreCase))
                    {
                        questionObj = await GenerateRandomCompareQuestionAsync();
                    }
                    else
                    {
                        questionObj = await GenerateRandomQuestionAsync();
                    }

                    if (questionObj == null)
                    {
                        await Clients.Caller.SendAsync("Error", "Failed to generate question on server.");
                        return;
                    }
                }

                // If compare-stat selected, build and store the plain compare object directly
                if ((roomData.SelectedGameId ?? string.Empty).Equals("higher-or-lower", StringComparison.OrdinalIgnoreCase) || (roomData.SelectedGameId ?? string.Empty).Equals("compare-stat", StringComparison.OrdinalIgnoreCase))
                {
                    var aName = questionObj.SelectToken("pokemonAName")?.ToString() ?? string.Empty;
                    var aImg = questionObj.SelectToken("pokemonAImageUrl")?.ToString() ?? string.Empty;
                    var aVal = questionObj.SelectToken("pokemonAValue")?.ToObject<int?>();

                    var bName = questionObj.SelectToken("pokemonBName")?.ToString() ?? string.Empty;
                    var bImg = questionObj.SelectToken("pokemonBImageUrl")?.ToString() ?? string.Empty;
                    var bVal = questionObj.SelectToken("pokemonBValue")?.ToObject<int?>();

                    var stat = questionObj.SelectToken("statToCompare")?.ToString() ?? string.Empty;

                    var plainOut = new {
                        pokemonAId = questionObj.SelectToken("pokemonAId")?.ToString() ?? string.Empty,
                        pokemonAName = aName,
                        pokemonAImageUrl = aImg,
                        pokemonAValue = aVal,
                        pokemonBId = questionObj.SelectToken("pokemonBId")?.ToString() ?? string.Empty,
                        pokemonBName = bName,
                        pokemonBImageUrl = bImg,
                        pokemonBValue = bVal,
                        statToCompare = stat
                    };

                    lock (roomData.SyncRoot)
                    {
                        roomData.CurrentQuestion = JObject.FromObject(plainOut);
                        roomData.QuestionStartedAt = DateTime.UtcNow;
                        roomData.Submissions = new Dictionary<string, object>();
                        roomData.QuestionsSent = (roomData.QuestionsSent <= 0) ? 1 : roomData.QuestionsSent + 1;
                    }

                    // Broadcast the question first so clients receive question payload
                    _logger.LogInformation("SendQuestionToRoom broadcasting for {RoomCode}: {Question}", roomCode, JObject.FromObject(plainOut).ToString());
                    await Clients.Group(roomCode).SendAsync("Question", plainOut);
                }
                else
                {
                    // Non-compare questions: normalize and send the plain converted object
                    var normalized = BuildNormalizedQuestion(questionObj);
                    var plain = ConvertNormalizedToPlainObject(normalized);

                    lock (roomData.SyncRoot)
                    {
                        roomData.CurrentQuestion = normalized;
                        roomData.QuestionStartedAt = DateTime.UtcNow;
                        roomData.Submissions = new Dictionary<string, object>();
                        roomData.QuestionsSent = (roomData.QuestionsSent <= 0) ? 1 : roomData.QuestionsSent + 1;
                    }

                    _logger.LogInformation("SendQuestionToRoom broadcasting for {RoomCode}: {Question}", roomCode, JObject.FromObject(plain).ToString());
                    await Clients.Group(roomCode).SendAsync("Question", plain);
                }
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

                // compute points: base + speed bonus depending on timeTakenMs
                int basePoints = 1000;
                int maxBonus = 500;
                var timeLeftMs = Math.Max(0, QuestionTimeoutMs - timeTakenMs);
                var speedBonus = (int)Math.Round((timeLeftMs / (double)QuestionTimeoutMs) * maxBonus);
                int points = correct ? basePoints + speedBonus : 0;

                lock (roomData.SyncRoot)
                {
                    player.Score += points;
                    // store playerName and points with submission so clients can display who answered what and awarded points
                    roomData.Submissions[Context.ConnectionId] = new { selectedValue, timeTakenMs, correct, playerName = player.Name, points };
                }

                _logger.LogInformation("Player {Player} submitted {Value} (correct={Correct}) in room {RoomCode} and earned {Points}", player.Name, selectedValue, correct, roomCode, points);

                // Persist best score for session (use max player score)
                try
                {
                    if (!string.IsNullOrWhiteSpace(roomData.CurrentSessionId))
                    {
                        var best = roomData.Players.Max(p => p.Score);
                        await _db.UpdateGameSessionScoreAsync(roomData.CurrentSessionId, best);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update GameSession score for room {RoomCode}", roomCode);
                }

                // Persist individual user question for analytics / replay if we have a current session id
                try
                {
                    if (!string.IsNullOrWhiteSpace(roomData.CurrentSessionId))
                    {
                        var answerPayload = new
                        {
                            playerName = player.Name,
                            connectionId = Context.ConnectionId,
                            selectedValue,
                            timeTakenMs,
                            correct,
                            question = question
                        };

                        var json = JsonSerializer.Serialize(answerPayload);
                        // question_id is unknown for server-generated questions; store NULL
                        await _db.InsertUserQuestionAsync(Guid.NewGuid().ToString(), roomData.CurrentSessionId, null, json, correct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist UserQuestion for session {SessionId} in room {RoomCode}", roomData.CurrentSessionId, roomCode);
                    // continue without failing the request
                }

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

                        // If we've reached the max questions, end the game and broadcast final leaderboard
                        if (roomData.QuestionsSent >= MAX_QUESTIONS_PER_GAME)
                        {
                            _logger.LogInformation("Max questions reached in room {RoomCode}, ending game", roomCode);
                            // fire-and-forget EndGame so we don't block current flow
                            _ = Task.Run(async () =>
                            {
                                try { await EndGame(roomCode); } catch (Exception ex) { _logger.LogWarning(ex, "EndGame failed for room {RoomCode}", roomCode); }
                            });
                        }
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

        // ------------------ Submit Compare Answer (clients) ------------------
        public async Task SubmitCompareAnswer(string roomCode, string selectedSide, int timeTakenMs)
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

                var aValToken = question.SelectToken("pokemonAValue") ?? question.SelectToken("PokemonAValue") ?? question.SelectToken("pokemon_a_value");
                var bValToken = question.SelectToken("pokemonBValue") ?? question.SelectToken("PokemonBValue") ?? question.SelectToken("pokemon_b_value");

                int aVal = 0, bVal = 0;
                if (aValToken != null)
                {
                    if (aValToken.Type == JTokenType.Integer || aValToken.Type == JTokenType.Float) aVal = aValToken.ToObject<int>();
                    else int.TryParse(aValToken.ToString(), out aVal);
                }
                if (bValToken != null)
                {
                    if (bValToken.Type == JTokenType.Integer || bValToken.Type == JTokenType.Float) bVal = bValToken.ToObject<int>();
                    else int.TryParse(bValToken.ToString(), out bVal);
                }

                string? correctSide = null;
                string? correctName = null;
                if (aVal == bVal)
                {
                    correctSide = "either";
                    correctName = question.SelectToken("pokemonAName")?.ToString() ?? question.SelectToken("PokemonAName")?.ToString();
                }
                else if (aVal > bVal)
                {
                    correctSide = "left";
                    correctName = question.SelectToken("pokemonAName")?.ToString() ?? question.SelectToken("PokemonAName")?.ToString();
                }
                else
                {
                    correctSide = "right";
                    correctName = question.SelectToken("pokemonBName")?.ToString() ?? question.SelectToken("PokemonBName")?.ToString();
                }

                bool correct = false;
                if (correctSide == "either") correct = true;
                else correct = string.Equals(selectedSide, correctSide, StringComparison.OrdinalIgnoreCase);

                int basePoints = 1000;
                int maxBonus = 500;
                var timeLeftMs = Math.Max(0, QuestionTimeoutMs - timeTakenMs);
                var speedBonus = (int)Math.Round((timeLeftMs / (double)QuestionTimeoutMs) * maxBonus);
                int points = correct ? basePoints + speedBonus : 0;

                lock (roomData.SyncRoot)
                {
                    player.Score += points;
                    roomData.Submissions[Context.ConnectionId] = new { selectedSide, timeTakenMs, correct, playerName = player.Name, points };
                }

                _logger.LogInformation("Player {Player} submitted {Side} (correct={Correct}) in room {RoomCode} and earned {Points}", player.Name, selectedSide, correct, roomCode, points);

                // Persist best score for session (use max player score)
                try
                {
                    if (!string.IsNullOrWhiteSpace(roomData.CurrentSessionId))
                    {
                        var best = roomData.Players.Max(p => p.Score);
                        await _db.UpdateGameSessionScoreAsync(roomData.CurrentSessionId, best);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update GameSession score for room {RoomCode}", roomCode);
                }

                // Persist individual user question for analytics / replay if we have a current session id
                try
                {
                    if (!string.IsNullOrWhiteSpace(roomData.CurrentSessionId))
                    {
                        var answerPayload = new
                        {
                            playerName = player.Name,
                            connectionId = Context.ConnectionId,
                            selectedSide,
                            timeTakenMs,
                            correct,
                            question = question
                        };

                        var json = JsonSerializer.Serialize(answerPayload);
                        await _db.InsertUserQuestionAsync(Guid.NewGuid().ToString(), roomData.CurrentSessionId, null, json, correct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist UserQuestion for session {SessionId} in room {RoomCode}", roomData.CurrentSessionId, roomCode);
                }

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

                        // If we've reached the max questions, end the game and broadcast final leaderboard
                        if (roomData.QuestionsSent >= MAX_QUESTIONS_PER_GAME)
                        {
                            _logger.LogInformation("Max questions reached in room {RoomCode}, ending game", roomCode);
                            // fire-and-forget EndGame so we don't block current flow
                            _ = Task.Run(async () =>
                            {
                                try { await EndGame(roomCode); } catch (Exception ex) { _logger.LogWarning(ex, "EndGame failed for room {RoomCode}", roomCode); }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send AllAnswered for room {RoomCode}", roomCode);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting compare answer in room {RoomCode}", roomCode);
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

                // Broadcast game over with leaderboard and roomCode (so clients can offer replay)
                // Use try-catch: if Clients is disposed (hub already torn down), log and skip broadcast
                try
                {
                    if (Clients != null)
                    {
                        await Clients.Group(roomCode).SendAsync("GameOver", new { leaderboard, roomCode });
                        _logger.LogInformation("Sent GameOver broadcast for room {RoomCode}", roomCode);
                    }
                    else
                    {
                        _logger.LogWarning("EndGame called for room {RoomCode} but Hub.Clients is disposed; GameOver broadcast skipped", roomCode);
                    }
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogWarning("EndGame: Hub disposed before GameOver broadcast for room {RoomCode}", roomCode);
                }

                // Persist final session score and end session
                try
                {
                    if (!string.IsNullOrWhiteSpace(roomData.CurrentSessionId))
                    {
                        var final = roomData.Players.Any() ? roomData.Players.Max(p => p.Score) : 0;
                        await _db.UpdateGameSessionScoreAsync(roomData.CurrentSessionId, final);
                        await _db.EndGameSessionAsync(roomData.CurrentSessionId, DateTime.UtcNow);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to finalize GameSession for room {RoomCode}", roomCode);
                }

                // Persist room ended in DB - use CancellationToken.None since Context may be disposed
                try
                {
                    await _gameRoomRepo.EndGameRoomAsync(roomCode, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to mark room {RoomCode} ended in DB during EndGame", roomCode);
                }

                roomData.GameStarted = false;
                roomData.CurrentQuestion = null;
                roomData.QuestionStartedAt = null;
                roomData.CurrentSessionId = null;

                _logger.LogInformation("Room {RoomCode} game ended by request; GameOver broadcast attempted", roomCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending game in room {RoomCode}", roomCode);
            }
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var random = Random.Shared;
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
                    if (normalizedQ is JObject nj && IsCompareQuestion(nj))
                    {
                        currentQuestion = nj;
                    }
                    else
                    {
                        currentQuestion = ConvertNormalizedToPlainObject((JObject)normalizedQ);
                    }
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

        // Expose a snapshot of room state for admin tooling
        public static List<object> GetRoomsSnapshot()
        {
            return Rooms.Values.Select(r => new
            {
                roomCode = r.RoomCode,
                players = r.Players.Select(p => new { p.Name, p.Score, p.IsHost, p.ConnectionId }).ToList(),
                gameStarted = r.GameStarted,
                selectedGameId = r.SelectedGameId,
                createdAt = r.CreatedAt,
                questionStartedAt = r.QuestionStartedAt
            }).ToList<object>();
        }

        // Allow admin/backend to forcibly remove a room from memory
        public static bool TryRemoveRoom(string roomCode)
        {
            return Rooms.TryRemove(roomCode.ToUpper(), out _);
        }

        private static int GetStatValueByName(PokemonData p, string statName)
        {
            return statName switch
            {
                "HP" => p.Hp,
                "Attack" => p.Attack,
                "Defence" => p.Defence,
                "Special Attack" => p.SpecialAttack,
                "Special Defence" => p.SpecialDefence,
                "Speed" => p.Speed,
                _ => 0
            };
        }

        private static bool IsCompareQuestion(JObject j)
        {
            if (j == null) return false;
            return j.SelectToken("pokemonAName") != null || j.SelectToken("pokemonBName") != null || j.SelectToken("pokemonAImageUrl") != null || j.SelectToken("pokemonBImageUrl") != null || j.SelectToken("statToCompare") != null;
        }
    }
}
