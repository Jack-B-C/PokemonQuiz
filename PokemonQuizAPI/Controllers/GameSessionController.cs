using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using PokemonQuizAPI.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;

namespace PokemonQuizAPI.Controllers
{
    [ApiController]
    [Route("api/game/session")]
    public class GameSessionController : ControllerBase
    {
        private readonly DatabaseHelper _db;
        private readonly IPokemonRepository _pokemonRepo;
        private readonly ILogger<GameSessionController> _logger;
        private const int QuestionTimeoutMs = 20000; // used for scoring

        // In-memory session store for singleplayer games
        private static readonly ConcurrentDictionary<string, SessionState> Sessions = new();

        public GameSessionController(DatabaseHelper db, IPokemonRepository pokemonRepo, ILogger<GameSessionController> logger)
        {
            _db = db;
            _pokemonRepo = pokemonRepo;
            _logger = logger;
        }

        public class StartRequest
        {
            public string Mode { get; set; } = "guess-stats"; // 'guess-stats' or 'compare-stat'
            public int Questions { get; set; } = 10;
            public int Choices { get; set; } = 4; // used only for guess-stats
        }

        public class AnswerRequest
        {
            // For guess-stats
            public int? SelectedValue { get; set; }
            // For compare-stat: "left" or "right"
            public string? SelectedSide { get; set; }
            public int TimeTakenMs { get; set; }
        }

        private class QuestionItem
        {
            // Common
            public string Id { get; set; } = string.Empty;
            // Fields used by guess-stats
            public string PokemonName { get; set; } = string.Empty;
            public string ImageUrl { get; set; } = string.Empty;
            public string StatToGuess { get; set; } = string.Empty;
            public int CorrectValue { get; set; }
            public List<int> Choices { get; set; } = new();

            // Fields used by compare-stat
            public string PokemonAId { get; set; } = string.Empty;
            public string PokemonAName { get; set; } = string.Empty;
            public string PokemonAImageUrl { get; set; } = string.Empty;
            public int PokemonAValue { get; set; }

            public string PokemonBId { get; set; } = string.Empty;
            public string PokemonBName { get; set; } = string.Empty;
            public string PokemonBImageUrl { get; set; } = string.Empty;
            public int PokemonBValue { get; set; }

            public string StatToCompare { get; set; } = string.Empty;
        }

        private class SessionState
        {
            public string SessionId { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty;
            public List<QuestionItem> Questions { get; set; } = new();
            public int CurrentIndex { get; set; }
            public int Score { get; set; }
            public DateTime StartedAt { get; set; }
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start([FromBody] StartRequest req)
        {
            if (req == null) req = new StartRequest();
            if (req.Questions <= 0) req.Questions = 10;
            if (req.Choices < 2) req.Choices = 4;

            var mode = req.Mode ?? "guess-stats";

            var all = await _pokemonRepo.GetAllPokemonAsync();
            if (all == null || all.Count == 0) return BadRequest(new { message = "No pokemon available" });

            var rnd = Random.Shared;
            var qlist = new List<QuestionItem>();

            if (mode == "guess-stats")
            {
                for (int i = 0; i < req.Questions; i++)
                {
                    var selected = all[rnd.Next(all.Count)];
                    var stats = new Dictionary<string, int>
                    {
                        ["HP"] = selected.Hp,
                        ["Attack"] = selected.Attack,
                        ["Defence"] = selected.Defence,
                        ["Special Attack"] = selected.SpecialAttack,
                        ["Special Defence"] = selected.SpecialDefence,
                        ["Speed"] = selected.Speed
                    };

                    var statEntry = stats.ElementAt(rnd.Next(stats.Count));
                    var correctValue = statEntry.Value;

                    var choices = new HashSet<int> { correctValue };
                    var intervals = new[] { 10, 20, 30, 40, 50 };
                    while (choices.Count < req.Choices)
                    {
                        var offset = intervals[rnd.Next(intervals.Length)];
                        if (rnd.Next(2) == 0) offset = -offset;
                        var fake = correctValue + offset;
                        fake = Math.Max(5, Math.Min(255, fake));
                        choices.Add(fake);
                    }

                    var choicesList = choices.OrderBy(_ => rnd.Next()).ToList();

                    qlist.Add(new QuestionItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        PokemonName = selected.Name,
                        ImageUrl = selected.ImageUrl ?? string.Empty,
                        StatToGuess = statEntry.Key,
                        CorrectValue = correctValue,
                        Choices = choicesList
                    });
                }
            }
            else if (mode == "compare-stat")
            {
                var statKeys = new[] { "HP", "Attack", "Defence", "Special Attack", "Special Defence", "Speed" };

                for (int i = 0; i < req.Questions; i++)
                {
                    // pick first pokemon
                    var a = all[rnd.Next(all.Count)];

                    // compute base stat total for 'a'
                    int aTotal = a.Hp + a.Attack + a.Defence + a.SpecialAttack + a.SpecialDefence + a.Speed;

                    // pick second pokemon that is close in base stat total
                    // sort candidates by absolute difference in BST and take a small top-N to add some variety
                    var candidates = all
                        .Where(p => p.Id != a.Id)
                        .OrderBy(p => Math.Abs((p.Hp + p.Attack + p.Defence + p.SpecialAttack + p.SpecialDefence + p.Speed) - aTotal))
                        .Take(12)
                        .ToList();

                    PokemonData b;
                    if (candidates.Count == 0)
                    {
                        // fallback to random if no candidates
                        b = all[rnd.Next(all.Count)];
                        if (b.Id == a.Id) b = all[(all.IndexOf(a) + 1) % all.Count];
                    }
                    else
                    {
                        // choose randomly among the closest candidates to avoid deterministic pairs
                        b = candidates[rnd.Next(candidates.Count)];
                    }

                    var statName = statKeys[rnd.Next(statKeys.Length)];
                    int aVal = GetStatValueByName(a, statName);
                    int bVal = GetStatValueByName(b, statName);

                    qlist.Add(new QuestionItem
                    {
                        Id = Guid.NewGuid().ToString(),
                        PokemonAId = a.Id,
                        PokemonAName = a.Name,
                        PokemonAImageUrl = a.ImageUrl ?? string.Empty,
                        PokemonAValue = aVal,
                        PokemonBId = b.Id,
                        PokemonBName = b.Name,
                        PokemonBImageUrl = b.ImageUrl ?? string.Empty,
                        PokemonBValue = bVal,
                        StatToCompare = statName
                    });
                }
            }
            else
            {
                return BadRequest(new { message = "Unsupported mode" });
            }

            var sessionId = Guid.NewGuid().ToString();
            var state = new SessionState
            {
                SessionId = sessionId,
                Mode = mode,
                Questions = qlist,
                CurrentIndex = 0,
                Score = 0,
                StartedAt = DateTime.UtcNow
            };

            Sessions[sessionId] = state;

            // Associate session with logged-in user if a valid Bearer token is supplied
            string? userId = null;
            try
            {
                var auth = Request.Headers["Authorization"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer "))
                {
                    // use range operator instead of Substring to satisfy analyzer
                    var token = auth["Bearer ".Length..].Trim();
                    var uid = await _db.GetUserIdForTokenAsync(token);
                    if (!string.IsNullOrWhiteSpace(uid)) userId = uid;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve user id from token when starting session");
            }

            try
            {
                await _db.CreateGameSessionAsync(sessionId, mode, userId, null, 0, state.StartedAt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist session {SessionId}", sessionId);
            }

            return Ok(new { sessionId, total = state.Questions.Count });
        }

        [HttpGet("{sessionId}/next")]
        public IActionResult Next(string sessionId)
        {
            if (!Sessions.TryGetValue(sessionId, out var state)) return NotFound(new { message = "Session not found" });
            if (state.CurrentIndex >= state.Questions.Count) return Ok(new { finished = true });

            var q = state.Questions[state.CurrentIndex];

            if (state.Mode == "guess-stats")
            {
                return Ok(new
                {
                    questionId = q.Id,
                    pokemonName = q.PokemonName,
                    imageUrl = q.ImageUrl,
                    statToGuess = q.StatToGuess,
                    choices = q.Choices,
                    index = state.CurrentIndex,
                    total = state.Questions.Count
                });
            }

            // compare-stat
            return Ok(new
            {
                questionId = q.Id,
                pokemonAId = q.PokemonAId,
                pokemonAName = q.PokemonAName,
                pokemonAImageUrl = q.PokemonAImageUrl,
                pokemonAValue = q.PokemonAValue,
                pokemonBId = q.PokemonBId,
                pokemonBName = q.PokemonBName,
                pokemonBImageUrl = q.PokemonBImageUrl,
                pokemonBValue = q.PokemonBValue,
                statToCompare = q.StatToCompare,
                index = state.CurrentIndex,
                total = state.Questions.Count
            });
        }

        [HttpPost("{sessionId}/answer/{questionId}")]
        public async Task<IActionResult> Answer(string sessionId, string questionId, [FromBody] AnswerRequest req)
        {
            if (!Sessions.TryGetValue(sessionId, out var state)) return NotFound(new { message = "Session not found" });

            var q = state.Questions.FirstOrDefault(x => x.Id == questionId);
            if (q == null) return NotFound(new { message = "Question not found" });

            int basePoints = 1000;
            int maxBonus = 500;
            var timeLeftMs = Math.Max(0, QuestionTimeoutMs - req.TimeTakenMs);
            var speedBonus = (int)Math.Round((timeLeftMs / (double)QuestionTimeoutMs) * maxBonus);
            int points = 0;
            bool correct = false;

            string? correctSide = null;
            string? correctName = null;

            if (state.Mode == "guess-stats")
            {
                if (!req.SelectedValue.HasValue) return BadRequest(new { message = "Missing SelectedValue" });
                correct = req.SelectedValue.Value == q.CorrectValue;
                points = correct ? basePoints + speedBonus : 0;
            }
            else // compare-stat
            {
                if (string.IsNullOrWhiteSpace(req.SelectedSide)) return BadRequest(new { message = "Missing SelectedSide" });
                // Determine correct side
                if (q.PokemonAValue == q.PokemonBValue)
                {
                    correctSide = "either"; // tie
                    correctName = q.PokemonAName; // either name is fine
                }
                else if (q.PokemonAValue > q.PokemonBValue)
                {
                    correctSide = "left";
                    correctName = q.PokemonAName;
                }
                else
                {
                    correctSide = "right";
                    correctName = q.PokemonBName;
                }

                if (correctSide == "either")
                {
                    // consider both answers correct
                    correct = true;
                    points = basePoints + speedBonus;
                }
                else
                {
                    correct = string.Equals(req.SelectedSide, correctSide, StringComparison.OrdinalIgnoreCase);
                    points = correct ? basePoints + speedBonus : 0;
                }
            }

            state.Score += points;
            state.CurrentIndex++;

            // persist user question and update session score
            try
            {
                await _db.InsertUserQuestionAsync(Guid.NewGuid().ToString(), sessionId, q.Id, System.Text.Json.JsonSerializer.Serialize(new { answer = state.Mode == "guess-stats" ? (object)(req.SelectedValue ?? -1) : (object)(req.SelectedSide ?? string.Empty) }), correct);
                await _db.UpdateGameSessionScoreAsync(sessionId, state.Score);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist answer for session {SessionId}", sessionId);
            }

            var finished = state.CurrentIndex >= state.Questions.Count;

            // Return correctSide and correctName for compare-stat to help client UI
            if (state.Mode == "compare-stat")
            {
                return Ok(new { correct, points, totalScore = state.Score, finished, correctSide, correctName });
            }

            return Ok(new { correct, points, totalScore = state.Score, finished });
        }

        [HttpPost("{sessionId}/end")]
        public async Task<IActionResult> End(string sessionId)
        {
            if (!Sessions.TryRemove(sessionId, out var state)) return NotFound(new { message = "Session not found" });

            try
            {
                await _db.UpdateGameSessionScoreAsync(sessionId, state.Score);
                await _db.EndGameSessionAsync(sessionId, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to finalize session {SessionId}", sessionId);
            }

            return Ok(new { sessionId, score = state.Score });
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
    }
}
