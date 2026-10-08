using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Processing;

namespace LakskongenSim.Core;

/// <summary>
/// Typed outcome of a player command issued against the simulation.
/// Lifecycle rule (Phase 1 cross-cutting): player verbs live in the library
/// and return <see cref="CommandResult"/> with a stable <see cref="ErrorKey"/>;
/// the Godot bridge keeps its legacy bool/dictionary/double surface by
/// projecting this result — the UI contract never changes in the same change
/// as the migration.
/// </summary>
public sealed class CommandResult
{
    /// <summary>True when the command fully applied.</summary>
    public bool Ok { get; private set; } = false;

    /// <summary>
    /// Stable machine-readable failure reason: "engine_not_ready",
    /// "site_not_found", "site_empty", "insufficient_funds",
    /// "invalid_loan_params", "unknown_kind". Empty for successes.
    /// </summary>
    public string ErrorKey { get; private set; } = "";

    /// <summary>Human-readable outcome, formatted for logs and toasts.</summary>
    public string Message { get; private set; } = "";

    /// <summary>Primary numeric payload (e.g. kg harvested); 0 otherwise.</summary>
    public double Value { get; private set; } = 0.0;

    /// <summary>Secondary numeric payload (e.g. NOK revenue from a harvest); 0 otherwise.</summary>
    public double Value2 { get; private set; } = 0.0;

    /// <summary>Optional identifier (e.g. loan id); empty otherwise.</summary>
    public string Id { get; private set; } = "";

    public static CommandResult Success(string message = "", double value = 0.0, double value2 = 0.0, string id = "") =>
        new() { Ok = true, Message = message, Value = value, Value2 = value2, Id = id };

    public static CommandResult Fail(string errorKey, string message, double value = 0.0) =>
        new() { Ok = false, ErrorKey = errorKey, Message = message, Value = value };
}

/// <summary>
/// One atomic outcome produced by the engine DURING Advance processing
/// (wellboat executions, storm impacts). The Godot bridge drains the ledger
/// after Advance/AdvanceDays and projects each note onto the legacy signal
/// surface; PlayerScriptProbe reads the same notes headless. The mutating
/// code owns its event-log entry — the bridge is presentation only.
/// </summary>
public sealed class DayNote
{
    /// <summary>Note kind: wellboat_rov / wellboat_freshwater /
    /// wellboat_delouse / wellboat_harvest / storm_impact.</summary>
    public string Kind { get; set; } = "";

    public string SiteId { get; set; } = "";
    public bool Success { get; set; } = false;
    public string Message { get; set; } = "";

    /// <summary>Signal event type (WeatherEvent projection), empty otherwise.</summary>
    public string EventType { get; set; } = "";
    public double Severity { get; set; } = 0.0;
    public double Value { get; set; } = 0.0;
}

/// <summary>
/// Player verbs, moved out of the Godot bridge (NEXT_LEVEL_PLAN Phase 1).
/// These run on the same state the UI shows and are the same entry points the
/// PlayerScriptProbe careers drive — one implementation, one path.
/// </summary>
public partial class SimulationEngine
{
    // ── Stocking ───────────────────────────────────────────────────────────

    /// <summary>
    /// Stock smolts at an owned sea site (Phase 1.4: strain-aware, P5:
    /// quality-carryover). Charges the calibrated rearing cost and seeds the
    /// incoming strain's lice load into the site's lice model.
    /// </summary>
    public CommandResult StockSmolts(string siteId, int count, string strainId = "standard")
        => StockSmolts(siteId, count, strainId, SmoltQualityTier.Standard);

    /// <summary>
    /// Stock smolts with an explicit post-smolt quality tier. The tier is
    /// stamped on the cohort so the sea phase applies its mortality / growth /
    /// time-to-harvest carryover multipliers.
    /// </summary>
    public CommandResult StockSmolts(string siteId, int count, string strainId, SmoltQualityTier qualityTier)
    {
        if (State?.SeaSites == null || Hatchery == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        var strain = SmoltStrainRegistry.Get(strainId);
        var quality = SmoltQualityRegistry.Get(qualityTier);
        decimal stockingCost = Hatchery.CalculateRearingCost(count, Hatchery.MaturationDays)
            * (decimal)strain.CostMultiplier;

        if (FinanceManager.Balance < stockingCost)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {stockingCost:N0} NOK for {count:N0} smolts");

        var population = new FishPopulation
        {
            ZoneId = site.ZoneId,
            SeaLiceLoad = strain.InitialLiceLoad
        };
        population.InjectDependencies(Growth, Mortality);

        var smoltCohort = new FishPopulation.Cohort(
            Hatchery.MaturationDays,
            count,
            strain.ReleaseWeightKg,
            LifeStage.Smolt,
            strain.BuildGenome()
        )
        {
            Quality = quality.Tier
        };
        population.Cohorts.Add(smoltCohort);

        site.Stock(population);
        State.Populations.Add(population);

        // Phase 2.1: seed the SITE's lice model from the incoming smolts
        // (no Reset — that would wipe resistance built up by earlier
        // cohorts). Without this the model sits at 0 lice forever and
        // AdvanceDay early-outs.
        if (strain.InitialLiceLoad > 0)
            site.Lice.SeedExternalInfection(Math.Max(0.05, strain.InitialLiceLoad));

        FinanceManager.Spend(stockingCost);

        var message = $"Stocked {count:N0} {quality.Name} {strain.Name} smolts " +
                      $"({strain.ReleaseWeightKg * 1000:F0}g) at {siteId}";

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Smolt Stocking",
            Category = EventCategory.Equipment,
            Description = $"{message} for {stockingCost:N0} NOK",
            BalanceChange = -stockingCost
        });

        return CommandResult.Success(message, count, id: siteId);
    }

    // ── Harvest ────────────────────────────────────────────────────────────

    /// <summary>
    /// Harvest every fish at one sea site. Value = biomass kg, Value2 = NOK
    /// revenue after contracts (oldest first), spot remainder, certification
    /// and VAP multipliers. Fails with "site_empty" when the site holds no
    /// meaningful biomass.
    /// </summary>
    public CommandResult HarvestSite(string siteId)
    {
        if (State?.SeaSites == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        // Harvest ONLY this site's pens (site.Pens identity). Populations
        // carry ZoneId (shared by every site in a zone), so filtering
        // State.Populations by zone would wipe neighbouring sites too.
        double totalBiomassKg = 0.0;
        int totalFishCount = 0;
        var populations = site.Pens.ToList();

        foreach (var pop in populations)
        {
            foreach (var cohort in pop.Cohorts.ToList()) // ToList() to avoid modification during iteration
            {
                double cohortBiomass = cohort.Count * cohort.MeanWeightKg;
                totalBiomassKg += cohortBiomass;
                totalFishCount += cohort.Count;
            }
        }

        if (totalBiomassKg < 0.1)
            return CommandResult.Fail("site_empty", $"Site '{siteId}' is empty");

        // Calculate revenue: oldest contracts first at locked prices,
        // remainder at spot — all scaled by certification multiplier.
        // Partial deliveries accrue no penalty (expiry settles shortfalls).
        decimal revenue = 0m;
        double remainingKg = totalBiomassKg;
        if (ContractSystem != null)
        {
            foreach (var c in ContractSystem.ActiveContracts.OrderBy(c => c.SignedAtWeek).ToList())
            {
                if (remainingKg <= 0.1) break;
                int deliver = (int)Math.Min(remainingKg, c.RemainingKg);
                if (deliver <= 0) continue;
                var (payment, _) = ContractSystem.FulfillContract(c.Id, deliver);
                revenue += payment;
                remainingKg -= deliver;
            }
        }
        decimal spotPrice = MarketSimulation.PricePerKg;
        revenue += (decimal)remainingKg * spotPrice;
        decimal certMultiplier = SustainabilityManager?.GetPriceMultiplier(
            SustainabilityManager.CurrentCertification) ?? 1.0m;
        revenue *= certMultiplier;
        // VAP fillet line (Phase 3.5): +12% when a plant is owned.
        // Phase 4.4: grade-C recipe (pale) halves the premium to +6%.
        bool vap = State.OwnsProcessingPlant;
        if (vap)
        {
            string grade = FeedRecipe.Grade(State.RecipeProteinPct, State.RecipeAstaxPpm);
            revenue *= FeedRecipe.VapMult(grade);
            if (grade == "C")
                State.EventLog.Add(new EventRecord
                {
                    Day = State.Day,
                    Name = "Blek filet (C)",
                    Category = EventCategory.Market,
                    Description = "Lav astaxanthin i fôret — VAP-premie halvert til +6%",
                    BalanceChange = 0m
                });
        }

        FinanceManager.AddRevenue(revenue);
        // D1: the sale finally lands in the P&L — Revenue was previously
        // maintained only by insurance payouts, so IPO gates read a lie.
        FinancialStatements?.RecordRevenue(revenue);

        // P15: player-supply market impact — the harvested tonnage moves the
        // market (accumulates downward price pressure, decays over days).
        MarketSimulation?.ApplyPlayerHarvest(totalBiomassKg / 1000.0);

        // Remove exactly the harvested populations (this site only)
        foreach (var pop in populations)
        {
            pop.Cohorts.Clear();
            State.Populations.Remove(pop);
        }
        site.Pens.Clear();

        // Reset site stocking density
        site.CurrentStockingDensity = 0f;

        var message = $"Harvested {totalBiomassKg:N0} kg ({totalFishCount:N0} fish) at {siteId}";

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Harvest",
            Category = EventCategory.Market,
            Description = $"{message} for {revenue:N0} NOK",
            BalanceChange = revenue
        });

        return CommandResult.Success(message, totalBiomassKg, (double)revenue, siteId);
    }

    // ── Finance ────────────────────────────────────────────────────────────

    /// <summary>
    /// Take out a new loan. The principal lands as liquidity via
    /// <c>FinanceManager.AddLoanPrincipal</c> (NOT revenue, S5); the liability
    /// is recorded in <see cref="GameState.ActiveLoans"/> and amortized daily
    /// in ProcessDailyFinance. Returns the loan id in <see cref="CommandResult.Id"/>.
    /// </summary>
    public CommandResult TakeLoan(double amount, int termMonths, double rate)
    {
        if (amount <= 0 || termMonths <= 0 || rate < 0)
            return CommandResult.Fail("invalid_loan_params", "Invalid loan parameters");

        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        // Cash in as liquidity (NOT revenue — it must be repaid).
        FinanceManager.AddLoanPrincipal((decimal)amount);

        string loanId = "loan_" + State.Day;
        State.ActiveLoans.Add(new LoanState
        {
            Id = loanId,
            Principal = (decimal)amount,
            InterestRate = (decimal)rate,
            TermDays = Math.Max(30, termMonths * 30),
            DaysTaken = 0
        });

        var message = $"Loan of {amount:N0} NOK approved at {rate:P1} for {termMonths} months";
        return CommandResult.Success(message, amount, id: loanId);
    }

    // ── Insurance ──────────────────────────────────────────────────────────

    /// <summary>
    /// Annual premium (NOK) at 100% coverage per catastrophe kind. Premiums are
    /// scaled linearly by the requested coverage fraction; the deductible is
    /// retained by the insured before cover applies (see <see cref="InsurancePolicy"/>).
    /// </summary>
    private static decimal BaseAnnualPremiumNok(InsuranceKind kind) => kind switch
    {
        InsuranceKind.Mortality => 120_000m,
        InsuranceKind.Bloom => 250_000m,
        InsuranceKind.Storm => 180_000m,
        InsuranceKind.ISA => 300_000m,
        _ => 0m
    };

    /// <summary>
    /// Enable/disable a catastrophe policy ("mortality" | "bloom" | "storm" |
    /// "isa", case-insensitive). Replaces any existing policy of that kind while
    /// preserving all other kinds.
    /// </summary>
    public CommandResult SetInsurancePolicy(string kind, bool enabled, double coverage, double deductible)
    {
        if (Insurance == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        if (!Enum.TryParse(kind, ignoreCase: true, out InsuranceKind parsed))
            return CommandResult.Fail("unknown_kind", $"Unknown insurance kind '{kind}'");

        double clampedCoverage = Math.Clamp(coverage, 0.0, 1.0);
        decimal ded = deductible > 0.0 ? (decimal)deductible : 0m;

        // InsuranceSystem exposes no per-kind removal, so rebuild: keep every
        // other kind's policies, then re-add this kind if enabled.
        var survivors = Insurance.Policies.Where(p => p.Kind != parsed).ToList();
        Insurance.ClearPolicies();
        foreach (var policy in survivors)
            Insurance.Subscribe(policy);

        if (enabled && clampedCoverage > 0.0)
        {
            decimal premium = BaseAnnualPremiumNok(parsed) * (decimal)clampedCoverage;
            Insurance.Subscribe(new InsurancePolicy(parsed, premium, clampedCoverage, ded));
        }

        bool active = enabled && clampedCoverage > 0.0;
        return CommandResult.Success(
            active ? $"Insurance '{InsuranceKindKey(parsed)}' active (premium base {BaseAnnualPremiumNok(parsed):N0} NOK)" :
                     $"Insurance '{InsuranceKindKey(parsed)}' disabled");
    }

    // ── Delousing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Apply a delousing treatment to one site (player verb, moved from
    /// GameBridge.OrderDelousing). Profile-driven cost and efficacy, per-pen
    /// mortality/harm via DelousingSystem; the site's lice kill and gill
    /// damage apply ONCE per site (the bridge applied gill damage once per
    /// pen, i.e. N× for N pens — fixed here). Fails before any mutation:
    /// unknown_method / site_not_found / site_empty / insufficient_funds.
    /// </summary>
    public CommandResult DelouseSite(string siteId, string method)
    {
        if (!Farm.DelousingSystem.TryParseMethodName(method, out var delousingMethod))
            return CommandResult.Fail("unknown_method", $"Invalid delousing method '{method}'");

        if (State?.SeaSites == null || SeaLice == null || IntegratedDisease == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        int totalFish = site.Pens.SelectMany(p => p.Cohorts).Sum(c => c.Count);
        if (totalFish <= 0)
            return CommandResult.Fail("site_empty", $"Site '{siteId}' has no fish to treat");

        var profile = Farm.DelousingSystem.GetProfile(delousingMethod);
        decimal totalCost = totalFish * profile.CostPerFish;

        if (FinanceManager.Balance < totalCost)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {totalCost:N0} NOK to treat {totalFish:N0} fish");

        double tempC = Weather?.CurrentTemperatureC ?? 10.0;
        double mortMult = ComputeSiteTreatmentMortalityMultiplier(site);

        // Phase 1 wave 3: the regulation-shared kernel treats every live pen;
        // gill damage lands ONCE on the site score (the old bridge multiplied
        // the dose scalar per pen).
        var outcome = ApplySiteDelousing(site, delousingMethod, tempC, mortMult);
        if (!outcome.Applied)
            return CommandResult.Fail("cancelled",
                $"Treatment cancelled: {outcome.CancelReason}");

        FinanceManager.Spend(outcome.Cost);
        FinancialStatements?.RecordOperatingExpense(outcome.Cost);
        site.GillScore = Math.Max(0.0, site.GillScore - outcome.GillDamage * 50.0);

        string description = $"Applied {delousingMethod} delousing to {siteId} " +
                             $"({outcome.TotalFish:N0} fish treated, {profile.Efficacy:P0} profile efficacy, {outcome.TotalDeaths:N0} handling deaths)";
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Delousing Treatment",
            Category = EventCategory.Disease,
            Description = description,
            BalanceChange = -totalCost
        });

        return CommandResult.Success(description, outcome.TotalFish, outcome.TotalDeaths, siteId);
    }

    private static string InsuranceKindKey(InsuranceKind kind) => kind.ToString().ToLowerInvariant();

    // ── Wellboat booking ───────────────────────────────────────────────────

    /// <summary>
    /// Book a spot-hire wellboat or ROV inspection (Phase 2.4/3.4): harvest
    /// (150k, 5d), delouse (100k + treatment at execution, 3d), rov (75k, 2d:
    /// net cleaning +15 integrity + findings report), freshwater (120k + bath
    /// at execution, 4d). One pending job per site+kind. Execution runs inside
    /// the engine's daily Advance (post-tick, see ProcessDueWellboatJobs).
    /// </summary>
    public CommandResult RequestWellboat(string siteId, string kind, string method = "")
    {
        if (State?.SeaSites == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        if (State.SeaSites.All(s => s.Id != siteId))
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        if (State.WellboatJobs.Any(j => j.SiteId == siteId && j.Kind == kind))
            return CommandResult.Fail("already_booked",
                $"Wellboat already booked for '{siteId}' ({kind})");

        int delay = kind switch { "delouse" => 3, "rov" => 2, "freshwater" => 4, _ => 5 };
        decimal hire = kind switch { "delouse" => 100_000m, "rov" => 75_000m, "freshwater" => 120_000m, _ => 150_000m };
        // Skipper: -25% hire via contacts, -1 day routing.
        hire *= CrewEffects.WellboatHireMultiplier(State);
        delay = Math.Max(1, delay - CrewEffects.WellboatDelayReduction(State));

        if (FinanceManager.Balance < hire)
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {hire:N0} NOK for the wellboat hire");

        FinanceManager.Spend(hire);
        State.WellboatJobs.Add(new Logistics.WellboatJob
        {
            SiteId = siteId,
            Kind = kind,
            Method = method,
            DueDay = State.Day + delay,
            HireCost = hire
        });

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Brønnbåt bestilt",
            Category = EventCategory.Equipment,
            Description = $"Brønnbåt bestilt til {siteId} ({kind}, ankomst om {delay}d)",
            BalanceChange = -hire
        });

        return CommandResult.Success(
            $"Wellboat booked for {siteId} ({kind}, arrival in {delay}d)", id: siteId);
    }

    // ── Load path ──────────────────────────────────────────────────────────

    /// <summary>
    /// Adopt a deserialized snapshot into the live engine exactly the way the
    /// game does (Phase 1: the load path gets ONE owner). The Godot bridge's
    /// LoadGame previously hand-copied this list; the game and the
    /// PlayerScriptProbe now share this method, so a drift between "player
    /// load" and "probe load" is impossible by construction.
    /// <para>
    /// The copy list is deliberately unchanged from the bridge's. Fields the
    /// old list missed (research progress, achievements, active scenario,
    /// tutorial, RAS/offshore) are still missed until the ISnapshotable
    /// rewrite — this method documents the gap, it does not fix it.
    /// </para>
    /// </summary>
    public void AdoptSavedState(GameState snapshot)
    {
        // Restore day counter and persistent collections.
        State.Day = snapshot.Day;
        // Ownership flags (were dropped on load; restored here incl. VAP tier).
        State.OwnsHatchery = snapshot.OwnsHatchery;
        State.OwnsFeedMill = snapshot.OwnsFeedMill;
        State.OwnsProcessingPlant = snapshot.OwnsProcessingPlant;
        State.ProcessingTier = snapshot.ProcessingTier ?? "";
        State.HasASCCertification = snapshot.HasASCCertification;

        State.Zones.Clear();
        State.SeaSites.Clear();
        State.Populations.Clear();
        State.EventLog.Clear();
        State.WellboatJobs.Clear();
        State.Crew.Clear();
        State.ActiveBlooms.Clear();
        State.ActiveCrises.Clear();
        State.ActiveLoans.Clear();
        State.TaxHistory.Clear();
        State.Acquisitions.Clear();
        State.Licenses.Clear();
        State.RecentMarketReports.Clear();
        State.AchievedVisualMilestones.Clear();
        if (State.CompetitorCompanies != null)
            State.CompetitorCompanies.Clear();

        foreach (var zone in snapshot.Zones)
            State.Zones.Add(zone);
        foreach (var site in snapshot.SeaSites)
            State.SeaSites.Add(site);
        foreach (var pop in snapshot.Populations)
            State.Populations.Add(pop);
        foreach (var evt in snapshot.EventLog)
            State.EventLog.Add(evt);
        if (snapshot.WellboatJobs != null)
            foreach (var job in snapshot.WellboatJobs)
                State.WellboatJobs.Add(job);
        if (snapshot.Crew != null)
            foreach (var member in snapshot.Crew)
                State.Crew.Add(member);
        if (snapshot.ActiveBlooms != null)
            foreach (var bloom in snapshot.ActiveBlooms)
                State.ActiveBlooms.Add(bloom);

        // Empire-system DTOs (save-rounded; engine rehydrates live systems).
        State.StockMarket = snapshot.StockMarket;
        State.BrandReputation = snapshot.BrandReputation;
        foreach (var crisis in snapshot.ActiveCrises)
            State.ActiveCrises.Add(crisis);
        State.VerticalIntegration = snapshot.VerticalIntegration;
        foreach (var loan in snapshot.ActiveLoans)
            State.ActiveLoans.Add(loan);
        foreach (var tax in snapshot.TaxHistory)
            State.TaxHistory.Add(tax);
        State.InvestorSystem = snapshot.InvestorSystem;
        foreach (var acq in snapshot.Acquisitions)
            State.Acquisitions.Add(acq);
        foreach (var lic in snapshot.Licenses)
            State.Licenses.Add(lic);
        foreach (var kvp in snapshot.FactionRelations)
            State.FactionRelations[kvp.Key] = kvp.Value;
        foreach (var rep in snapshot.RecentMarketReports)
            State.RecentMarketReports.Add(rep);
        State.FishWelfareCondition = snapshot.FishWelfareCondition;
        foreach (var m in snapshot.AchievedVisualMilestones)
            State.AchievedVisualMilestones.Add(m);
        if (snapshot.CompetitorCompanies != null && State.CompetitorCompanies != null)
            foreach (var c in snapshot.CompetitorCompanies)
                State.CompetitorCompanies.Add(c);
        State.EmpireProgression = snapshot.EmpireProgression;

        // Re-inject dependencies into deserialized populations and restore
        // depth/empire systems + model snapshots (same call the game makes).
        RestoreFromLoad(snapshot);
    }
}