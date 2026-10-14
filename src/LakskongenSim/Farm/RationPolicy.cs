using System;
using System.Text.Json.Serialization;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// How much a holding (sea pen, offshore pen, land tank) feeds its fish: a
    /// fraction of their appetite (roadmap S2-03). 1 feeds to satiation; a lower
    /// fraction is a restricted ration (less growth, better conversion, wider
    /// size spread); 0 starves the fish (they lose weight). The feeding plan
    /// (U2-04) sets it; the growth law reads it every day.
    /// </summary>
    public sealed class RationPolicy
    {
        /// <summary>Fraction of the fish's appetite fed each day (0-1).</summary>
        public double FractionOfAppetite { get; init; } = 1.0;

        /// <summary>Feed to satiation (the default).</summary>
        public static RationPolicy FullAppetite { get; } = new();

        /// <summary>A ration of <paramref name="fraction"/> of appetite, clamped to 0-1.</summary>
        public static RationPolicy OfAppetite(double fraction)
        {
            if (!double.IsFinite(fraction))
                throw new ArgumentOutOfRangeException(nameof(fraction), fraction, "ration must be a finite fraction");
            return new RationPolicy { FractionOfAppetite = Math.Clamp(fraction, 0.0, 1.0) };
        }

        /// <summary>The fraction the growth law uses: clamped to 0-1 (a corrupt save cannot overfeed).</summary>
        [JsonIgnore]
        public double EffectiveFraction => double.IsFinite(FractionOfAppetite) ? Math.Clamp(FractionOfAppetite, 0.0, 1.0) : 1.0;
    }
}
