using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace LakskongenSim.Core;

/// <summary>
/// Deterministic RNG service with named streams (NEXT_LEVEL_PLAN Phase 1.4).
/// Streams are derived from the game seed and a stable FNV-1a name hash via a
/// SplitMix64 core — independent of System.Random's implementation-defined
/// sequence and of .NET's process-randomized string hashing.
///
/// API contract:
/// - SCOPE names are frozen constants (<see cref="SimRngStreams"/>); each
///   scope belongs to exactly one consumer class.
/// - PER-IDENTITY draws MUST use the suffix overloads, e.g.
///   <c>Rng.Stream("delouse", site.Id)</c> — never one shared stream iterated
///   in list order, because adding or reordering sites would re-randomize
///   the whole world (stream-sharing bug class, see SimRngTests).
/// - A returned stream keeps its own position. Repeated Stream(name) calls
///   within one SimRng return the same stream instance (draws continue),
///   which keeps call-order coupling visible instead of silently new.
/// </summary>
public sealed class SimRng
{
    private const ulong FnvOffset = 0xcbf29ce484222325UL;
    private const ulong FnvPrime = 0x100000001b3UL;

    private readonly ulong _gameSeed;
    private readonly Dictionary<string, SimRngStream> _streams = new(StringComparer.Ordinal);

    public SimRng(int seed) => _gameSeed = (ulong)seed;

    /// <summary>
    /// Unseeded fallback. The live game always passes a seed (StartNewGame
    /// stores GameState.RandomSeed); this exists only for headless/legacy
    /// entry points and uses a crypto RNG so no gameplay stream is derived
    /// from wall-clock correlations.
    /// </summary>
    public static SimRng CreateUnseeded()
        => new(RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue));

    /// <summary>Get (or lazily create) the single stream for a scope.</summary>
    public SimRngStream Stream(string scope) => Stream(scope, Array.Empty<string>());

    /// <summary>
    /// Get a per-identity stream: the scope salt is chained through the
    /// suffixes, so drawing order between different identity sets can never
    /// influence each other's sequences. Both overloads share the key
    /// derivation — Stream(name) and Stream(name, []) are the same stream.
    /// </summary>
    public SimRngStream Stream(string scope, params string[] suffixes)
    {
        string key = scope + "\u0001" + string.Join("\u0001", suffixes);
        if (_streams.TryGetValue(key, out var existing))
            return existing;

        ulong state = _gameSeed ^ Fnv1a(scope);
        foreach (var suffix in suffixes)
            state = Mix(state ^ Fnv1a(suffix));
        var stream = new SimRngStream(Mix(state));
        _streams[key] = stream;
        return stream;
    }

    /// <summary>Capture every named stream position (Phase 1 wave 6).</summary>
    public Dictionary<string, ulong> CaptureStates()
    {
        var states = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var kvp in _streams)
            states[kvp.Key] = kvp.Value.ExportState();
        return states;
    }

    /// <summary>Restore stream positions for the streams that exist.</summary>
    public void ImportStates(Dictionary<string, ulong> states)
    {
        foreach (var kvp in states)
            if (_streams.TryGetValue(kvp.Key, out var stream))
                stream.ImportState(kvp.Value);
    }

    internal static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Fnv1a(string value)
    {
        ulong hash = FnvOffset;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= FnvPrime;
        }
        return hash;
    }
}

/// <summary>
    /// Frozen stream scope names. One owner class per scope; per-identity draws
    /// go through the suffix overloads.
    /// </summary>
    public static class SimRngStreams
    {
        /// <summary>AIFeedingSystem camera-reading noise (daily tick).</summary>
        public const string AiFeeding = "ai_feeding";

        /// <summary>New crew member morale roll (GameBridge.HireWorker).</summary>
        public const string HireWorker = "hire_worker";

        /// <summary>Transfer losses on wellboat cohort moves (per source+destination).</summary>
        public const string CohortTransfer = "cohort_transfer";

        /// <summary>Treatment handling deaths: per-site + pen-index suffix
        /// (DelouseSite, freshwater wellboat execution).</summary>
        public const string Delouse = "delouse";

        /// <summary>Weather system: temperature noise, wind, wave, Markov state transitions.</summary>
        public const string Weather = "weather";

        /// <summary>Market simulation: Ornstein-Uhlenbeck noise, weekly volatility, seasonality.</summary>
        public const string Market = "market";

        /// <summary>Monte Carlo price engine: multi-path simulation draws.</summary>
        public const string MonteCarloPrice = "monte_carlo_price";

        /// <summary>Competitor AI: strategy rolls, harvest timing, investment decisions.</summary>
        public const string CompetitorAI = "competitor_ai";

        /// <summary>Offshore farm: escape chance, escape count.</summary>
        public const string OffshoreFarm = "offshore_farm";

        /// <summary>RAS facility: temperature noise, capital cost variance.</summary>
        public const string RASFacility = "ras_facility";

        /// <summary>Stochastic disease engine: outbreak rolls, progression draws.</summary>
        public const string StochasticDisease = "stochastic_disease";

        /// <summary>Plankton bloom: bloom trigger, intensity, crash timing.</summary>
        public const string PlanktonBloom = "plankton_bloom";

        /// <summary>Genetics: mutation rolls, Mendelian inheritance draws.</summary>
        public const string Genetics = "genetics";

        /// <summary>SDE Growth Model: process noise in growth SDE.</summary>
        public const string SDEGrowth = "sde_growth";

        /// <summary>Vertical integration: construction timing variance, cost overruns.</summary>
        public const string VerticalIntegration = "vertical_integration";

        /// <summary>Acquisition system: target selection, valuation noise.</summary>
        public const string Acquisition = "acquisition";

        /// <summary>License auction: rival bid rolls, auction-openers (Phase 4.1).
        /// Per-identity draws use suffix overloads: (auctionId, rivalId).</summary>
        public const string LicenseAuction = "license_auction";

        /// <summary>Stock market: price event generation, volatility draws.</summary>
        public const string StockMarket = "stock_market";

        /// <summary>Crisis capitalism: trigger rolls, severity draws.</summary>
        public const string CrisisCapitalism = "crisis_capitalism";

        /// <summary>Growth model: smolt weight spread, daily growth noise.</summary>
        public const string Growth = "growth";

        /// <summary>Storyteller: daily event chance, event/target picks.</summary>
        public const string Storyteller = "storyteller";

        /// <summary>Currency system: FX noise, intervention draws.</summary>
        public const string Currency = "currency";

        /// <summary>Market intelligence: forecast noise, news-event rolls.</summary>
        public const string Intelligence = "intelligence";

        /// <summary>Stakeholder factions: influence rolls, demand draws.</summary>
        public const string Factions = "factions";
    }

/// <summary>
/// A named RNG stream (SplitMix64 core). Returns uniform 53-bit doubles in
/// [0,1) via <see cref="NextDouble"/> and bounded ints via <see cref="Next(int)"/>.
/// </summary>
public sealed class SimRngStream
{
    private ulong _state;

    internal SimRngStream(ulong state) => _state = state;

    internal ulong ExportState() => _state;
    internal void ImportState(ulong state) => _state = state;

    public double NextDouble() => (MixStep() >> 11) * (1.0 / 9007199254740992.0); // 2^53 grid

    public float NextSingle() => (float)NextDouble();

    public int Next(int maxExclusive)
    {
        if (maxExclusive <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must be positive");
        return (int)(MixStep() % (ulong)maxExclusive);
    }

    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range is empty");
        return minInclusive + Next(maxExclusive - minInclusive);
    }

    private ulong MixStep()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}