using System;
using System.Text.Json.Serialization;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm
{
    /// <summary>
    /// The feed a site's barge carries (roadmap U2-04): one of three recipes
    /// priced on their energy. Feed suppliers price a recipe on its nutrients,
    /// so a recipe's price per kg and its FCR move together and the cost per kg
    /// of growth stays about the same; what differs is the feed tonnage (sludge,
    /// nitrogen and phosphorus under the pens) and, for the economy recipe, the
    /// fillet colour a processing plant sees (calibration <c>feedPlan</c>).
    /// </summary>
    public enum FeedRecipeKind
    {
        /// <summary>The calibrated commercial feed (<c>feed.feedPricePerKgFeedNOK</c>, the growth law's FCR).</summary>
        Standard = 0,

        /// <summary>More fat, more energy per kg: less feed for the same growth, dearer per kg.</summary>
        HighEnergy = 1,

        /// <summary>Less energy and less astaxanthin: cheaper per kg, more feed for the same growth, paler fillets.</summary>
        Economy = 2
    }

    /// <summary>
    /// A site's feeding plan (roadmap U2-04): the share of their appetite its
    /// pens are fed (S2-03's <see cref="RationPolicy"/>, set on every pen's
    /// population), the recipe its barge carries, and how long the plan holds.
    /// A site with no plan (<c>SeaSite.FeedPlan == null</c>) feeds the standard
    /// recipe to full appetite, as every site did before U2-04.
    /// </summary>
    public sealed class SiteFeedPlan
    {
        /// <summary>Share of their appetite the site's fish are fed (0-1).</summary>
        public double RationFraction { get; init; } = 1.0;

        /// <summary>The recipe the site's barge carries.</summary>
        public FeedRecipeKind Recipe { get; init; } = FeedRecipeKind.Standard;

        /// <summary>The day the plan was set.</summary>
        public int StartDay { get; init; }

        /// <summary>
        /// The first day the plan no longer holds (the site goes back to the
        /// standard plan that day); -1 for a plan that holds until it is changed.
        /// </summary>
        public int EndDay { get; init; } = -1;

        /// <summary>True on a day the plan feeds the site (from its start, before its end).</summary>
        public bool HoldsOn(int day) => EndDay < 0 || day < EndDay;

        /// <summary>The ration as the growth law takes it.</summary>
        [JsonIgnore]
        public RationPolicy Ration => RationPolicy.OfAppetite(RationFraction);
    }

    /// <summary>What a recipe does to the feed: its FCR and its price, each relative to the standard feed.</summary>
    /// <param name="FcrFactor">Multiplier on the fish's FCR (kg feed per kg growth).</param>
    /// <param name="PriceFactor">Multiplier on the feed price per kg.</param>
    /// <param name="AstaxanthinPpm">Astaxanthin in the feed (mg/kg), which colours the fillet.</param>
    /// <param name="ProteinPct">Crude protein in the feed (%).</param>
    public readonly record struct FeedRecipeEffect(double FcrFactor, double PriceFactor, double AstaxanthinPpm, double ProteinPct)
    {
        /// <summary>The standard feed: no change.</summary>
        public static FeedRecipeEffect Standard(FeedPlanCalibration cal) =>
            new(1.0, 1.0, cal.StandardAstaxanthinPpm, cal.StandardProteinPct);

        /// <summary>The effect of <paramref name="recipe"/> under <paramref name="cal"/>.</summary>
        public static FeedRecipeEffect Of(FeedRecipeKind recipe, FeedPlanCalibration cal)
        {
            if (cal == null) throw new ArgumentNullException(nameof(cal));
            return recipe switch
            {
                FeedRecipeKind.HighEnergy => new(cal.HighEnergyFcrFactor, cal.HighEnergyPriceFactor,
                    cal.StandardAstaxanthinPpm, cal.HighEnergyProteinPct),
                FeedRecipeKind.Economy => new(cal.EconomyFcrFactor, cal.EconomyPriceFactor,
                    cal.EconomyAstaxanthinPpm, cal.EconomyProteinPct),
                _ => Standard(cal)
            };
        }
    }
}
