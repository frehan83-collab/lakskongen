using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Data;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using LakskongenSim.Harvest;
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

    /// <summary>The sim day the outcome happened on (stamped by Advance; -1
    /// until then). A multi-day Advance drains every day's notes at once, so
    /// the bridge dates its alerts by this, not by the day it drains (U1-05).</summary>
    public int Day { get; set; } = -1;
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
        // (event text: Norwegian, see SimText)
        if (State?.SeaSites == null || Hatchery == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        if (count <= 0)
            return CommandResult.Fail("invalid_count", $"Smolt count must be positive (got {count})");

        var site = State.SeaSites.FirstOrDefault(s => s.Id == siteId);
        if (site == null)
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        var strain = SmoltStrainRegistry.Get(strainId);
        var quality = SmoltQualityRegistry.Get(qualityTier);

        // MAB is a hard licence limit: refuse stocking that would put the
        // site's live biomass over it, as cohort transfers already do (F1129).
        double incomingTonnes = count * strain.ReleaseWeightKg / 1000.0;
        if (site.CurrentBiomassTonnes + incomingTonnes > site.MABTonnes)
            return CommandResult.Fail("mab_exceeded",
                $"Stocking {count:N0} smolts ({incomingTonnes:F1} t) would put {siteId} over its MAB " +
                $"({site.CurrentBiomassTonnes:F1} + {incomingTonnes:F1} > {site.MABTonnes:F0} t)");
        // S2-01a: under the licence register the production plan's projected
        // peak must fit the area's licence MTB and the site's locality MTB, and
        // no reduction order may run in the area.
        var capacity = CheckStockingCapacity(site, count, strain.ReleaseWeightKg);
        if (capacity != null) return capacity;
        // S2-01b: the smolts go into the fewest empty pens that hold them
        // within § 47a's 200,000 fish and § 25's 25 kg/m³ at the plan's peak.
        var penRoom = CheckSitePenRoom(site, count, ForecastPenStocking(site, strain, qualityTier), out var allocation);
        if (penRoom != null) return penRoom;

        return PlaceSmolts(site, allocation!, strain, quality);
    }

    /// <summary>Norwegian name of a delousing method for the event log.</summary>
    private static string DelousingName(DelousingMethod method) => method switch
    {
        DelousingMethod.HydrogenPeroxide => "hydrogenperoksid",
        DelousingMethod.Thermolicer => "termisk avlusing",
        DelousingMethod.Hydrolicer => "spyling",
        DelousingMethod.CleanerFish => "rensefisk",
        DelousingMethod.LaserDelousing => "laser",
        DelousingMethod.FreshwaterBath => "ferskvannsbad",
        DelousingMethod.MedicatedFeed => "medisinfôr",
        DelousingMethod.LiceSkirts => "luseskjørt",
        _ => method.ToString()
    };

    /// <summary>Norwegian name of a wellboat job kind for the event log.</summary>
    private static string WellboatJobName(string kind) => kind switch
    {
        "harvest" => "slakting",
        "delouse" => "avlusing",
        "rov" => "ROV-inspeksjon",
        "freshwater" => "ferskvannsbehandling",
        _ => kind
    };

    // ── Harvest ────────────────────────────────────────────────────────────

    /// <summary>
    /// Harvest every fish at one sea site now, through the S2-04 pipeline
    /// (<see cref="HarvestNow"/>, express): a spot wellboat and the
    /// slaughterhouse's waiting pen, the HOG yield, the size-class and grade
    /// prices, contracts first, the harvest cost booked as cost of goods sold.
    /// Value = live kg, Value2 = gross sales (NOK). Fails with "site_empty"
    /// when the site holds no meaningful biomass. A planned harvest
    /// (fasting at the farm, a wellboat slot, no surcharge) is
    /// <see cref="OrderHarvest"/>.
    /// </summary>
    public CommandResult HarvestSite(string siteId)
    {
        if (State?.SeaSites == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");

        if (State.SeaSites.All(s => s.Id != siteId))
            return CommandResult.Fail("site_not_found", $"Site '{siteId}' not found");

        // Harvest ONLY this site's pens (site.Pens identity). Populations
        // carry ZoneId (shared by every site in a zone), so filtering
        // State.Populations by zone would wipe neighbouring sites too.
        return HarvestNowAs(siteId, null, 1.0, HarvestMode.Express, "site_empty", $"Site '{siteId}' is empty");
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

        // The bank prices the loan: never below the calibrated lending rate
        // (the caller-chosen rate let a script borrow at 0%).
        rate = Math.Max(rate, _balancing.LoanInterestRate);

        // Credit limit: total debt ≤ calibrated multiple of equity (cash +
        // standing biomass at the norm price − existing loans). Both values were
        // calibrated but unused, so borrowing was unlimited.
        decimal limit = LoanCapacityNok();
        decimal existing = State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal));
        if (existing + (decimal)amount > limit)
            return CommandResult.Fail("credit_limit",
                $"Credit limit reached: {Math.Max(0m, limit - existing):N0} NOK available", (double)Math.Max(0m, limit - existing));

        // Cash in as liquidity and a balance-sheet liability (NOT revenue).
        ReceiveLoan((decimal)amount);

        // Unique among live loans: the highest same-day suffix + 1. (Counting the
        // same-day loans reused a live id after an early repayment, and the
        // "loan_5" prefix also matched day 50.)
        string prefix = $"loan_{State.Day}_";
        int next = 1 + State.ActiveLoans
            .Select(l => l.Id.StartsWith(prefix, StringComparison.Ordinal)
                         && int.TryParse(l.Id.AsSpan(prefix.Length), out int n) ? n : 0)
            .DefaultIfEmpty(0).Max();
        string loanId = prefix + next;
        State.ActiveLoans.Add(new LoanState
        {
            Id = loanId,
            Principal = (decimal)amount,
            OriginalPrincipal = (decimal)amount,
            InterestRate = (decimal)rate,
            // The term runs whole calendar months from today (S2-02; was 30-day months).
            TermDays = SimCalendar.DaysInMonthsFrom(State.Day, Math.Max(1, termMonths)),
            DaysTaken = 0
        });

        var message = $"Loan of {amount:N0} NOK approved at {rate:P1} for {termMonths} months";
        return CommandResult.Success(message, amount, id: loanId);
    }

    /// <summary>
    /// Repay loan principal early, oldest loan first (player verb; the finance
    /// panel's repay button called a method that existed nowhere). Principal
    /// only — interest accrues daily — booked as a liability repayment; a loan
    /// paid down to zero closes. Value = principal repaid. Fails before any
    /// mutation: invalid_amount / no_loans / insufficient_funds.
    /// </summary>
    public CommandResult RepayLoan(decimal amount)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (amount <= 0m)
            return CommandResult.Fail("invalid_amount", $"Repayment must be positive (got {amount:N0})");
        decimal outstanding = State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal));
        if (outstanding <= 0m)
            return CommandResult.Fail("no_loans", "No outstanding loans");

        decimal repay = Math.Min(amount, outstanding);
        if (!TryPayCost(repay, CostBooking.LoanPrincipal, "loan_principal"))
            return CommandResult.Fail("insufficient_funds",
                $"Insufficient funds: need {repay:N0} NOK to repay", (double)repay);

        decimal left = repay;
        foreach (var loan in State.ActiveLoans.OrderByDescending(l => l.DaysTaken).ThenBy(l => l.Id, StringComparer.Ordinal).ToList())
        {
            if (left <= 0m) break;
            decimal take = Math.Min(left, Math.Max(0m, loan.Principal));
            loan.Principal -= take;
            left -= take;
            if (loan.Principal <= 0.01m)
                State.ActiveLoans.Remove(loan);
        }
        return CommandResult.Success($"Repaid {repay:N0} NOK of loan principal", (double)repay);
    }

    /// <summary>
    /// What the active loans cost per average calendar month (365/12 days) at
    /// today's balances: each loan's principal slice plus interest, as the daily
    /// amortization pays them.
    /// </summary>
    public decimal MonthlyLoanPaymentNok()
    {
        if (State?.ActiveLoans == null) return 0m;
        decimal monthly = 0m;
        foreach (var loan in State.ActiveLoans)
        {
            int remainingDays = Math.Max(1, loan.TermDays - loan.DaysTaken);
            monthly += (loan.Principal / remainingDays + loan.Principal * loan.InterestRate / SimCalendar.DaysPerYear)
                * (decimal)SimCalendar.AverageDaysPerMonth;
        }
        return monthly;
    }

    /// <summary>
    /// Maximum total loan principal the bank extends: the calibrated multiple of
    /// equity, where equity = cash + standing biomass valued at the norm price −
    /// outstanding loan principal (never below zero).
    /// </summary>
    public decimal LoanCapacityNok()
    {
        if (State == null || FinanceManager == null) return 0m;
        decimal normPrice = PenPriceCouncil?.MonthlyPenPrice > 0m
            ? PenPriceCouncil.MonthlyPenPrice
            : MarketSimulation?.PricePerKg ?? 0m;
        decimal biomassValue = (decimal)State.Populations.Sum(p => p.GetTotalBiomassKg()) * normPrice;
        decimal debt = State.ActiveLoans.Sum(l => Math.Max(0m, l.Principal));
        decimal equity = Math.Max(0m, FinanceManager.Balance + biomassValue - debt);
        return equity * (decimal)_balancing.MaxLoanMultiplier;
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

        double tempC = SeaTemperatureAtSite(site); // the site's own water (S2-02)
        double mortMult = ComputeSiteTreatmentMortalityMultiplier(site);

        // Phase 1 wave 3: the regulation-shared kernel treats every live pen;
        // gill damage lands ONCE on the site score (the old bridge multiplied
        // the dose scalar per pen).
        var outcome = ApplySiteDelousing(site, delousingMethod, tempC, mortMult);
        if (!outcome.Applied)
            return CommandResult.Fail("cancelled",
                $"Treatment cancelled: {outcome.CancelReason}");

        // The treatment has happened: pay it even if the final cost exceeds the
        // estimate checked above (it used to be free when Spend was refused).
        PayCost(outcome.Cost, CostBooking.Opex, "delousing");
        site.GillScore = Math.Max(0.0, site.GillScore - outcome.GillDamage * 50.0);

        string description = SimText.Nb($"Avlusing ({DelousingName(delousingMethod)}) på {site.Label}: {outcome.TotalFish:N0} fisk behandlet, {profile.Efficacy:P0} virkning, {outcome.TotalDeaths:N0} døde under håndteringen");
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

        PayCost(hire, CostBooking.Opex, "wellboat");
        // S2-04: a booked harvest wellboat means the fish stop eating now, so
        // their gut is empty when the boat comes (kvalitetsforskriften § 16).
        if (kind == "harvest")
            foreach (var pen in State.SeaSites.First(s => s.Id == siteId).Pens)
                StartFasting(pen);
        State.WellboatJobsBooked++;
        var job = new Logistics.WellboatJob
        {
            SiteId = siteId,
            Kind = kind,
            Method = method,
            BookedDay = State.Day,
            Sequence = State.WellboatJobsBooked,
            DueDay = State.Day + delay,
            HireCost = hire
        };
        State.WellboatJobs.Add(job);

        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Brønnbåt bestilt",
            Category = EventCategory.Equipment,
            Description = $"Brønnbåt bestilt til {State.SeaSites.FirstOrDefault(s => s.Id == siteId)?.Label ?? siteId} ({WellboatJobName(kind)}, ankommer om {delay} d)",
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
    /// Every persisted GameState field is adopted here; the live systems are
    /// then rehydrated in <see cref="RestoreFromLoad"/>. SaveLoadMatrixTests
    /// guards the whole path: a save taken at any day must continue
    /// bit-identically to the uninterrupted run.
    /// </para>
    /// </summary>
    public void AdoptSavedState(GameState snapshot)
    {
        // Restore day counter and persistent collections.
        State.Day = snapshot.Day;
        // A legacy save without a seed (0) keeps the seed this engine was
        // initialized with, so the next save of the game records it (P0-07).
        State.RandomSeed = snapshot.RandomSeed != 0 ? snapshot.RandomSeed : Rng.Seed;
        // Ownership flags (were dropped on load; restored here incl. VAP tier).
        State.OwnsHatchery = snapshot.OwnsHatchery;
        State.OwnsFeedMill = snapshot.OwnsFeedMill;
        State.OwnsProcessingPlant = snapshot.OwnsProcessingPlant;
        State.ProcessingTier = snapshot.ProcessingTier ?? "";
        State.HasASCCertification = snapshot.HasASCCertification;
        // AI feeding ownership + calibration (drives the live AIFeeding
        // instance rehydration below; dropped on load before wave 6).
        State.OwnsAIFeedingSystem = snapshot.OwnsAIFeedingSystem;
        State.AIFeedingCalibration = snapshot.AIFeedingCalibration;
        State.AIFeedingDaysSinceInstall = snapshot.AIFeedingDaysSinceInstall;
        // Player-tuned policies lost on load before wave 6: feed recipe
        // (the bridge re-applies it post-load), night-shift policy.
        State.RecipeProteinPct = snapshot.RecipeProteinPct;
        State.RecipeReplacePct = snapshot.RecipeReplacePct;
        State.RecipeAstaxPpm = snapshot.RecipeAstaxPpm;
        State.NightShiftPolicy = snapshot.NightShiftPolicy;
        // Research / scenario / tutorial progress (scenario wins, tutorial
        // gating and research effects all read these — all dropped before).
        State.ActiveResearchId = snapshot.ActiveResearchId;
        State.ResearchProgress = snapshot.ResearchProgress;
        State.ActiveScenarioId = snapshot.ActiveScenarioId;
        State.ScenarioIsWon = snapshot.ScenarioIsWon;
        State.ScenarioIsLost = snapshot.ScenarioIsLost;
        State.ScenarioLossReason = snapshot.ScenarioLossReason ?? "";
        State.ScenarioStartDay = snapshot.ScenarioStartDay;
        // S2-02: the home production area (0 in legacy saves: resolved from the sites).
        State.HomeProductionArea = snapshot.HomeProductionArea;
        State.TutorialCurrentStepIndex = snapshot.TutorialCurrentStepIndex;
        // Economy / sustainability / external-data continuity.
        State.ContractActive = snapshot.ContractActive;
        State.AcquisitionCount = snapshot.AcquisitionCount;
        State.BestFCR = snapshot.BestFCR;
        State.BestGrowthBreedingValue = snapshot.BestGrowthBreedingValue;
        State.BestDiseaseResistanceBreedingValue = snapshot.BestDiseaseResistanceBreedingValue;
        State.MinimumBalanceEver = snapshot.MinimumBalanceEver;
        State.FeedSustainabilityRating = snapshot.FeedSustainabilityRating;
        State.SustainabilityScore = snapshot.SustainabilityScore;
        State.SustainabilityCertification = snapshot.SustainabilityCertification;
        State.EnableResourceRentTax = snapshot.EnableResourceRentTax;
        State.ResourceRentPaid = snapshot.ResourceRentPaid;
        State.HarvestedKgThisTaxYear = snapshot.HarvestedKgThisTaxYear;
        State.TotalSalesRevenueNok = snapshot.TotalSalesRevenueNok;
        State.TotalHarvestRevenueNok = snapshot.TotalHarvestRevenueNok;
        State.OpeningBalanceNok = snapshot.OpeningBalanceNok;
        State.ResourceRentHarvestValueThisTaxYear = snapshot.ResourceRentHarvestValueThisTaxYear;
        State.HarvestCostThisTaxYear = snapshot.HarvestCostThisTaxYear;
        State.PenPriceCouncilLastValue = snapshot.PenPriceCouncilLastValue;
        State.ExternalAquacultureSnapshot = snapshot.ExternalAquacultureSnapshot;
        State.ExternalDataAnalytics = snapshot.ExternalDataAnalytics;
        State.ExternalHealthCrisis = snapshot.ExternalHealthCrisis;
        State.ExternalHealthAnalytics = snapshot.ExternalHealthAnalytics;
        State.GrowthState = snapshot.GrowthState;
        State.MortalityState = snapshot.MortalityState;

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
        State.LicenseAuctions.Clear();
        State.RecentMarketReports.Clear();
        State.AchievedVisualMilestones.Clear();
        State.DailySnapshots.Clear();
        State.ProcessingSnapshots.Clear();
        State.Broodstock.Clear();
        State.UnlockedResearchIds.Clear();
        State.CompletedTutorialStepIds.Clear();
        State.UnlockedAchievementIds.Clear();
        State.ExportMarkets.Clear();
        State.RASFacilities.Clear();
        State.OffshoreFarms.Clear();
        State.PostSmoltFacilities.Clear();
        State.InsurancePolicies.Clear();
        State.BloomMitigations.Clear();
        if (State.ExternalHealthWarnings != null)
            State.ExternalHealthWarnings.Clear();
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
        State.WellboatJobsBooked = snapshot.WellboatJobsBooked;
        // S2-04 harvest pipeline (empty / zero in saves from before it).
        State.HarvestOrders.Clear();
        if (snapshot.HarvestOrders != null)
            State.HarvestOrders.AddRange(snapshot.HarvestOrders);
        State.HarvestReports.Clear();
        if (snapshot.HarvestReports != null)
            State.HarvestReports.AddRange(snapshot.HarvestReports);
        State.HarvestOrdersPlaced = snapshot.HarvestOrdersPlaced;
        State.HarvestBatchesSold = snapshot.HarvestBatchesSold;
        State.TotalHarvestedHogKg = snapshot.TotalHarvestedHogKg;
        State.TotalHarvestedLiveKg = snapshot.TotalHarvestedLiveKg;
        State.SolvencyWarningPeak = snapshot.SolvencyWarningPeak;
        State.BankruptDay = snapshot.BankruptDay;
        if (snapshot.Crew != null)
            foreach (var member in snapshot.Crew)
                State.Crew.Add(member);
        if (snapshot.ActiveBlooms != null)
            foreach (var bloom in snapshot.ActiveBlooms)
                State.ActiveBlooms.Add(bloom);
        if (snapshot.ExternalHealthWarnings != null)
            foreach (var warning in snapshot.ExternalHealthWarnings)
                State.ExternalHealthWarnings?.Add(warning);
        foreach (var snap in snapshot.DailySnapshots)
            State.DailySnapshots.Add(snap);
        foreach (var snap in snapshot.ProcessingSnapshots)
            State.ProcessingSnapshots.Add(snap);
        foreach (var genome in snapshot.Broodstock)
            State.Broodstock.Add(genome);
        foreach (var id in snapshot.UnlockedResearchIds)
            State.UnlockedResearchIds.Add(id);
        foreach (var id in snapshot.CompletedTutorialStepIds)
            State.CompletedTutorialStepIds.Add(id);
        foreach (var id in snapshot.UnlockedAchievementIds)
            State.UnlockedAchievementIds.Add(id);
        foreach (var market in snapshot.ExportMarkets)
            State.ExportMarkets.Add(market);
        foreach (var facility in snapshot.RASFacilities)
            State.RASFacilities.Add(facility);
        foreach (var farm in snapshot.OffshoreFarms)
            State.OffshoreFarms.Add(farm);
        foreach (var facility in snapshot.PostSmoltFacilities)
            State.PostSmoltFacilities.Add(facility);
        foreach (var policy in snapshot.InsurancePolicies)
            State.InsurancePolicies.Add(policy);
        foreach (var mitigation in snapshot.BloomMitigations)
            State.BloomMitigations.Add(mitigation);

        // Live-system rehydration: TutorialSystem snapshots progress at
        // construction (fresh state) and ResearchSystem owns node flags —
        // both must re-read the adopted fields or the tutorial re-pays
        // finished-step rewards and research effects stay dead after load.
        Tutorial?.ResyncFromState();
        Achievements?.ResyncFromState();
        if (Research != null)
        {
            foreach (var id in State.UnlockedResearchIds)
                Research.ForceUnlock(id);
            if (State.ActiveResearchId != null)
                Research.RestoreProgress(State.ActiveResearchId, State.ResearchProgress);
        }

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
        State.CorporateFinance = snapshot.CorporateFinance;
        foreach (var acq in snapshot.Acquisitions)
            State.Acquisitions.Add(acq);
        foreach (var lic in snapshot.Licenses)
            State.Licenses.Add(lic);
        if (snapshot.LicenseAuctions != null)
            foreach (var auction in snapshot.LicenseAuctions)
                State.LicenseAuctions.Add(auction);
        // S2-01a: the licence register and the biomass orders. A save from
        // before S2-01 has no rules (null): migrated after the load below.
        State.CapacityRules = snapshot.CapacityRules;
        State.LicencesGranted = snapshot.LicencesGranted;
        State.LicenceHoldings.Clear();
        if (snapshot.LicenceHoldings != null)
            State.LicenceHoldings.AddRange(snapshot.LicenceHoldings);
        State.CapacityBreaches.Clear();
        if (snapshot.CapacityBreaches != null)
            State.CapacityBreaches.AddRange(snapshot.CapacityBreaches);
        // F2-03: the plots' saved state (empty in a pre-F2 save and in a game
        // that never built a facility); the catalogue's attributes join below.
        State.SitePlots.Clear();
        if (snapshot.SitePlots != null)
            State.SitePlots.AddRange(snapshot.SitePlots.Where(p => p != null && !string.IsNullOrEmpty(p.Id)));
        AdoptFacilityFinanceState(snapshot); // F2-04: assets, projects, permit cases, ring-fence
        AdoptDriftsplanState(snapshot);      // F2-05: the standing orders
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
        State.CompetitorAI = snapshot.CompetitorAI;
        State.Contracts = snapshot.Contracts;
        State.DiseasePrevalence = snapshot.DiseasePrevalence;
        State.CompanyLice = snapshot.CompanyLice;
        State.Tax = snapshot.Tax;

        // Re-inject dependencies into deserialized populations and restore
        // depth/empire systems + model snapshots (same call the game makes).
        RestoreFromLoad(snapshot);

        // S2-01a: a pre-S2-01 save gets one licence per site (its old MAB) in
        // the site's production area, resolved after the load assigned areas.
        MigrateCapacityRegisterAfterLoad();
        // S2-01b: a save from before real pens gets one pen per population.
        MigratePensAfterLoad();
        // F2-03: the plots get their attributes back from the catalogue, and a
        // pre-F2 save's facilities become the F2 unit kinds (offshore sea sites,
        // post-smolt land sites, land sites on pads). Idempotent.
        RehydrateSitePlotsAfterLoad();
        MigrateFacilities();
    }

    // ─── Phase 4.5: Attention Inbox hook ─────────────────────────────────

    /// <summary>Times the player has opened the Attention Inbox this session
    /// (in-memory only; the panel state itself lives in GDScript).</summary>
    public int AttentionInboxOpenedCount { get; private set; }

    /// <summary>
    /// Player opened the Attention Inbox. UI panel state lives in GDScript;
    /// this engine-side hook keeps one call path for the bridge and lets
    /// headless player-path probes observe the interaction.
    /// </summary>
    public void OpenAttentionInbox() => AttentionInboxOpenedCount++;
}
