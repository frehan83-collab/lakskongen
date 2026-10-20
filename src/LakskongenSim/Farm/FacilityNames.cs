using System;
using System.Collections.Generic;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// The names the UI and mods use for an offshore structure class and a RAS
    /// scale (roadmap F2-03 acceptance 8). The bridge documented "Ship",
    /// "Submerged" and "Mega", which <c>Enum.TryParse</c> never accepted (the
    /// enums say ShipShaped, Submersible, and have no Mega), and it accepted a
    /// number ("7") as an undefined enum value. Pure; case, spaces, '-' and '_'
    /// do not matter.
    /// </summary>
    public static class FacilityNames
    {
        private static readonly Dictionary<string, OffshoreType> OffshoreAliases = new(StringComparer.Ordinal)
        {
            ["semisubmersible"] = OffshoreType.SemiSubmersible,
            ["semi"] = OffshoreType.SemiSubmersible,
            ["of1"] = OffshoreType.SemiSubmersible,
            ["of1class"] = OffshoreType.SemiSubmersible,
            ["havmerd"] = OffshoreType.SemiSubmersible,
            ["shipshaped"] = OffshoreType.ShipShaped,
            ["ship"] = OffshoreType.ShipShaped,
            ["havfarm"] = OffshoreType.ShipShaped,
            ["havfarmclass"] = OffshoreType.ShipShaped,
            ["skipsmerd"] = OffshoreType.ShipShaped,
            ["submersible"] = OffshoreType.Submersible,
            ["submerged"] = OffshoreType.Submersible,
        };

        private static readonly Dictionary<string, RASScale> ScaleAliases = new(StringComparer.Ordinal)
        {
            ["small"] = RASScale.Small,
            ["medium"] = RASScale.Medium,
            ["large"] = RASScale.Large,
            // The 20,000 t scale is the "mega facility" of RASFacility.GetCAPEX.
            ["mega"] = RASScale.Large,
        };

        /// <summary>An offshore structure class by name: its enum name or an alias ("semi_submersible", "Ship", "Submerged", "OF1", "Havfarm").</summary>
        public static bool TryParseOffshoreType(string? text, out OffshoreType type)
            => OffshoreAliases.TryGetValue(Key(text), out type);

        /// <summary>A RAS scale by name: its enum name or "Mega" (the 20,000 t Large).</summary>
        public static bool TryParseRasScale(string? text, out RASScale scale)
            => ScaleAliases.TryGetValue(Key(text), out scale);

        private static string Key(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var chars = new List<char>(text.Length);
            foreach (char c in text.Trim())
                if (c != '_' && c != '-' && c != ' ')
                    chars.Add(char.ToLowerInvariant(c));
            return new string(chars.ToArray());
        }
    }
}
