using System;
using System.Collections.Generic;

namespace LakskongenSim.Environment.Geo
{
    /// <summary>
    /// Readable place labels for the candidate sites. A candidate's <c>near</c>
    /// is the nearest Kartverket N250 label text, and N250 splits long names
    /// over two text objects ("Bømlafjord-" over "tunnelen") or keeps a
    /// lower-case second line ("hammar"). Some labels name a feature, not a
    /// place ("Haugesund lufthavn"). <see cref="Clean"/> turns each into a
    /// label a player can read: an override for a known non-place or split
    /// label, else the text with a trailing hyphen trimmed and the first
    /// letter capitalised. The shipped data is never edited (its hashes are pinned).
    /// </summary>
    public static class PlaceLabels
    {
        /// <summary>
        /// Labels that name an airport, tunnel, lighthouse, monastery or park
        /// instead of the place, and split labels whose full name is known
        /// (the candidate lies in the named HTH area or by the named island).
        /// </summary>
        private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
        {
            ["Haugesund lufthavn"] = "Haugesund",
            ["Stokmarknes lufthavn"] = "Stokmarknes",
            ["Vardøtunnelen"] = "Vardø",
            ["Homborsund fyr"] = "Homborsund",
            ["Selje kloster"] = "Selje",
            ["Jomfruland nasjonalpark"] = "Jomfruland",
            ["Færder nasjonalpark"] = "Færder",
            // HTH21-02 lies in the Fugløybanken offshore area.
            ["Fugløy-"] = "Fugløybanken",
            // PO05-12 lies off Valderøya (Giske).
            ["Valder-"] = "Valderøya",
        };

        /// <summary>The readable label of a candidate's <c>near</c> text.</summary>
        public static string Clean(string near)
        {
            if (near == null) throw new ArgumentNullException(nameof(near));
            string text = near.Trim();
            if (Overrides.TryGetValue(text, out var label)) return label;
            text = text.TrimEnd('-').TrimEnd();
            if (text.Length == 0) return text;
            return char.ToUpperInvariant(text[0]) + text[1..];
        }
    }
}
