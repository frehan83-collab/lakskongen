using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Farm;

namespace StateHashProbe;

/// <summary>
/// The state a golden trace hashes every day (F2-01), as canonical text:
/// each group maps an entity key (a pen, a site, a ledger line) to
/// "field=value;field=value" with exact values (doubles round-trip, decimals
/// without trailing zeros). Hashes and tags are SHA-256 prefixes of that text,
/// so a trace names the group, the entity and, at a snapshot, the field.
/// </summary>
public sealed class GoldenTraceState
{
    /// <summary>The hashed groups in report order (the first differing one names a divergence).</summary>
    public static readonly string[] GroupOrder = { "pop", "lice", "mtb", "mort", "ledger", "cash" };

    /// <summary>What each group holds (written to every trace header).</summary>
    public static readonly IReadOnlyDictionary<string, string> GroupDescriptions = new SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        ["pop"] = "every population (GameState.Populations) keyed by its pen (sea), tank or pen index (facility) or holding ordinal: fish count, biomass kg, per cohort count, mean weight kg, weight CV, age days, life stage",
        ["lice"] = "every sea site's lice model: chalimus, pre-adult, adult female and adult male per fish, copepodid density",
        ["mtb"] = "capacity: the regime, per production area the licences, pooled licence MTB t, standing biomass t and an open reduction order's deadline; per sea site the locality MTB t",
        ["mort"] = "the S2-05 mortality ledger, cumulative: fish, kg and NOK per death cause and per removal kind",
        ["ledger"] = "the cash ledger's statements: period P&L lines, assets, liabilities, biomass inventory, lifetime totals, cost and revenue lines by category",
        ["cash"] = "FinanceManager.Balance, outstanding loan principal, unbooked cash",
        ["rng"] = "every SimRng stream's position (the day hash covers the states; the day line lists each stream's draws that day)",
    };

    private const ulong SplitMixGamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Multiplicative inverse of the SplitMix64 gamma mod 2^64: a state difference times it is the draw count.</summary>
    private static readonly ulong GammaInverse = InvertOdd(SplitMixGamma);

    /// <summary>Group name → entity key → canonical text.</summary>
    public IReadOnlyDictionary<string, SortedDictionary<string, string>> Groups { get; }

    /// <summary>Stream name ("scope" or "scope/suffix") → SplitMix64 state.</summary>
    public SortedDictionary<string, ulong> Streams { get; }

    private GoldenTraceState(Dictionary<string, SortedDictionary<string, string>> groups, SortedDictionary<string, ulong> streams)
    {
        Groups = groups;
        Streams = streams;
    }

    /// <summary>Read the traced state of <paramref name="engine"/> now.</summary>
    public static GoldenTraceState Capture(SimulationEngine engine)
    {
        var groups = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["pop"] = Populations(engine),
            ["lice"] = Lice(engine),
            ["mtb"] = Capacity(engine),
            ["mort"] = Mortality(engine),
            ["ledger"] = Ledger(engine),
            ["cash"] = Cash(engine),
        };
        var streams = new SortedDictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var kv in engine.Rng.CaptureStates())
            streams[StreamName(kv.Key)] = kv.Value;
        return new GoldenTraceState(groups, streams);
    }

    /// <summary>
    /// 64-bit hash (16 hex) of every group and stream: it detects a divergence
    /// on the day it happens; the 16-bit group hashes only name the group.
    /// </summary>
    public string DayHash()
    {
        var sb = new StringBuilder();
        foreach (var group in GroupOrder)
            sb.Append(group).Append('\u001d').Append(GroupText(group));
        sb.Append("rng").Append('\u001d').Append(StreamText());
        return Hex(sb.ToString(), 8);
    }

    /// <summary>16-bit hash (4 hex) of a whole group: names the group of a divergence the day hash found.</summary>
    public string GroupHash(string group) => Hex(GroupText(group), 2);

    /// <summary>16-bit hash (4 hex) of every stream position.</summary>
    public string StreamHash() => Hex(StreamText(), 2);

    /// <summary>16-bit tag (4 hex) of one keyed value (a new stream's state).</summary>
    public static string Tag(string key, string value) => Hex(key + "\u001f" + value, 2);

    private string GroupText(string group)
    {
        var sb = new StringBuilder();
        foreach (var kv in Groups[group])
            sb.Append(kv.Key).Append('\u001f').Append(kv.Value).Append('\u001e');
        return sb.ToString();
    }

    private string StreamText()
    {
        var sb = new StringBuilder();
        foreach (var kv in Streams)
            sb.Append(kv.Key).Append('\u001f').Append(StateHex(kv.Value)).Append('\u001e');
        return sb.ToString();
    }

    /// <summary>Draws a stream made between two states (SplitMix64 adds the gamma once per draw).</summary>
    public static ulong DrawsBetween(ulong before, ulong after) => unchecked((after - before) * GammaInverse);

    /// <summary>A stream state as 16 hex digits.</summary>
    public static string StateHex(ulong state) => state.ToString("x16", CultureInfo.InvariantCulture);

    /// <summary>First <paramref name="bytes"/> bytes of SHA-256(UTF-8 text) as lower-case hex.</summary>
    public static string Hex(string text, int bytes)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, bytes).ToLowerInvariant();

    // ── Groups ─────────────────────────────────────────────────────────────

    private static SortedDictionary<string, string> Populations(SimulationEngine engine)
    {
        var keys = PopulationKeys(engine);
        var group = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var population in engine.State.Populations)
        {
            var sb = new StringBuilder();
            sb.Append("n=").Append(I(population.TotalCount)).Append(";kg=").Append(D(population.GetTotalBiomassKg()));
            for (int c = 0; c < population.Cohorts.Count; c++)
            {
                var cohort = population.Cohorts[c];
                string p = ";c" + I(c) + ".";
                sb.Append(p).Append("n=").Append(I(cohort.Count))
                  .Append(p).Append("w=").Append(D(cohort.MeanWeightKg))
                  .Append(p).Append("cv=").Append(D(cohort.WeightCv))
                  .Append(p).Append("age=").Append(I(cohort.AgeDays))
                  .Append(p).Append("stage=").Append(cohort.Stage.ToString());
            }
            group[Unique(group, keys[population])] = sb.ToString();
        }
        return group;
    }

    /// <summary>
    /// A population's key: its pen id at a sea site (S2-01b), "site/unpenned#k"
    /// for a site population without a pen, "facility/tNN" or "farm/pNN" for
    /// a RAS tank or offshore pen, otherwise "holding#k" by its order in the
    /// game's list (hatchery batches). Never the population's Guid.
    /// </summary>
    private static Dictionary<FishPopulation, string> PopulationKeys(SimulationEngine engine)
    {
        var keys = new Dictionary<FishPopulation, string>(ReferenceEqualityComparer.Instance);
        foreach (var site in engine.State.SeaSites)
        {
            int unpenned = 0;
            foreach (var population in site.Pens)
                keys.TryAdd(population, site.PenOf(population)?.Id ?? $"{site.Id}/unpenned#{I(unpenned++)}");
        }
        foreach (var facility in engine.State.RASFacilities)
            for (int t = 0; t < facility.Tanks.Count; t++)
                keys.TryAdd(facility.Tanks[t], $"{facility.Id}/t{t + 1:D2}");
        foreach (var farm in engine.State.OffshoreFarms)
            for (int p = 0; p < farm.Pens.Count; p++)
                keys.TryAdd(farm.Pens[p], $"{farm.Id}/p{p + 1:D2}");
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var population in engine.State.Populations)
        {
            if (keys.ContainsKey(population)) continue;
            string holding = string.IsNullOrEmpty(population.SiteId) ? "unassigned" : population.SiteId;
            ordinals.TryGetValue(holding, out int k);
            ordinals[holding] = k + 1;
            keys[population] = $"{holding}#{I(k)}";
        }
        return keys;
    }

    private static SortedDictionary<string, string> Lice(SimulationEngine engine)
    {
        var group = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var site in engine.State.SeaSites)
        {
            var lice = site.Lice;
            group[Unique(group, "site/" + site.Id)] =
                $"ch={D(lice.ChalimusPerFish)};pa={D(lice.PreAdultPerFish)};af={D(lice.AdultFemalePerFish)};am={D(lice.AdultMalePerFish)};cop={D(lice.CopepodidDensity)}";
        }
        return group;
    }

    private static SortedDictionary<string, string> Capacity(SimulationEngine engine)
    {
        var state = engine.State;
        var group = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["regime"] = $"rules={(state.CapacityRules?.ToString() ?? "none")};licences={I(state.LicenceHoldings.Count)}"
        };
        var areas = new SortedSet<int>();
        foreach (var site in state.SeaSites)
        {
            areas.Add(engine.ProductionAreaOfSite(site));
            group[Unique(group, "site/" + site.Id)] = $"mtb={D(site.MABTonnes)}";
        }
        foreach (var licence in state.LicenceHoldings) areas.Add(licence.PooledArea);
        foreach (int area in areas)
        {
            var order = engine.OpenCapacityBreach(area);
            group[$"area/{area:D2}"] =
                $"licences={I(state.LicenceHoldings.Count(l => l.PooledArea == area))};pool={D(engine.AreaLicenceMtbTonnes(area))};" +
                $"standing={D(engine.AreaStandingBiomassTonnes(area))};order={I(order?.DeadlineDay ?? -1)}";
        }
        return group;
    }

    private static SortedDictionary<string, string> Mortality(SimulationEngine engine)
    {
        var book = engine.Mortality.Ledger.Cumulative();
        var group = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var cause in MortalityCauses.All)
            group["death/" + MortalityCauses.Id(cause)] = Tally(book.Deaths(cause));
        foreach (var removal in MortalityCauses.AllRemovals)
            group["removed/" + MortalityCauses.Id(removal)] = Tally(book.Removed(removal));
        return group;
    }

    private static string Tally(MortalityTally t) => $"fish={I(t.Fish)};kg={D(t.Kg)};nok={M(t.ValueNok)}";

    private static SortedDictionary<string, string> Ledger(SimulationEngine engine)
    {
        var fs = engine.FinancialStatements;
        var life = fs.ExportLifetime();
        var group = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["period"] = $"revenue={M(fs.Revenue)};cogs={M(fs.COGS)};opex={M(fs.OperatingExpenses)};interest={M(fs.InterestExpense)};taxes={M(fs.Taxes)};quarter={I(fs.CurrentPeriod)}",
            ["balance"] = $"assets={M(fs.Assets)};liabilities={M(fs.Liabilities)};biomass={M(fs.BiomassInventoryValue)}",
            ["lifetime"] = $"revenue={M(life.Revenue)};cogs={M(life.Cogs)};opex={M(life.Opex)};interest={M(life.Interest)};taxes={M(life.Taxes)};" +
                           $"capex={M(life.Capex)};borrowed={M(life.LiabilitiesIncurred)};repaid={M(life.LiabilitiesRepaid)};equity={M(life.EquityRaised)}",
        };
        foreach (var kv in fs.CostBreakdown) group["cost/" + kv.Key] = "nok=" + M(kv.Value);
        foreach (var kv in fs.RevenueBreakdown) group["revenue/" + kv.Key] = "nok=" + M(kv.Value);
        return group;
    }

    private static SortedDictionary<string, string> Cash(SimulationEngine engine) => new(StringComparer.Ordinal)
    {
        ["balance"] = "nok=" + M(engine.FinanceManager.Balance),
        ["debt"] = "nok=" + M(engine.TotalDebtNok),
        ["unbooked"] = "nok=" + M(engine.UnbookedCashTotal),
    };

    // ── Canonical numbers ─────────────────────────────────────────────────

    /// <summary>A double that round-trips exactly; -0 is written as 0 (equal in value).</summary>
    public static string D(double v) => v == 0.0 ? "0" : v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A decimal's value without trailing zeros (5.00 and 5 are one value).</summary>
    public static string M(decimal v) => (v / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture);

    private static string I(long v) => v.ToString(CultureInfo.InvariantCulture);

    private static string Unique(SortedDictionary<string, string> group, string key)
    {
        if (!group.ContainsKey(key)) return key;
        int n = 2;
        while (group.ContainsKey($"{key}~{I(n)}")) n++;
        return $"{key}~{I(n)}";
    }

    private static string StreamName(string key)
    {
        var parts = key.Split('\u0001');
        return string.Join("/", parts.Where((part, i) => i == 0 || part.Length > 0));
    }

    private static ulong InvertOdd(ulong a)
    {
        unchecked
        {
            ulong x = a; // a·a ≡ 1 (mod 8) for odd a; each Newton step doubles the correct bits
            for (int i = 0; i < 6; i++) x *= 2UL - a * x;
            return x;
        }
    }
}
