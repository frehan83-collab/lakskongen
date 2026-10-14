using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using LakskongenSim.Environment;

namespace LakskongenSim.Harvest
{
    /// <summary>Where a harvest order stands (S2-04).</summary>
    public enum HarvestOrderStatus
    {
        /// <summary>The fish fast; the wellboat slot has not come or the gut is not empty yet.</summary>
        Fasting = 0,
        /// <summary>Slaughter has started: batches go as the daily capacity allows.</summary>
        Harvesting = 1,
        /// <summary>Every planned fish is slaughtered (or died first).</summary>
        Completed = 2,
        /// <summary>The player called it off; the fish feed again.</summary>
        Cancelled = 3,
    }

    /// <summary>One pen (population) in a harvest order and how many of its fish the order takes.</summary>
    [Serializable]
    public sealed class HarvestOrderPen
    {
        /// <summary>The pen's population id.</summary>
        public string PopulationId { get; set; } = "";

        /// <summary>
        /// The sea pen holding that population (S2-01b, "po3_nord/p03"); empty
        /// for a land tank or an offshore pen, and in orders saved before S2-01b.
        /// </summary>
        public string PenId { get; set; } = "";

        /// <summary>Fish the order takes from this pen (the pen's count times the order's fraction).</summary>
        public int FishToHarvest { get; set; }

        /// <summary>Fish slaughtered from this pen so far.</summary>
        public int FishHarvested { get; set; }

        /// <summary>True once the pen gave all it owes or has no fish left.</summary>
        public bool Done { get; set; }

        /// <summary>Fish still to come from this pen.</summary>
        [JsonIgnore]
        public int FishRemaining => Done ? 0 : Math.Max(0, FishToHarvest - FishHarvested);
    }

    /// <summary>
    /// A planned harvest (S2-04): the fish of chosen pens (all of them or a
    /// share of each) fast, a wellboat collects them from the slot day, and the
    /// slaughterhouse takes them batch by batch within its daily capacity.
    /// Serialized in <c>GameState.HarvestOrders</c>.
    /// </summary>
    [Serializable]
    public sealed class HarvestOrder
    {
        /// <summary>Deterministic id: order day, holding and the game's order sequence.</summary>
        public string Id { get; set; } = "";

        /// <summary>The sea site, land facility or offshore farm whose fish are harvested.</summary>
        public string HoldingId { get; set; } = "";

        /// <summary>What holds the fish.</summary>
        public HoldingKind HoldingKind { get; set; } = HoldingKind.SeaPen;

        /// <summary>How the fish reach slaughter.</summary>
        public HarvestRouteKind Route { get; set; } = HarvestRouteKind.SeaWellboat;

        /// <summary>Share of each chosen pen's fish the order takes (0-1].</summary>
        public double Fraction { get; set; } = 1.0;

        /// <summary>The pens in the order.</summary>
        public List<HarvestOrderPen> Pens { get; set; } = new();

        /// <summary>Game day the order was placed (fasting starts that day).</summary>
        public int OrderedDay { get; set; }

        /// <summary>First day the wellboat (or hall) may take fish.</summary>
        public int SlotDay { get; set; }

        /// <summary>The game's running order number when placed.</summary>
        public int Sequence { get; set; }

        /// <summary>Where the order stands.</summary>
        public HarvestOrderStatus Status { get; set; } = HarvestOrderStatus.Fasting;

        /// <summary>Day the last batch was slaughtered (-1 while open).</summary>
        public int CompletedDay { get; set; } = -1;

        /// <summary>Fish slaughtered so far.</summary>
        public int FishHarvested { get; set; }

        /// <summary>Live kilos slaughtered so far.</summary>
        public double LiveKg { get; set; }

        /// <summary>Gutted (HOG) kilos sold so far.</summary>
        public double HogKg { get; set; }

        /// <summary>Gross sales so far (NOK, booked as harvest revenue).</summary>
        public decimal GrossRevenueNok { get; set; }

        /// <summary>Harvest cost so far (NOK, booked as cost of goods sold).</summary>
        public decimal HarvestCostNok { get; set; }

        /// <summary>True while fish are still to come.</summary>
        [JsonIgnore]
        public bool IsOpen => Status is HarvestOrderStatus.Fasting or HarvestOrderStatus.Harvesting;

        /// <summary>Fish still to come.</summary>
        [JsonIgnore]
        public int FishRemaining => Pens.Sum(p => p.FishRemaining);

        /// <summary>The id of the <paramref name="sequence"/>-th order: day, holding, sequence.</summary>
        public static string ComposeId(int orderedDay, string holdingId, int sequence)
            => $"harvest-d{orderedDay}-{holdingId}-{sequence}";
    }
}
