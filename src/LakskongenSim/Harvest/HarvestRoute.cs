using System;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;

namespace LakskongenSim.Harvest
{
    /// <summary>How live fish reach slaughter (S2-04).</summary>
    public enum HarvestRouteKind
    {
        /// <summary>A coastal sea site: a wellboat to the contracted slaughterhouse.</summary>
        SeaWellboat = 0,
        /// <summary>An exposed offshore farm: a wellboat from open water (dearer transport).</summary>
        OffshoreWellboat = 1,
        /// <summary>A land-based farm: a pipe or truck to its own processing hall, no wellboat.</summary>
        OnSiteHall = 2,
    }

    /// <summary>
    /// The way a holding's fish travel to slaughter (S2-04), and what it costs
    /// and carries a day. The farm types plug in here: a land-based farm with its
    /// own processing hall is <see cref="HarvestRouteKind.OnSiteHall"/> (no
    /// wellboat, its hall's capacity); an offshore unit pays an offshore
    /// wellboat. Sea routes share the player's slaughterhouse contract; each
    /// land hall is its own capacity pool (<see cref="CapacityPool"/>).
    /// </summary>
    /// <param name="Kind">Route kind.</param>
    /// <param name="TransportCostPerKgHogNok">Wellboat (or pipe/truck) cost per kg HOG.</param>
    /// <param name="SlaughterCostPerKgHogNok">Slaughter, gutting and packing per kg HOG.</param>
    /// <param name="DailyCapacityTonnesHog">Gutted tonnes the route can slaughter a day.</param>
    /// <param name="LeadDays">Days from the order to the first slot.</param>
    /// <param name="CapacityPool">Fish of routes in the same pool share one daily capacity.</param>
    public sealed record HarvestRoute(
        HarvestRouteKind Kind,
        decimal TransportCostPerKgHogNok,
        decimal SlaughterCostPerKgHogNok,
        double DailyCapacityTonnesHog,
        int LeadDays,
        string CapacityPool)
    {
        /// <summary>Capacity pool of the player's slaughterhouse contract (sea and offshore fish).</summary>
        public const string SlaughterhousePool = "slaughterhouse";

        /// <summary>True when a wellboat carries the fish (sea and offshore routes).</summary>
        public bool NeedsWellboat => Kind != HarvestRouteKind.OnSiteHall;

        /// <summary>Harvest cost per kg HOG on this route (transport + slaughter).</summary>
        public decimal CostPerKgHogNok => TransportCostPerKgHogNok + SlaughterCostPerKgHogNok;

        /// <summary>
        /// The route of fish held by <paramref name="kind"/> at holding
        /// <paramref name="holdingId"/>. A land tank's hall slaughters
        /// <paramref name="hallCapacityTonnesHog"/> a day (the farm type supplies
        /// it; the slaughterhouse contract's capacity when unknown) and needs no
        /// wellboat lead time beyond fasting.
        /// </summary>
        public static HarvestRoute For(HoldingKind kind, HarvestCalibration calibration, string holdingId = "",
            double? hallCapacityTonnesHog = null)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double seaCapacity = Math.Min(calibration.SlaughterCapacityTonnesHogPerDay,
                calibration.WellboatCapacityTonnesLivePerDay * calibration.HogYieldFraction);
            return kind switch
            {
                HoldingKind.LandTank => new HarvestRoute(HarvestRouteKind.OnSiteHall, 0m,
                    calibration.SlaughterCostPerKgHogNok,
                    hallCapacityTonnesHog is > 0.0 ? hallCapacityTonnesHog.Value : calibration.SlaughterCapacityTonnesHogPerDay,
                    0, "hall:" + holdingId),
                HoldingKind.Offshore => new HarvestRoute(HarvestRouteKind.OffshoreWellboat,
                    calibration.WellboatCostPerKgHogNok * calibration.OffshoreWellboatCostMultiplier,
                    calibration.SlaughterCostPerKgHogNok, seaCapacity, calibration.WellboatLeadDays, SlaughterhousePool),
                _ => new HarvestRoute(HarvestRouteKind.SeaWellboat, calibration.WellboatCostPerKgHogNok,
                    calibration.SlaughterCostPerKgHogNok, seaCapacity, calibration.WellboatLeadDays, SlaughterhousePool)
            };
        }
    }
}
