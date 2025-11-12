using Microsoft.AspNetCore.Mvc;
using PokemonQuizAPI.Data;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace PokemonQuizAPI.Controllers
{
    // Minimal controller to render an admin Razor page which can fetch admin APIs client-side
    [Route("admin")]
    public class AdminController : Controller
    {
        private readonly DatabaseHelper _db;
        private readonly IWebHostEnvironment _env;

        public AdminController(DatabaseHelper db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            // Gather basic admin data for server-rendered view: active rooms, total games, users
            var rooms = Hubs.GameHub.GetRoomsSnapshot();
            var totalGames = await _db.GetTotalGamesPlayedAsync();

            var usersDebug = await _db.GetAllUsersDebugAsync();

            // Get aggregated games played counts per user and merge into view model
            var gamesCounts = await _db.GetGamesPlayedCountsAsync();

            var usersWithRoles = new List<object>();
            foreach (var u in usersDebug)
            {
                try
                {
                    // u is an anonymous object with id, username, email, hasPassword
                    var idProp = u.GetType().GetProperty("id");
                    var id = idProp?.GetValue(u)?.ToString();
                    int gamesPlayed = 0;
                    if (!string.IsNullOrWhiteSpace(id) && gamesCounts != null && gamesCounts.TryGetValue(id, out var gp)) gamesPlayed = gp;

                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        var info = await _db.GetUserInfoByIdAsync(id);
                        usersWithRoles.Add(new { id = info.Id ?? id, username = info.Username ?? (u.GetType().GetProperty("username")?.GetValue(u)?.ToString() ?? ""), email = info.Email ?? (u.GetType().GetProperty("email")?.GetValue(u)?.ToString() ?? ""), role = info.Role ?? string.Empty, gamesPlayed });
                    }
                    else
                    {
                        usersWithRoles.Add(new { id = "", username = u.GetType().GetProperty("username")?.GetValue(u)?.ToString() ?? "", email = u.GetType().GetProperty("email")?.GetValue(u)?.ToString() ?? "", role = string.Empty, gamesPlayed });
                    }
                }
                catch
                {
                    usersWithRoles.Add(new { id = "", username = "", email = "", role = string.Empty, gamesPlayed = 0 });
                }
            }

            var model = new Models.AdminViewModel
            {
                TotalGames = totalGames,
                Rooms = rooms,
                Users = usersWithRoles
            };

            return View("Index", model);
        }

        [HttpGet("/{**slug}")]
        public IActionResult Spa() => RedirectToAction(nameof(Index));
    }
}
