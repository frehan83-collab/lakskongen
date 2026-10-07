using System;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Crew member (Phase 3.1): modifiers with faces, not Sims.
    /// Three roles with concrete hooks into live systems:
    /// roekter (storm/net protection), veterinaer (treatment survival),
    /// skipper (wellboat cost/speed). Serializable for saves.
    /// </summary>
    [Serializable]
    public class CrewMember
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Role { get; set; } = "general";
        public decimal MonthlySalaryNok { get; set; } = 45_000m;
        public int Morale { get; set; } = 80;
        public int HiredDay { get; set; }

        public CrewMember() { }
    }

    /// <summary>Staff roster helpers: hiring candidates and live effect queries.</summary>
    public static class CrewEffects
    {
        public static readonly (string role, string name, decimal salary)[] Candidates =
        {
            ("roekter", "Elias Fjord", 42_000m),
            ("veterinaer", "Kari Våg", 58_000m),
            ("skipper", "Olav Strand", 48_000m),
            ("roekter", "Mona Haugen", 40_000m),
            ("veterinaer", "Per Salomonsen", 55_000m),
            ("skipper", "Ingrid Båt", 47_000m),
        };

        public static bool HasRole(GameState s, string role)
        {
            if (s.Crew == null) return false;
            foreach (var c in s.Crew)
                if (c.Role == role) return true;
            return false;
        }

        /// <summary>Storm fish-loss multiplier (roekter halves handling losses).</summary>
        public static double StormLossMultiplier(GameState s) => HasRole(s, "roekter") ? 0.5 : 1.0;

        /// <summary>Net decay multiplier (roekter maintenance).</summary>
        public static double NetDecayMultiplier(GameState s) => HasRole(s, "roekter") ? 0.75 : 1.0;

        /// <summary>Treatment mortality multiplier (veterinaer supervision).</summary>
        public static double TreatmentMortalityMultiplier(GameState s) => HasRole(s, "veterinaer") ? 0.5 : 1.0;

        /// <summary>Wellboat hire discount (skipper contacts).</summary>
        public static decimal WellboatHireMultiplier(GameState s) => HasRole(s, "skipper") ? 0.75m : 1.0m;

        /// <summary>Wellboat delay reduction in days (skipper routing).</summary>
        public static int WellboatDelayReduction(GameState s) => HasRole(s, "skipper") ? 1 : 0;
    }
}
