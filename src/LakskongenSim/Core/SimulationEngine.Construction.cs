using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Environment;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// Facility construction projects and permit cases (roadmap F2-04,
/// docs/FARM_TYPES_SPEC.md §3.2-§3.7): the mechanics every farm type's build
/// shares. The lanes define the phase lists and the permit terms in their own
/// files and file projects and cases through these verbs (F2-07's offshore
/// build, F2-14's land build and permits); the post-tick phase
/// <see cref="ConstructionAndPermitsTickSystem"/> advances them. A game with
/// no project and no case draws nothing and books nothing here.
/// </summary>
public partial class SimulationEngine
{
    // ─── Construction projects ──────────────────────────────────────────────

    /// <summary>
    /// Start a construction project for <paramref name="unitId"/>: its phases
    /// (each with a planned length and a share of the budget; the shares add
    /// up to 1), the contract and the company's equity share. The first phase
    /// starts at once unless the player starts it, it falls in a closed month
    /// or its payment cannot be made; it then waits and is retried each day.
    /// Fails before any change: engine_not_ready, invalid_project (no unit, no
    /// phase, a non-positive budget or equity, a phase without days, duplicate
    /// keys, or shares that do not add up to 1).
    /// </summary>
    public CommandResult StartConstructionProject(string unitId, FacilityAssetKind kind, decimal budgetNok,
        IReadOnlyList<ConstructionPhase> phases, ContractForm contract = ContractForm.Managed, double equityShare = 1.0)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        string? problem = ConstructionProblem(unitId, budgetNok, phases, equityShare);
        if (problem != null)
            return CommandResult.Fail("invalid_project", problem);

        var project = new ConstructionProject
        {
            Id = $"cp_{State.NextConstructionProjectNumber++}",
            UnitId = unitId,
            AssetKind = kind,
            Contract = contract,
            BudgetNok = budgetNok,
            EquityShare = equityShare,
            Phases = phases.ToList(),
        };
        State.ConstructionProjects.Add(project);
        TryStartCurrentPhase(project);
        return CommandResult.Success($"Construction project {project.Id} for {unitId} started", (double)budgetNok, project.Phases.Count, project.Id);
    }

    /// <summary>
    /// The player starts the current phase of a project when that phase waits
    /// for the player (e.g. the tow). Fails: not_found, not_ready (no
    /// player-started phase waiting); a phase in a closed month or without the
    /// money waits and starts on the first day it can.
    /// </summary>
    public CommandResult StartConstructionPhase(string projectId)
    {
        var project = State?.ConstructionProjects.FirstOrDefault(p => p.Id == projectId);
        if (project == null)
            return CommandResult.Fail("not_found", $"No construction project '{projectId}'");
        if (project.Status != ConstructionStatus.Waiting || project.Current is not { PlayerStarted: true } || project.PlayerStartRequested)
            return CommandResult.Fail("not_ready", $"{projectId} has no phase waiting for the player");
        project.PlayerStartRequested = true;
        bool started = TryStartCurrentPhase(project);
        return CommandResult.Success(started
            ? $"{projectId}: phase '{project.Current?.Key}' started"
            : $"{projectId}: phase '{project.Current?.Key}' starts as soon as it can", id: project.Id);
    }

    /// <summary>The projects in their saved order.</summary>
    public IReadOnlyList<ConstructionProject> ConstructionProjects
        => State?.ConstructionProjects ?? (IReadOnlyList<ConstructionProject>)Array.Empty<ConstructionProject>();

    private static string? ConstructionProblem(string unitId, decimal budgetNok, IReadOnlyList<ConstructionPhase> phases, double equityShare)
    {
        if (string.IsNullOrWhiteSpace(unitId)) return "a project needs a unit";
        if (budgetNok <= 0m) return "a project needs a positive budget";
        if (!(equityShare > 0.0 && equityShare <= 1.0)) return "the equity share must lie in (0, 1]";
        if (phases == null || phases.Count == 0) return "a project needs at least one phase";
        if (phases.Any(p => p == null || string.IsNullOrWhiteSpace(p.Key) || p.PlannedDays < 1 || !(p.CapexShare >= 0.0)))
            return "every phase needs a key, at least one day and a non-negative share";
        if (phases.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count() != phases.Count)
            return "phase keys must be unique";
        if (phases.Any(p => p.ClosedMonths.Any(m => m < 1 || m > 12)))
            return "closed months are 1-12";
        if (phases.Count(p => p.ClosedMonths.Distinct().Count() == 12) > 0)
            return "a phase cannot be closed every month";
        double shares = phases.Sum(p => p.CapexShare);
        if (Math.Abs(shares - 1.0) > 1e-9) return $"phase shares add up to {shares}, not 1";
        return null;
    }

    /// <summary>
    /// Start the project's current phase today if it can: the player has
    /// started it (when it is the player's), the month is open and its payment
    /// is made. Then, and only then, its two uniforms are drawn. Returns
    /// whether the phase started.
    /// </summary>
    private bool TryStartCurrentPhase(ConstructionProject project)
    {
        var phase = project.Current;
        if (phase == null || project.Status != ConstructionStatus.Waiting) return false;
        if (phase.PlayerStarted && !project.PlayerStartRequested) return false;
        if (phase.ClosedMonths.Length > 0 && phase.ClosedMonths.Contains(SimCalendar.Month(State.Day))) return false;

        var calibration = _calibration.Facilities.Common.Construction;
        decimal payment = ConstructionSchedule.StartPayment(calibration, project, phase);
        if (!TryPayCost(payment, CostBooking.Capex, "facilities")) return false;
        project.PaidNOK += payment;

        var stream = Rng.Stream(SimRngStreams.FacilityConstruction, project.Id, phase.Key);
        double costDraw = stream.NextDouble();
        double durationDraw = stream.NextDouble();
        project.OverrunFactor = ConstructionSchedule.OverrunFactor(calibration, project.Contract, costDraw);
        project.DurationFactor = ConstructionSchedule.DurationFactor(calibration, durationDraw);
        project.PhaseDays = ConstructionSchedule.PhaseDays(phase, project.DurationFactor);
        project.PhaseStartDay = State.Day;
        project.SuspendedDays = 0;
        project.Status = ConstructionStatus.Building;
        return true;
    }

    /// <summary>
    /// Advance every project one day: a phase whose days have run ends (its
    /// overrun is paid, on credit when cash is short: the contractor's bill is
    /// owed), the next phase starts when it can, and a project whose last
    /// phase ended is complete and its cost enters the asset register from today.
    /// </summary>
    internal void AdvanceConstructionProjects()
    {
        if (State == null) return;
        var projects = State.ConstructionProjects;
        for (int i = 0; i < projects.Count; i++)
        {
            var project = projects[i];
            if (project.Status == ConstructionStatus.Complete) continue;
            // F2-14: a phase that stops in its closed months (sea works in
            // April-June) stands still today; its end moves a day on.
            if (project.Status == ConstructionStatus.Building && project.Current is { SuspendedInClosedMonths: true } seasonal
                && seasonal.ClosedMonths.Contains(SimCalendar.Month(State.Day)))
                project.SuspendedDays++;
            if (project.Status == ConstructionStatus.Building && State.Day >= project.PhaseEndDay)
                FinishCurrentPhase(project);
            if (project.Status == ConstructionStatus.Waiting)
                TryStartCurrentPhase(project);
        }
    }

    private void FinishCurrentPhase(ConstructionProject project)
    {
        var phase = project.Current!;
        decimal overrun = ConstructionSchedule.OverrunPayment(project, phase);
        if (overrun > 0m)
        {
            PayCost(overrun, CostBooking.Capex, "facilities",
                SimText.Nb($"Byggeprosjektet {project.Id} gikk over budsjettet: {overrun:N0} kr betales på kreditt"));
            project.PaidNOK += overrun;
        }
        project.PhaseCompletedDays.Add(State.Day);
        PublishConstructionPhaseEvents(project, project.CurrentPhase, overrun); // F2-05: typed events with the unit
        project.CurrentPhase++;
        project.PlayerStartRequested = false;
        project.PhaseStartDay = -1;
        project.PhaseDays = 0;
        project.SuspendedDays = 0;
        if (project.CurrentPhase >= project.Phases.Count)
        {
            project.Status = ConstructionStatus.Complete;
            RegisterFacilityAsset(project.UnitId, project.AssetKind, project.PaidNOK, State.Day);
        }
        else
        {
            project.Status = ConstructionStatus.Waiting;
        }
    }

    // ─── Permit cases ──────────────────────────────────────────────────────

    /// <summary>
    /// File a permit case on <paramref name="terms"/> (the lane's decision
    /// window, refusal probability and fee). The fee is paid as opex at once
    /// and attributed to the unit kind the plot serves; the case's decision
    /// day and outcome are drawn now (two uniforms) and revealed on that day.
    /// Fails before any change: engine_not_ready, invalid_terms,
    /// already_pending (a pending case of the same kind for the plot and unit),
    /// insufficient_funds.
    /// </summary>
    public CommandResult SubmitPermitCase(PermitKind kind, string plotId, string unitId, PermitTerms terms,
        IReadOnlyList<string>? mitigations = null, IReadOnlyDictionary<string, double>? conditions = null)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        string? problem = terms == null ? "no terms" : terms.Problem();
        if (problem != null)
            return CommandResult.Fail("invalid_terms", problem);
        plotId ??= "";
        unitId ??= "";
        if (State.PermitCases.Any(c => c.IsPending && c.Kind == kind && c.PlotId == plotId && c.UnitId == unitId))
            return CommandResult.Fail("already_pending", $"A {kind} case for '{plotId}' is already pending");
        if (!TryPayCost(terms!.FeeNok, CostBooking.Opex, "permit_fees"))
            return CommandResult.Fail("insufficient_funds", $"Insufficient funds for the {terms.FeeNok:N0} NOK fee", (double)terms.FeeNok);
        AttributeUnitCost(unitId, PermitUnitKind(plotId, kind), terms.FeeNok);

        var permit = new PermitCase
        {
            Id = $"pc_{State.NextPermitCaseNumber++}",
            Kind = kind,
            PlotId = plotId,
            UnitId = unitId,
            SubmittedDay = State.Day,
            Mitigations = mitigations?.ToList() ?? new List<string>(),
            Conditions = conditions?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
                         ?? new Dictionary<string, double>(StringComparer.Ordinal),
        };
        DrawPermitRound(permit, terms);
        State.PermitCases.Add(permit);
        return CommandResult.Success($"Permit case {permit.Id} ({kind}) submitted; decision on day {permit.DecisionDay}",
            permit.RefusalProbability, permit.DecisionDay, permit.Id);
    }

    /// <summary>
    /// Appeal a refused case on new terms (the appeal's window and refusal
    /// probability; spec §2C: a refusal can be appealed). Draws the round's two
    /// uniforms. Fails: not_found, not_refused, invalid_terms, insufficient_funds.
    /// </summary>
    public CommandResult AppealPermitCase(string caseId, PermitTerms terms)
    {
        var permit = State?.PermitCases.FirstOrDefault(c => c.Id == caseId);
        if (permit == null)
            return CommandResult.Fail("not_found", $"No permit case '{caseId}'");
        if (permit.Status != PermitStatus.Refused)
            return CommandResult.Fail("not_refused", $"{caseId} is {permit.Status}, not refused");
        string? problem = terms == null ? "no terms" : terms.Problem();
        if (problem != null)
            return CommandResult.Fail("invalid_terms", problem);
        if (!TryPayCost(terms!.FeeNok, CostBooking.Opex, "permit_fees"))
            return CommandResult.Fail("insufficient_funds", $"Insufficient funds for the {terms.FeeNok:N0} NOK fee", (double)terms.FeeNok);
        AttributeUnitCost(permit.UnitId, PermitUnitKind(permit.PlotId, permit.Kind), terms.FeeNok);
        permit.Round++;
        permit.SubmittedDay = State!.Day;
        permit.Status = PermitStatus.Appealed;
        DrawPermitRound(permit, terms);
        return CommandResult.Success($"{caseId} appealed; decision on day {permit.DecisionDay}",
            permit.RefusalProbability, permit.DecisionDay, permit.Id);
    }

    /// <summary>Withdraw a pending case (no refund). Fails: not_found, not_pending.</summary>
    public CommandResult WithdrawPermitCase(string caseId)
    {
        var permit = State?.PermitCases.FirstOrDefault(c => c.Id == caseId);
        if (permit == null)
            return CommandResult.Fail("not_found", $"No permit case '{caseId}'");
        if (!permit.IsPending)
            return CommandResult.Fail("not_pending", $"{caseId} is {permit.Status}");
        permit.Status = PermitStatus.Withdrawn;
        return CommandResult.Success($"{caseId} withdrawn", id: permit.Id);
    }

    /// <summary>The permit cases in their saved order.</summary>
    public IReadOnlyList<PermitCase> PermitCases
        => State?.PermitCases ?? (IReadOnlyList<PermitCase>)Array.Empty<PermitCase>();

    /// <summary>Decide every pending case whose decision day has come, by its pre-drawn uniform.</summary>
    internal void DecidePermitCases()
    {
        if (State == null) return;
        foreach (var permit in State.PermitCases)
        {
            if (!permit.IsPending || State.Day < permit.DecisionDay) continue;
            permit.Status = permit.WillBeRefused ? PermitStatus.Refused : PermitStatus.Approved;
            permit.DecidedDay = State.Day;
            PublishPermitDecision(permit); // F2-05
        }
    }

    /// <summary>One decision round's two uniforms: the decision day inside the terms' window, then the outcome draw.</summary>
    private void DrawPermitRound(PermitCase permit, PermitTerms terms)
    {
        var stream = Rng.Stream(SimRngStreams.FacilityPermits, permit.Id, permit.Round.ToString(System.Globalization.CultureInfo.InvariantCulture));
        double dayDraw = stream.NextDouble();
        double outcomeDraw = stream.NextDouble();
        permit.RefusalProbability = terms.RefusalProbability;
        permit.DecisionDay = PermitCase.DecisionDayFor(permit.SubmittedDay, terms, dayDraw);
        permit.OutcomeDraw = outcomeDraw;
    }

    /// <summary>
    /// The unit kind a case's fee is spent on: the plot's (a land pad's fees are
    /// land costs, an offshore slot's offshore costs), or, for a case tied to no
    /// known plot, the kind's own (the land permits are land costs, the
    /// licence and area cases offshore costs, a dispensation a sea pen's).
    /// </summary>
    private HoldingKind PermitUnitKind(string plotId, PermitKind kind)
    {
        var plot = string.IsNullOrEmpty(plotId) ? null : SitePlots.FirstOrDefault(p => p.Id == plotId);
        if (plot != null)
            return plot.Kind == Farm.PlotKind.OffshoreSlot ? HoldingKind.Offshore : HoldingKind.LandTank;
        return kind switch
        {
            PermitKind.DevelopmentLicence or PermitKind.Conversion or PermitKind.OffshoreArea => HoldingKind.Offshore,
            PermitKind.Dispensation => HoldingKind.SeaPen,
            _ => HoldingKind.LandTank,
        };
    }
}

/// <summary>
/// POST-TICK phase (F2-04, spec §3.3 "ConstructionAndPermits"): after the
/// wellboat jobs, advance the facility construction projects, decide the
/// permit cases due today and book the F2 units' depreciation through today.
/// A game with no project, case or asset does nothing here.
/// </summary>
internal sealed class ConstructionAndPermitsTickSystem : ITickSystem
{
    public void Tick(SimulationEngine engine)
    {
        engine.AdvanceConstructionProjects();
        engine.DecidePermitCases();
        engine.BookFacilityDepreciation();
    }
}
