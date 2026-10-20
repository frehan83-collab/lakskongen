using System;
using System.Collections.Generic;

namespace LakskongenSim.Harvest
{
    /// <summary>One slaughter day of a projected harvest (U2-05).</summary>
    /// <param name="Day">Game day of the batch.</param>
    /// <param name="Fish">Fish slaughtered that day.</param>
    /// <param name="HogKg">Gutted kilos slaughtered that day.</param>
    /// <param name="ReferencePriceNok">The reference price expected that day (NOK/kg).</param>
    /// <param name="GrossRevenueNok">The batch's expected gross sale (NOK).</param>
    public sealed record HarvestProjectionDay(int Day, int Fish, double HogKg, decimal ReferencePriceNok, decimal GrossRevenueNok);

    /// <summary>
    /// What a planned harvest is expected to bring if it is ordered after
    /// <see cref="WaitDays"/> days (roadmap U2-05, the harvest planner's
    /// "wait N weeks"): the fish grown on the tick's law in the water expected
    /// until each pen's fast, the expected deaths, each pen fasting ahead of
    /// its turn and slaughtered within the slaughterhouse's daily capacity
    /// behind the orders already queued, each batch sold on contracts first
    /// and then at the price the market is expected to pay that day (the
    /// order's own batches pressing it), less the harvest cost and the feed the
    /// fish eat until their fast. Twin-lite: computed from copies, it draws no
    /// random numbers and changes nothing.
    /// </summary>
    public sealed record HarvestProjection
    {
        /// <summary>True when the harvest can be ordered (now or after the wait).</summary>
        public bool Ok { get; init; }

        /// <summary>Stable failure key (as <see cref="HarvestQuote.ErrorKey"/>, plus "invalid_wait"); empty when ok.</summary>
        public string ErrorKey { get; init; } = "";

        /// <summary>Human-readable outcome.</summary>
        public string Message { get; init; } = "";

        /// <summary>The holding projected.</summary>
        public string HoldingId { get; init; } = "";

        /// <summary>How the fish would travel.</summary>
        public HarvestRouteKind Route { get; init; }

        /// <summary>Days waited before ordering (0 = order today).</summary>
        public int WaitDays { get; init; }

        /// <summary>Game day the order is placed.</summary>
        public int OrderDay { get; init; }

        /// <summary>Days of fasting the order expects (from the water expected on the order day).</summary>
        public int FastingDays { get; init; }

        /// <summary>The wellboat slot (the order day plus the lead time or the fast, whichever is longer).</summary>
        public int SlotDay { get; init; }

        /// <summary>First slaughter day.</summary>
        public int FirstSlaughterDay { get; init; }

        /// <summary>Last slaughter day.</summary>
        public int LastSlaughterDay { get; init; }

        /// <summary>Fish slaughtered.</summary>
        public int Fish { get; init; }

        /// <summary>Live kilos slaughtered.</summary>
        public double LiveKg { get; init; }

        /// <summary>Gutted kilos sold.</summary>
        public double HogKg { get; init; }

        /// <summary>Mean live weight at slaughter (kg).</summary>
        public double MeanLiveWeightKg { get; init; }

        /// <summary>Weight CV at slaughter.</summary>
        public double WeightCv { get; init; }

        /// <summary>Gutted kilos per size class (&lt;1, 1-2 ... 7+ kg).</summary>
        public double[] HogKgBySizeClass { get; init; } = Array.Empty<double>();

        /// <summary>Superior share of the gutted kilos.</summary>
        public double SuperiorShare { get; init; }

        /// <summary>Ordinary share of the gutted kilos.</summary>
        public double OrdinaryShare { get; init; }

        /// <summary>Production share of the gutted kilos.</summary>
        public double ProductionShare { get; init; }

        /// <summary>Mean size-class factor.</summary>
        public double SizeFactor { get; init; } = 1.0;

        /// <summary>Mean grade factor.</summary>
        public double GradeFactor { get; init; } = 1.0;

        /// <summary>Today's reference price (NOK/kg).</summary>
        public decimal SpotTodayNok { get; init; }

        /// <summary>The reference price expected over the slaughter days, weighted by their gutted kilos (NOK/kg).</summary>
        public decimal ExpectedReferencePriceNok { get; init; }

        /// <summary>Gutted kilos delivered on contracts.</summary>
        public double ContractHogKg { get; init; }

        /// <summary>Sales on contracts (NOK, before the processing plant's premium).</summary>
        public decimal ContractRevenueNok { get; init; }

        /// <summary>Gutted kilos sold at spot.</summary>
        public double SpotHogKg { get; init; }

        /// <summary>Sales at spot (NOK, before the processing plant's premium).</summary>
        public decimal SpotRevenueNok { get; init; }

        /// <summary>The processing plant's value-added premium (1 without a plant).</summary>
        public decimal VapMultiplier { get; init; } = 1m;

        /// <summary>Expected gross sale (NOK): contracts and spot, times the premium.</summary>
        public decimal GrossRevenueNok { get; init; }

        /// <summary>Transport (NOK).</summary>
        public decimal TransportCostNok { get; init; }

        /// <summary>Slaughter, gutting and packing (NOK).</summary>
        public decimal SlaughterCostNok { get; init; }

        /// <summary>Gross sale less the harvest cost (NOK).</summary>
        public decimal NetRevenueNok => GrossRevenueNok - TransportCostNok - SlaughterCostNok;

        /// <summary>Feed the fish are expected to eat (delivered) until their fast starts (kg).</summary>
        public double FeedKg { get; init; }

        /// <summary>That feed's cost (NOK), the autopilot's spill included.</summary>
        public decimal FeedCostNok { get; init; }

        /// <summary>Net sale less the feed until the fast: what to compare between waits (NOK).</summary>
        public decimal NetAfterFeedNok => NetRevenueNok - FeedCostNok;

        /// <summary>The daily mortality the projection expects (share of the fish a day).</summary>
        public double DailyMortality { get; init; }

        /// <summary>Fish expected to die before slaughter.</summary>
        public double ExpectedDeaths { get; init; }

        /// <summary>The slaughter days, oldest first.</summary>
        public IReadOnlyList<HarvestProjectionDay> Days { get; init; } = Array.Empty<HarvestProjectionDay>();
    }
}
