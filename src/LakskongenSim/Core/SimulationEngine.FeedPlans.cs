using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using LakskongenSim.Farm;

namespace LakskongenSim.Core;

/// <summary>
/// Feeding plans (roadmap U2-04): the player decides how much of their
/// appetite a site's fish are fed (S2-03's <see cref="RationPolicy"/>, set on
/// every pen's population), which recipe its barge carries and for how long.
/// <list type="bullet">
/// <item><see cref="SetFeedPlan"/> sets one site's plan and
/// <see cref="SetFeedPlanForAllSites"/> every site's ("Fôr alle"); the standard
/// plan (full appetite, the standard feed, no window) clears a site's plan.</item>
/// <item>The biology tick hands each site's plan to its pens before they eat
/// (<see cref="ApplyFeedPlans"/>): a pen stocked since yesterday takes its
/// site's ration, and a plan whose window has run out gives way to the
/// standard plan.</item>
/// <item><see cref="ProjectFeedPlan"/> projects a plan (twin-lite, pure, no
/// random draws): the site's cohorts grown on the tick's growth law in the
/// water expected over the window, with the tick's appetite, density and
/// spreading; growth, feed, FCR and cost per day for the plan card.</item>
/// </list>
/// A site without a plan feeds as every site did before U2-04, so a game that
/// never sets one is unchanged (the golden traces).
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Ration stops, limits and recipes of the feeding plans (calibration <c>feedPlan</c>).</summary>
    public FeedPlanCalibration FeedPlanCalibration => _calibration.FeedPlan;

    /// <summary>
    /// Set a site's feeding plan: <paramref name="rationPct"/> % of appetite
    /// (calibration <c>feedPlan.minRation</c> to 100), <paramref name="recipe"/>,
    /// for <paramref name="windowDays"/> days from today (0 = until changed).
    /// The pens' rations change at once. Fails with <c>engine_not_ready</c>,
    /// <c>site_not_found</c>, <c>ration_out_of_range</c> or
    /// <c>window_out_of_range</c>.
    /// </summary>
    public CommandResult SetFeedPlan(string siteId, double rationPct, FeedRecipeKind recipe, int windowDays)
    {
        if (State == null) return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return CommandResult.Fail("site_not_found", $"No site '{siteId}'");
        var check = CheckFeedPlan(rationPct, recipe, windowDays);
        if (check != null) return check;
        var plan = ApplyPlan(site, rationPct / 100.0, recipe, windowDays);
        return CommandResult.Success(DescribePlan(site, plan), value: plan?.RationFraction ?? 1.0,
            value2: plan?.EndDay ?? -1, id: site.Id);
    }

    /// <summary>
    /// "Fôr alle": the same plan for every sea site (<see cref="SetFeedPlan"/>'s
    /// checks once; <see cref="CommandResult.Value"/> is the number of sites).
    /// </summary>
    public CommandResult SetFeedPlanForAllSites(double rationPct, FeedRecipeKind recipe, int windowDays)
    {
        if (State == null) return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var check = CheckFeedPlan(rationPct, recipe, windowDays);
        if (check != null) return check;
        if (State.SeaSites.Count == 0) return CommandResult.Fail("site_not_found", "No sea sites");
        foreach (var site in State.SeaSites)
            ApplyPlan(site, rationPct / 100.0, recipe, windowDays);
        return CommandResult.Success($"Feeding plan for {State.SeaSites.Count} sites", value: State.SeaSites.Count);
    }

    /// <summary>The plan that feeds <paramref name="siteId"/> today; null for the standard plan (or no such site).</summary>
    public SiteFeedPlan? FeedPlanOf(string siteId)
    {
        var site = State?.SeaSites.FirstOrDefault(s => s.Id == siteId);
        return site?.FeedPlan is { } plan && plan.HoldsOn(State!.Day) ? plan : null;
    }

    /// <summary>What the feed at <paramref name="site"/> does today (the standard feed for no site or no plan).</summary>
    internal FeedRecipeEffect FeedRecipeOf(SeaSite? site)
    {
        var cal = _calibration.FeedPlan;
        return site?.FeedPlan is { } plan && plan.HoldsOn(State.Day)
            ? FeedRecipeEffect.Of(plan.Recipe, cal)
            : FeedRecipeEffect.Standard(cal);
    }

    /// <summary>
    /// The fillet grade the processing plant gives fish from <paramref name="site"/>
    /// (<see cref="Processing.FeedRecipe.Grade"/>): its plan's recipe while a
    /// plan feeds it, otherwise the company recipe (<see cref="GameState.RecipeProteinPct"/>,
    /// <see cref="GameState.RecipeAstaxPpm"/>), as before U2-04.
    /// </summary>
    internal string FilletGradeOf(SeaSite? site)
    {
        if (site?.FeedPlan is { } plan && plan.HoldsOn(State.Day))
        {
            var effect = FeedRecipeEffect.Of(plan.Recipe, _calibration.FeedPlan);
            return Processing.FeedRecipe.Grade(effect.ProteinPct, effect.AstaxanthinPpm);
        }
        return Processing.FeedRecipe.Grade(State.RecipeProteinPct, State.RecipeAstaxPpm);
    }

    /// <summary>
    /// Feed-spreading precision at <paramref name="site"/> (0-1, Phase 6
    /// precision feeding): by the site's watch mode, plus 0.1 with AI feeding.
    /// Poor spreading raises the FCR, the waste and the size spread.
    /// </summary>
    internal double SpreadQualityOf(SeaSite? site)
    {
        double quality = site?.WatchMode switch
        {
            "watch" => 0.95,
            "scan" => 0.85,
            "auto" => 0.65,
            _ => FeedSystem.DefaultSpreadQuality
        };
        if (AIFeeding != null && AIFeeding.IsInstalled)
            quality = Math.Min(1.0, quality + 0.1);
        return quality;
    }

    /// <summary>
    /// The biology tick's first step: every site's plan reaches its pens, and a
    /// plan whose window has run out gives way to the standard plan (its pens
    /// back to full appetite). Sites with no plan are not touched.
    /// </summary>
    internal void ApplyFeedPlans()
    {
        for (int i = 0; i < State.SeaSites.Count; i++)
        {
            var site = State.SeaSites[i];
            var plan = site.FeedPlan;
            if (plan == null) continue;
            if (!plan.HoldsOn(State.Day))
            {
                site.FeedPlan = null;
                SetRations(site, 1.0);
                continue;
            }
            SetRations(site, plan.RationFraction);
        }
    }

    private CommandResult? CheckFeedPlan(double rationPct, FeedRecipeKind recipe, int windowDays)
    {
        var cal = _calibration.FeedPlan;
        double min = cal.MinRation * 100.0;
        if (!double.IsFinite(rationPct) || rationPct < min - 1e-9 || rationPct > 100.0 + 1e-9)
            return CommandResult.Fail("ration_out_of_range", $"Ration {rationPct:0.#} % is outside {min:0} to 100 % of appetite");
        if (windowDays < 0 || windowDays > cal.MaxWindowDays)
            return CommandResult.Fail("window_out_of_range", $"Window {windowDays} days is outside 0 to {cal.MaxWindowDays}");
        if (!Enum.IsDefined(recipe))
            return CommandResult.Fail("unknown_recipe", $"Unknown recipe {recipe}");
        return null;
    }

    /// <summary>Store the plan (null for the standard plan) and set the pens' rations now.</summary>
    private SiteFeedPlan? ApplyPlan(SeaSite site, double fraction, FeedRecipeKind recipe, int windowDays)
    {
        fraction = Math.Clamp(fraction, 0.0, 1.0);
        bool standard = fraction >= 1.0 && recipe == FeedRecipeKind.Standard && windowDays == 0;
        site.FeedPlan = standard ? null : new SiteFeedPlan
        {
            RationFraction = fraction,
            Recipe = recipe,
            StartDay = State.Day,
            EndDay = windowDays > 0 ? State.Day + windowDays : -1
        };
        SetRations(site, fraction);
        return site.FeedPlan;
    }

    /// <summary>Every pen's population at <paramref name="site"/> fed <paramref name="fraction"/> of its appetite.</summary>
    private static void SetRations(SeaSite site, double fraction)
    {
        for (int p = 0; p < site.Pens.Count; p++)
        {
            var population = site.Pens[p];
            if (population.Ration != null && population.Ration.FractionOfAppetite == fraction) continue;
            population.Ration = fraction >= 1.0 ? RationPolicy.FullAppetite : RationPolicy.OfAppetite(fraction);
        }
    }

    private string DescribePlan(SeaSite site, SiteFeedPlan? plan)
        => plan == null
            ? $"{site.Label}: standard feeding plan"
            : $"{site.Label}: {plan.RationFraction * 100.0:0} % of appetite, {plan.Recipe} feed"
                + (plan.EndDay >= 0 ? $" until day {plan.EndDay}" : "");

    // ─── Projection (twin-lite) ───────────────────────────────────────────

    /// <summary>
    /// Project <paramref name="days"/> days of a plan at <paramref name="siteId"/>
    /// without touching the game: every cohort of the site's pens is grown on
    /// the tick's law (<see cref="Growth"/>.Law) at the plan's ration and
    /// recipe, in the water expected each day (the area's normal sea
    /// temperature plus today's anomaly, fading at its calibrated pace; a land
    /// tank or a zone with its own temperature keeps today's), with today's
    /// appetite (oxygen, gills), the density its growing fish reach in their
    /// pens, the site's spreading and the day length. The site's fish are held
    /// constant (deaths are not projected). Null when there is no such site.
    /// Pure: draws no random numbers and changes nothing.
    /// </summary>
    public FeedPlanProjection? ProjectFeedPlan(string siteId, double rationPct, FeedRecipeKind recipe, int days)
    {
        if (State == null || Growth == null) return null;
        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return null;
        days = Math.Max(1, days);
        double ration = Math.Clamp(double.IsFinite(rationPct) ? rationPct / 100.0 : 1.0, 0.0, 1.0);
        var effect = FeedRecipeEffect.Of(recipe, _calibration.FeedPlan);
        double spread = SpreadQualityOf(site);
        var law = Growth.Law;

        var total = new ProjectionSums();
        foreach (var population in site.Pens)
            ProjectPopulation(population, site, ration, effect, spread, law, days, ref total);

        decimal price = Feed.FeedCostPerKg * (decimal)effect.PriceFactor;
        double spill = Optimization.RemoteOpsCentre.FeedSpillFraction(site.WatchMode);
        double cost = total.DeliveredKg * (double)price * (1.0 + spill);
        return new FeedPlanProjection(site.Id, days, ration, recipe, total.Fish,
            total.StartBiomassKg, total.EndBiomassKg, total.GrowthKg, total.EatenKg, total.DeliveredKg, cost);
    }

    private struct ProjectionSums
    {
        public int Fish;
        public double StartBiomassKg, EndBiomassKg, GrowthKg, EatenKg, DeliveredKg;
    }

    /// <summary>One population's days of the projection, added to <paramref name="sums"/>.</summary>
    private void ProjectPopulation(FishPopulation population, SeaSite site, double ration, FeedRecipeEffect effect,
        double spread, TgcGrowthLaw law, int days, ref ProjectionSums sums)
    {
        var holding = HoldingOf(population);
        var water = WaterOf(holding, population);
        var zone = holding.Zone;
        double volume = DensityVolumeM3(holding, population);
        var containment = ContainmentTech.ModifiersFor(site.Containment);

        // Appetite as the tick builds it: today's oxygen saturation, and the
        // gills, which heal week by week while no bloom stands at the site
        // (BloomEventDeck.GillRecoveryPerWeek; a new bloom is not foreseen).
        double oxygenAppetite = 1.0;
        if (zone != null)
            oxygenAppetite = WaterQuality.CalculateDOSaturationModifier(WaterQuality.CalculateDOSaturation(
                water.OxygenMgPerL, water.TemperatureC, water.SalinityPsu));
        double gill = site.GillScore;
        double gillRecoveryPerDay = State.ActiveBlooms.Any(b => b.SiteId == site.Id)
            ? 0.0 : BloomEventDeck.GillRecoveryPerWeek / SimCalendar.DaysPerWeek;
        if (population.IsFasting) ration = 0.0;

        var weights = new double[population.Cohorts.Count];
        var cvs = new double[population.Cohorts.Count];
        for (int ci = 0; ci < weights.Length; ci++)
        {
            var c = population.Cohorts[ci];
            weights[ci] = c.MeanWeightKg;
            cvs[ci] = c.WeightCv;
            sums.Fish += c.Count;
            sums.StartBiomassKg += c.Count * c.MeanWeightKg;
        }

        double research = 1.0 + GrowthRateBonus;
        double deliveredPerEaten = 1.0 + (FeedSystem.PrecisionFCRMultiplier(spread) - 1.0)
            * (1.0 - ResearchEffectKeys.ClampReduction(FeedWasteReduction));
        for (int day = 0; day < days; day++)
        {
            double appetite = oxygenAppetite * (0.6 + 0.4 * (gill / 100.0));
            gill = Math.Min(100.0, gill + gillRecoveryPerDay);
            double temperature = ExpectedTemperature(water, zone, day);
            double biomass = 0.0;
            for (int ci = 0; ci < weights.Length; ci++)
                biomass += population.Cohorts[ci].Count * weights[ci];
            double densityForO2 = (zone != null ? biomass / volume : DefaultStockingDensityKgM3) * containment.OxygenDemandMult;
            double densityGrowth = zone != null ? WaterQuality.CalculateStockingDensityModifier(densityForO2) : 1.0;
            double dayLength = water.Kind == HoldingKind.LandTank
                ? TgcGrowthLaw.ContinuousLightHours
                : DayLength.Hours(ProductionAreas.LatitudeDeg(AreaWithClimatology(water.ProductionArea)),
                    SimCalendar.DayOfYear(State.Day + day));
            for (int ci = 0; ci < weights.Length; ci++)
            {
                var cohort = population.Cohorts[ci];
                var conditions = new GrowthConditions(temperature, ration, appetite,
                    CohortGrowthMultiplier(cohort) * containment.GrowthMult * research,
                    densityGrowth, spread, dayLength);
                double satiationFcr = Feed.SatiationFcr(temperature, cohort.Stage, law.FcrSizeFactor(weights[ci]),
                    densityForO2, appetite, FCRReduction) * effect.FcrFactor;
                var grown = law.GrowOneDay(weights[ci], cvs[ci], cohort.Stage, conditions, satiationFcr);
                sums.GrowthKg += cohort.Count * Math.Max(0.0, grown.GrowthKg);
                sums.EatenKg += cohort.Count * grown.EatenFeedKg;
                sums.DeliveredKg += cohort.Count * grown.EatenFeedKg * deliveredPerEaten;
                weights[ci] = grown.WeightKg;
                cvs[ci] = grown.WeightCv;
            }
        }
        for (int ci = 0; ci < weights.Length; ci++)
            sums.EndBiomassKg += population.Cohorts[ci].Count * weights[ci];
    }

    /// <summary>
    /// The water temperature a holding can expect <paramref name="dayAhead"/>
    /// days from now (0 = the next simulated day): a land tank, or a zone with
    /// its own temperature, keeps today's; sea water is its area's normal for
    /// that day plus the shared anomaly and a storyteller anomaly, each fading
    /// at its calibrated e-folding time (the expectation of the AR(1) weather).
    /// </summary>
    internal double ExpectedTemperature(in HoldingWater today, Zone? zone, int dayAhead)
    {
        if (today.Kind == HoldingKind.LandTank || Weather == null || (zone != null && !zone.FollowsSeaTemperature))
            return today.TemperatureC;
        var sea = _calibration.SeaTemperature;
        int ahead = dayAhead + 1;
        double slow = Weather.SeaAnomalyC * Math.Exp(-ahead / Math.Max(1.0, sea.AnomalyEFoldingDays));
        double eventual = Weather.EventAnomalyC * Math.Exp(-ahead / Math.Max(1.0, sea.EventAnomalyEFoldingDays));
        double normal = ProductionAreas.ClimatologyC(AreaWithClimatology(today.ProductionArea), Weather.SimulationDay + ahead);
        return Math.Max(ProductionAreas.SeawaterFreezingPointC, normal + slow + eventual);
    }

    private static int AreaWithClimatology(int area)
        => ProductionAreas.HasClimatology(area) ? area : ProductionAreas.NationalReference;
}

/// <summary>
/// A feeding plan projected at a site (<see cref="SimulationEngine.ProjectFeedPlan"/>):
/// the site's fish over the window and what they grow, eat and cost.
/// </summary>
/// <param name="SiteId">The site.</param>
/// <param name="Days">Days projected.</param>
/// <param name="RationFraction">Share of appetite fed (0-1).</param>
/// <param name="Recipe">The recipe fed.</param>
/// <param name="Fish">Fish at the site (held constant).</param>
/// <param name="StartBiomassKg">Biomass today (kg).</param>
/// <param name="EndBiomassKg">Biomass at the end of the window (kg).</param>
/// <param name="GrowthKg">Biomass the fish gain over the window (kg).</param>
/// <param name="EatenFeedKg">Feed the fish eat (kg): the biological FCR's numerator.</param>
/// <param name="FeedKg">Feed delivered (kg): what was eaten plus what poor spreading wastes.</param>
/// <param name="CostNok">Feed cost (NOK), the autopilot's spill included.</param>
public sealed record FeedPlanProjection(
    string SiteId, int Days, double RationFraction, FeedRecipeKind Recipe, int Fish,
    double StartBiomassKg, double EndBiomassKg, double GrowthKg, double EatenFeedKg, double FeedKg, double CostNok)
{
    /// <summary>Growth per day (kg/day).</summary>
    public double GrowthKgPerDay => Days > 0 ? GrowthKg / Days : 0.0;

    /// <summary>Feed delivered per day (kg/day).</summary>
    public double FeedKgPerDay => Days > 0 ? FeedKg / Days : 0.0;

    /// <summary>Feed cost per day (NOK/day).</summary>
    public double CostNokPerDay => Days > 0 ? CostNok / Days : 0.0;

    /// <summary>FCR: feed delivered per kg of growth (0 with no growth).</summary>
    public double Fcr => GrowthKg > 0.0 ? FeedKg / GrowthKg : 0.0;

    /// <summary>Biological FCR: feed eaten per kg of growth (0 with no growth).</summary>
    public double BiologicalFcr => GrowthKg > 0.0 ? EatenFeedKg / GrowthKg : 0.0;

    /// <summary>Feed cost per kg of growth (NOK/kg; 0 with no growth).</summary>
    public double CostPerKgGrowthNok => GrowthKg > 0.0 ? CostNok / GrowthKg : 0.0;

    /// <summary>Mean weight of the site's fish at the end of the window (kg; 0 with no fish).</summary>
    public double EndMeanWeightKg => Fish > 0 ? EndBiomassKg / Fish : 0.0;
}
