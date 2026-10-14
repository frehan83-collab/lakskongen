using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LakskongenSim.Harvest
{
    /// <summary>How a batch reached slaughter (S2-04).</summary>
    public enum HarvestMode
    {
        /// <summary>A planned order: fasted at the farm, a booked slot within capacity.</summary>
        Planned = 0,
        /// <summary>Taken today without a plan: a spot wellboat and the slaughterhouse's waiting pen (surcharge).</summary>
        Express = 1,
        /// <summary>A harvest wellboat hired at a flat fee (<c>RequestWellboat("harvest")</c>): no per-kg transport.</summary>
        HiredWellboat = 2,
        /// <summary>A forced harvest ordered by Mattilsynet, sold at a penalty price.</summary>
        Forced = 3,
    }

    /// <summary>
    /// One slaughtered batch (S2-04): what went in, what came out and what it
    /// earned, exactly as booked in the ledger. The figures the harvest report
    /// (U2-05) shows. Serialized in <c>GameState.HarvestReports</c>.
    /// </summary>
    [Serializable]
    public sealed class HarvestReport
    {
        /// <summary>Report id: the day, holding and a per-game sequence.</summary>
        public string Id { get; set; } = "";

        /// <summary>Game day of the slaughter.</summary>
        public int Day { get; set; }

        /// <summary>The sea site, land facility or offshore farm.</summary>
        public string HoldingId { get; set; } = "";

        /// <summary>The holding's display name.</summary>
        public string HoldingLabel { get; set; } = "";

        /// <summary>The order the batch belongs to; empty for an express, hired or forced harvest.</summary>
        public string OrderId { get; set; } = "";

        /// <summary>How the batch reached slaughter.</summary>
        public HarvestMode Mode { get; set; } = HarvestMode.Planned;

        /// <summary>How the fish travelled.</summary>
        public HarvestRouteKind Route { get; set; } = HarvestRouteKind.SeaWellboat;

        /// <summary>Fish slaughtered.</summary>
        public int Fish { get; set; }

        /// <summary>Live kilos slaughtered.</summary>
        public double LiveKg { get; set; }

        /// <summary>Gutted (HOG) kilos sold: live kilos × the HOG yield.</summary>
        public double HogKg { get; set; }

        /// <summary>Mean live weight of the fish (kg).</summary>
        public double MeanLiveWeightKg { get; set; }

        /// <summary>Weight CV of the fish.</summary>
        public double WeightCv { get; set; }

        /// <summary>Gutted kilos per size class (&lt;1, 1-2 ... 7+ kg HOG).</summary>
        public List<double> HogKgBySizeClass { get; set; } = new();

        /// <summary>Superior share of the gutted kilos.</summary>
        public double SuperiorShare { get; set; }

        /// <summary>Ordinary share of the gutted kilos.</summary>
        public double OrdinaryShare { get; set; }

        /// <summary>Production share of the gutted kilos.</summary>
        public double ProductionShare { get; set; }

        /// <summary>Mean size-class price factor of the batch.</summary>
        public double SizeFactor { get; set; } = 1.0;

        /// <summary>Mean grade price factor of the batch.</summary>
        public double GradeFactor { get; set; } = 1.0;

        /// <summary>
        /// Reference-equivalent kilos: Σ gutted kilos × size-class factor × grade
        /// factor. Without contracts, a processing plant or a penalty the gross
        /// sale is exactly this × <see cref="ReferencePriceNok"/>.
        /// </summary>
        public double ReferenceEquivalentKg { get; set; }

        /// <summary>The day's reference price: superior HOG, 4-5 kg (NOK/kg).</summary>
        public decimal ReferencePriceNok { get; set; }

        /// <summary>Gutted kilos delivered on contracts (at their locked prices).</summary>
        public double ContractHogKg { get; set; }

        /// <summary>Sales on contracts (NOK, part of <see cref="GrossRevenueNok"/>).</summary>
        public decimal ContractRevenueNok { get; set; }

        /// <summary>Processing-plant (VAP) price multiplier applied (1 without a plant).</summary>
        public decimal VapMultiplier { get; set; } = 1m;

        /// <summary>Price penalty of a forced harvest (1 otherwise).</summary>
        public decimal PriceMultiplier { get; set; } = 1m;

        /// <summary>
        /// Gross sales booked as harvest revenue (NOK): HOG kg × (contract or spot
        /// price) × size-class factor × grade factor × VAP × penalty.
        /// </summary>
        public decimal GrossRevenueNok { get; set; }

        /// <summary>Wellboat (or pipe) transport booked (NOK).</summary>
        public decimal TransportCostNok { get; set; }

        /// <summary>Slaughter, gutting and packing booked (NOK).</summary>
        public decimal SlaughterCostNok { get; set; }

        /// <summary>Express surcharge booked (NOK).</summary>
        public decimal SurchargeNok { get; set; }

        /// <summary>The whole harvest cost booked as cost of goods sold (NOK).</summary>
        [JsonIgnore]
        public decimal HarvestCostNok => TransportCostNok + SlaughterCostNok + SurchargeNok;

        /// <summary>What the batch added to cash (NOK).</summary>
        [JsonIgnore]
        public decimal NetRevenueNok => GrossRevenueNok - HarvestCostNok;
    }
}
