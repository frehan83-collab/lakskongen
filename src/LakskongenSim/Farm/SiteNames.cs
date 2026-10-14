using System.Collections.Generic;
using System.Linq;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Player-facing names for sea sites (roadmap U1-03: the UI showed raw ids
    /// such as "po3_nord" or "site_2"). Scenario sites carry their own name; a
    /// site the player builds takes the first free name from a fixed list of
    /// Norwegian coastal place-name forms, so the choice is deterministic.
    /// </summary>
    public static class SiteNames
    {
        /// <summary>Generated site names, in the order they are handed out.</summary>
        public static readonly IReadOnlyList<string> Pool = new[]
        {
            "Storholmen", "Langøya", "Kvitneset", "Brattvika", "Revsholmen", "Skjærvika",
            "Sandøya", "Hestneset", "Måkeskjæret", "Furuholmen", "Djupvika", "Selvika",
            "Bjørnøya", "Tarebukta", "Kobbeneset", "Lyngholmen", "Steinvika", "Ospeneset",
        };

        /// <summary>The first pool name no existing site uses; after the pool,
        /// "Lokalitet N" with the first free N.</summary>
        public static string NextFor(IEnumerable<SeaSite> existing)
        {
            var used = new HashSet<string>(existing.Select(s => s.Label));
            foreach (var name in Pool)
                if (!used.Contains(name)) return name;
            for (int n = 1; ; n++)
            {
                var name = $"Lokalitet {n}";
                if (!used.Contains(name)) return name;
            }
        }

        /// <summary>A readable form of a site id: "po3_nord" → "Po3 nord".</summary>
        public static string Humanize(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            var words = id.Replace('_', ' ').Replace('-', ' ').Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var text = string.Join(' ', words).ToLowerInvariant();
            return char.ToUpperInvariant(text[0]) + text[1..];
        }
    }
}
