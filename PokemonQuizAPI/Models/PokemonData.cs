namespace PokemonQuizAPI.Models
{
    public class PokemonData
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Type1 { get; set; }
        public string? Type2 { get; set; }
        public int Hp { get; set; }
        public int Attack { get; set; }
        public int Defence { get; set; }
        public int SpecialAttack { get; set; }
        public int SpecialDefence { get; set; }
        public int Speed { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime FetchedAt { get; set; }
    }
}