using System;

namespace LakskongenSim.Harvest
{
    /// <summary>
    /// What a harvest would bring if ordered now (S2-04): the fish, their
    /// gutted kilos by size class and grade, the gross sale today (contracts,
    /// spot at today's reference price, the processing plant's premium), the harvest cost and the schedule (fasting, the
    /// wellboat slot, the slaughter days). Read-only: quoting moves nothing and
    /// draws no random numbers. The harvest planner (U2-05) shows it.
    /// </summary>
    public sealed record HarvestQuote
    {
        /// <summary>True when the harvest can be ordered.</summary>
        public bool Ok { get; init; }

        /// <summary>Stable failure key ("holding_not_found", "pen_not_found", "invalid_fraction", "holding_empty", "pen_in_order"); empty when ok.</summary>
        public string ErrorKey { get; init; } = "";

        /// <summary>Human-readable outcome.</summary>
        public string Message { get; init; } = "";

        /// <summary>The holding quoted.</summary>
        public string HoldingId { get; init; } = "";

        /// <summary>How the fish would travel.</summary>
        public HarvestRouteKind Route { get; init; }

        /// <summary>Express (today, surcharge) or planned (fast, slot).</summary>
        public HarvestMode Mode { get; init; } = HarvestMode.Planned;

        /// <summary>Fish to harvest.</summary>
        public int Fish { get; init; }

        /// <summary>Live kilos today.</summary>
        public double LiveKg { get; init; }

        /// <summary>Gutted kilos today.</summary>
        public double HogKg { get; init; }

        /// <summary>Mean live weight (kg).</summary>
        public double MeanLiveWeightKg { get; init; }

        /// <summary>Weight CV.</summary>
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

        /// <summary>Today's reference price (superior HOG 4-5 kg, NOK/kg).</summary>
        public decimal ReferencePriceNok { get; init; }

        /// <summary>Gutted kilos the running contracts take (oldest first, at their prices).</summary>
        public double ContractHogKg { get; init; }

        /// <summary>Sales on contracts (NOK, before the processing plant's premium).</summary>
        public decimal ContractRevenueNok { get; init; }

        /// <summary>Gutted kilos sold at today's spot.</summary>
        public double SpotHogKg { get; init; }

        /// <summary>Sales at today's spot (NOK, before the processing plant's premium).</summary>
        public decimal SpotRevenueNok { get; init; }

        /// <summary>The processing plant's value-added premium (1 without a plant).</summary>
        public decimal VapMultiplier { get; init; } = 1m;

        /// <summary>
        /// Gross sale today (NOK): contracts first, the rest at today's spot,
        /// times the processing plant's premium; what a sale today books (U2-05).
        /// </summary>
        public decimal GrossRevenueNok { get; init; }

        /// <summary>Transport cost (NOK).</summary>
        public decimal TransportCostNok { get; init; }

        /// <summary>Slaughter, gutting and packing (NOK).</summary>
        public decimal SlaughterCostNok { get; init; }

        /// <summary>Express surcharge (NOK).</summary>
        public decimal SurchargeNok { get; init; }

        /// <summary>Gross sale less the harvest cost (NOK).</summary>
        public decimal NetRevenueNok => GrossRevenueNok - TransportCostNok - SlaughterCostNok - SurchargeNok;

        /// <summary>Days the fish still need to fast (estimate from today's water).</summary>
        public int FastingDays { get; init; }

        /// <summary>First slaughter day (the wellboat slot, after the fast and the queue).</summary>
        public int FirstSlaughterDay { get; init; }

        /// <summary>Day the last fish is slaughtered (estimate at today's weights).</summary>
        public int LastSlaughterDay { get; init; }

        /// <summary>The route's daily capacity (tonnes HOG).</summary>
        public double DailyCapacityTonnesHog { get; init; }
    }
}
