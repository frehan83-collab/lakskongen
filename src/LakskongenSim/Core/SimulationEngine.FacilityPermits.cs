using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Land;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>One permit of a land plan, as the build preview's permit timeline shows it (spec §6.2).</summary>
/// <param name="Kind">The permit.</param>
/// <param name="Status">"approved", "pre_zoned", "pending", "refused", "needed".</param>
/// <param name="CaseId">The case that decides it ("" when none).</param>
/// <param name="DecisionDay">A pending case's decision day, a decided case's decision day (-1 when none).</param>
/// <param name="Quote">What a new case would be: decision window, refusal odds and drivers, fee.</param>
/// <param name="CoveredTonnes">Yearly production (t HOG) the approved cases cover (0 for zoning and biosecurity).</param>
public sealed record LandPermitLine(PermitKind Kind, string Status, string CaseId, int DecisionDay, LandPermitQuote Quote,
    double CoveredTonnes);

/// <summary>
/// The preview of a land build (spec §6.2 step 4, F2-14): the budget and its
/// payment schedule per phase with the managed overrun's p50/p90 (turnkey: the
/// premium and no overrun), the schedule with the first tanks and completion
/// (planned, p50, p90), the first harvest, the permit timeline with refusal
/// odds and drivers, the grid, and the verdict: "" when the build may start
/// now, else the first refusal key of <see cref="SimulationEngine.BuildLandSite"/>
/// or <see cref="SimulationEngine.ExpandLandSite"/>. A pure read.
/// </summary>
public sealed record LandBuildPreview
{
    /// <summary>The pad.</summary>
    public string PlotId { get; init; } = "";
    /// <summary>The site being expanded ("" for a new site).</summary>
    public string SiteId { get; init; } = "";
    /// <summary>What the build adds.</summary>
    public LandTemplate Template { get; init; }
    /// <summary>A new lot, or a pilot's expansion.</summary>
    public LandBuildKind Kind { get; init; }
    /// <summary>Managed or turnkey.</summary>
    public ContractForm Contract { get; init; }
    /// <summary>"" when the build may start now; else a <see cref="LandSiteReasons"/> key.</summary>
    public string Verdict { get; init; } = "";
    /// <summary>The permits a build still lacks (empty when none).</summary>
    public IReadOnlyList<PermitKind> MissingPermits { get; init; } = Array.Empty<PermitKind>();
    /// <summary>The unit's own capex (NOK).</summary>
    public decimal UnitCapexNok { get; init; }
    /// <summary>Blasting the pad, paid in the site works of the first build on it (NOK).</summary>
    public decimal BlastingNok { get; init; }
    /// <summary>The denitrification stage the approved discharge permit commits to (NOK).</summary>
    public decimal DenitrificationNok { get; init; }
    /// <summary>The project's budget (NOK): unit, blasting, denitrification.</summary>
    public decimal BudgetNok { get; init; }
    /// <summary>The turnkey premium (NOK; 0 for managed).</summary>
    public decimal PremiumNok { get; init; }
    /// <summary>Median total overrun (NOK; 0 for turnkey).</summary>
    public decimal OverrunP50Nok { get; init; }
    /// <summary>90th-percentile total overrun (NOK; 0 for turnkey).</summary>
    public decimal OverrunP90Nok { get; init; }
    /// <summary>The payments per phase on the planned schedule.</summary>
    public IReadOnlyList<LandPhasePayment> Payments { get; init; } = Array.Empty<LandPhasePayment>();
    /// <summary>Day construction would start (today).</summary>
    public int StartDay { get; init; }
    /// <summary>Planned day of the first tanks (no slip; the closed season counted).</summary>
    public int FirstTanksDay { get; init; }
    /// <summary>Planned day the unit is complete.</summary>
    public int CompleteDay { get; init; }
    /// <summary>The engine's distribution of the outcome (p50/p90 of overrun, first tanks, completion).</summary>
    public LandConstructionPhases.LandBuildRisk Risk { get; init; }
    /// <summary>Days the plan takes from stocking to its harvest weight at the setpoint.</summary>
    public int CycleDays { get; init; }
    /// <summary>Planned day of the first harvest: the first tanks plus a cycle.</summary>
    public int FirstHarvestDay { get; init; }
    /// <summary>The permit timeline: every permit the site needs, with its status and quote.</summary>
    public IReadOnlyList<LandPermitLine> Permits { get; init; } = Array.Empty<LandPermitLine>();
    /// <summary>Probability that at least one needed permit is refused (the needed cases together).</summary>
    public double PackageRefusalProbability { get; init; }
    /// <summary>The site's yearly production after the build (t HOG).</summary>
    public double ProductionTonnes { get; init; }
    /// <summary>The electrical design load of the site's lots after the build (MW).</summary>
    public double DesignPowerMW { get; init; }
    /// <summary>The pad's grid capacity, with finished grid projects (MW).</summary>
    public double GridCapacityMW { get; init; }
    /// <summary>The pad's grid feeds, with finished grid projects.</summary>
    public int GridFeeds { get; init; }
}

/// <summary>A land construction project as the presenter and the boards read it (F2-14).</summary>
/// <param name="SiteId">The land site.</param>
/// <param name="LotId">The lot being built or expanded.</param>
/// <param name="ProjectId">The project ("cp_N").</param>
/// <param name="Kind">"new_lot" or "pilot_expansion".</param>
/// <param name="Template">What is being built.</param>
/// <param name="Contract">"Managed" or "Turnkey".</param>
/// <param name="Status">"Waiting", "Building", "Complete", "Cancelled".</param>
/// <param name="PhaseKey">The current phase ("" once complete).</param>
/// <param name="PhaseIndex">Index of the current phase.</param>
/// <param name="PhaseCount">Phases of the project.</param>
/// <param name="PhaseProgress">Worked share of the current phase (0-1).</param>
/// <param name="Progress">Share of the project's planned days done (0-1).</param>
/// <param name="Suspended">True on a closed-season day the current phase stands still.</param>
/// <param name="FirstTanksDay">Day the first tanks took (or are planned to take) fish.</param>
/// <param name="CompleteDay">Day the unit was (or is planned to be) complete.</param>
/// <param name="BudgetNok">The budget (NOK).</param>
/// <param name="PaidNok">Paid so far (NOK).</param>
/// <param name="Accelerated">True once accelerated.</param>
public sealed record LandConstructionView(string SiteId, string LotId, string ProjectId, string Kind, LandTemplate Template,
    string Contract, string Status, string PhaseKey, int PhaseIndex, int PhaseCount, double PhaseProgress, double Progress,
    bool Suspended, int FirstTanksDay, int CompleteDay, decimal BudgetNok, decimal PaidNok, bool Accelerated);

/// <summary>
/// Land permits, the grid and phased construction (roadmap F2-14,
/// docs/FARM_TYPES_SPEC.md §2C, §3.4, §6.2, §6.3). A land site is applied for
/// on a pad as a package of permit cases (<see cref="ApplyLandPermits"/>:
/// zoning unless the pad is zoned, the land aquaculture permit, the discharge
/// permit, the sea-works permit and the biosecurity approval), each decided on
/// its own day by the shared permit mechanism (F2-04: two uniforms drawn at
/// submission, so the refusal probability shown is the probability decided);
/// a refusal can be appealed (180 days) or applied for again with a
/// mitigation. Once the permits cover the build, <see cref="BuildLandSite"/>
/// starts its construction project (managed or turnkey; phases paid as capex;
/// the sea works never in April-June) and the lot's tanks take fish as its
/// phases end: the HFS lot's first tanks at 22 months and all of it at 30
/// ([L:R1]). <see cref="ExpandLandSite"/> grows a pilot into a full lot or adds
/// the next lot; <see cref="StartGridProject"/> queues capacity or a second
/// feed; the moratorium flag (<c>facilities.land.permits.moratorium</c>)
/// refuses new land applications.
/// </summary>
public partial class SimulationEngine
{
    // ── The pad's permits and grid ─────────────────────────────────────────

    /// <summary>True when the pad is zoned for aquaculture: pre-zoned, or by an approved zoning case.</summary>
    public bool IsPadZoned(string plotId)
    {
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null) return false;
        return plot.PreZoned || PermitCases.Any(c => c.PlotId == plotId && c.Kind == PermitKind.Zoning && c.Status == PermitStatus.Approved);
    }

    /// <summary>Yearly production (t HOG) the approved <paramref name="kind"/> cases on a pad cover (the largest).</summary>
    public double PermittedProductionTonnes(string plotId, PermitKind kind)
        => PermitCases.Where(c => c.PlotId == plotId && c.Kind == kind && c.Status == PermitStatus.Approved)
            .Select(c => c.Conditions.TryGetValue(LandPermitSystem.MaxProductionKey, out var t) ? t : 0.0)
            .DefaultIfEmpty(0.0).Max();

    /// <summary>The pad's grid capacity (MW): the catalogue's, plus every finished capacity project.</summary>
    public double EffectiveGridCapacityMW(string plotId)
    {
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        return (plot?.GridCapacityMW ?? 0.0) + FinishedGridProjects(plotId, LandPermitSystem.AddGridMwKey);
    }

    /// <summary>The pad's grid feeds: the plot's, plus every finished second-feed project (a feed counts only once built).</summary>
    public int EffectiveGridFeeds(string plotId)
    {
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        return (plot?.GridFeeds ?? 0) + (int)Math.Round(FinishedGridProjects(plotId, LandPermitSystem.AddGridFeedsKey));
    }

    private double FinishedGridProjects(string plotId, string key)
        => PermitCases.Where(c => c.PlotId == plotId && c.Kind == PermitKind.GridConnection && c.Status == PermitStatus.Approved)
            .Sum(c => c.Conditions.TryGetValue(key, out var v) ? v : 0.0);

    /// <summary>The electrical design load (MW) of <paramref name="templates"/> on a pad.</summary>
    private double DesignPowerMW(SitePlot plot, IEnumerable<LandTemplate> templates)
    {
        var site = new LandIntakeSite(plot.StaticLiftM, 0.0, Math.Max(0.0, plot.DeepIntakeReachM) / 1000.0, plot.PumpCapacityM3sPerLot);
        return templates.Sum(t => LandConstructionPhases.DesignPowerMW(t, LandCalibration, site));
    }

    /// <summary>
    /// The permit timeline of a land plan on a pad: <paramref name="lots"/>
    /// units of <paramref name="template"/> in total on the pad (the yearly
    /// production the permits must cover), with <paramref name="mitigations"/>.
    /// Each line says whether the permit is approved, pending, refused or
    /// still needed, and what a new case would be. A pure read.
    /// </summary>
    public IReadOnlyList<LandPermitLine> QuoteLandPermits(string plotId, LandTemplate template, int lots = 1,
        IReadOnlyList<string>? mitigations = null)
    {
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || State == null) return Array.Empty<LandPermitLine>();
        double production = LandCalibration.For(template).NameplateTonnesHogPerYear * Math.Max(1, lots);
        return PermitLines(plot, production, mitigations ?? Array.Empty<string>());
    }

    private IReadOnlyList<LandPermitLine> PermitLines(SitePlot plot, double productionTonnes, IReadOnlyCollection<string> mitigations)
    {
        var permits = LandCalibration.Permits;
        double scale = LandPermitSystem.ScaleFactor(productionTonnes, LandCalibration.Hfs.NameplateTonnesHogPerYear, permits);
        var lines = new List<LandPermitLine>();
        bool survey = mitigations.Contains(LandPermitSystem.EnvironmentalSurvey);
        bool surveyCharged = false;
        foreach (var kind in LandPermitSystem.SiteKinds)
        {
            var cases = PermitCases.Where(c => c.PlotId == plot.Id && c.Kind == kind).ToList();
            bool byProduction = kind is PermitKind.LandAquaculture or PermitKind.Discharge or PermitKind.SeaWorks;
            double covered = byProduction ? PermittedProductionTonnes(plot.Id, kind) : 0.0;
            PermitCase? approved = cases.LastOrDefault(c => c.Status == PermitStatus.Approved
                && (!byProduction || Coverage(c) >= productionTonnes - 1e-6));
            PermitCase? pending = cases.LastOrDefault(c => c.IsPending
                && (!byProduction || Coverage(c) >= productionTonnes - 1e-6));
            PermitCase? latest = cases.LastOrDefault();
            string status;
            PermitCase? shown;
            if (kind == PermitKind.Zoning && plot.PreZoned) { status = "pre_zoned"; shown = null; }
            else if (approved != null) { status = "approved"; shown = approved; }
            else if (pending != null) { status = "pending"; shown = pending; }
            else if (latest != null && latest.Status == PermitStatus.Refused) { status = "refused"; shown = latest; }
            else { status = "needed"; shown = null; }
            bool chargeSurvey = survey && !surveyCharged && status is "needed" or "refused";
            if (chargeSurvey) surveyCharged = true;
            var quote = LandPermitSystem.Quote(kind, plot.NatureSensitivity, scale, mitigations, permits,
                chargeSurvey ? permits.SurveyCostNok : 0m);
            int decision = shown == null ? -1 : shown.IsPending ? shown.DecisionDay : shown.DecidedDay;
            lines.Add(new LandPermitLine(kind, status, shown?.Id ?? "", decision, quote, covered));
        }
        return lines;
    }

    private static double Coverage(PermitCase c)
        => c.Conditions.TryGetValue(LandPermitSystem.MaxProductionKey, out var t) ? t : 0.0;

    // ── Applications, appeals, grid projects ───────────────────────────────

    /// <summary>
    /// Apply for every permit a land plan on a pad still needs (spec §6.2 step
    /// 5: "Land submits permit cases"): <paramref name="lots"/> units of
    /// <paramref name="template"/> in total on the pad, with
    /// <paramref name="mitigations"/> (<see cref="LandPermitSystem.Mitigations"/>).
    /// The needed cases are filed at once, each on its quoted terms (the
    /// environmental survey's cost with the first). Fails before any change:
    /// engine_not_ready, not_found (no such pad), out_of_range (lots, an
    /// unknown mitigation), plot_taken, lots_exhausted, not_unlocked,
    /// moratorium, already_pending, nothing_to_apply, insufficient_funds.
    /// Value = the package's refusal probability, Value2 = cases filed.
    /// </summary>
    public CommandResult ApplyLandPermits(string plotId, LandTemplate template, int lots = 1, IReadOnlyList<string>? mitigations = null)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || plot.Kind != PlotKind.LandPad)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land pad '{plotId}'");
        var offered = (mitigations ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (!Enum.IsDefined(template) || lots < 1 || LandPermitSystem.UnknownMitigation(offered) is { } unknown)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Template {template}, {lots} lots, mitigations {string.Join(",", offered)}");
        var site = LandSiteOnPlot(plot);
        if (!plot.IsFree && site == null)
            return CommandResult.Fail(LandSiteReasons.PlotTaken, $"'{plotId}' is held by '{plot.OwnerUnitId}'");
        if (lots > PadRoomFor(plot, template))
            return CommandResult.Fail(LandSiteReasons.LotsExhausted, $"'{plotId}' holds {PadRoomFor(plot, template)} units of {template}");
        if (!GatesMetForApplication(template))
            return CommandResult.Fail(LandSiteReasons.NotUnlocked, $"{FarmTypeVariantOf(template)} is locked");

        double production = LandCalibration.For(template).NameplateTonnesHogPerYear * lots;
        var lines = PermitLines(plot, production, offered);
        if (lines.Any(l => l.Status == "pending"))
            return CommandResult.Fail(LandSiteReasons.AlreadyPending,
                $"Pending on '{plotId}': {string.Join(", ", lines.Where(l => l.Status == "pending").Select(l => l.Kind))}");
        var needed = lines.Where(l => l.Status is "needed" or "refused").ToList();
        if (needed.Count == 0)
            return CommandResult.Fail(LandSiteReasons.NothingToApply, $"Every permit for {production:N0} t on '{plotId}' is approved");
        if (LandCalibration.Permits.Moratorium && needed.Any(l => LandPermitSystem.HeldByMoratorium(l.Kind)))
            return CommandResult.Fail(LandSiteReasons.Moratorium, "A moratorium holds new land aquaculture applications");
        decimal fees = needed.Sum(l => l.Quote.FeeNok);
        if (FinanceManager.Balance < fees)
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"The applications cost {fees:N0} NOK", (double)fees);

        var conditions = PlanConditions(template, lots, production);
        var filed = new List<string>();
        foreach (var line in needed)
        {
            var submitted = SubmitPermitCase(line.Kind, plot.Id, site?.Id ?? "", line.Quote.Terms, offered,
                line.Kind is PermitKind.Zoning or PermitKind.Biosecurity ? null : conditions);
            if (!submitted.Ok) return submitted; // cannot happen after the checks above
            filed.Add(submitted.Id ?? "");
        }
        double package = 1.0 - needed.Aggregate(1.0, (q, l) => q * (1.0 - l.Quote.RefusalProbability));
        return CommandResult.Success($"Applied for {needed.Count} permits on {plot.Id}: {string.Join(", ", filed)}",
            package, filed.Count, string.Join(",", filed));
    }

    /// <summary>
    /// Apply for zoning alone on a pad (spec §6.1: the HFS card's "zoned pad"
    /// gate; the municipality's plan does not wait for the research). Fails:
    /// not_found, out_of_range, plot_taken, already_pending, nothing_to_apply
    /// (the pad is zoned), insufficient_funds.
    /// </summary>
    public CommandResult ApplyForZoning(string plotId, IReadOnlyList<string>? mitigations = null)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || plot.Kind != PlotKind.LandPad)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land pad '{plotId}'");
        var offered = (mitigations ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (LandPermitSystem.UnknownMitigation(offered) is { } unknown)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown mitigation '{unknown}'");
        if (!plot.IsFree && LandSiteOnPlot(plot) == null)
            return CommandResult.Fail(LandSiteReasons.PlotTaken, $"'{plotId}' is held by '{plot.OwnerUnitId}'");
        var line = PermitLines(plot, LandCalibration.Hfs.NameplateTonnesHogPerYear, offered).Single(l => l.Kind == PermitKind.Zoning);
        if (line.Status == "pending")
            return CommandResult.Fail(LandSiteReasons.AlreadyPending, $"Zoning of '{plotId}' is pending");
        if (line.Status is "approved" or "pre_zoned")
            return CommandResult.Fail(LandSiteReasons.NothingToApply, $"'{plotId}' is zoned");
        return SubmitPermitCase(PermitKind.Zoning, plot.Id, LandSiteOnPlot(plot)?.Id ?? "", line.Quote.Terms, offered);
    }

    /// <summary>
    /// Appeal a refused land case (spec §2C, F2-14 acceptance 2): decided in
    /// 180 days at the refused case's probability × the appeal factor (drawn
    /// now, two uniforms). Fails: not_found, not_refused, insufficient_funds.
    /// </summary>
    public CommandResult AppealLandPermit(string caseId)
    {
        var permit = PermitCases.FirstOrDefault(c => c.Id == caseId);
        if (permit == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No permit case '{caseId}'");
        if (permit.Status != PermitStatus.Refused)
            return CommandResult.Fail(LandSiteReasons.NotRefused, $"{caseId} is {permit.Status}, not refused");
        var quote = LandPermitSystem.AppealQuote(permit.Kind, permit.RefusalProbability, LandCalibration.Permits);
        return AppealPermitCase(caseId, quote.Terms);
    }

    /// <summary>
    /// Apply again for a refused land case's permit with
    /// <paramref name="mitigations"/> added (F2-14 acceptance 2): a new case on
    /// the same pad for the same production, on terms quoted with the
    /// mitigations. Fails: not_found, not_refused, out_of_range, moratorium,
    /// already_pending, insufficient_funds.
    /// </summary>
    public CommandResult ReapplyLandPermit(string caseId, IReadOnlyList<string>? mitigations = null)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var refused = PermitCases.FirstOrDefault(c => c.Id == caseId);
        if (refused == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No permit case '{caseId}'");
        if (refused.Status != PermitStatus.Refused)
            return CommandResult.Fail(LandSiteReasons.NotRefused, $"{caseId} is {refused.Status}, not refused");
        var offered = refused.Mitigations.Concat(mitigations ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToList();
        if (LandPermitSystem.UnknownMitigation(offered) is { } unknown)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown mitigation '{unknown}'");
        if (LandCalibration.Permits.Moratorium && LandPermitSystem.HeldByMoratorium(refused.Kind))
            return CommandResult.Fail(LandSiteReasons.Moratorium, "A moratorium holds new land aquaculture applications");
        var plot = SitePlots.FirstOrDefault(p => p.Id == refused.PlotId);
        double production = Coverage(refused) > 0.0 ? Coverage(refused) : LandCalibration.Hfs.NameplateTonnesHogPerYear;
        double scale = LandPermitSystem.ScaleFactor(production, LandCalibration.Hfs.NameplateTonnesHogPerYear, LandCalibration.Permits);
        bool newSurvey = offered.Contains(LandPermitSystem.EnvironmentalSurvey) && !refused.Mitigations.Contains(LandPermitSystem.EnvironmentalSurvey);
        var quote = LandPermitSystem.Quote(refused.Kind, plot?.NatureSensitivity ?? 0.0, scale, offered, LandCalibration.Permits,
            newSurvey ? LandCalibration.Permits.SurveyCostNok : 0m);
        var result = SubmitPermitCase(refused.Kind, refused.PlotId, refused.UnitId, quote.Terms, offered, refused.Conditions);
        return result.Ok || result.ErrorKey != "already_pending"
            ? result
            : CommandResult.Fail(LandSiteReasons.AlreadyPending, result.Message);
    }

    /// <summary>
    /// Queue a grid project at a pad (spec §2C, F2-14 acceptance 7):
    /// <c>"capacity"</c> adds the calibration's step (10 MW) and
    /// <c>"second_feed"</c> a second, independent feed, each counted only once
    /// it is finished, 365-1,095 days on; the connection fee per 10 MW is paid
    /// now as opex. Fails: not_found, out_of_range (an unknown kind),
    /// plot_taken, already_pending, insufficient_funds.
    /// </summary>
    public CommandResult StartGridProject(string plotId, string kind)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot == null || plot.Kind != PlotKind.LandPad)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land pad '{plotId}'");
        if (kind != LandPermitSystem.GridCapacity && kind != LandPermitSystem.GridSecondFeed)
            return CommandResult.Fail(LandSiteReasons.OutOfRange, $"Unknown grid project '{kind}'");
        var site = LandSiteOnPlot(plot);
        if (!plot.IsFree && site == null)
            return CommandResult.Fail(LandSiteReasons.PlotTaken, $"'{plotId}' is held by '{plot.OwnerUnitId}'");
        var permits = LandCalibration.Permits;
        bool capacity = kind == LandPermitSystem.GridCapacity;
        double connected = capacity ? permits.GridProjectStepMW : EffectiveGridCapacityMW(plotId);
        var quote = LandPermitSystem.GridQuote(connected, permits);
        var conditions = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [capacity ? LandPermitSystem.AddGridMwKey : LandPermitSystem.AddGridFeedsKey] = capacity ? permits.GridProjectStepMW : 1.0,
        };
        var result = SubmitPermitCase(PermitKind.GridConnection, plot.Id, site?.Id ?? "", quote.Terms, null, conditions);
        return result.Ok || result.ErrorKey != "already_pending"
            ? result
            : CommandResult.Fail(LandSiteReasons.AlreadyPending, result.Message);
    }

    /// <summary>The company's land site on <paramref name="plot"/>, or null.</summary>
    private RASFacility? LandSiteOnPlot(SitePlot plot)
        => string.IsNullOrEmpty(plot.OwnerUnitId) ? null : FindLandSite(plot.OwnerUnitId) is { } s && s.PlotId == plot.Id ? s : null;

    /// <summary>Every gate of the template's variant but the zoned pad (the package zones it) and money (paid at the build).</summary>
    private bool GatesMetForApplication(LandTemplate template)
    {
        var status = FarmTypeStatus(FarmTypeVariantOf(template));
        return status != null && status.Gates
            .Where(g => g.Gate.Kind is not FarmTypeGateKind.ZonedPad and not FarmTypeGateKind.Funds)
            .All(g => g.Met);
    }

    /// <summary>The conditions a land plan's permits carry: yearly production, standing biomass, density limit.</summary>
    private Dictionary<string, double> PlanConditions(LandTemplate template, int lots, double productionTonnes)
    {
        var t = LandCalibration.For(template);
        double volume = t.GrowOutTanks * LandTank.VolumeOf(t.GrowOutDiameterM, t.WaterDepthM)
            + t.PreGrowTanks * LandTank.VolumeOf(t.PreGrowDiameterM, t.WaterDepthM);
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [LandPermitSystem.MaxProductionKey] = productionTonnes,
            [LandPermitSystem.MaxStandingKey] = lots * volume * t.MaxDensityKgM3 / 1000.0,
            [LandPermitSystem.MaxDensityKey] = t.MaxDensityKgM3,
        };
    }

    // ── Building ───────────────────────────────────────────────────────────

    /// <summary>
    /// The preview of building <paramref name="template"/> as a new site on
    /// <paramref name="plotId"/> under <paramref name="contract"/> (spec §6.2
    /// step 4). <paramref name="mitigations"/> price the permit cases still to
    /// file. A pure read: nothing changes and nothing draws.
    /// </summary>
    public LandBuildPreview PreviewLandBuild(string plotId, LandTemplate template, ContractForm contract = ContractForm.Managed,
        IReadOnlyList<string>? mitigations = null)
    {
        var plot = SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (State == null || plot == null || plot.Kind != PlotKind.LandPad || !Enum.IsDefined(template))
            return new LandBuildPreview { PlotId = plotId ?? "", Template = template, Contract = contract, Verdict = LandSiteReasons.NotFound };
        return BuildPreview(plot, null, template, LandBuildKind.NewLot, contract, mitigations ?? Array.Empty<string>());
    }

    /// <summary>The preview of <see cref="ExpandLandSite"/> on <paramref name="siteId"/> (a pilot's expansion, or its next lot).</summary>
    public LandBuildPreview PreviewLandExpansion(string siteId, ContractForm contract = ContractForm.Managed,
        IReadOnlyList<string>? mitigations = null)
    {
        var site = FindLandSite(siteId);
        var plot = site == null ? null : SitePlots.FirstOrDefault(p => p.Id == site.PlotId);
        if (State == null || site == null || !site.HasLots || plot == null)
            return new LandBuildPreview { SiteId = siteId ?? "", Contract = contract, Verdict = LandSiteReasons.NotFound };
        var (template, kind) = NextBuildOf(site);
        return BuildPreview(plot, site, template, kind, contract, mitigations ?? Array.Empty<string>());
    }

    /// <summary>What expanding a site builds: a pilot lot's expansion, else another unit of its first lot's template.</summary>
    private static (LandTemplate Template, LandBuildKind Kind) NextBuildOf(RASFacility site)
    {
        var pilot = site.Lots.FirstOrDefault(l => l.Template == LandTemplate.HfsPilotLot && string.IsNullOrEmpty(l.ExpansionProjectId));
        if (pilot != null) return (LandTemplate.HfsLot, LandBuildKind.PilotExpansion);
        var first = site.Lots[0].Template;
        return (first == LandTemplate.HfsPilotLot ? LandTemplate.HfsLot : first, LandBuildKind.NewLot);
    }

    private LandBuildPreview BuildPreview(SitePlot plot, RASFacility? site, LandTemplate template, LandBuildKind kind,
        ContractForm contract, IReadOnlyCollection<string> mitigations)
    {
        var cal = LandCalibration;
        var risk = _calibration.Facilities.Common.Construction;
        // The site's units after the build: its lots (a pilot being expanded counts as full) and the new one.
        var after = site == null
            ? new List<LandTemplate> { template }
            : site.Lots.Select(l => l.Template == LandTemplate.HfsPilotLot
                    && (!string.IsNullOrEmpty(l.ExpansionProjectId) || kind == LandBuildKind.PilotExpansion)
                    ? LandTemplate.HfsLot : l.Template)
                .Concat(kind == LandBuildKind.NewLot ? new[] { template } : Array.Empty<LandTemplate>()).ToList();
        double production = after.Sum(t => cal.For(t).NameplateTonnesHogPerYear);
        var lines = PermitLines(plot, production, mitigations);
        var missing = lines.Where(l => l.Status is not ("approved" or "pre_zoned")).Select(l => l.Kind).ToList();
        bool denitrification = PermitCases.Any(c => c.PlotId == plot.Id && c.Kind == PermitKind.Discharge
            && c.Status == PermitStatus.Approved && c.Mitigations.Contains(LandPermitSystem.Denitrification));
        decimal unit = LandConstructionPhases.UnitCapexNok(cal.Build, template, kind);
        decimal blasting = site == null && kind == LandBuildKind.NewLot ? plot.BlastCostNOK : 0m;
        decimal denitrify = denitrification ? unit * (decimal)cal.Build.DenitrificationCapexShare : 0m;
        var plan = LandConstructionPhases.PlanOf(cal.Build, template, kind);
        var phases = LandConstructionPhases.Phases(plan, cal.Permits, unit + denitrify, blasting);
        decimal budget = unit + denitrify + (plan.SiteWorksDays > 0 ? blasting : 0m);
        int start = State.Day;
        var schedule = LandConstructionPhases.Schedule(phases, start);
        var payments = LandConstructionPhases.Payments(phases, budget, contract, risk, start);
        var outcome = LandConstructionPhases.Risk(phases, contract, risk, start);
        var t = cal.For(template);
        var forecast = LandStockingPlanner.Project(Growth.Law, LandStockingPlan.For(t, cal.Production)
            with { TemperatureC = site?.TargetWaterTemp ?? t.DefaultSetpointC });
        double designMw = DesignPowerMW(plot, after);
        double gridMw = EffectiveGridCapacityMW(plot.Id);

        string verdict = "";
        if (site == null && !plot.IsFree) verdict = LandSiteReasons.PlotTaken;
        else if (site != null && site.Lots.Any(l => !IsLotCommissionedNow(l) || !string.IsNullOrEmpty(l.ExpansionProjectId) && l.Template == LandTemplate.HfsPilotLot))
            verdict = LandSiteReasons.NotReady;
        else if (after.Count(a => a == LandTemplate.PostSmoltModule) > plot.PostSmoltModules
                 || after.Count(a => a != LandTemplate.PostSmoltModule) > plot.PhaseLots)
            verdict = LandSiteReasons.LotsExhausted;
        // Every gate but money and the zoned pad: the build's own pad must be
        // zoned, which the permits check names (a site's own expansion was
        // unlocked when it was built).
        else if (site == null && !GatesMetForApplication(template))
            verdict = LandSiteReasons.NotUnlocked;
        else if (missing.Count > 0) verdict = LandSiteReasons.PermitsMissing;
        else if (designMw > gridMw + 1e-9) verdict = LandSiteReasons.GridCapacity;
        else if (FinanceManager != null && payments.Count > 0 && FinanceManager.Balance < payments[0].PaymentNok)
            verdict = LandSiteReasons.InsufficientFunds;

        double package = 1.0 - lines.Where(l => l.Status is "needed" or "refused")
            .Aggregate(1.0, (q, l) => q * (1.0 - l.Quote.RefusalProbability));
        decimal premium = contract == ContractForm.Turnkey ? budget * (decimal)risk.TurnkeyPremium : 0m;
        return new LandBuildPreview
        {
            PlotId = plot.Id,
            SiteId = site?.Id ?? "",
            Template = template,
            Kind = kind,
            Contract = contract,
            Verdict = verdict,
            MissingPermits = missing,
            UnitCapexNok = unit,
            BlastingNok = plan.SiteWorksDays > 0 ? blasting : 0m,
            DenitrificationNok = denitrify,
            BudgetNok = budget,
            PremiumNok = premium,
            OverrunP50Nok = budget * (decimal)outcome.OverrunP50,
            OverrunP90Nok = budget * (decimal)outcome.OverrunP90,
            Payments = payments,
            StartDay = start,
            FirstTanksDay = schedule.FirstTanksDay,
            CompleteDay = schedule.CompleteDay,
            Risk = outcome,
            CycleDays = forecast.CycleDays,
            FirstHarvestDay = schedule.FirstTanksDay + forecast.CycleDays,
            Permits = lines,
            PackageRefusalProbability = package,
            ProductionTonnes = production,
            DesignPowerMW = designMw,
            GridCapacityMW = gridMw,
            GridFeeds = EffectiveGridFeeds(plot.Id),
        };
    }

    /// <summary>
    /// Start building <paramref name="template"/> as a new land site on
    /// <paramref name="plotId"/> (spec §3.4 <c>BuildLandSite</c>): the site
    /// and its lot exist from today (the presenter shows the build), its
    /// construction project starts (managed or turnkey, phases paid as capex,
    /// the pad's blasting in the site works) and its tanks take fish as the
    /// project's phases end. Fails before any change with the preview's
    /// verdict: not_found, plot_taken, lots_exhausted, not_unlocked,
    /// permits_missing, grid_capacity, insufficient_funds.
    /// </summary>
    public CommandResult BuildLandSite(string plotId, LandTemplate template, ContractForm contract = ContractForm.Managed, string name = "")
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        var preview = PreviewLandBuild(plotId, template, contract);
        if (preview.Verdict != "")
            return CommandResult.Fail(preview.Verdict, VerdictMessage(preview));
        var plot = SitePlots.First(p => p.Id == plotId);
        var site = NewLandSite(plot, template, name);
        site.DenitrificationCommitted = preview.DenitrificationNok > 0m;
        var started = StartLandProject(site, template, LandBuildKind.NewLot, contract, preview);
        if (!started.Ok) return started;
        ApplyDriftsplanDefaults(site.Id); // F2-05: the land lane's default standing orders (F2-15/F2-16 add them)
        LogFacilityBuilt("Landanlegg under bygging",
            SimText.Nb($"{site.Name} bygges på {plot.Name}: {preview.BudgetNok:N0} kr, første kar dag {preview.FirstTanksDay}"),
            0m);
        return CommandResult.Success($"Construction of '{site.Name}' ({template}) started on {plot.Id}",
            (double)preview.BudgetNok, preview.FirstTanksDay, site.Id);
    }

    /// <summary>
    /// Grow a land site (spec §3.4 <c>ExpandLandSite</c>): a pilot lot into a
    /// full HFS lot (+1.25 bn, no blasting, no sea works), else the next lot
    /// or module of its template on the same pad. Fails with the preview's
    /// verdict: not_found, not_ready (a lot still being built or expanded),
    /// lots_exhausted, not_unlocked, permits_missing (the land, discharge and
    /// sea-works permits must cover the site's production after it),
    /// grid_capacity, insufficient_funds.
    /// </summary>
    public CommandResult ExpandLandSite(string siteId, ContractForm contract = ContractForm.Managed)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        if (FindLandSite(siteId) is { HasLots: true } synced) SyncLandSite(synced);
        var preview = PreviewLandExpansion(siteId, contract);
        if (preview.Verdict != "")
            return CommandResult.Fail(preview.Verdict, VerdictMessage(preview));
        var site = FindLandSite(siteId)!;
        var started = StartLandProject(site, preview.Template, preview.Kind, contract, preview);
        if (!started.Ok) return started;
        return CommandResult.Success($"{(preview.Kind == LandBuildKind.PilotExpansion ? "Expansion" : "Next lot")} of '{site.Name}' started",
            (double)preview.BudgetNok, preview.CompleteDay, started.Id);
    }

    private static string VerdictMessage(LandBuildPreview preview) => preview.Verdict switch
    {
        LandSiteReasons.PermitsMissing => $"Missing permits: {string.Join(", ", preview.MissingPermits)}",
        LandSiteReasons.GridCapacity => $"The build needs {preview.DesignPowerMW:F1} MW; the pad's grid carries {preview.GridCapacityMW:F1} MW",
        LandSiteReasons.InsufficientFunds => $"The first phase costs {(preview.Payments.Count > 0 ? preview.Payments[0].PaymentNok : 0m):N0} NOK",
        _ => $"Cannot build {preview.Template} on '{preview.PlotId}': {preview.Verdict}",
    };

    /// <summary>The construction project of a build, and its lot (a new lot, or the pilot it expands).</summary>
    private CommandResult StartLandProject(RASFacility site, LandTemplate template, LandBuildKind kind, ContractForm contract,
        LandBuildPreview preview)
    {
        var cal = LandCalibration;
        var plan = LandConstructionPhases.PlanOf(cal.Build, template, kind);
        var phases = LandConstructionPhases.Phases(plan, cal.Permits, preview.UnitCapexNok + preview.DenitrificationNok, preview.BlastingNok);
        var project = StartConstructionProject(site.Id, FacilityAssetKind.LandSite, preview.BudgetNok, phases, contract);
        if (!project.Ok) return project;
        if (kind == LandBuildKind.PilotExpansion)
            site.Lots.First(l => l.Template == LandTemplate.HfsPilotLot && string.IsNullOrEmpty(l.ExpansionProjectId))
                .ExpansionProjectId = project.Id!;
        else
            AddLot(site, template, project.Id!, -1);
        site.CAPEX += preview.BudgetNok;
        RefreshLandMirrors(site);
        return project;
    }

    /// <summary>
    /// Accelerate a land project's current phase (spec §6.3): pay the
    /// calibration's share (15 %) of the phase's remaining budget now, as
    /// capex, and cut its remaining days by the calibration's share (20 %).
    /// Once a project. Fails: not_found, not_ready (not building, or already
    /// accelerated), insufficient_funds.
    /// </summary>
    public CommandResult AccelerateLandConstruction(string projectId)
    {
        var project = FindProject(projectId);
        if (project == null || FindLandSite(project.UnitId) == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land construction project '{projectId}'");
        if (project.Status != ConstructionStatus.Building || project.Accelerated || project.Current == null)
            return CommandResult.Fail(LandSiteReasons.NotReady, $"{projectId} has no phase under way to accelerate");
        var build = LandCalibration.Build;
        int remaining = Math.Max(0, project.PhaseEndDay - State.Day);
        double remainingShare = project.PhaseDays > 0 ? (double)remaining / project.PhaseDays : 0.0;
        decimal phaseBudget = project.BudgetNok * (decimal)project.Current.CapexShare * (decimal)project.EquityShare;
        decimal cost = phaseBudget * (decimal)(build.AccelerateCostShare * remainingShare);
        if (!TryPayCost(cost, CostBooking.Capex, "facilities"))
            return CommandResult.Fail(LandSiteReasons.InsufficientFunds, $"Acceleration costs {cost:N0} NOK", (double)cost);
        int cut = (int)Math.Round(remaining * build.AccelerateTimeCut, MidpointRounding.AwayFromZero);
        project.PhaseDays = Math.Max(1, project.PhaseDays - cut);
        project.PaidNOK += cost;
        project.Accelerated = true;
        return CommandResult.Success($"{projectId}: phase '{project.Current.Key}' {cut} days sooner for {cost:N0} NOK",
            (double)cost, cut, projectId);
    }

    /// <summary>
    /// Cancel a land project that has handed over no tank yet (F2-14
    /// acceptance 5): no later phase starts or is paid, the money paid stays
    /// spent, and the lot it built goes (the site too when it has no lot left,
    /// freeing the pad); a cancelled expansion leaves the pilot a pilot.
    /// Fails: not_found, not_ready (complete, cancelled, or tanks already
    /// handed over).
    /// </summary>
    public CommandResult CancelLandConstruction(string projectId)
    {
        var project = FindProject(projectId);
        if (project == null || project.AssetKind != FacilityAssetKind.LandSite)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land construction project '{projectId}'");
        if (project.Status is ConstructionStatus.Complete or ConstructionStatus.Cancelled)
            return CommandResult.Fail(LandSiteReasons.NotReady, $"{projectId} is {project.Status}");
        var site = FindLandSite(project.UnitId);
        if (site == null)
            return CommandResult.Fail(LandSiteReasons.NotFound, $"No land site '{project.UnitId}'");
        SyncLandSite(site);
        var lot = site.Lots.FirstOrDefault(l => l.ConstructionProjectId == projectId);
        var expanded = site.Lots.FirstOrDefault(l => l.ExpansionProjectId == projectId);
        if (lot != null && lot.IsCommissioned)
            return CommandResult.Fail(LandSiteReasons.NotReady, $"{lot.Id} already holds ready tanks");
        project.Status = ConstructionStatus.Cancelled;
        if (expanded != null) expanded.ExpansionProjectId = "";
        if (lot != null) site.Lots.Remove(lot);
        if (site.Lots.Count == 0 && site.Tanks.Count == 0)
        {
            RASFacilities.Remove(site);
            var plot = FindPlot(site.PlotId);
            if (plot != null && plot.OwnerUnitId == site.Id) plot.OwnerUnitId = "";
        }
        else
        {
            RefreshLandMirrors(site);
        }
        LogFacilityEvent("Byggeprosjekt avlyst",
            SimText.Nb($"{site.Name}: prosjektet {projectId} er avlyst; {project.PaidNOK:N0} kr er brukt"), 0m);
        return CommandResult.Success($"{projectId} cancelled; {project.PaidNOK:N0} NOK spent", (double)project.PaidNOK, id: projectId);
    }

    // ── Views ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every land construction project with its phase and progress (the
    /// presenter's construction state per unit: phase, progress 0-1), its
    /// first tanks and completion (done, or planned from today), its money.
    /// </summary>
    public IReadOnlyList<LandConstructionView> LandConstructionViews()
    {
        var views = new List<LandConstructionView>();
        if (State == null) return views;
        foreach (var site in LandSites)
        {
            foreach (var lot in site.Lots)
            {
                foreach (var (projectId, kind) in new[] { (lot.ConstructionProjectId, "new_lot"), (lot.ExpansionProjectId, "pilot_expansion") })
                {
                    var project = FindProject(projectId);
                    if (project == null) continue;
                    var (first, complete) = PlannedDaysOf(project, lot, kind == "pilot_expansion");
                    var phase = project.Current;
                    bool suspended = project.Status == ConstructionStatus.Building && phase is { SuspendedInClosedMonths: true }
                        && phase.ClosedMonths.Contains(SimCalendar.Month(State.Day));
                    views.Add(new LandConstructionView(site.Id, lot.Id, project.Id, kind,
                        kind == "pilot_expansion" ? LandTemplate.HfsLot : lot.Template, project.Contract.ToString(),
                        project.Status.ToString(), phase?.Key ?? "", Math.Min(project.CurrentPhase, project.Phases.Count),
                        project.Phases.Count, PhaseProgressOf(project), ProjectProgressOf(project), suspended, first, complete,
                        project.BudgetNok, project.PaidNOK, project.Accelerated));
                }
            }
        }
        return views;
    }

    /// <summary>A project's first-tanks and completion days: done, or planned from today on its remaining phases.</summary>
    private (int FirstTanks, int Complete) PlannedDaysOf(ConstructionProject project, LandLot lot, bool expansion)
    {
        if (project.Status == ConstructionStatus.Complete || project.Status == ConstructionStatus.Cancelled)
        {
            int end = project.PhaseCompletedDays.Count > 0 ? project.PhaseCompletedDays[^1] : -1;
            return (expansion ? end : LotDaysNow(lot).FirstTanksDay, end);
        }
        // The phase under way ends on its end day; the phases after it run their planned days from there.
        bool building = project.Status == ConstructionStatus.Building;
        int start = building ? project.PhaseEndDay : State.Day;
        int from = building ? project.CurrentPhase + 1 : project.CurrentPhase;
        var rest = project.Phases.Skip(from).ToList();
        var schedule = LandConstructionPhases.Schedule(rest, start);
        int complete = rest.Count == 0 ? start : schedule.CompleteDay;
        if (expansion) return (complete, complete);
        if (LotDaysNow(lot).FirstTanksDay is int done and >= 0) return (done, complete);
        int firstIndex = project.Phases.FindIndex(p => p.Key == LandConstructionPhases.FirstTanks);
        if (firstIndex < 0) return (complete, complete);
        if (firstIndex < project.PhaseCompletedDays.Count) return (project.PhaseCompletedDays[firstIndex], complete);
        if (building && firstIndex == project.CurrentPhase) return (start, complete);
        return (firstIndex - from >= 0 && firstIndex - from < schedule.Phases.Count ? schedule.Phases[firstIndex - from].EndDay : complete, complete);
    }
}
