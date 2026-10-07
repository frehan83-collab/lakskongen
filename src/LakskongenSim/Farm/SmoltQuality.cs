using System;
using LakskongenSim.Biology;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// Smolt / post-smolt quality tier. Ordered from cheapest (Standard) to the
    /// most expensive land-based large smolt (SuperSmolt).
    /// </summary>
    public enum SmoltQualityTier
    {
        /// <summary>Conventional ~80-120 g smolt, full sea-phase exposure. Default (0).</summary>
        Standard = 0,

        /// <summary>~200-300 g "large smolt", partial carryover benefit.</summary>
        Large = 1,

        /// <summary>~400-1000 g RAS post-smolt ("SuperSmolt"), maximal carryover.</summary>
        SuperSmolt = 2
    }

    /// <summary>
    /// Immutable, deterministic carryover profile for a smolt quality tier.
    ///
    /// Grounding (Chilean post-smolt / "SuperSmolt" production trial, ~320M smolts):
    /// <list type="bullet">
    /// <item>Mortality at 4 months in sea: <b>1.02%</b> (SuperSmolt) vs 1.53% / 1.65%
    /// for conventional smolt. Ratio 1.02/1.53 ≈ 0.67.</item>
    /// <item>Cumulative mortality at harvest: <b>5.69%</b> vs 8.6% / 8.3%.
    /// Ratio 5.69/8.6 ≈ 0.66 — consistent with the 4-month carryover, so a single
    /// sea-phase baseline mortality multiplier ≈ <b>0.67</b> is used.</item>
    /// <item>Time-to-harvest: <b>~51%</b> of SuperSmolt harvested by month 13 vs only
    /// 17% / 28% of conventional — a <b>~0.70</b> time-to-harvest multiplier.</item>
    /// <item>Median weight at 7 months: <b>2,055 g</b> vs 1,781 g / 1,874 g.
    /// Ratio 2,055/1,781 ≈ 1.154 (≈1.13 averaged) — a <b>~1.13</b> growth multiplier.</item>
    /// </list>
    /// Large smolt is modelled as an intermediate tier between the two observed
    /// populations. Multipliers are applied to the sea-phase baseline, not to
    /// hatchery/freshwater phases, preserving existing baseline behaviour.
    /// </summary>
    public sealed record SmoltQuality(
        SmoltQualityTier Tier,
        string Name,
        double MortalityMultiplier,
        double GrowthMultiplier,
        double TimeToHarvestMultiplier,
        double Quality01,
        string Evidence)
    {
        /// <summary>Apply carryover to a sea-phase baseline mortality fraction (e.g. 0.086).</summary>
        public double ApplyMortality(float baselineMortality) =>
            Math.Clamp(baselineMortality * MortalityMultiplier, 0.0, 1.0);

        /// <summary>Apply carryover to a sea-phase baseline growth rate.</summary>
        public double ApplyGrowth(double baselineGrowth) => baselineGrowth * GrowthMultiplier;

        /// <summary>Apply carryover to a baseline days-to-harvest figure.</summary>
        public double ApplyDaysToHarvest(double baselineDays) => baselineDays * TimeToHarvestMultiplier;

        /// <summary>Deterministic blend of two quality values (weight on <paramref name="other"/>).</summary>
        public double BlendQuality(SmoltQuality other, double weight)
        {
            double w = Math.Clamp(weight, 0.0, 1.0);
            return Quality01 * (1.0 - w) + other.Quality01 * w;
        }

        /// <summary>Stamp this quality tier onto a cohort so the sea phase can read it.</summary>
        public void Stamp(FishPopulation.Cohort cohort)
        {
            if (cohort is null) throw new ArgumentNullException(nameof(cohort));
            cohort.Quality = Tier;
        }
    }

    /// <summary>
    /// Static registry and pure factories for <see cref="SmoltQuality"/>.
    /// All deltas are constants — no randomness, no I/O.
    /// </summary>
    public static class SmoltQualityRegistry
    {
        private const string ChileEvidence =
            "Chile post-smolt (~320M smolts): 4-mo mortality 1.02% vs 1.53/1.65%; " +
            "harvest cumulative 5.69% vs 8.6/8.3%; ~51% harvested by month 13 vs 17/28%; " +
            "median 7-mo weight 2,055g vs 1,781/1,874g.";

        /// <summary>Standard conventional smolt: baseline (all multipliers = 1).</summary>
        public static readonly SmoltQuality Standard = new(
            SmoltQualityTier.Standard, "Standard S1",
            MortalityMultiplier: 1.00,
            GrowthMultiplier: 1.00,
            TimeToHarvestMultiplier: 1.00,
            Quality01: 0.35,
            Evidence: "Baseline conventional 80-120g smolt.");

        /// <summary>Large smolt (~200-300g): roughly halfway to SuperSmolt carryover.</summary>
        public static readonly SmoltQuality Large = new(
            SmoltQualityTier.Large, "Large Smolt",
            MortalityMultiplier: 0.82,
            GrowthMultiplier: 1.06,
            TimeToHarvestMultiplier: 0.88,
            Quality01: 0.65,
            Evidence: "Intermediate between baseline and RAS post-smolt; " + ChileEvidence);

        /// <summary>RAS SuperSmolt (~400-1000g): full observed carryover.</summary>
        public static readonly SmoltQuality SuperSmolt = new(
            SmoltQualityTier.SuperSmolt, "SuperSmolt",
            MortalityMultiplier: 0.67,   // 1.02 / 1.53, consistent with 5.69 / 8.6
            GrowthMultiplier: 1.13,      // 2,055 / 1,781 (avg with 1,874)
            TimeToHarvestMultiplier: 0.70, // 51% vs 17/28% harvested by month 13
            Quality01: 1.00,
            Evidence: ChileEvidence);

        /// <summary>All tiers ordered cheapest → best.</summary>
        public static readonly System.Collections.Generic.IReadOnlyList<SmoltQuality> All =
            new[] { Standard, Large, SuperSmolt };

        /// <summary>Resolve a tier deterministically (defaults to Standard for unknown values).</summary>
        public static SmoltQuality Get(SmoltQualityTier tier) => tier switch
        {
            SmoltQualityTier.Large => Large,
            SmoltQualityTier.SuperSmolt => SuperSmolt,
            _ => Standard
        };

        /// <summary>
        /// Derive a quality value in [0,1] from a sea-transfer weight in kg.
        /// Monotonic: 0.10 kg → 0.35, 0.30 kg → 0.65, 1.00 kg → 1.00.
        /// Pure and clamped; used to synthesise a carryover profile from raw
        /// transfer weight when no explicit tier is available.
        /// </summary>
        public static double QualityFromTransferWeightKg(double weightKg)
        {
            const double lo = 0.10, mid = 0.30, hi = 1.00;
            if (weightKg <= lo) return Standard.Quality01;
            if (weightKg >= hi) return SuperSmolt.Quality01;
            if (weightKg <= mid)
            {
                double t = (weightKg - lo) / (mid - lo);
                return Standard.Quality01 + t * (Large.Quality01 - Standard.Quality01);
            }
            double u = (weightKg - mid) / (hi - mid);
            return Large.Quality01 + u * (SuperSmolt.Quality01 - Large.Quality01);
        }

        /// <summary>Nearest tier for a transfer weight (deterministic, no randomness).</summary>
        public static SmoltQualityTier TierFromTransferWeightKg(double weightKg)
        {
            double q = QualityFromTransferWeightKg(weightKg);
            if (q >= (Large.Quality01 + SuperSmolt.Quality01) / 2.0) return SmoltQualityTier.SuperSmolt;
            if (q >= (Standard.Quality01 + Large.Quality01) / 2.0) return SmoltQualityTier.Large;
            return SmoltQualityTier.Standard;
        }
    }
}