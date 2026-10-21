using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;

namespace LakskongenSim.Finance
{
    /// <summary>
    /// Generated names for the independent rival farmers on the real coast
    /// (STAGE23_PLAN track A, A3; roadmap D17; rulings OD8 and OD10): a coast or
    /// nature word, a farming-trade word and the legal form, "Krykkje Merdlag AS".
    /// <list type="bullet">
    /// <item>The words are curated code literals (docs/BALANCE.md §1ab lists
    /// them for the owner's review), so tests/test_text_correctness.py scans
    /// them with the rest of the sim's text.</item>
    /// <item>No word is a place: a name holding any token of a locality's or a
    /// municipality's name in <c>localities.geojson</c> is rejected here, and
    /// the curated lists hold none. Real places double as company names
    /// (Bremnes, Eidsfjord, Kvarøy), and the registry's holders are mostly
    /// "&lt;place&gt; &lt;trade&gt;".</item>
    /// <item>No word is a CamelCase token or an English word TextLint flags;
    /// ParodyNamesTests checks every name against BRAND_RE, the place tokens
    /// and TextLint's rules, and the curation was checked offline against the
    /// raw registry's holder names (0 matches, never committed).</item>
    /// </list>
    /// <see cref="Of"/> is deterministic: names are dealt in one global order
    /// (index-major across the 14 area slots 0-13), each by a StableHash pick
    /// with linear probing past names already dealt, so every (area, index)
    /// has one name in every process, whatever order they are asked in, and no
    /// two are equal. No RNG and no game state.
    /// </summary>
    public static class ParodyNames
    {
        /// <summary>The legal form every generated name ends in.</summary>
        public const string LegalForm = "AS";

        /// <summary>Area slots: 0 (a locality with no production area) and PO1-PO13.</summary>
        public const int AreaSlots = 14;

        /// <summary>Coast and nature words: birds, sea life, sea and weather, gear, shore plants.</summary>
        private static readonly string[] CoastWords =
        {
            // Sea and shore birds.
            "Skarv", "Ærfugl", "Måke", "Terne", "Lunde", "Alke", "Teist", "Lomvi", "Krykkje", "Havhest",
            "Tjeld", "Svartbak", "Gråmåke", "Havørn", "Storlom", "Smålom", "Siland", "Laksand", "Havelle",
            "Sjøorre", "Gråhegre", "Strandsnipe", "Rødstilk", "Steinvender", "Fiskeørn", "Havsule",
            "Polarmåke", "Fjellvåk", "Sandlo", "Myrsnipe",
            // Sea life.
            "Stortare", "Blæretang", "Sukkertare", "Krill", "Raudåte", "Brisling", "Sild", "Makrell", "Sei",
            "Lyr", "Hyse", "Kveite", "Breiflabb", "Steinbit", "Rognkjeks", "Leppefisk", "Berggylt",
            "Grønngylt", "Sjøstjerne", "Kråkebolle", "Manet", "Krabbe", "Reke", "Sjøpølse", "Sjøanemone",
            "Taskekrabbe", "Strandsnegl", "Albueskjell",
            // Sea and weather.
            "Bølge", "Dønning", "Brenning", "Fjære", "Kuling", "Bris", "Skodde", "Morild", "Havgule",
            "Nordavind", "Sønnavind", "Vestavind", "Austavind", "Havdis", "Solgangsvind", "Flomål",
            "Springflo", "Fjærestein", "Bårebrott", "Kvitskavl",
            // Gear and the working shore.
            "Anker", "Kjøl", "Spant", "Seil", "Åre", "Garn", "Fyrlykt", "Bøye", "Pullert", "Trosse",
            "Vaier", "Fortøyning", "Notbøter", "Merdring", "Fôrflåte", "Naust", "Rorbu", "Kaipåle",
            "Hiveline",
            // Shore plants.
            "Lyng", "Einer", "Rogn", "Multe", "Tyttebær", "Krekling", "Røsslyng", "Strandkål", "Strandrug",
            "Strandvindel", "Rosenrot", "Fjæresauløk", "Sølvbunke", "Marehalm", "Tiriltunge",
        };

        /// <summary>Farming-trade words (none is a word the registry's holder names use).</summary>
        private static readonly string[] TradeWords =
        {
            "Sjøbruk", "Kystbruk", "Merdbruk", "Laksebruk", "Havgård", "Merdlag", "Havlag", "Kystlag",
            "Notlag", "Oppdrettslag",
        };

        private static readonly object Gate = new();
        private static readonly List<string> Dealt = new();
        private static readonly HashSet<string> Taken = new(StringComparer.Ordinal);
        private static readonly Lazy<string[]> LazyCombinations = new(BuildCombinations);

        /// <summary>The coast and nature words, in list order (review and tests).</summary>
        public static IReadOnlyList<string> CoastVocabulary => CoastWords;

        /// <summary>The trade words, in list order (review and tests).</summary>
        public static IReadOnlyList<string> TradeVocabulary => TradeWords;

        /// <summary>Every name the vocabulary allows, after the place-token rejection, in list order.</summary>
        public static IReadOnlyList<string> Combinations => LazyCombinations.Value;

        /// <summary>
        /// The name of independent <paramref name="index"/> (0-based) in area
        /// slot <paramref name="po"/> (0-13): unique across every (area, index).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The area is outside 0-13 or the index negative.</exception>
        /// <exception cref="InvalidOperationException">The vocabulary has no free name left.</exception>
        public static string Of(int po, int index)
        {
            if (po < 0 || po >= AreaSlots) throw new ArgumentOutOfRangeException(nameof(po), po, "Area slots are 0-13");
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), index, "Indexes start at 0");
            long ordinal = (long)index * AreaSlots + po;
            lock (Gate)
            {
                while (Dealt.Count <= ordinal)
                    Dealt.Add(Deal(Dealt.Count));
                return Dealt[(int)ordinal];
            }
        }

        /// <summary>True when a name holds a token of any locality or municipality name (case-insensitive).</summary>
        public static bool HasPlaceToken(string name) => Words(name).Any(PlaceTokens.Value.Contains);

        private static readonly Lazy<HashSet<string>> PlaceTokens = new(() =>
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in GeoLocalities.Default.All)
            {
                tokens.UnionWith(Words(l.Name));
                tokens.UnionWith(Words(l.Municipality));
            }
            return tokens;
        });

        private static readonly Regex WordPattern = new("[A-Za-zÀ-ÖØ-öø-ÿ]+", RegexOptions.CultureInvariant);

        private static IEnumerable<string> Words(string text)
            => WordPattern.Matches(text ?? "").Select(m => m.Value.ToLowerInvariant());

        private static string[] BuildCombinations()
        {
            var names = new List<string>(CoastWords.Length * TradeWords.Length);
            foreach (var coast in CoastWords)
                foreach (var trade in TradeWords)
                {
                    string name = $"{coast} {trade} {LegalForm}";
                    if (!HasPlaceToken(name))
                        names.Add(name);
                }
            return names.ToArray();
        }

        private static string Deal(int ordinal)
        {
            var names = LazyCombinations.Value;
            if (Taken.Count >= names.Length)
                throw new InvalidOperationException($"The parody-name vocabulary is exhausted ({names.Length} names)");
            int start = (int)(StableHash.Of("parody_name", ordinal) % (ulong)names.Length);
            for (int step = 0; ; step++)
            {
                string name = names[(start + step) % names.Length];
                if (Taken.Add(name)) return name;
            }
        }
    }
}
