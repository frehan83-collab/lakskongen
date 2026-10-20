#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment.Geo;
using LakskongenSim.Finance;
using LakskongenSim.Tests.Geo;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Rivals;

/// <summary>
/// The generated independents' names (STAGE23_PLAN track A, A3; D17; rulings
/// OD8 and OD10): deterministic, unique, never a real brand (BRANDS read from
/// tests/test_text_correctness.py), never a place token from the localities,
/// and clean under TextLint's rules (rules read from godot/tests/TextLint.gd).
/// </summary>
public class ParodyNamesTests
{
    private readonly ITestOutputHelper _output;

    public ParodyNamesTests(ITestOutputHelper output) => _output = output;

    /// <summary>Names a game can show: every slot's first 40, and every independent of the starting layout.</summary>
    private static IReadOnlyList<string> Sample()
    {
        var names = new List<string>();
        for (int index = 0; index < 40; index++)
            for (int po = 0; po < ParodyNames.AreaSlots; po++)
                names.Add(ParodyNames.Of(po, index));
        var layout = RivalLayout.Build(new CompetitorAISystem(7).Competitors.ToArray(), new HashSet<int>());
        names.AddRange(layout.Owners.Where(o => !o.IsMajor).Select(o => o.Name));
        return names.Distinct().ToArray();
    }

    private static string TextLintSource() => File.ReadAllText(Path.Combine(GeoTestData.RepoRoot, "godot", "tests", "TextLint.gd"));

    /// <summary>TextLint.gd's ENGLISH_WORDS, read from the GDScript so the two cannot drift.</summary>
    private static HashSet<string> EnglishWords()
    {
        string gd = TextLintSource();
        int start = gd.IndexOf("const ENGLISH_WORDS", StringComparison.Ordinal);
        Assert.True(start >= 0, "ENGLISH_WORDS not found in TextLint.gd");
        int end = gd.IndexOf(']', start);
        var words = Regex.Matches(gd[start..end], "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(words.Count > 100, "ENGLISH_WORDS parsed too short");
        return words;
    }

    /// <summary>A TextLint pattern constant ("ENUM_UPPER" …) as a .NET regex.</summary>
    private static Regex TextLintPattern(string name)
    {
        var m = Regex.Match(TextLintSource(), "const " + name + " := \"((?:[^\"\\\\]|\\\\.)*)\"");
        Assert.True(m.Success, name + " not found in TextLint.gd");
        return new Regex(m.Groups[1].Value.Replace("\\\\", "\\"));
    }

    private static HashSet<string> PlaceTokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var word = new Regex("[A-Za-zÀ-ÖØ-öø-ÿ]+");
        foreach (var l in GeoLocalities.Default.All)
            foreach (var text in new[] { l.Name, l.Municipality })
                foreach (Match m in word.Matches(text))
                    tokens.Add(m.Value.ToLowerInvariant());
        return tokens;
    }

    [Fact]
    public void Names_AreDeterministic_AndUniqueAcrossEveryAreaAndIndex()
    {
        var first = Enumerable.Range(0, 30).SelectMany(i => Enumerable.Range(0, ParodyNames.AreaSlots).Select(po => ParodyNames.Of(po, i))).ToArray();
        var again = Enumerable.Range(0, 30).Reverse().SelectMany(i => Enumerable.Range(0, ParodyNames.AreaSlots).Reverse().Select(po => ParodyNames.Of(po, i))).Reverse().ToArray();
        Assert.Equal(first, again);
        Assert.Equal(first.Length, first.Distinct().Count());
        Assert.All(first, n => Assert.Matches(@"^\S+ \S+ AS$", n));
    }

    /// <summary>
    /// The dealing is the documented one, re-derived here: ordinal g = index ×
    /// 14 + area, StableHash.Of("parody_name", g) into the combinations, then
    /// linear probing past names already dealt. Any process gets these names.
    /// </summary>
    [Fact]
    public void TheDealing_IsTheDocumentedHashWithLinearProbing()
    {
        var combos = ParodyNames.Combinations;
        var taken = new HashSet<string>(StringComparer.Ordinal);
        for (int g = 0; g < 14 * 25; g++)
        {
            int start = (int)(StableHash.Of("parody_name", g) % (ulong)combos.Count);
            string name = Enumerable.Range(0, combos.Count).Select(k => combos[(start + k) % combos.Count]).First(n => !taken.Contains(n));
            taken.Add(name);
            Assert.Equal(name, ParodyNames.Of(g % 14, g / 14));
        }
        Assert.Equal("Steinvender Sjøbruk AS", ParodyNames.Of(0, 0));
    }

    [Fact]
    public void NoName_IsARealBrand()
    {
        var names = Sample();
        Assert.Contains("Nordlaks", GeoTestData.Brands);
        var hits = names.Where(GeoTestData.HasBrand).ToArray();
        Assert.True(hits.Length == 0, "brand names: " + string.Join(", ", hits));
        Assert.True(GeoTestData.HasBrand("Skarv Nordlaks AS"), "the brand check must catch a planted brand");
        _output.WriteLine($"{names.Count} names checked");
    }

    [Fact]
    public void NoName_HoldsAPlaceToken_AndTheGeneratorRejectsOne()
    {
        var tokens = PlaceTokens();
        var hits = Sample().Where(n => Regex.Matches(n, "[A-Za-zÀ-ÖØ-öø-ÿ]+").Any(m => tokens.Contains(m.Value.ToLowerInvariant()))).ToArray();
        Assert.True(hits.Length == 0, "place tokens: " + string.Join(", ", hits));
        // No curated word is a place, and the guard catches one that would be.
        Assert.DoesNotContain(ParodyNames.CoastVocabulary.Concat(ParodyNames.TradeVocabulary), w => tokens.Contains(w.ToLowerInvariant()));
        Assert.True(ParodyNames.HasPlaceToken("Kvinnherad Merdlag AS"));
        Assert.True(ParodyNames.HasPlaceToken("Skarv Frøya AS"));
        Assert.False(ParodyNames.HasPlaceToken("Skarv Merdlag AS"));
        Assert.Equal(ParodyNames.CoastVocabulary.Count * ParodyNames.TradeVocabulary.Count, ParodyNames.Combinations.Count);
    }

    [Fact]
    public void Names_PassTextLint_NoEnumIdFormatOrEnglishWord()
    {
        var english = EnglishWords();
        var patterns = new[] { "ENUM_UPPER", "ENUM_LOWER", "ID_UPPER", "ID_SNAKE", "FORMAT", "RAW" }.Select(TextLintPattern).ToArray();
        var word = TextLintPattern("WORD");
        foreach (var name in Sample())
        {
            foreach (var p in patterns)
                Assert.False(p.IsMatch(name), $"{name}: TextLint {p}");
            foreach (Match w in word.Matches(name))
                Assert.DoesNotContain(w.Value.ToLowerInvariant(), english);
        }
        // Non-vacuity: the parsed rules catch what they should.
        Assert.Matches(patterns[0], "SalmonCo");
        Assert.Contains("the", english);
    }

    [Fact]
    public void TheVocabulary_IsCurated_NoRepeats()
    {
        var coast = ParodyNames.CoastVocabulary;
        var trade = ParodyNames.TradeVocabulary;
        Assert.True(coast.Count >= 100, $"{coast.Count} coast words");
        Assert.Equal(10, trade.Count);
        Assert.Equal(coast.Count, coast.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(trade.Count, trade.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(coast.Concat(trade), w => Assert.Matches("^[A-ZÆØÅ][a-zæøåôé]+$", w));
        _output.WriteLine($"{coast.Count} × {trade.Count} = {ParodyNames.Combinations.Count} names");
    }

    /// <summary>
    /// The vocabulary the captain reviewed (ruling OD10, geo/int 2026-10-21):
    /// 112 coast and nature words and 10 trade words, 1,120 names, pinned by
    /// content so no word changes unreviewed. The review replaced "Tare" with
    /// "Stortare": "Tare" equals a hyphen-joined token of one registry holder's
    /// name (checked offline against ~/.cache/lakskongen-geo, counts only).
    /// To change a word: check it against BRANDS, the place tokens and,
    /// offline, the registry's holder names (0 matches under whitespace and
    /// hyphen tokenisation), then re-pin here and RivalLayoutTests'
    /// PinnedDigest with a RUN_LOG §7 row.
    /// </summary>
    [Fact]
    public void TheVocabulary_IsTheReviewedOne()
    {
        Assert.Equal(112, ParodyNames.CoastVocabulary.Count);
        Assert.Equal(10, ParodyNames.TradeVocabulary.Count);
        Assert.Equal(1120, ParodyNames.Combinations.Count);
        Assert.DoesNotContain("Tare", ParodyNames.CoastVocabulary);
        string text = string.Join("\n", ParodyNames.CoastVocabulary) + "\n\n" + string.Join("\n", ParodyNames.TradeVocabulary);
        Assert.Equal(ReviewedVocabulary, StableHash.Of(text).ToString("x16", CultureInfo.InvariantCulture));
    }

    /// <summary>The digest of the reviewed word lists (<see cref="TheVocabulary_IsTheReviewedOne"/>).</summary>
    private const string ReviewedVocabulary = "173eaaf258fc7b78";

    [Fact]
    public void Of_RefusesAnAreaOutsideTheSlotsOrANegativeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ParodyNames.Of(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParodyNames.Of(ParodyNames.AreaSlots, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParodyNames.Of(3, -1));
    }

    [Fact]
    public void TheRenamedNorthernMajor_IsClearlyNotTheRealBrand()
    {
        var company = new CompetitorAISystem(7).Competitors.Single(c => c.Id == "nordlaks_nord");
        Assert.Equal("Kongsemne Havbruk AS", company.Name);
        Assert.False(GeoTestData.HasBrand(company.Name));
        Assert.DoesNotContain("laks", company.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nord", company.Name, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(company.Name, Sample());
    }
}
