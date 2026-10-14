using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Capacity v1 (roadmap S2-01a): licences and MTB.
/// <list type="bullet">
/// <item><b>Licences.</b> A licence holds 780 t of MTB, 945 t in Troms and
/// Finnmark (S2-02's area data). The company's licences in one production
/// area form one biomass cap its sea sites there share
/// (akvakulturdriftsforskriften § 47 second paragraph).</item>
/// <item><b>Localities.</b> Each site is held to its own locality MTB
/// (<see cref="SeaSite.MABTonnes"/>, § 47 third paragraph). Building a site
/// clears a locality and adds 0 t of licence MTB.</item>
/// <item><b>Stocking</b> is checked against the production plan's projected
/// peak (<see cref="CapacityPlanner"/>), for the area and for the site.</item>
/// <item><b>Daily enforcement</b> (post-tick, <see cref="EnforceCapacity"/>):
/// biomass over a cap opens a reduction order; every day the illegal biomass
/// grows past its peak, the infringement fee and the confiscation of the gain
/// are booked on the new kilos; at the deadline the regulator harvests the
/// excess at the company's cost.</item>
/// </list>
/// The rules run once the game has a licence register
/// (<see cref="GameState.CapacityRules"/> = <see cref="CapacityRegime.LicencesV1"/>):
/// a scenario start, or a save from before S2-01 migrated on load. Land-based
/// units (RAS, post-smolt) hold a land permit and are outside MTB.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Closed breach episodes kept in the save (the open ones are always kept).</summary>
    internal const int ClosedBreachHistory = 24;

    private const double KgPerTonne = 1000.0;

    /// <summary>True while the game runs under capacity v1 (a licence register exists).</summary>
    public bool CapacityRulesActive => State?.CapacityRules == CapacityRegime.LicencesV1;

    /// <summary>The company's sea licences.</summary>
    public IReadOnlyList<LicenceHolding> LicenceHoldings
        => State?.LicenceHoldings ?? (IReadOnlyList<LicenceHolding>)Array.Empty<LicenceHolding>();

    /// <summary>The biomass cap (t) the company's licences give in a production area.</summary>
    public double AreaLicenceMtbTonnes(int productionArea)
        => State == null ? 0.0 : LicenceRegister.PooledMtbTonnes(State.LicenceHoldings, productionArea);

    /// <summary>Biomass (t) standing today in the company's sea sites in a production area.</summary>
    public double AreaStandingBiomassTonnes(int productionArea)
        => SeaSitesInArea(productionArea).Sum(s => s.CurrentBiomassTonnes);

    /// <summary>Sea sites of the company that lie in a production area, by id.</summary>
    internal List<SeaSite> SeaSitesInArea(int productionArea)
    {
        var sites = new List<SeaSite>();
        if (State?.SeaSites == null) return sites;
        foreach (var site in State.SeaSites)
            if (ProductionAreaOfSite(site) == productionArea)
                sites.Add(site);
        sites.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return sites;
    }

    /// <summary>The open reduction order in a production area, or null.</summary>
    public CapacityBreach? OpenCapacityBreach(int productionArea)
        => State?.CapacityBreaches?.FirstOrDefault(b => b.IsOpen && b.Area == productionArea);

    // ─── The register ──────────────────────────────────────────────────────

    /// <summary>
    /// Add one licence to <paramref name="state"/>'s register (deterministic id
    /// from the state's grant counter). Used by scenario starts, auction wins
    /// and acquisitions; never by a site build.
    /// </summary>
    internal static LicenceHolding GrantLicence(GameState state, int productionArea, double mtbTonnes,
        string registeredSiteId, string origin, decimal pricePaidNok = 0m)
    {
        state.LicencesGranted++;
        return LicenceRegister.Grant(state.LicenceHoldings, state.LicencesGranted, productionArea,
            mtbTonnes, registeredSiteId, origin, state.Day, pricePaidNok);
    }

    /// <summary>
    /// A save written before S2-01 (<see cref="GameState.CapacityRules"/> null)
    /// held one licence per site, its MAB: give each site that licence in its
    /// production area and open the register, so the old capacity carries over
    /// and the new rules apply from the first day.
    /// </summary>
    internal void MigrateCapacityRegisterAfterLoad()
    {
        if (State == null || State.CapacityRules.HasValue) return;
        foreach (var site in State.SeaSites.OrderBy(s => s.Id, StringComparer.Ordinal))
            GrantLicence(State, ProductionAreaOfSite(site), site.MABTonnes, site.Id, LicenceOrigin.LegacySave);
        State.CapacityRules = CapacityRegime.LicencesV1;
    }

    // ─── The production plan ───────────────────────────────────────────────

    /// <summary>The farmer's plan the stocking check projects (calibration, S2-01a).</summary>
    private CapacityPlanSettings PlanSettings()
        => new(_calibration.Capacity.PlanningThermalGrowthCoefficient,
            _calibration.Biology.TargetHarvestWeightKg,
            CapacityPlanner.DailySurvivalFor(_calibration.Health.SeaMortalityPerCycle, _calibration.Biology.GrowthToHarvestDays),
            _calibration.Capacity.PlanningHorizonDays);

    /// <summary>Climatological water temperature the plan grows fish in.</summary>
    private static double PlanTemperatureC(int productionArea, int day)
        => ProductionAreas.ClimatologyC(
            ProductionAreas.HasClimatology(productionArea) ? productionArea : ProductionAreas.NationalReference, day);

    /// <summary>Every live cohort in the company's sea sites in a production area, as plan input.</summary>
    private List<PlannedCohort> PlannedCohortsInArea(int productionArea)
    {
        var cohorts = new List<PlannedCohort>();
        foreach (var site in SeaSitesInArea(productionArea))
            foreach (var pen in site.Pens)
                foreach (var cohort in pen.Cohorts)
                    if (cohort.Count > 0)
                        cohorts.Add(new PlannedCohort(site.Id, productionArea, cohort.Count, cohort.MeanWeightKg));
        return cohorts;
    }

    /// <summary>The plan's peak for a production area, with an optional new cohort.</summary>
    private CapacityProjection ProjectArea(int productionArea, PlannedCohort? extra = null)
    {
        var cohorts = PlannedCohortsInArea(productionArea);
        if (extra.HasValue) cohorts.Add(extra.Value);
        return CapacityPlanner.Project(cohorts, State.Day, PlanSettings(), PlanTemperatureC);
    }

    // ─── Stocking ──────────────────────────────────────────────────────────

    /// <summary>
    /// The capacity check every stocking runs (smolts, a post-smolt transfer):
    /// under capacity v1, no reduction order may run in the area, the company
    /// must hold licence MTB there, and the production plan's peak with the new
    /// fish must stay within the area's licence MTB and the site's locality
    /// MTB. Null when the stocking may go ahead.
    /// </summary>
    internal CommandResult? CheckStockingCapacity(SeaSite site, int count, double weightKg)
    {
        var preview = EvaluateStocking(site, count, weightKg, includeMaxCount: false);
        return preview.Allowed ? null : CommandResult.Fail(preview.ReasonKey, preview.Message);
    }

    /// <summary>
    /// What stocking <paramref name="count"/> smolts of <paramref name="strainId"/>
    /// at a site would do to the caps, with the most smolts it can take now.
    /// The same check <see cref="StockSmolts(string,int,string)"/> runs.
    /// </summary>
    public StockingCapacityPreview PreviewStocking(string siteId, int count, string strainId = "standard")
    {
        var site = State?.SeaSites?.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return new StockingCapacityPreview(false, "site_not_found", $"Site '{siteId}' not found",
                0, 0.0, 0.0, 0.0, 0.0, State?.Day ?? 0, 0);
        var strain = SmoltStrainRegistry.Get(strainId);
        return EvaluateStocking(site, Math.Max(0, count), strain.ReleaseWeightKg, includeMaxCount: true);
    }

    /// <summary>Most smolts of a strain a site can take now under every capacity check (0 when none).</summary>
    public int MaxStockableSmolts(string siteId, string strainId = "standard")
        => PreviewStocking(siteId, 0, strainId).MaxCount;

    private StockingCapacityPreview EvaluateStocking(SeaSite site, int count, double weightKg, bool includeMaxCount)
    {
        int area = ProductionAreaOfSite(site);
        double poolT = AreaLicenceMtbTonnes(area);
        double siteMtbT = site.MABTonnes;
        int today = State.Day;

        if (!CapacityRulesActive)
        {
            // No register: the per-site check in StockSmolts is the only rule.
            double headroomKg = Math.Max(0.0, siteMtbT * KgPerTonne - site.CurrentBiomassTonnes * KgPerTonne);
            int max = includeMaxCount && weightKg > 0.0 ? (int)Math.Floor(headroomKg / weightKg) : 0;
            return new StockingCapacityPreview(true, CapacityReasons.None, "No licence register: per-site MAB only",
                area, poolT, AreaStandingBiomassTonnes(area), siteMtbT, site.CurrentBiomassTonnes, today, max);
        }

        var open = OpenCapacityBreach(area);
        if (open != null)
            return new StockingCapacityPreview(false, CapacityReasons.OrderOpen,
                $"A biomass reduction order runs in production area {area} until day {open.DeadlineDay}: no stocking",
                area, poolT, AreaStandingBiomassTonnes(area), siteMtbT, site.CurrentBiomassTonnes, today, 0);
        if (poolT <= 0.0)
            return new StockingCapacityPreview(false, CapacityReasons.NoLicence,
                $"The company holds no licence in production area {area}",
                area, poolT, AreaStandingBiomassTonnes(area), siteMtbT, site.CurrentBiomassTonnes, today, 0);

        var projection = ProjectArea(area, new PlannedCohort(site.Id, area, count, weightKg));
        double areaPeak = projection.PeakTonnes;
        double sitePeak = projection.PeakTonnesBySite.TryGetValue(site.Id, out var p) ? p : 0.0;
        int peakDay = today + projection.PeakDay;
        int maxCount = includeMaxCount ? MaxCountThatFits(site, area, weightKg, poolT, siteMtbT) : 0;

        if (areaPeak > poolT)
            return new StockingCapacityPreview(false, CapacityReasons.AreaProjectedPeak,
                $"Stocking {count:N0} smolts at {site.Id} takes the plan's peak in production area {area} to " +
                $"{areaPeak:F1} t on day {peakDay}, over the licence MTB of {poolT:F0} t",
                area, poolT, areaPeak, siteMtbT, sitePeak, peakDay, maxCount);
        if (sitePeak > siteMtbT)
            return new StockingCapacityPreview(false, CapacityReasons.SiteProjectedPeak,
                $"Stocking {count:N0} smolts at {site.Id} takes the site's plan peak to {sitePeak:F1} t, " +
                $"over its locality MTB of {siteMtbT:F0} t",
                area, poolT, areaPeak, siteMtbT, sitePeak, peakDay, maxCount);
        return new StockingCapacityPreview(true, CapacityReasons.None,
            $"Plan peak {areaPeak:F1} of {poolT:F0} t in production area {area} on day {peakDay}",
            area, poolT, areaPeak, siteMtbT, sitePeak, peakDay, maxCount);
    }

    /// <summary>Largest count whose plan peak fits both caps (the peak never falls as fish are added).</summary>
    private int MaxCountThatFits(SeaSite site, int area, double weightKg, double poolT, double siteMtbT)
    {
        if (weightKg <= 0.0) return 0;
        bool Fits(int n)
        {
            var projection = ProjectArea(area, new PlannedCohort(site.Id, area, n, weightKg));
            double sitePeak = projection.PeakTonnesBySite.TryGetValue(site.Id, out var p) ? p : 0.0;
            return projection.PeakTonnes <= poolT && sitePeak <= siteMtbT;
        }
        if (!Fits(0)) return 0;
        // The new fish alone weigh count x weight on day 0, so the cap bounds the count.
        int hi = (int)Math.Min(int.MaxValue - 1L, (long)Math.Ceiling(Math.Min(poolT, siteMtbT) * KgPerTonne / weightKg) + 1);
        int lo = 0;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (Fits(mid)) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // ─── Building ──────────────────────────────────────────────────────────

    /// <summary>Locality MTB (t) a site the player builds is cleared for (calibration).</summary>
    public double BuiltSiteLocalityMtbTonnes => _calibration.Capacity.BuiltSiteLocalityMtbTonnes;

    /// <summary>
    /// What building a site in <paramref name="zoneId"/> would cost and add:
    /// its locality MTB, 0 t of licence MTB, the area's licence MTB and the pens.
    /// </summary>
    public SiteBuildPreview PreviewBuildSeaSite(string zoneId)
    {
        decimal cost = SiteBuildCostNOK;
        int area = ProductionAreaOfZone(zoneId);
        double areaMtb = AreaLicenceMtbTonnes(area);
        if (!CanBuildSeaSite(zoneId, out var reason))
        {
            string key = State?.Zones?.Any(z => z.Id == zoneId) == true ? "growth_licence_required" : "zone_not_found";
            return new SiteBuildPreview(false, key, reason, cost, area, BuiltSiteLocalityMtbTonnes, 0.0, areaMtb, StandardPensPerSite);
        }
        if (FinanceManager == null || FinanceManager.Balance < cost)
            return new SiteBuildPreview(false, "insufficient_funds",
                $"Insufficient funds: need {cost:N0} NOK to build the site", cost, area, BuiltSiteLocalityMtbTonnes,
                0.0, areaMtb, StandardPensPerSite);
        return new SiteBuildPreview(true, "", "", cost, area, BuiltSiteLocalityMtbTonnes, 0.0, areaMtb, StandardPensPerSite);
    }

    // ─── Daily enforcement (post-tick) ─────────────────────────────────────

    /// <summary>
    /// Read every production area's biomass against its caps (post-tick,
    /// after the day's wellboat harvests). Biomass over the licence MTB or a
    /// site over its locality MTB opens a reduction order; each day the
    /// illegal biomass grows past the episode's peak, the fee and the
    /// confiscation are booked on the new kilos; back under every cap closes
    /// the order; at the deadline the excess is harvested at the company's cost.
    /// </summary>
    internal void EnforceCapacity()
    {
        if (!CapacityRulesActive || State?.SeaSites == null) return;

        var areas = new SortedSet<int>();
        foreach (var site in State.SeaSites) areas.Add(ProductionAreaOfSite(site));
        foreach (var breach in State.CapacityBreaches)
            if (breach.IsOpen) areas.Add(breach.Area);

        foreach (int area in areas)
        {
            var sites = SeaSitesInArea(area);
            var reading = ReadArea(area, sites);
            var open = OpenCapacityBreach(area);

            if (reading.IllegalKg <= 0.0)
            {
                if (open != null) CloseBreachComplied(open);
                continue;
            }

            open ??= OpenBreach(area);
            ChargeBreach(open, reading);

            if (State.Day >= open.DeadlineDay)
                ForceHarvestExcess(open, sites);
        }

        PruneClosedBreaches();
    }

    /// <summary>One area's biomass against its caps.</summary>
    private readonly record struct AreaReading(double StandingKg, double PoolKg, double AreaExcessKg,
        double SiteExcessKg, List<string> SitesOver)
    {
        /// <summary>The illegal biomass: one fish counted once, whichever cap it breaks.</summary>
        public double IllegalKg => Math.Max(AreaExcessKg, SiteExcessKg);
    }

    private AreaReading ReadArea(int area, List<SeaSite> sites)
    {
        double standing = 0.0, siteExcess = 0.0;
        var over = new List<string>();
        foreach (var site in sites)
        {
            double kg = site.CurrentBiomassTonnes * KgPerTonne;
            standing += kg;
            double excess = kg - site.MABTonnes * KgPerTonne;
            if (excess > 0.0) { siteExcess += excess; over.Add(site.Id); }
        }
        double pool = AreaLicenceMtbTonnes(area) * KgPerTonne;
        return new AreaReading(standing, pool, Math.Max(0.0, standing - pool), siteExcess, over);
    }

    private CapacityBreach OpenBreach(int area)
    {
        var breach = new CapacityBreach
        {
            Id = $"mtb_{area}_d{State.Day}",
            Area = area,
            StartDay = State.Day,
            DeadlineDay = State.Day + _calibration.Capacity.OrderDeadlineDays,
            Status = CapacityBreachStatus.Open,
        };
        State.CapacityBreaches.Add(breach);
        return breach;
    }

    /// <summary>Book the fee and the confiscation on the kilos past the episode's peak; log the first day.</summary>
    private void ChargeBreach(CapacityBreach breach, AreaReading reading)
    {
        bool first = breach.DaysOver == 0;
        breach.DaysOver++;
        breach.LastIllegalKg = reading.IllegalKg;
        breach.AreaCapExceeded |= reading.AreaExcessKg > 0.0;
        foreach (var id in reading.SitesOver)
            if (!breach.SitesOverLocality.Contains(id)) breach.SitesOverLocality.Add(id);
        breach.SitesOverLocality.Sort(StringComparer.Ordinal);

        double newKg = reading.IllegalKg - breach.PeakIllegalKg;
        decimal fee = 0m, confiscation = 0m;
        if (newKg > 0.0)
        {
            var cap = _calibration.Capacity;
            decimal spot = MarketSimulation?.PricePerKg ?? _calibration.Economy.BaseSalmonPricePerKgNOK;
            decimal salesValue = BiomassBreachRules.SalesValuePerKgNok(spot, cap.LiveToGuttedFactor);
            decimal netGain = BiomassBreachRules.NetGainPerKgNok(spot, cap.LiveToGuttedFactor,
                _calibration.Feed.FeedPricePerKgFeedNOK, cap.EconomicFeedFactor, cap.OtherVariableCostPerKgNok);
            fee = BiomassBreachRules.FeeIncrementNok(newKg, salesValue, breach.FineNok, cap.InfringementFeeCeilingNok);
            confiscation = BiomassBreachRules.ConfiscationIncrementNok(newKg, netGain);
            breach.PeakIllegalKg = reading.IllegalKg;
            breach.FineNok += fee;
            breach.ConfiscatedNok += confiscation;
            PayCost(fee, CostBooking.Opex, "mtb_fee");
            PayCost(confiscation, CostBooking.Opex, "mtb_confiscation");
        }

        // The order is logged once, on its first day; the later charges are in
        // the P&L and the closing entry sums them.
        if (!first) return;
        string where = reading.AreaExcessKg > 0.0
            ? SimText.Nb($"{AreaLabel(breach.Area)}: {reading.StandingKg / KgPerTonne:F1} t står mot {reading.PoolKg / KgPerTonne:F0} t tillatelses-MTB")
            : SimText.Nb($"{string.Join(", ", reading.SitesOver.Select(SiteLabelOf))} over lokalitetens MTB");
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "MTB-overskridelse",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{where}. Fiskeridirektoratet krever biomassen under MTB innen dag {breach.DeadlineDay}. Overtredelsesgebyr {fee:N0} kr og inndragning av vinning {confiscation:N0} kr for {reading.IllegalKg / KgPerTonne:F1} t ulovlig biomasse; mer for hver ny topp."),
            BalanceChange = -(fee + confiscation)
        });
        State.TrimEventLog();
        _dayNotes.Add(new DayNote
        {
            Kind = "capacity_breach",
            SiteId = reading.SitesOver.FirstOrDefault() ?? "",
            Success = false,
            Message = $"Biomass over MTB in production area {breach.Area}: {reading.IllegalKg / KgPerTonne:F1} t illegal, order due day {breach.DeadlineDay}",
            Value = (double)(fee + confiscation)
        });
        FireEvent($"Biomass over MTB in production area {breach.Area}: reduce it by day {breach.DeadlineDay}");
    }

    private void CloseBreachComplied(CapacityBreach breach)
    {
        breach.Status = CapacityBreachStatus.Complied;
        breach.ClosedDay = State.Day;
        breach.LastIllegalKg = 0.0;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "MTB-pålegg oppfylt",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{AreaLabel(breach.Area)}: biomassen er under MTB igjen. Pålegget er oppfylt; gebyr {breach.FineNok:N0} kr og inndragning {breach.ConfiscatedNok:N0} kr står."),
            BalanceChange = 0m
        });
        State.TrimEventLog();
        _dayNotes.Add(new DayNote
        {
            Kind = "capacity_complied",
            Success = true,
            Message = $"Biomass back under MTB in production area {breach.Area}"
        });
    }

    /// <summary>
    /// The deadline passed: harvest each site down to its locality MTB less the
    /// headroom, then the area down to its licence MTB less the headroom,
    /// heaviest fish first, sold at the forced-harvest price.
    /// </summary>
    private void ForceHarvestExcess(CapacityBreach breach, List<SeaSite> sites)
    {
        var cap = _calibration.Capacity;
        double keep = 1.0 - cap.ForcedHarvestHeadroomFraction;
        double removedKg = 0.0;

        foreach (var site in sites)
        {
            double target = site.MABTonnes * KgPerTonne * keep;
            double excess = site.CurrentBiomassTonnes * KgPerTonne - target;
            if (excess > 0.0) removedKg += RemoveHeaviestFish(site, excess);
        }

        double pool = AreaLicenceMtbTonnes(breach.Area) * KgPerTonne;
        double areaExcess = sites.Sum(s => s.CurrentBiomassTonnes) * KgPerTonne - pool * keep;
        if (areaExcess > 0.0)
        {
            var byWeight = sites
                .Where(s => s.Pens.Any(p => p.TotalCount > 0))
                .OrderByDescending(SiteMeanWeightKg)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList();
            foreach (var site in byWeight)
            {
                if (areaExcess <= 0.0) break;
                double removed = RemoveHeaviestFish(site, areaExcess);
                removedKg += removed;
                areaExcess -= removed;
            }
        }

        decimal spot = MarketSimulation?.PricePerKg ?? _calibration.Economy.BaseSalmonPricePerKgNOK;
        decimal price = Math.Round(spot * (decimal)cap.ForcedHarvestPriceFactor, 2);
        decimal revenue = Math.Round((decimal)removedKg * price, 2);
        if (revenue > 0m)
        {
            ReceiveIncome(revenue, "forced_harvest");
            State.TotalSalesRevenueNok += revenue;
            State.TotalHarvestRevenueNok += revenue;
            RecordHarvestForResourceRent(removedKg);
        }

        breach.ForcedHarvestKg += removedKg;
        breach.ForcedHarvestRevenueNok += revenue;
        breach.Status = CapacityBreachStatus.ForcedHarvest;
        breach.ClosedDay = State.Day;

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Tvangsslakting (MTB)",
            Category = EventCategory.Regulation,
            Description = SimText.Nb($"{AreaLabel(breach.Area)}: fristen gikk ut. {removedKg / KgPerTonne:F1} t ble slaktet på selskapets regning til {price:F2} kr/kg."),
            BalanceChange = revenue
        });
        State.TrimEventLog();
        _dayNotes.Add(new DayNote
        {
            Kind = "capacity_breach",
            Success = false,
            Message = $"Forced harvest of {removedKg / KgPerTonne:F1} t in production area {breach.Area}",
            Value = (double)revenue
        });
    }

    /// <summary>Remove about <paramref name="kg"/> of the heaviest fish of a site (whole fish); returns the kg removed.</summary>
    private static double RemoveHeaviestFish(SeaSite site, double kg)
    {
        var cohorts = new List<(FishPopulation Pen, FishPopulation.Cohort Cohort, int Order)>();
        int order = 0;
        foreach (var pen in site.Pens)
            foreach (var cohort in pen.Cohorts)
                if (cohort.Count > 0 && cohort.MeanWeightKg > 0.0)
                    cohorts.Add((pen, cohort, order++));
        cohorts.Sort((a, b) =>
        {
            int byWeight = b.Cohort.MeanWeightKg.CompareTo(a.Cohort.MeanWeightKg);
            return byWeight != 0 ? byWeight : a.Order.CompareTo(b.Order);
        });

        double removed = 0.0;
        foreach (var (pen, cohort, _) in cohorts)
        {
            double left = kg - removed;
            if (left <= 0.0) break;
            int fish = (int)Math.Min(cohort.Count, Math.Ceiling(left / cohort.MeanWeightKg));
            double weight = cohort.MeanWeightKg;
            int taken = pen.RemoveAlive(cohort, fish, FishRemoval.ForcedHarvest);
            removed += taken * weight;
            pen.TriggerOnPopulationChanged();
        }
        return removed;
    }

    private static double SiteMeanWeightKg(SeaSite site)
    {
        int fish = site.Pens.Sum(p => p.TotalCount);
        return fish > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish : 0.0;
    }

    /// <summary>Keep every open episode and the latest closed ones (they are appended in time order).</summary>
    private void PruneClosedBreaches()
    {
        int drop = State.CapacityBreaches.Count(b => !b.IsOpen) - ClosedBreachHistory;
        for (int i = 0; i < State.CapacityBreaches.Count && drop > 0;)
        {
            if (!State.CapacityBreaches[i].IsOpen) { State.CapacityBreaches.RemoveAt(i); drop--; }
            else i++;
        }
    }

    private static string AreaLabel(int area)
        => ProductionAreas.IsValid(area) ? $"{ProductionAreas.Get(area).Code} {ProductionAreas.Get(area).Name}" : "Produksjonsområdet";

    // ─── The picture for the UI ────────────────────────────────────────────

    /// <summary>
    /// The company's capacity: one row per production area with a site or a
    /// licence (licences, cap, standing biomass, headroom, the plan's peak, an
    /// open order), one row per site, the register and the breach history.
    /// </summary>
    public CapacityOverview GetCapacityOverview()
    {
        if (State == null) return new CapacityOverview();
        var areaSet = new SortedSet<int>();
        foreach (var site in State.SeaSites) areaSet.Add(ProductionAreaOfSite(site));
        foreach (var licence in State.LicenceHoldings) areaSet.Add(licence.PooledArea);

        var areas = new List<AreaCapacityView>();
        var sitePeaks = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (int area in areaSet)
        {
            var projection = ProjectArea(area);
            foreach (var kv in projection.PeakTonnesBySite) sitePeaks[kv.Key] = kv.Value;
            double pool = AreaLicenceMtbTonnes(area);
            double standing = AreaStandingBiomassTonnes(area);
            var open = OpenCapacityBreach(area);
            areas.Add(new AreaCapacityView(area, ProductionAreas.IsValid(area) ? ProductionAreas.Get(area).Code : "",
                LicenceRegister.CountIn(State.LicenceHoldings, area), pool, standing, pool - standing,
                projection.PeakTonnes, State.Day + projection.PeakDay, open != null, open?.DeadlineDay ?? -1));
        }

        var sites = new List<SiteCapacityView>();
        foreach (var site in State.SeaSites.OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            int area = ProductionAreaOfSite(site);
            double siteHeadroom = site.MABTonnes - site.CurrentBiomassTonnes;
            double headroom = CapacityRulesActive
                ? Math.Min(siteHeadroom, AreaLicenceMtbTonnes(area) - AreaStandingBiomassTonnes(area))
                : siteHeadroom;
            sites.Add(new SiteCapacityView(site.Id, site.Label, area, site.MABTonnes, site.CurrentBiomassTonnes,
                headroom, sitePeaks.TryGetValue(site.Id, out var peak) ? peak : 0.0,
                State.LicenceHoldings.Count(l => l.RegisteredSiteId == site.Id)));
        }

        return new CapacityOverview
        {
            Regime = State.CapacityRules ?? CapacityRegime.Unregulated,
            Areas = areas,
            Sites = sites,
            Licences = State.LicenceHoldings.ToList(),
            Breaches = State.CapacityBreaches.ToList(),
        };
    }
}
