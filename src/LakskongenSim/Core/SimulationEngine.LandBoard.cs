using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Harvest;

namespace LakskongenSim.Core;

/// <summary>
/// The land board's reads (roadmap F2-15 for F2-32, docs/FARM_TYPES_SPEC.md
/// §6.5 "Land board"): each lot's and tank's water against its comfort band
/// with the constraint that binds it, the cohorts against their plans, the
/// competence and its Oppkjøring milestones, the three Vannstrategi presets
/// with their projected kWh/kg, growth and maturation risk, the harvest
/// planner (tank groups against the expected weekly price with the S2-04 size
/// grades) and the post-smolt planner. Every read is pure: it changes no saved
/// field and draws nothing (the projections run on copies).
/// </summary>
public partial class SimulationEngine
{
    // ── The board: lots, tanks, the binding constraint ─────────────────────

    /// <summary>
    /// The land board of a site: its lots in the 3D layout order (lot order,
    /// tank order), each tank's water ring against its comfort band and the
    /// constraint that binds it, its cohort against the plan; each lot's
    /// worst tank. Null for an unknown site or one without lots.
    /// </summary>
    public LandBoardView? LandBoardOf(string siteId)
    {
        var site = FindLandSite(siteId);
        if (State == null || site == null || !site.HasLots) return null;
        var cal = LandCalibration;
        var lots = new List<LandLotBoardView>();
        foreach (var lot in site.Lots)
        {
            var t = cal.For(lot.Template);
            var tanks = new List<LandTankBoardView>();
            for (int i = 0; i < lot.Tanks.Count; i++)
            {
                var tank = lot.Tanks[i];
                var fish = site.PopulationIn(tank);
                int count = fish?.TotalCount ?? 0;
                double kg = fish?.GetTotalBiomassKg() ?? 0.0;
                double weight = count > 0 ? kg / count : 0.0;
                double density = tank.VolumeM3 > 0.0 ? kg / tank.VolumeM3 : 0.0;
                var status = LandStatus.Evaluate(tank.DoMgL, tank.Co2MgL, tank.TanMgL, tank.TemperatureC, site.TargetWaterTemp,
                    density, t.MaxDensityKgM3, cal);
                var plan = count > 0 ? tank.Plan : null;
                string planId = site.HarvestPlans.FirstOrDefault(p => p.Status is LandHarvestPlanStatus.Planned or LandHarvestPlanStatus.Ordered
                    && p.TankIds.Contains(tank.Id))?.Id ?? "";
                tanks.Add(new LandTankBoardView(tank.Id, tank.Pen.Number, tank.Role, IsTankReadyNow(lot, i), count, weight, kg, density,
                    status, plan?.PlanWeightKg ?? 0.0, plan == null ? 1.0 : plan.ShareOfPlan(weight), plan?.ShortfallDays ?? 0,
                    plan == null || count <= 0 ? "" : plan.Driver(), tank.StockedDay, count > 0 && tank.StockedDay >= 0 ? State.Day - tank.StockedDay : 0,
                    count > 0 ? PostSmoltBridge.TierOf(weight, tank.SeawaterDays, SmoltBridge) : SmoltQualityTier.Standard,
                    tank.SeawaterDays, planId));
            }
            var stocked = tanks.Where(k => k.Fish > 0).ToList();
            var worst = (stocked.Count > 0 ? stocked : tanks)
                .OrderByDescending(k => k.Status.Level).ThenByDescending(k => k.Status.Load).FirstOrDefault();
            lots.Add(new LandLotBoardView(lot.Id, lot.Template, IsLotCommissionedNow(lot), worst?.Status.Level ?? LandStatusLevel.Ok,
                worst?.Status.Binding ?? "", worst?.TankId ?? "", tanks, stocked.Count(k => k.Status.Level != LandStatusLevel.Ok)));
        }
        return new LandBoardView(site.Id, site.Name, site.Kind, lots);
    }

    // ── Competence and the Oppkjøring ──────────────────────────────────────

    /// <summary>The Oppkjøring's utilisation milestones (spec §3.3, §6.5): 55 → 70 → 85 → 100 % of the nameplate.</summary>
    public static readonly double[] OppkjoringMilestones = { 0.55, 0.70, 0.85, 1.00 };

    /// <summary>
    /// A grow-out site's competence (spec §3.3, §6.5 "Oppkjøring"): what it is
    /// made of, the utilisation cap and growth it gives, the milestones with the
    /// competence each needs, and every lever with its gain, its cost and
    /// whether it runs. Null for an unknown site or one without lots.
    /// </summary>
    public LandCompetenceView? LandCompetenceViewOf(string siteId)
    {
        var site = FindLandSite(siteId);
        if (State == null || site == null || !site.HasLots) return null;
        var cal = LandCalibration.Competence;
        var breakdown = LandCompetenceOf(site);
        bool applies = HasLandCompetence(site);
        var milestones = OppkjoringMilestones.Select(cap => new LandOppkjoringStep(cap,
            CompetenceModel.CompetenceForCap(cap, cal), applies ? LandUtilisationCapOf(site) >= cap - 1e-9 : true)).ToList();
        var c = site.Competence;
        bool consultancyRuns = c.ConsultancyStartDay >= 0 && State.Day < c.ConsultancyStartDay + cal.ConsultancyDays;
        var levers = new List<LandCompetenceLever>
        {
            new(LandCompetenceLevers.Research, cal.Research, 0m, 0m, LandResearchDone(), -1),
            new(LandCompetenceLevers.Driftsleder, cal.Driftsleder, 0m, cal.DriftslederNokPerYear, c.DriftslederSinceDay >= 0, -1),
            new(LandCompetenceLevers.Training, cal.TrainingMax, cal.TrainingNok, 0m, c.TrainingStartDay >= 0,
                c.TrainingStartDay >= 0 ? c.TrainingStartDay + (int)Math.Ceiling(cal.TrainingMax / Math.Max(1e-9, cal.TrainingPerStep)) * cal.TrainingStepDays : -1),
            new(LandCompetenceLevers.Consultancy, cal.Consultancy, cal.ConsultancyNok, 0m, consultancyRuns,
                c.ConsultancyStartDay >= 0 ? c.ConsultancyStartDay + cal.ConsultancyDays : -1),
            new(LandCompetenceLevers.EarlyHiring, cal.EarlyHiring, 0m, 0m, c.StaffHiredDay >= 0, -1),
            new(LandCompetenceLevers.Cycles, cal.PerCycle, 0m, 0m, c.CompletedCycles > 0, -1),
        };
        return new LandCompetenceView(site.Id, applies, breakdown, LandUtilisationCapOf(site), LandCompetenceGrowthMultiplierOf(site),
            LandGrowthMultiplierOf(site), c.CompletedCycles, LandRotationFish(site), c.HarvestedFish, milestones, levers,
            site.FeedRationFraction, LandFcrPenaltyOf(site), c.FcrPenaltyUntilDay, LandNameplateTonnesNow(site), AnnualProductionCapTonnes(site));
    }

    // ── Vannstrategi presets ───────────────────────────────────────────────

    /// <summary>The preset ids (spec §6.5), in the board's order.</summary>
    public static readonly string[] VannstrategiPresetIds = { "sparsom", "balansert", "hoy_tetthet" };

    private readonly Dictionary<string, (int Day, string Key, IReadOnlyList<LandPresetProjection> Presets)> _presetCache = new(StringComparer.Ordinal);

    /// <summary>The preset's settings for <paramref name="template"/>: a loop unit (RAS hall, post-smolt module) keeps its own reuse.</summary>
    internal LandWaterStrategy PresetStrategy(string presetId, LandTemplate template, double currentReuse)
    {
        var p = PresetCalibration(presetId) ?? throw new ArgumentOutOfRangeException(nameof(presetId), presetId, "Unknown preset");
        bool loop = template is LandTemplate.RasHall or LandTemplate.PostSmoltModule;
        double reuse = loop ? currentReuse : Math.Min(p.Reuse, LandCalibration.Intake.MaxReuse);
        return new LandWaterStrategy(reuse, p.DeepIntakeShare, p.SetpointC, p.BlendTowardSetpoint);
    }

    private LandPresetCalibration? PresetCalibration(string presetId) => presetId switch
    {
        "sparsom" => LandCalibration.Vannstrategi.Sparsom,
        "balansert" => LandCalibration.Vannstrategi.Balansert,
        "hoy_tetthet" => LandCalibration.Vannstrategi.HoyTetthet,
        _ => null,
    };

    /// <summary>
    /// The three Vannstrategi presets of a site (spec §6.5), each with the
    /// engine's projection of its first unit over the coming year in steady
    /// rotation (<see cref="LandLotProjector"/> on the site's pad, intake lines,
    /// heat recovery and production area, the plan at the preset's setpoint and
    /// the site's growth): electricity per kg of live-weight growth, the growth
    /// a year, the plan's cycle and the maturation risk (the share of the fish
    /// maturing by harvest, <see cref="LandMaturation"/>). A pure read, cached
    /// for the day. Empty for an unknown site or one without lots.
    /// </summary>
    public IReadOnlyList<LandPresetProjection> VannstrategiPresetsOf(string siteId)
    {
        var site = FindLandSite(siteId);
        if (State == null || site == null || !site.HasLots || site.Lots.Count == 0)
            return Array.Empty<LandPresetProjection>();
        var lot = site.Lots.FirstOrDefault(IsLotCommissionedNow) ?? site.Lots[0];
        double growth = LandGrowthMultiplierOf(site) * SmoltStrainRegistry.Get("standard").BuildGenome().GetGrowthMultiplier();
        string key = FormattableString.Invariant($"{lot.Template}|{lot.HeatRecovery}|{growth:R}|{site.RecirculationRate:R}|{site.PlotId}");
        if (_presetCache.TryGetValue(site.Id, out var cached) && cached.Day == State.Day && cached.Key == key)
            return cached.Presets;

        var cal = LandCalibration;
        var t = cal.For(lot.Template);
        var intake = LandIntakeOf(site);
        int area = ProductionAreaOfZone(site.ZoneId);
        var current = site.WaterStrategy;
        var presets = new List<LandPresetProjection>();
        foreach (var id in VannstrategiPresetIds)
        {
            var strategy = PresetStrategy(id, lot.Template, site.RecirculationRate);
            var plan = LandStockingPlan.For(t, cal.Production) with { TemperatureC = strategy.SetpointC, GrowthMultiplier = growth };
            var run = LandLotProjector.Run(new LandProjectionSettings
            {
                Template = lot.Template,
                Strategy = strategy,
                Site = intake,
                HeatRecovery = lot.HeatRecovery,
                ProductionArea = area,
                StartDay = State.Day,
                Days = SimCalendar.DaysPerYear,
                Plan = plan,
                RecordDays = true,
            }, cal, Growth.Law);
            int cycleDays = LandStockingPlanner.Project(Growth.Law, plan).CycleDays;
            var tankC = run.Daily.Select(d => d.TankC).ToList();
            double index = LandMaturation.IndexOverCycle(tankC, cycleDays, cal.Maturation);
            bool active = Math.Abs(current.Reuse - strategy.Reuse) < 1e-9 && Math.Abs(current.SetpointC - strategy.SetpointC) < 1e-9
                && current.BlendTowardSetpoint == strategy.BlendTowardSetpoint
                && (strategy.BlendTowardSetpoint || Math.Abs(current.DeepIntakeShare - strategy.DeepIntakeShare) < 1e-9);
            presets.Add(new LandPresetProjection(id, strategy, run.KWhPerKgGrowth, run.KWhPerKgGrowth * cal.Energy.PowerPriceNokPerKWh,
                run.NetGrowthKg / 1000.0, run.ProductionIndexKgM3Yr, cycleDays, run.MeanTankC, run.LoxPerKgGrowth, run.MeanCo2MgL,
                index, LandMaturation.MaturingShareOfFish(index, cal.Maturation), active));
        }
        _presetCache[site.Id] = (State.Day, key, presets);
        return presets;
    }

    /// <summary>
    /// Set a site's Vannstrategi to a preset (spec §6.5): its reuse (a loop
    /// unit keeps its own), deep share or blend, and setpoint, through
    /// <see cref="SetVannstrategi"/>. Fails: not_found, out_of_range (an unknown preset).
    /// </summary>
    public CommandResult ApplyVannstrategiPreset(string siteId, string presetId)
    {
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots || site.Lots.Count == 0)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        if (PresetCalibration(presetId) == null)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown preset '{presetId}'");
        var strategy = PresetStrategy(presetId, site.Lots[0].Template, site.RecirculationRate);
        var result = SetVannstrategi(site.Id, strategy.Reuse, strategy.DeepIntakeShare, strategy.SetpointC, strategy.BlendTowardSetpoint);
        return result.Ok ? CommandResult.Success($"{site.Name} runs the {presetId} preset", strategy.Reuse, strategy.SetpointC, presetId) : result;
    }

    // ── The harvest planner ────────────────────────────────────────────────

    /// <summary>
    /// The harvest planner of a site (spec §6.5 "Du bestemmer når du selger"):
    /// the coming ISO weeks (as far as a plan reaches) with the price the market
    /// is expected to pay in each (the seasonal curve, <see cref="Market.MarketSimulation.ExpectedPrices"/>),
    /// and each stocked tank's mean weight projected into each week (the growth
    /// law at the tank's water and the site's growth, fed to appetite) with the
    /// S2-04 size-grade factor at that weight. A pure read. Null for an unknown
    /// site or one without lots.
    /// </summary>
    public LandHarvestPlannerView? LandHarvestPlannerOf(string siteId)
    {
        var site = FindLandSite(siteId);
        if (State == null || site == null || !site.HasLots) return null;
        int weeks = LandCalibration.Growth.HarvestPlanWeeks;
        int firstMonday = IsoWeekStartDay(State.Day) + SimCalendar.DaysPerWeek;
        int horizon = firstMonday + weeks * SimCalendar.DaysPerWeek - State.Day;
        var prices = MarketSimulation?.ExpectedPrices(horizon) ?? Array.Empty<decimal>();
        var weekViews = new List<LandHarvestWeek>();
        for (int w = 0; w < weeks; w++)
        {
            int monday = firstMonday + w * SimCalendar.DaysPerWeek;
            decimal sum = 0m;
            int n = 0;
            for (int d = monday; d < monday + SimCalendar.DaysPerWeek; d++)
            {
                int i = d - State.Day - 1;
                if (i >= 0 && i < prices.Length) { sum += prices[i]; n++; }
            }
            weekViews.Add(new LandHarvestWeek(monday, SimCalendar.IsoWeek(monday), n > 0 ? sum / n : 0m));
        }

        var harvest = HarvestCalibration;
        var tanks = new List<LandHarvestTankForecast>();
        double growth = LandGrowthMultiplierOf(site);
        Span<double> classes = stackalloc double[SizeClassPriceLadder.ClassCount];
        foreach (var lot in site.Lots)
            foreach (var tank in lot.Tanks)
            {
                var fish = site.PopulationIn(tank);
                if (fish == null || fish.TotalCount <= 0) continue;
                double weight = fish.GetTotalBiomassKg() / fish.TotalCount;
                double cv = fish.Cohorts.Where(c => c.Count > 0).Select(c => c.WeightCv).DefaultIfEmpty(0.0).Max();
                if (!(cv > 0.0)) cv = Growth.Law.InitialWeightCv(weight);
                double genome = fish.Cohorts.Where(c => c.Count > 0).Select(c => c.Genetics?.GetGrowthMultiplier() ?? 1.0).DefaultIfEmpty(1.0).Max();
                var conditions = new GrowthConditions(tank.TemperatureC, GrowthMultiplier: growth * genome,
                    DayLengthHours: TgcGrowthLaw.ContinuousLightHours);
                var byWeek = new List<double>(weeks);
                var factors = new List<double>(weeks);
                double w0 = weight, cv0 = cv;
                int day = State.Day;
                foreach (var week in weekViews)
                {
                    for (; day < week.WeekStartDay; day++)
                    {
                        var grown = Growth.Law.GrowOneDay(w0, cv0, LifeStage.Seawater, conditions, satiationFcr: 0.0);
                        w0 = grown.WeightKg;
                        cv0 = grown.WeightCv;
                    }
                    classes.Clear();
                    SizeClassPriceLadder.AddCohort(classes, 1000, w0, cv0, harvest.HogYieldFraction);
                    byWeek.Add(w0);
                    factors.Add(SizeClassPriceLadder.MeanFactor(harvest, classes));
                }
                string planId = site.HarvestPlans.FirstOrDefault(p => p.Status is LandHarvestPlanStatus.Planned or LandHarvestPlanStatus.Ordered
                    && p.TankIds.Contains(tank.Id))?.Id ?? "";
                tanks.Add(new LandHarvestTankForecast(tank.Id, fish.TotalCount, weight, byWeek, factors, planId));
            }
        double hall = HarvestRoute.For(HoldingKind.LandTank, harvest, site.Id).DailyCapacityTonnesHog;
        return new LandHarvestPlannerView(site.Id, weekViews, tanks, site.HarvestPlans.ToList(), hall);
    }

    // ── The post-smolt planner ─────────────────────────────────────────────

    /// <summary>
    /// The post-smolt planner of a site (spec §6.5): its water's salinity (the
    /// blend a river intake allows, the H2S sulphate factor), each stocked
    /// tank's weight against the module's target, its salinity record, the
    /// tier it would carry to sea today, desmoltification, vaccination, what the
    /// fish would sell for and what they would cost bought at their tier, any
    /// transfer booked; and the fjord sites it can send fish to with their MTB
    /// headroom after the transfers already booked. A pure read. Null for an
    /// unknown site or one without lots.
    /// </summary>
    public PostSmoltPlannerView? PostSmoltPlannerOf(string siteId)
    {
        var site = FindLandSite(siteId);
        if (State == null || site == null || !site.HasLots) return null;
        var cal = SmoltBridge;
        var tanks = new List<PostSmoltTankView>();
        foreach (var lot in site.Lots)
        {
            var t = LandCalibration.For(lot.Template);
            foreach (var tank in lot.Tanks)
            {
                var fish = site.PopulationIn(tank);
                int count = fish?.TotalCount ?? 0;
                double weight = count > 0 ? fish!.GetTotalBiomassKg() / count : 0.0;
                var tier = count > 0 ? PostSmoltBridge.TierOf(weight, tank.SeawaterDays, cal) : SmoltQualityTier.Standard;
                var strain = SmoltStrainRegistry.Get(string.IsNullOrEmpty(tank.StrainId) ? "standard" : tank.StrainId);
                var job = site.Transfers.FirstOrDefault(j => j.TankId == tank.Id);
                tanks.Add(new PostSmoltTankView(tank.Id, count, weight, t.PlanHarvestWeightKg, tier, tank.SeawaterDays,
                    tank.LowSalinityDays, PostSmoltBridge.IsDesmoltified(tank.LowSalinityDays, cal), fish != null && fish.VaccinatedDay >= 0,
                    (decimal)(count * weight) * cal.SalePriceNokPerKgLive, count * SmoltTierCostPerFish(tier, strain, SmoltSource.Purchased),
                    job?.Id ?? "", job?.Boat.DueDay ?? -1, job?.DestinationSiteId ?? ""));
            }
        }
        var destinations = FjordSites().Select(s =>
        {
            double pending = PendingTransferTonnesTo(s.Id);
            return new PostSmoltDestinationView(s.Id, s.Label, s.MABTonnes, s.CurrentBiomassTonnes, pending,
                Math.Max(0.0, s.MABTonnes - s.CurrentBiomassTonnes - pending));
        }).ToList();
        return new PostSmoltPlannerView(site.Id, LandSalinityOf(site), LandPadHasRiverIntake(site), LandSulphateFactorOf(site),
            cal.SuperSmoltMinWeightKg, cal.LargeMinWeightKg, cal.SuperSmoltMinSalinityPpt, cal.SeawaterRecordDays,
            cal.DesmoltSalinityPpt, cal.DesmoltDays, tanks, destinations, site.Transfers.ToList());
    }
}

/// <summary>Competence levers by id (the bridge's keys, text <c>LAND_LEVER_*</c>).</summary>
public static class LandCompetenceLevers
{
    /// <summary>Research <c>land-based-ras</c>.</summary>
    public const string Research = "research";
    /// <summary>An operations manager (driftsleder).</summary>
    public const string Driftsleder = "driftsleder";
    /// <summary>A training programme.</summary>
    public const string Training = "training";
    /// <summary>An external consultancy.</summary>
    public const string Consultancy = "consultancy";
    /// <summary>Staff hired well before the first fish.</summary>
    public const string EarlyHiring = "early_hiring";
    /// <summary>Completed production cycles.</summary>
    public const string Cycles = "cycles";
}

/// <summary>One tank on the land board.</summary>
/// <param name="TankId">The tank.</param>
/// <param name="Number">Its number on the site.</param>
/// <param name="Role">Grow-out, pre-grow-out or post-smolt.</param>
/// <param name="Ready">True when the tank takes fish.</param>
/// <param name="Fish">Fish in it.</param>
/// <param name="MeanWeightKg">Their mean live weight (kg).</param>
/// <param name="BiomassKg">Their live weight (kg).</param>
/// <param name="DensityKgM3">Biomass over the tank's volume (kg/m³).</param>
/// <param name="Status">The water ring and the binding constraint.</param>
/// <param name="PlanWeightKg">The cohort plan's weight today (kg; 0 without a plan).</param>
/// <param name="ShareOfPlan">The actual weight over the plan's (1 without a plan).</param>
/// <param name="ShortfallDays">Days in a row below the shortfall share of the plan.</param>
/// <param name="ShortfallDriver">The driver that has cost the most growth lately ("" when empty).</param>
/// <param name="StockedDay">Day the fish were stocked (-1 when empty).</param>
/// <param name="DaysInTank">Days since the stocking.</param>
/// <param name="Tier">The tier the fish would carry to sea today.</param>
/// <param name="SeawaterDays">Days in a row at or above the SuperSmolt salinity.</param>
/// <param name="HarvestPlanId">The harvest plan the tank is in ("" when none).</param>
public sealed record LandTankBoardView(string TankId, int Number, TankRole Role, bool Ready, int Fish, double MeanWeightKg,
    double BiomassKg, double DensityKgM3, LandTankStatus Status, double PlanWeightKg, double ShareOfPlan, int ShortfallDays,
    string ShortfallDriver, int StockedDay, int DaysInTank, SmoltQualityTier Tier, int SeawaterDays, string HarvestPlanId);

/// <summary>One lot (or post-smolt module) on the land board.</summary>
/// <param name="LotId">The lot.</param>
/// <param name="Template">Its unit.</param>
/// <param name="Commissioned">True when a tank takes fish.</param>
/// <param name="Level">Its worst stocked tank's status.</param>
/// <param name="Binding">That tank's binding constraint.</param>
/// <param name="WorstTankId">That tank (the board's one click to the worst tank).</param>
/// <param name="Tanks">Its tanks in the 3D layout order.</param>
/// <param name="Exceptions">Stocked tanks outside their comfort band.</param>
public sealed record LandLotBoardView(string LotId, LandTemplate Template, bool Commissioned, LandStatusLevel Level, string Binding,
    string WorstTankId, IReadOnlyList<LandTankBoardView> Tanks, int Exceptions);

/// <summary>A site's land board.</summary>
/// <param name="SiteId">The site.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Grow-out or post-smolt.</param>
/// <param name="Lots">Its lots in order.</param>
public sealed record LandBoardView(string SiteId, string Name, LandSiteKind Kind, IReadOnlyList<LandLotBoardView> Lots);

/// <summary>One Oppkjøring milestone: a utilisation cap, the competence it needs and whether the site has it.</summary>
/// <param name="Cap">The utilisation cap (share of nameplate a year).</param>
/// <param name="Competence">The competence that reaches it.</param>
/// <param name="Reached">True when the site's cap is at or above it.</param>
public readonly record struct LandOppkjoringStep(double Cap, double Competence, bool Reached);

/// <summary>One competence lever: its gain, its cost and whether it runs.</summary>
/// <param name="Id">The lever (<see cref="LandCompetenceLevers"/>).</param>
/// <param name="Gain">Competence it adds (the most, for the training programme; per cycle for cycles).</param>
/// <param name="OneOffNok">Its one-off cost (NOK).</param>
/// <param name="PerYearNok">Its yearly cost (NOK).</param>
/// <param name="Active">True when it runs or ran.</param>
/// <param name="UntilDay">Day it ends or completes (-1 when open-ended or not started).</param>
public readonly record struct LandCompetenceLever(string Id, double Gain, decimal OneOffNok, decimal PerYearNok, bool Active, int UntilDay);

/// <summary>A site's competence and Oppkjøring (<see cref="SimulationEngine.LandCompetenceViewOf"/>).</summary>
public sealed record LandCompetenceView(string SiteId, bool Applies, CompetenceBreakdown Breakdown, double UtilisationCap,
    double GrowthAgainstPlan, double GrowthMultiplier, int CompletedCycles, long RotationFish, long HarvestedFish,
    IReadOnlyList<LandOppkjoringStep> Milestones, IReadOnlyList<LandCompetenceLever> Levers, double FeedRationFraction,
    double FcrPenalty, int FcrPenaltyUntilDay, double NameplateTonnes, double AnnualCapTonnes);

/// <summary>A Vannstrategi preset and the engine's projection of it.</summary>
/// <param name="Id">The preset ("sparsom", "balansert", "hoy_tetthet").</param>
/// <param name="Strategy">Its reuse, deep share or blend, and setpoint.</param>
/// <param name="KWhPerKg">Electricity per kg of live-weight growth.</param>
/// <param name="EnergyNokPerKg">That at the power price (NOK per kg LW growth).</param>
/// <param name="GrowthTonnesPerYear">Live-weight growth of the unit in a year (t).</param>
/// <param name="ProductionIndexKgM3Yr">Live weight produced per m³ of tank a year.</param>
/// <param name="CycleDays">The plan's days to the harvest weight at the preset's setpoint.</param>
/// <param name="MeanTankC">The tanks' mean temperature (°C).</param>
/// <param name="LoxPerKg">Liquid oxygen per kg of growth.</param>
/// <param name="MeanCo2MgL">Feed-weighted mean CO2 (mg/L).</param>
/// <param name="MaturationIndex">The maturation index a cohort reaches by harvest.</param>
/// <param name="MaturationRisk">The share of the fish maturing by harvest (mixed-sex stock).</param>
/// <param name="Active">True when the site runs this preset now.</param>
public sealed record LandPresetProjection(string Id, LandWaterStrategy Strategy, double KWhPerKg, double EnergyNokPerKg,
    double GrowthTonnesPerYear, double ProductionIndexKgM3Yr, int CycleDays, double MeanTankC, double LoxPerKg, double MeanCo2MgL,
    double MaturationIndex, double MaturationRisk, bool Active);

/// <summary>A week of the harvest planner: its Monday, ISO week and the price expected.</summary>
public readonly record struct LandHarvestWeek(int WeekStartDay, int IsoWeek, decimal ExpectedPriceNok);

/// <summary>A tank on the harvest planner: its fish, their weight projected into each week, and the size-grade factor there.</summary>
public sealed record LandHarvestTankForecast(string TankId, int Fish, double MeanWeightKg, IReadOnlyList<double> WeightByWeekKg,
    IReadOnlyList<double> SizeFactorByWeek, string HarvestPlanId);

/// <summary>A site's harvest planner (<see cref="SimulationEngine.LandHarvestPlannerOf"/>).</summary>
public sealed record LandHarvestPlannerView(string SiteId, IReadOnlyList<LandHarvestWeek> Weeks, IReadOnlyList<LandHarvestTankForecast> Tanks,
    IReadOnlyList<LandHarvestPlan> Plans, double HallTonnesPerDay);

/// <summary>A tank on the post-smolt planner.</summary>
public sealed record PostSmoltTankView(string TankId, int Fish, double MeanWeightKg, double TargetWeightKg, SmoltQualityTier Tier,
    int SeawaterDays, int LowSalinityDays, bool Desmoltified, bool Vaccinated, decimal SaleValueNok, decimal BoughtValueNok,
    string TransferJobId, int TransferDueDay, string TransferDestinationId);

/// <summary>A fjord site the post-smolt planner can send fish to.</summary>
public sealed record PostSmoltDestinationView(string SeaSiteId, string Label, double MtbTonnes, double BiomassTonnes,
    double PendingTonnes, double HeadroomTonnes);

/// <summary>A site's post-smolt planner (<see cref="SimulationEngine.PostSmoltPlannerOf"/>).</summary>
public sealed record PostSmoltPlannerView(string SiteId, double SalinityPpt, bool HasRiverIntake, double SulphateFactor,
    double SuperSmoltMinWeightKg, double LargeMinWeightKg, double SuperSmoltMinSalinityPpt, int SeawaterRecordDays,
    double DesmoltSalinityPpt, int DesmoltDays, IReadOnlyList<PostSmoltTankView> Tanks,
    IReadOnlyList<PostSmoltDestinationView> Destinations, IReadOnlyList<LandTransferJob> Transfers);
