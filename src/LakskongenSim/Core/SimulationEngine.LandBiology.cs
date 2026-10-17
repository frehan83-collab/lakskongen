using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Data;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Harvest;

namespace LakskongenSim.Core;

/// <summary>
/// Grow-out biology, competence and harvest planning on land (roadmap F2-15,
/// docs/FARM_TYPES_SPEC.md §2A-2C, §3.3 "Growth and competence", §3.4, §6.5):
/// <list type="bullet">
/// <item>Competence (<see cref="CompetenceModel"/>): 0.4 at the start, raised by
/// research, a driftsleder, a training programme, a consultancy, early hiring
/// and completed cycles; it sets the utilisation cap (<c>util_cap</c>, 55 % →
/// 100 %) and the growth on the law (the achieved result at 0.4, the plan at
/// 1.0). A post-smolt site runs at its nameplate and the plan's growth.</item>
/// <item>Each tank's cohort runs against its plan (<see cref="LandCohortPlan"/>):
/// below 85 % of it for 30 days raises <c>GrowthShortfall</c> naming the driver
/// that cost the most (CO2, temperature, the feeding plan, the gills' load,
/// density).</item>
/// <item>The feeding plan (<see cref="SetLandFeedPlan"/>); a change at
/// competence &lt; 0.6 raises the FCR by 0.12 for 60 days.</item>
/// <item>Harvests planned into an ISO week (<see cref="PlanLandHarvest"/>): the
/// S2-04 order goes out so the hall's slot falls on the week's Monday.</item>
/// <item>The running costs beyond energy, oxygen and staff (maintenance,
/// insurance, sludge and the other lines, the levers) and the site's own books
/// (<see cref="RASFacility.Account"/>: the farming cost per kg HOG and its
/// energy share).</item>
/// </list>
/// </summary>
public partial class SimulationEngine
{
    /// <summary>The research whose completion adds to a land site's competence.</summary>
    public const string LandResearchNodeId = "land-based-ras";

    // ── Competence ─────────────────────────────────────────────────────────

    /// <summary>True when competence applies to <paramref name="site"/>: an F2 grow-out site (a post-smolt site runs on the plan).</summary>
    public static bool HasLandCompetence(RASFacility site)
        => site != null && site.HasLots && site.Kind == LandSiteKind.GrowOut;

    /// <summary>True when the company has researched <see cref="LandResearchNodeId"/>.</summary>
    private bool LandResearchDone()
        => Research?.Nodes.TryGetValue(LandResearchNodeId, out var node) == true && node.Unlocked;

    /// <summary>
    /// The competence of a land grow-out site today and what it is made of (a
    /// pure read). A post-smolt or pre-F2 site reads as at the plan (1.0).
    /// </summary>
    public CompetenceBreakdown LandCompetenceOf(RASFacility site)
    {
        var cal = LandCalibration.Competence;
        if (!HasLandCompetence(site))
            return new CompetenceBreakdown(1.0, 0, 0, 0, 0, 0, 0, 1.0);
        return CompetenceModel.Evaluate(site.Competence, State?.Day ?? 0, LandResearchDone(), cal);
    }

    /// <summary>The site's utilisation cap: the share of its nameplate a year its competence runs (1 where competence does not apply).</summary>
    public double LandUtilisationCapOf(RASFacility site)
        => HasLandCompetence(site) ? CompetenceModel.UtilisationCap(LandCompetenceOf(site).Total, LandCalibration.Competence) : 1.0;

    /// <summary>The growth the site's competence gives its fish on the law (1 where competence does not apply).</summary>
    public double LandCompetenceGrowthMultiplierOf(RASFacility site)
        => HasLandCompetence(site) ? CompetenceModel.GrowthMultiplier(LandCompetenceOf(site).Total, LandCalibration.Competence) : 1.0;

    /// <summary>Nameplate production of the site's commissioned lots (t HOG a year, a pure read).</summary>
    public double LandNameplateTonnesNow(RASFacility site)
        => site == null ? 0.0 : site.Lots.Where(IsLotCommissionedNow).Sum(l => LandCalibration.For(l.Template).NameplateTonnesHogPerYear);

    /// <summary>FCR the feed-plan penalty adds today (0 when none runs).</summary>
    internal double LandFcrPenaltyOf(RASFacility site)
        => site != null && State != null && site.Competence.FcrPenaltyUntilDay >= State.Day ? LandCalibration.Growth.FeedPlanFcrPenalty : 0.0;

    /// <summary>
    /// Hire an operations manager (driftsleder) at a land grow-out site
    /// (spec §3.4 <c>HireDriftsleder</c>): +0.1 competence while employed, at
    /// the calibration's yearly cost booked day by day. Fails: not_found,
    /// wrong_site_kind, already_active.
    /// </summary>
    public CommandResult HireDriftsleder(string siteId)
    {
        var (site, refused) = CompetenceSite(siteId);
        if (refused != null) return refused;
        if (site!.Competence.DriftslederSinceDay >= 0)
            return CommandResult.Fail(LandSiteReasons.AlreadyActive, $"{site.Name} already has a driftsleder");
        site.Competence.DriftslederSinceDay = State.Day;
        LogFacilityEvent("Driftsleder ansatt", SimText.Nb($"{site.Name} har ansatt en driftsleder"), 0m);
        return CommandResult.Success($"{site.Name} hired a driftsleder", (double)LandCalibration.Competence.DriftslederNokPerYear, id: site.Id);
    }

    /// <summary>
    /// Start a training programme at a land grow-out site (spec §3.4
    /// <c>StartTrainingProgramme</c>): +0.05 competence every 6 months, up to
    /// +0.15, for its one-off cost. One programme a site. Fails: not_found,
    /// wrong_site_kind, already_active, insufficient_funds.
    /// </summary>
    public CommandResult StartTrainingProgramme(string siteId)
    {
        var (site, refused) = CompetenceSite(siteId);
        if (refused != null) return refused;
        if (site!.Competence.TrainingStartDay >= 0)
            return CommandResult.Fail(LandSiteReasons.AlreadyActive, $"{site.Name} has run its training programme");
        decimal cost = LandCalibration.Competence.TrainingNok;
        if (!TryPayCost(cost, CostBooking.Opex, "land_competence"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"Need {cost:N0} NOK", (double)cost);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, cost);
        site.Account.CompetenceNok += cost;
        site.Competence.TrainingStartDay = State.Day;
        LogFacilityEvent("Opplæringsprogram startet", SimText.Nb($"{site.Name} har startet et opplæringsprogram"), -cost);
        return CommandResult.Success($"{site.Name} started its training programme", (double)cost, id: site.Id);
    }

    /// <summary>
    /// Hire an external consultancy at a land grow-out site (spec §3.4
    /// <c>HireConsultancy</c>): +0.1 competence for 12 months, for its one-off
    /// cost. Fails: not_found, wrong_site_kind, already_active (one runs),
    /// insufficient_funds.
    /// </summary>
    public CommandResult HireConsultancy(string siteId)
    {
        var (site, refused) = CompetenceSite(siteId);
        if (refused != null) return refused;
        var cal = LandCalibration.Competence;
        if (site!.Competence.ConsultancyStartDay >= 0 && State.Day < site.Competence.ConsultancyStartDay + cal.ConsultancyDays)
            return CommandResult.Fail(LandSiteReasons.AlreadyActive, $"{site.Name} has a consultancy on site");
        if (!TryPayCost(cal.ConsultancyNok, CostBooking.Opex, "land_competence"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"Need {cal.ConsultancyNok:N0} NOK", (double)cal.ConsultancyNok);
        AttributeUnitCost(site.Id, HoldingKind.LandTank, cal.ConsultancyNok);
        site.Account.CompetenceNok += cal.ConsultancyNok;
        site.Competence.ConsultancyStartDay = State.Day;
        LogFacilityEvent("Konsulenter engasjert", SimText.Nb($"{site.Name} har engasjert eksterne konsulenter i {cal.ConsultancyDays} dager"), -cal.ConsultancyNok);
        return CommandResult.Success($"{site.Name} hired a consultancy", (double)cal.ConsultancyNok, id: site.Id);
    }

    /// <summary>
    /// Hire a land grow-out site's operating staff before its first fish (spec
    /// §3.3 "hiring staff ≥ 180 d before first fish": +0.1 competence when the
    /// first fish come at least 180 days later). Their wages run from today for
    /// every lot, built or not. Fails: not_found, wrong_site_kind,
    /// already_active (hired), not_ready (the site already holds fish).
    /// </summary>
    public CommandResult HireLandStaffEarly(string siteId)
    {
        var (site, refused) = CompetenceSite(siteId);
        if (refused != null) return refused;
        if (site!.Competence.StaffHiredDay >= 0)
            return CommandResult.Fail(LandSiteReasons.AlreadyActive, $"{site.Name}'s staff are already hired");
        if (site.Competence.FirstFishDay >= 0)
            return CommandResult.Fail(LandSiteReasons.NotReady, $"{site.Name} already holds fish");
        site.Competence.StaffHiredDay = State.Day;
        LogFacilityEvent("Driftsmannskap ansatt", SimText.Nb($"{site.Name} har ansatt driftsmannskapet før første utsett"), 0m);
        return CommandResult.Success($"{site.Name} hired its staff early", id: site.Id);
    }

    private (RASFacility? Site, CommandResult? Refused) CompetenceSite(string siteId)
    {
        if (State == null || FinanceManager == null)
            return (null, CommandResult.Fail("engine_not_ready", "Engine not initialized"));
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return (null, CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'"));
        if (!HasLandCompetence(site))
            return (null, CommandResult.Fail(LandSiteReasons.WrongSiteKind, $"'{site.Name}' is a post-smolt site: it runs on the plan"));
        return (site, null);
    }

    // ── The feeding plan ───────────────────────────────────────────────────

    /// <summary>Lowest share of the appetite a land feeding plan may give (as U2-04's sea plans: 50 %).</summary>
    public const double MinLandRationPct = 50.0;

    /// <summary>
    /// Set a land site's feeding plan (spec §3.3, F2-15): every tank fed
    /// <paramref name="rationPct"/> % of the appetite from tomorrow. A change at
    /// competence under 0.6 raises the FCR by 0.12 for 60 days (the 2025 feed
    /// change of [L:R2]). Fails: not_found, out_of_range (50-100 %).
    /// </summary>
    public CommandResult SetLandFeedPlan(string siteId, double rationPct)
    {
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        if (!(rationPct >= MinLandRationPct && rationPct <= 100.0))
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Ration {MinLandRationPct:F0}-100 % of the appetite");
        double fraction = rationPct / 100.0;
        if (Math.Abs(fraction - site.FeedRationFraction) < 1e-12)
            return CommandResult.Success($"{site.Name} already feeds {rationPct:F0} %", rationPct, id: site.Id);
        site.FeedRationFraction = fraction;
        foreach (var population in site.Tanks)
            population.Ration = RationPolicy.OfAppetite(fraction);
        var growth = LandCalibration.Growth;
        site.Competence.FeedPlanChangedDay = State.Day;
        double competence = LandCompetenceOf(site).Total;
        bool penalty = competence < growth.FeedPlanPenaltyBelowCompetence;
        if (penalty)
            site.Competence.FcrPenaltyUntilDay = State.Day + growth.FeedPlanPenaltyDays - 1;
        LogFacilityEvent("Fôringsplan endret", SimText.Nb($"{site.Name} fôrer nå {rationPct:F0} % av appetitten"), 0m);
        return CommandResult.Success(penalty
                ? $"{site.Name} feeds {rationPct:F0} %; at competence {competence:F2} the change raises the FCR by {growth.FeedPlanFcrPenalty:F2} for {growth.FeedPlanPenaltyDays} days"
                : $"{site.Name} feeds {rationPct:F0} %",
            rationPct, penalty ? growth.FeedPlanFcrPenalty : 0.0, site.Id);
    }

    // ── Stocking: the fish's record and plan ───────────────────────────────

    /// <summary>
    /// A tank was stocked (<see cref="StockLandTank"/>): its fish's record
    /// starts (strain, day, an empty salinity record), its plan starts at the
    /// setpoint and the site's competence, the site's feeding plan feeds it,
    /// and the site's first fish day is set.
    /// </summary>
    private void NoteLandStocking(RASFacility site, LandTank tank, FishPopulation population, SmoltStrain strain)
    {
        tank.ClearFishRecord();
        tank.StrainId = strain.Id;
        tank.StockedDay = State.Day;
        var cohort = population.Cohorts[0];
        double cv = cohort.WeightCv > 0.0 ? cohort.WeightCv : Growth.Law.InitialWeightCv(cohort.MeanWeightKg);
        tank.Plan = LandCohortPlan.Start(State.Day, cohort.MeanWeightKg, cv, site.TargetWaterTemp,
            (cohort.Genetics?.GetGrowthMultiplier() ?? 1.0) * LandCompetenceGrowthMultiplierOf(site));
        if (site.FeedRationFraction < 1.0)
            population.Ration = RationPolicy.OfAppetite(site.FeedRationFraction);
        if (site.Competence.FirstFishDay < 0)
            site.Competence.FirstFishDay = State.Day;
    }

    // ── The day ────────────────────────────────────────────────────────────

    /// <summary>
    /// The land lane's close of a site's day (the end of <see cref="TickLandSiteDay"/>,
    /// after the water and the energy): the salinity record, the site's books
    /// (feed, the running costs, the harvests), each cohort against its plan
    /// and the growth-shortfall warning, the competence's cycles, the planned
    /// harvests' orders and the transfers to sea whose boat comes today.
    /// Draws nothing.
    /// </summary>
    internal void CloseLandSiteDay(RASFacility site)
    {
        NoteLandSalinityDay(site);
        double feedKg = BookLandFeed(site);
        BookLandRunningCosts(site, feedKg);
        CountLandHarvests(site);
        var shortfall = TrackCohortPlans(site);
        RunLandHarvestPlans(site);
        RunDueLandTransfers(site);
        site.Account.Day = State.Day;
        if (SimCalendar.IsLastDayOfYear(State.Day))
        {
            site.LastYear = site.Account.Minus(site.AccountAtYearStart);
            site.AccountAtYearStart = site.Account.Clone();
        }
        if (shortfall != null) PublishGrowthShortfall(site, shortfall.Value);
    }

    /// <summary>The feed the biology bought for the site's fish today, into its books. Returns the kg delivered.</summary>
    private double BookLandFeed(RASFacility site)
    {
        double kg = 0.0;
        decimal nok = 0m;
        foreach (var population in site.Tanks)
        {
            var (deliveredKg, costNok) = LandFeedBoughtToday(population);
            kg += deliveredKg;
            nok += costNok;
        }
        site.Account.FeedKg += kg;
        site.Account.FeedNok += nok;
        return kg;
    }

    /// <summary>
    /// The running costs beyond energy, oxygen and staff (Bjørndal &amp; Tusvik
    /// 2017 in 2025 NOK, <c>facilities.land.costs</c>): maintenance and the
    /// plant's insurance on the commissioned units' capex, fish insurance and
    /// sludge on the day's feed, laboratory, chemicals and office on the
    /// nameplate, the driftsleder's wages, and early-hired staff on lots not
    /// yet built. Each line is attributed to the site (out of the rent base).
    /// </summary>
    private void BookLandRunningCosts(RASFacility site, double feedKg)
    {
        var cal = LandCalibration;
        var costs = cal.Costs;
        decimal capex = 0m, nameplate = 0m;
        int unbuiltStaff = 0;
        foreach (var lot in site.Lots)
        {
            if (lot.IsCommissioned)
            {
                capex += LandUnitCapexNok(lot.Template);
                nameplate += (decimal)cal.For(lot.Template).NameplateTonnesHogPerYear;
            }
            else
            {
                unbuiltStaff += cal.For(lot.Template).Staff;
            }
        }
        decimal perDay = 1m / SimCalendar.DaysPerYear;
        decimal maintenance = capex * (decimal)costs.MaintenanceShareOfCapexPerYear * perDay;
        decimal insurance = capex * (decimal)costs.EquipmentInsuranceShareOfCapexPerYear * perDay
            + (decimal)feedKg * costs.FishInsuranceNokPerKgFeed;
        decimal other = nameplate * costs.OtherNokPerTonneNameplateYear * perDay + (decimal)feedKg * costs.SludgeNokPerKgFeed;
        decimal competence = site.Competence.DriftslederSinceDay >= 0 && HasLandCompetence(site)
            ? cal.Competence.DriftslederNokPerYear * perDay
            : 0m;
        decimal earlyStaff = site.Competence.StaffHiredDay >= 0 && unbuiltStaff > 0
            ? unbuiltStaff * cal.Operations.StaffCostNokPerFteYear * perDay
            : 0m;
        if (maintenance > 0m) PayCost(maintenance, CostBooking.Opex, "land_maintenance");
        if (insurance > 0m) PayCost(insurance, CostBooking.Opex, "land_insurance");
        if (other > 0m) PayCost(other, CostBooking.Opex, "land_other");
        if (competence > 0m) PayCost(competence, CostBooking.Opex, "land_competence");
        if (earlyStaff > 0m) PayCost(earlyStaff, CostBooking.Opex, "land_staff");
        AttributeUnitCost(site.Id, HoldingKind.LandTank, maintenance + insurance + other + competence + earlyStaff); // F2-04 ring-fence
        site.Account.MaintenanceNok += maintenance;
        site.Account.InsuranceNok += insurance;
        site.Account.OtherNok += other;
        site.Account.CompetenceNok += competence;
        site.Account.StaffNok += earlyStaff;
    }

    /// <summary>The budget of one unit of <paramref name="template"/> (NOK, F2-14's capex, before the pad's blasting).</summary>
    public decimal LandUnitCapexNok(LandTemplate template)
    {
        var build = LandCalibration.Build;
        return template switch
        {
            LandTemplate.HfsLot => build.HfsLotCapexNok,
            LandTemplate.HfsPilotLot => build.HfsPilotCapexNok,
            LandTemplate.RasHall => build.RasHallCapexNok,
            LandTemplate.PostSmoltModule => build.PostSmoltModuleCapexNok,
            _ => 0m,
        };
    }

    /// <summary>
    /// The site's harvests today (the S2-04 reports dated today) into its
    /// books, and the competence's completed cycles: one each time the site
    /// has harvested another full rotation of its grow-out tanks' planned fish.
    /// </summary>
    private void CountLandHarvests(RASFacility site)
    {
        long fish = 0;
        foreach (var report in State.HarvestReports)
        {
            if (report.Day != State.Day || report.HoldingId != site.Id) continue;
            site.Account.HarvestedFish += report.Fish;
            site.Account.HarvestedHogKg += report.HogKg;
            site.Account.HarvestedLiveKg += report.LiveKg;
            site.Account.HarvestRevenueNok += report.GrossRevenueNok;
            site.Account.HarvestNok += report.HarvestCostNok;
            fish += report.Fish;
        }
        if (fish <= 0 || !HasLandCompetence(site)) return;
        site.Competence.HarvestedFish += fish;
        long rotation = LandRotationFish(site);
        if (rotation <= 0) return;
        int cycles = (int)Math.Min(int.MaxValue, site.Competence.HarvestedFish / rotation);
        if (cycles > site.Competence.CompletedCycles)
        {
            site.Competence.CompletedCycles = cycles;
            LogFacilityEvent("Produksjonssyklus fullført",
                SimText.Nb($"{site.Name} har fullført {cycles} produksjonssyklus(er): kompetansen er nå {LandCompetenceOf(site).Total:F2}"), 0m);
        }
    }

    /// <summary>
    /// Fish one rotation of the site's commissioned grow-out tanks harvests on
    /// the plan: each tank's room for the plan's peak, × the plan's survival.
    /// </summary>
    public long LandRotationFish(RASFacility site)
    {
        var cal = LandCalibration;
        long fish = 0;
        foreach (var lot in site.Lots.Where(IsLotCommissionedNow))
        {
            var t = cal.For(lot.Template);
            var forecast = LandStockingPlanner.Project(Growth.Law, LandStockingPlan.For(t, cal.Production) with
            {
                TemperatureC = site.TargetWaterTemp,
            });
            foreach (var tank in lot.Tanks.Where(k => k.Role == TankRole.GrowOut))
                fish += (long)Math.Round(forecast.MaxSmolts(tank.VolumeM3, t.PlanPeakDensityKgM3) * cal.Production.CycleSurvival);
        }
        return fish;
    }

    /// <summary>A growth shortfall to report: the worst tank's share of plan, its driver and the tanks behind.</summary>
    private readonly record struct LandShortfall(string TankId, double ShareOfPlan, string Driver, int TanksBehind);

    /// <summary>
    /// Each stocked tank's cohort against its plan: the plan grows a day; the
    /// day's losses to CO2, temperature, the feeding plan, the gills' load and
    /// density go into the drivers' running means; a cohort below 85 % of the
    /// plan counts its days. Returns the site's shortfall when one has lasted
    /// 30 days (the worst tank), else null.
    /// </summary>
    private LandShortfall? TrackCohortPlans(RASFacility site)
    {
        var cal = LandCalibration;
        var law = Growth.Law;
        LandShortfall? worst = null;
        int behind = 0;
        foreach (var lot in site.Lots)
        {
            foreach (var tank in lot.Tanks)
            {
                var fish = site.PopulationIn(tank);
                if (fish == null || fish.TotalCount <= 0 || tank.Plan == null) continue;
                var plan = tank.Plan;
                plan.AdvancePlan(law);
                double density = tank.VolumeM3 > 0.0 ? fish.GetTotalBiomassKg() / tank.VolumeM3 : 0.0;
                double saturation = WaterQuality.CalculateDOSaturation(tank.DoMgL, tank.TemperatureC, LandSalinityOf(site));
                double gill = LandWaterModel.TanAppetiteFactor(tank.TanMgL, cal.Tan) * WaterQuality.CalculateDOSaturationModifier(saturation);
                double planThermal = law.RelativeGrowthAt(plan.TemperatureC);
                double thermal = planThermal > 0.0 ? law.RelativeGrowthAt(tank.TemperatureC) / planThermal : 1.0;
                double ration = fish.Ration?.EffectiveFraction ?? 1.0;
                plan.NoteLosses(
                    co2: 1.0 - LandWaterModel.Co2AppetiteFactor(tank.Co2MgL, cal.Co2),
                    temperature: 1.0 - thermal,
                    feedPlan: 1.0 - ration,
                    gill: 1.0 - gill,
                    density: 1.0 - LandWaterModel.DensityGrowthMultiplier(density, cal.Density));
                double share = plan.ShareOfPlan(fish.GetTotalBiomassKg() / fish.TotalCount);
                if (!plan.NoteStanding(fish.GetTotalBiomassKg() / fish.TotalCount, cal.Growth)) continue;
                behind++;
                if (worst == null || share < worst.Value.ShareOfPlan)
                    worst = new LandShortfall(tank.Id, share, plan.Driver(), 0);
            }
        }
        return worst == null ? null : worst.Value with { TanksBehind = behind };
    }

    /// <summary>
    /// Tell the player a cohort has stood below 85 % of its plan for 30 days
    /// (spec §3.5 GrowthShortfall, warning, counter verb build_competence),
    /// naming the driver that cost it the most. Reported every day it holds:
    /// the bus keeps it one notification per site and driver.
    /// </summary>
    private void PublishGrowthShortfall(RASFacility site, LandShortfall shortfall)
    {
        PublishGameEvent(new GameEventRequest(GameEventType.GrowthShortfall, State.Day)
        {
            UnitId = site.Id,
            Variant = shortfall.Driver,
            Value = shortfall.ShareOfPlan,
            Message = SimText.Nb($"Tilvekstsvikt på {site.Name}: {shortfall.TanksBehind} kar under {LandCalibration.Growth.ShortfallShare:P0} av planen i {LandCalibration.Growth.ShortfallDays} dager, verst {shortfall.TankId} med {shortfall.ShareOfPlan:P0} av planvekten. Årsak: {DriverName(shortfall.Driver)}."),
        });
    }

    /// <summary>Norwegian name of a growth-shortfall driver for messages.</summary>
    internal static string DriverName(string driver) => driver switch
    {
        GrowthShortfallDrivers.Co2 => "CO₂ i karet",
        GrowthShortfallDrivers.Temperature => "vanntemperaturen",
        GrowthShortfallDrivers.FeedPlan => "fôringsplanen",
        GrowthShortfallDrivers.GillHealth => "gjellehelse (ammoniakk og oksygen)",
        GrowthShortfallDrivers.Density => "tettheten",
        _ => driver,
    };

    // ── Harvest planning ───────────────────────────────────────────────────

    /// <summary>Sim day of the Monday of <paramref name="day"/>'s ISO week.</summary>
    public static int IsoWeekStartDay(int day) => day - (SimCalendar.IsoWeekday(day) - 1);

    /// <summary>
    /// What harvesting <paramref name="tankIds"/> in the ISO week of
    /// <paramref name="weekStartDay"/> would bring (a pure read on U2-05's
    /// projection): the order goes out so the hall's slot falls on the week's
    /// Monday; the fish slaughtered within the hall's daily capacity behind the
    /// harvests already ordered or planned, at the price the market is
    /// expected to pay that week, with the S2-04 size and grade factors.
    /// </summary>
    public LandHarvestPlanQuote QuoteLandHarvestPlan(string siteId, IReadOnlyCollection<string> tankIds, int weekStartDay)
    {
        if (State == null)
            return LandHarvestPlanQuote.Refused("engine_not_ready", "Engine not initialized");
        var site = FindLandSite(siteId);
        if (site == null || !site.HasLots)
            return LandHarvestPlanQuote.Refused(LandSiteReasons.NotFound, $"No land site with tanks '{siteId}'");
        if (tankIds == null || tankIds.Count == 0)
            return LandHarvestPlanQuote.Refused(LandSiteReasons.OutOfRange, "Choose at least one tank");
        var populations = new List<string>();
        var tanks = new List<string>();
        foreach (var id in tankIds.Distinct())
        {
            var tank = site.FindTank(id);
            if (tank == null)
                return LandHarvestPlanQuote.Refused(LandSiteReasons.TankNotFound, $"No tank '{id}' at '{site.Name}'");
            var fish = site.PopulationIn(tank);
            if (fish == null || fish.TotalCount <= 0)
                return LandHarvestPlanQuote.Refused("holding_empty", $"{tank.Id} holds no fish");
            if (site.HarvestPlans.Any(p => p.Status is LandHarvestPlanStatus.Planned or LandHarvestPlanStatus.Ordered && p.TankIds.Contains(tank.Id)))
                return LandHarvestPlanQuote.Refused(LandSiteReasons.AlreadyPlanned, $"{tank.Id} is in another harvest plan");
            populations.Add(fish.Id);
            tanks.Add(tank.Id);
        }
        int monday = IsoWeekStartDay(weekStartDay);
        int week = SimCalendar.IsoWeek(monday);
        int lastDay = monday + SimCalendar.DaysPerWeek - 1;
        var now = QuoteHarvest(site.Id, populations, 1.0);
        if (!now.Ok)
            return LandHarvestPlanQuote.Refused(now.ErrorKey, now.Message);
        int wait = monday - now.FastingDays - State.Day;
        int horizon = LandCalibration.Growth.HarvestPlanWeeks * SimCalendar.DaysPerWeek;
        if (wait < 0 || monday - State.Day > horizon || wait > MaxHarvestWaitDays)
            return LandHarvestPlanQuote.Refused(LandSiteReasons.NoSlot,
                $"Week {week} starts on day {monday}: a plan needs {now.FastingDays} days of fasting ahead and reaches {LandCalibration.Growth.HarvestPlanWeeks} weeks")
                with { WeekStartDay = monday, IsoWeek = week, TankIds = tanks };
        var projection = ProjectHarvest(site.Id, populations, 1.0, wait);
        if (!projection.Ok)
            return LandHarvestPlanQuote.Refused(projection.ErrorKey, projection.Message) with { WeekStartDay = monday, IsoWeek = week, TankIds = tanks };
        double hallTonnes = HarvestRoute.For(HoldingKind.LandTank, HarvestCalibration, site.Id).DailyCapacityTonnesHog;
        double otherPlans = PlannedHarvestHogKgInWeek(site, monday);
        var quote = new LandHarvestPlanQuote(true, "", "", site.Id, tanks, monday, week, projection.SlotDay,
            projection.FirstSlaughterDay, projection.LastSlaughterDay, projection.Fish, projection.HogKg, projection.MeanLiveWeightKg,
            projection.SizeFactor, projection.GradeFactor, projection.ExpectedReferencePriceNok, projection.GrossRevenueNok,
            projection.NetAfterFeedNok, hallTonnes, otherPlans);
        if (projection.LastSlaughterDay > lastDay || (projection.HogKg + otherPlans) / 1000.0 > hallTonnes * (lastDay - projection.SlotDay + 1) + 1e-9)
            return quote with { Allowed = false, Reason = LandSiteReasons.NoSlot, Message =
                $"The hall slaughters {hallTonnes:N0} t HOG a day: {projection.HogKg / 1000.0:N0} t (and {otherPlans / 1000.0:N0} t planned) do not fit week {week}" };
        return quote with { Message = $"{projection.Fish:N0} fish, {projection.HogKg / 1000.0:N0} t HOG in week {week}" };
    }

    /// <summary>Gutted kilos the site's other plans and orders are expected to send through the hall in the week of <paramref name="monday"/>.</summary>
    private double PlannedHarvestHogKgInWeek(RASFacility site, int monday)
    {
        double kg = 0.0;
        foreach (var plan in site.HarvestPlans)
        {
            if (plan.WeekStartDay != monday || plan.Status != LandHarvestPlanStatus.Planned) continue;
            foreach (var id in plan.TankIds)
            {
                var fish = site.FindTank(id) is { } tank ? site.PopulationIn(tank) : null;
                if (fish != null) kg += fish.GetTotalBiomassKg() * HarvestCalibration.HogYieldFraction;
            }
        }
        return kg;
    }

    /// <summary>
    /// Plan a tank group's harvest into an ISO week (spec §3.4 <c>PlanHarvest</c>,
    /// §6.5 "Du bestemmer når du selger"): the land tick orders it (S2-04) so
    /// the hall's slot falls on the week's Monday, and the fish sell that week.
    /// Fails as <see cref="QuoteLandHarvestPlan"/>: not_found, tank_not_found,
    /// holding_empty, already_planned, out_of_range, no_slot (past, too close
    /// for the fast, too far ahead, or the hall cannot take it that week).
    /// Id = the plan.
    /// </summary>
    public CommandResult PlanLandHarvest(string siteId, IReadOnlyCollection<string> tankIds, int weekStartDay)
    {
        var quote = QuoteLandHarvestPlan(siteId, tankIds, weekStartDay);
        if (!quote.Allowed)
            return CommandResult.Fail(quote.Reason, quote.Message);
        var site = FindLandSite(siteId)!;
        site.HarvestPlansMade++;
        var plan = new LandHarvestPlan
        {
            Id = $"{site.Id}/hp{site.HarvestPlansMade}",
            TankIds = quote.TankIds.ToList(),
            WeekStartDay = quote.WeekStartDay,
            IsoWeek = quote.IsoWeek,
            PlannedOnDay = State.Day,
        };
        site.HarvestPlans.Add(plan);
        LogFacilityEvent("Slakting planlagt",
            SimText.Nb($"{site.Name}: {string.Join(", ", plan.TankIds)} slaktes i uke {plan.IsoWeek} (ca. {quote.HogKg / 1000.0:N0} tonn sløyd)"), 0m);
        return CommandResult.Success($"Harvest of {plan.TankIds.Count} tanks planned in week {plan.IsoWeek}", quote.HogKg,
            (double)quote.GrossRevenueNok, plan.Id);
    }

    /// <summary>Cancel a harvest plan whose order is not placed yet. Fails: not_found, not_ready.</summary>
    public CommandResult CancelLandHarvestPlan(string siteId, string planId)
    {
        var site = FindLandSite(siteId);
        var plan = site?.HarvestPlans.FirstOrDefault(p => p.Id == planId);
        if (site == null || plan == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No harvest plan '{planId}'");
        if (plan.Status != LandHarvestPlanStatus.Planned)
            return CommandResult.Fail(LandSiteReasons.NotReady, $"{plan.Id} is {plan.Status}");
        plan.Status = LandHarvestPlanStatus.Cancelled;
        return CommandResult.Success($"{plan.Id} cancelled", id: plan.Id);
    }

    /// <summary>
    /// The site's planned harvests (the land tick): a plan whose fast must
    /// begin today places its S2-04 order (the slot on the week's Monday); an
    /// order that completed marks its plan done. Old finished plans are pruned.
    /// </summary>
    private void RunLandHarvestPlans(RASFacility site)
    {
        foreach (var plan in site.HarvestPlans)
        {
            if (plan.Status == LandHarvestPlanStatus.Ordered)
            {
                var order = State.HarvestOrders.FirstOrDefault(o => o.Id == plan.OrderId);
                if (order == null || !order.IsOpen) plan.Status = LandHarvestPlanStatus.Done;
                continue;
            }
            if (plan.Status != LandHarvestPlanStatus.Planned) continue;
            var populations = plan.TankIds.Select(id => site.FindTank(id) is { } t ? site.PopulationIn(t) : null)
                .Where(p => p != null && p.TotalCount > 0).Select(p => p!.Id).ToList();
            if (populations.Count == 0)
            {
                plan.Status = LandHarvestPlanStatus.Failed;
                plan.FailureKey = "holding_empty";
                continue;
            }
            var quote = QuoteHarvest(site.Id, populations, 1.0);
            if (quote.Ok && State.Day + quote.FastingDays < plan.WeekStartDay) continue; // not yet: the fast begins later
            var ordered = OrderHarvest(site.Id, populations, 1.0);
            if (ordered.Ok)
            {
                plan.Status = LandHarvestPlanStatus.Ordered;
                plan.OrderId = ordered.Id;
                plan.OrderedDay = State.Day;
            }
            else
            {
                plan.Status = LandHarvestPlanStatus.Failed;
                plan.FailureKey = ordered.ErrorKey ?? "";
                LogFacilityEvent("Planlagt slakting avlyst", SimText.Nb($"{site.Name}: {plan.Id} kunne ikke bestilles ({ordered.Message})"), 0m);
            }
        }
        site.HarvestPlans.RemoveAll(p => p.Status is LandHarvestPlanStatus.Done or LandHarvestPlanStatus.Cancelled or LandHarvestPlanStatus.Failed
            && p.WeekStartDay + 2 * SimCalendar.DaysPerYear / 12 < State.Day);
    }
}

/// <summary>What harvesting a tank group in a week would bring (<see cref="SimulationEngine.QuoteLandHarvestPlan"/>).</summary>
/// <param name="Allowed">True when the plan may be made.</param>
/// <param name="Reason">Refusal key, "" when allowed.</param>
/// <param name="Message">Why, for logs.</param>
/// <param name="SiteId">The land site.</param>
/// <param name="TankIds">The tanks.</param>
/// <param name="WeekStartDay">Sim day of the week's Monday.</param>
/// <param name="IsoWeek">ISO week number.</param>
/// <param name="SlotDay">The hall's slot (the Monday when the order goes out on time).</param>
/// <param name="FirstSlaughterDay">First slaughter day.</param>
/// <param name="LastSlaughterDay">Last slaughter day.</param>
/// <param name="Fish">Fish slaughtered.</param>
/// <param name="HogKg">Gutted kilos sold.</param>
/// <param name="MeanLiveWeightKg">Mean live weight at slaughter (kg).</param>
/// <param name="SizeFactor">The S2-04 size-class factor (fish under 3 kg HOG sell at a discount).</param>
/// <param name="GradeFactor">The S2-04 grade factor.</param>
/// <param name="ExpectedReferencePriceNok">The reference price expected that week (NOK/kg, the seasonal curve).</param>
/// <param name="GrossRevenueNok">Expected gross sale.</param>
/// <param name="NetRevenueNok">Expected sale less the harvest cost and the feed until the fast.</param>
/// <param name="HallTonnesPerDay">The hall's daily capacity (t HOG).</param>
/// <param name="OtherPlannedHogKg">Gutted kilos other plans send through the hall that week.</param>
public sealed record LandHarvestPlanQuote(bool Allowed, string Reason, string Message, string SiteId, IReadOnlyList<string> TankIds,
    int WeekStartDay, int IsoWeek, int SlotDay, int FirstSlaughterDay, int LastSlaughterDay, int Fish, double HogKg, double MeanLiveWeightKg,
    double SizeFactor, double GradeFactor, decimal ExpectedReferencePriceNok, decimal GrossRevenueNok, decimal NetRevenueNok,
    double HallTonnesPerDay, double OtherPlannedHogKg)
{
    /// <summary>A refusal.</summary>
    public static LandHarvestPlanQuote Refused(string reason, string message)
        => new(false, reason, message, "", Array.Empty<string>(), 0, 0, 0, 0, 0, 0, 0.0, 0.0, 1.0, 1.0, 0m, 0m, 0m, 0.0, 0.0);
}
