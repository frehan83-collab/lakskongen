using System;
using System.Collections.Generic;

namespace LakskongenSim.Core;

/// <summary>
/// The research effect keys the engine reads — the single registry shared by
/// the built-in tree, <c>godot/data/research_nodes.json</c>, mods and the
/// consumers (review F1001). A key outside <see cref="Live"/> has no effect;
/// the research system reports it when nodes load.
/// </summary>
public static class ResearchEffectKeys
{
    /// <summary>Fractional bonus on the SDE growth rate.</summary>
    public const string GrowthRateBonus = "growth_rate_bonus";

    /// <summary>Fractional reduction of the feed conversion ratio.</summary>
    public const string FcrReduction = "fcr_reduction";

    /// <summary>Fractional reduction of feed waste.</summary>
    public const string FeedWasteReduction = "feed_waste_reduction";

    /// <summary>Fraction of the baseline (non-disease) mortality hazard removed.</summary>
    public const string MortalityReduction = "mortality_reduction";

    /// <summary>Fraction of the disease mortality hazard removed.</summary>
    public const string DiseaseResistanceBonus = "disease_resistance_bonus";

    /// <summary>Fractional premium on the spot price.</summary>
    public const string MarketPriceBonus = "market_price_bonus";

    /// <summary>Points added to the 0-100 sustainability score.</summary>
    public const string SustainabilityScoreBonus = "sustainability_score_bonus";

    /// <summary>Fraction of new sea-lice settlement blocked at every site.</summary>
    public const string LiceReduction = "lice_reduction";

    /// <summary>Fractional reduction of fuel and electricity emissions (Scope 1-2).</summary>
    public const string CarbonReduction = "carbon_reduction";

    /// <summary>Every key with a consumer in the engine.</summary>
    public static readonly IReadOnlySet<string> Live = new HashSet<string>(StringComparer.Ordinal)
    {
        GrowthRateBonus, FcrReduction, FeedWasteReduction, MortalityReduction,
        DiseaseResistanceBonus, MarketPriceBonus, SustainabilityScoreBonus,
        LiceReduction, CarbonReduction,
    };

    /// <summary>Keys whose values are score points rather than fractions.</summary>
    public static readonly IReadOnlySet<string> PointKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        SustainabilityScoreBonus,
    };

    /// <summary>
    /// Ceiling on a summed fractional reduction, so stacked nodes can never
    /// remove a hazard, emission or settlement entirely.
    /// </summary>
    public const double MaxReduction = 0.9;

    /// <summary>A summed reduction clamped to [0, <see cref="MaxReduction"/>].</summary>
    public static double ClampReduction(double value) => Math.Clamp(value, 0.0, MaxReduction);
}
