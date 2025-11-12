using System.Collections.Generic;

namespace PokemonQuizAPI.Models
{
    public class AdminViewModel
    {
        public int TotalGames { get; set; }
        public List<object> Rooms { get; set; } = new();
        public List<object> Users { get; set; } = new();
    }
}
