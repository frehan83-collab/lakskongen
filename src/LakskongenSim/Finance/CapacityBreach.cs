using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>Lifecycle of a <see cref="CapacityBreach"/> (save data; stable strings).</summary>
    public static class CapacityBreachStatus
    {
        /// <summary>Biomass is over a cap and the reduction order runs.</summary>
        public const string Open = "open";

        /// <summary>The company brought its biomass under every cap before the deadline.</summary>
        public const string Complied = "complied";

        /// <summary>The deadline passed: the regulator had the excess harvested at the company's cost.</summary>
        public const string ForcedHarvest = "forced_harvest";
    }

    /// <summary>
    /// One episode of standing biomass over MTB in a production area (S2-01a):
    /// from the first day the company's sea sites there hold more than the
    /// area's licence MTB or a site holds more than its locality MTB, to the day
    /// the biomass is back under both. The illegal biomass of the episode is
    /// the larger of the area excess and the sum of the sites' locality excesses
    /// (one fish is counted once). Every day it grows past its peak, the
    /// regulator books the infringement fee and the confiscation of the gain
    /// on the new kilos; the order runs from the first day.
    /// </summary>
    public sealed class CapacityBreach
    {
        /// <summary>Deterministic id: "mtb_{area}_d{startDay}".</summary>
        public string Id { get; set; } = "";

        /// <summary>Production area of the breach.</summary>
        public int Area { get; set; }

        /// <summary>First day over a cap.</summary>
        public int StartDay { get; set; }

        /// <summary>The reduction order's deadline (sim day).</summary>
        public int DeadlineDay { get; set; }

        /// <summary>Day the episode closed (-1 while open).</summary>
        public int ClosedDay { get; set; } = -1;

        /// <summary><see cref="CapacityBreachStatus"/>.</summary>
        public string Status { get; set; } = CapacityBreachStatus.Open;

        /// <summary>Most illegal biomass (kg) the episode reached; fee and confiscation follow it.</summary>
        public double PeakIllegalKg { get; set; }

        /// <summary>Illegal biomass (kg) on the last day checked.</summary>
        public double LastIllegalKg { get; set; }

        /// <summary>Days the area was over a cap in this episode.</summary>
        public int DaysOver { get; set; }

        /// <summary>Infringement fee booked so far (NOK, overtredelsesgebyr; capped).</summary>
        public decimal FineNok { get; set; }

        /// <summary>Gain confiscated so far (NOK, administrativ inndragning).</summary>
        public decimal ConfiscatedNok { get; set; }

        /// <summary>Kilos the regulator had harvested at the deadline.</summary>
        public double ForcedHarvestKg { get; set; }

        /// <summary>What the forced harvest sold for (NOK).</summary>
        public decimal ForcedHarvestRevenueNok { get; set; }

        /// <summary>True when the area's licence MTB was exceeded at some point of the episode.</summary>
        public bool AreaCapExceeded { get; set; }

        /// <summary>Sites that were over their locality MTB at some point of the episode (sorted).</summary>
        public List<string> SitesOverLocality { get; set; } = new();

        /// <summary>True while the order runs.</summary>
        public bool IsOpen => Status == CapacityBreachStatus.Open;
    }

    /// <summary>
    /// The regulator's arithmetic for a biomass breach (S2-01a), pure.
    /// <list type="bullet">
    /// <item>Confiscation of the gain (administrativ inndragning,
    /// akvakulturloven § 29 a; FOR-2013-12-20-1675 § 6): per kilo of illegal
    /// standing biomass, the sales price converted from live to gutted weight
    /// (0.83 for salmon), less feed (economic feed factor x feed price) and
    /// the other variable costs (insurance, slaughter incl. freight).</item>
    /// <item>Infringement fee (overtredelsesgebyr, akvakulturloven § 30): the
    /// excess kilos at their sales value (the per-kilo measure of the 2007
    /// regulation, FOR-2007-03-29-361 § 10, as the game's rule for today's
    /// discretionary fee), never above 15 G per case (FOR-2013-12-20-1675 § 8).</item>
    /// </list>
    /// </summary>
    public static class BiomassBreachRules
    {
        /// <summary>
        /// Net gain (NOK) per kilo of illegal live biomass: never negative (a
        /// loss-making kilo has no gain to confiscate).
        /// </summary>
        public static decimal NetGainPerKgNok(decimal spotPerKgNok, double liveToGuttedFactor,
            decimal feedPricePerKgNok, double economicFeedFactor, decimal otherVariableCostPerKgNok)
        {
            decimal sales = spotPerKgNok * (decimal)liveToGuttedFactor;
            decimal feed = feedPricePerKgNok * (decimal)economicFeedFactor;
            return Math.Max(0m, sales - feed - otherVariableCostPerKgNok);
        }

        /// <summary>Sales value (NOK) per kilo of illegal live biomass: the fee's measure.</summary>
        public static decimal SalesValuePerKgNok(decimal spotPerKgNok, double liveToGuttedFactor)
            => Math.Max(0m, spotPerKgNok * (decimal)liveToGuttedFactor);

        /// <summary>
        /// Fee to add when the episode's illegal biomass grows by
        /// <paramref name="newKg"/> kilos: their sales value, up to what is left
        /// under the case's ceiling.
        /// </summary>
        public static decimal FeeIncrementNok(double newKg, decimal salesValuePerKgNok,
            decimal feeSoFarNok, decimal ceilingNok)
        {
            if (newKg <= 0.0) return 0m;
            decimal room = Math.Max(0m, ceilingNok - feeSoFarNok);
            return Math.Round(Math.Min(room, (decimal)newKg * salesValuePerKgNok), 2);
        }

        /// <summary>Confiscation to add for <paramref name="newKg"/> more illegal kilos.</summary>
        public static decimal ConfiscationIncrementNok(double newKg, decimal netGainPerKgNok)
            => newKg <= 0.0 ? 0m : Math.Round((decimal)newKg * netGainPerKgNok, 2);
    }
}
