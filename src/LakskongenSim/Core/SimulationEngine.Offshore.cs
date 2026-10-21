using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Environment;
using LakskongenSim.Events;
using LakskongenSim.Farm;
using LakskongenSim.Farm.Offshore;
using LakskongenSim.Finance;

namespace LakskongenSim.Core;

/// <summary>
/// The offshore unit the player builds and runs (roadmap F2-07,
/// docs/FARM_TYPES_SPEC.md §3.3, §3.4 ".Offshore", §6.2-6.3): the build with
/// its preview (contract, licence value, consortium, payment schedule, first
/// stocking), the construction project and its player-started tow, the
/// shakedown and the teething deck, sensors, inspection and repair, and the
/// consortium's buy-out. Bookings, logistics and the unit's day are in
/// <c>SimulationEngine.OffshoreOps.cs</c>. Every verb returns a
/// <see cref="CommandResult"/> and refuses before any change.
/// </summary>
public partial class SimulationEngine
{
    /// <summary>Refusal keys of the offshore verbs (spec §3.4; text <c>CMD_ERROR_&lt;KEY&gt;</c>).</summary>
    public static class OffshoreReasons
    {
        public const string NotUnlocked = "not_unlocked";
        public const string PlotNotFound = "plot_not_found";
        public const string NotAnOffshoreSlot = "not_an_offshore_slot";
        public const string PlotTaken = "plot_taken";
        public const string ExposureExceedsDesign = "exposure_exceeds_design";
        public const string InsufficientFunds = "insufficient_funds";
        public const string InvalidType = "invalid_type";
        public const string OutOfRange = "out_of_range";
        public const string NotFound = "not_found";
        public const string NotReady = "not_ready";
        public const string Sheltering = "sheltering";
        public const string AlreadyStocked = "already_stocked";
        public const string NotRevealed = "not_revealed";
        public const string AlreadyFixing = "already_fixing";
        public const string AlreadyInstalled = "already_installed";
        public const string NoWindow = "no_window";
        public const string NotConsortium = "not_consortium";
        public const string NotOperational = "not_operational";
        public const string ShakedownActive = "shakedown_active";
        public const string NothingToRepair = "nothing_to_repair";
        // F2-08
        public const string AlreadyRunning = "already_running";
        public const string MaxLevel = "max_level";
        public const string ApprovalWithdrawn = "approval_withdrawn";
        public const string NotAppealable = "not_appealable";
        // F2-09
        public const string LicenceMissing = "licence_missing";
        public const string LicenceExpired = "licence_expired";
        public const string LicencesUnplaced = "licences_unplaced";
        public const string RoundClosed = "round_closed";
        public const string GateUnmet = "gate_unmet";
        public const string AlreadyApplied = "already_applied";
        public const string InvalidConcept = "invalid_concept";
        public const string CriteriaUnmet = "criteria_unmet";
        public const string NotPending = "not_pending";
        public const string NothingDue = "nothing_due";
        public const string PaymentLapsed = "payment_lapsed";
        public const string ZoneFull = "zone_full";
        public const string ZoneNotFound = "zone_not_found";
    }

    // ─── Build ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Preview a unit of <paramref name="type"/> on <paramref name="plotId"/>
    /// (spec §6.2 step 4): the contract and its payment schedule per phase,
    /// the consortium option, the development licences' value, the teething
    /// deck shown before construction, the planned first stocking, the unlock
    /// gates and the refusal the build would give. Pure.
    /// </summary>
    public OffshoreBuildPreview PreviewOffshoreBuild(string plotId, OffshoreType type, ContractForm contract, double equityShare)
    {
        var cal = OffshoreCal;
        var unitClass = OffshoreProfiles.ClassOf(type, cal);
        string refusal = OffshoreBuildRefusal(plotId, type, equityShare, out _);
        var common = _calibration.Facilities.Common;
        var reference = common.Catalog;
        int licences = type == OffshoreType.ShipShaped ? reference.Havfarm.Licences : reference.Of1.Licences;
        double licenceTonnes = licences * reference.LicenceSizeTonnesMtb;
        decimal licenceMarketValue = (decimal)licenceTonnes * reference.OpenPen.LicencePriceNokPerTonneMtb;
        decimal conversionFee = licences * reference.ConversionFeeNokPerLicence;

        var phases = unitClass == null ? Array.Empty<ConstructionPhase>() : OffshoreProfiles.PhasesFor(unitClass, cal.Units.Payments, cal.Operability);
        decimal budget = unitClass?.ContractCapexNok ?? 0m;
        double equity = equityShare > 0.0 && equityShare <= 1.0 ? equityShare : 1.0;
        var probe = new ConstructionProject { BudgetNok = budget, EquityShare = equity, Contract = contract };
        var schedule = new List<OffshorePhasePayment>(phases.Count);
        int offset = 0;
        decimal total = 0m;
        foreach (var phase in phases)
        {
            decimal pay = ConstructionSchedule.StartPayment(common.Construction, probe, phase);
            decimal median = contract == ContractForm.Turnkey ? 0m : budget * (decimal)phase.CapexShare * (decimal)equity * (decimal)common.Construction.OverrunMedian;
            decimal p90 = contract == ContractForm.Turnkey ? 0m : budget * (decimal)phase.CapexShare * (decimal)equity * (decimal)common.Construction.OverrunP90;
            schedule.Add(new OffshorePhasePayment(phase.Key, State.Day + offset, phase.PlannedDays, pay, median, p90, phase.PlayerStarted));
            offset += phase.PlannedDays;
            total += pay;
        }
        int buildDays = unitClass == null ? 0 : OffshoreProfiles.PlannedBuildDays(unitClass, cal.Operability);
        bool first = !OffshoreUnits.Any(u => u.Offshore?.FarmType == type && u.Offshore.HasTeethingDeck);
        var teething = cal.Teething;
        var gates = FarmTypeStatus(OffshoreProfiles.VariantOf(type))?.Gates ?? Array.Empty<FarmTypeGateStatus>();
        decimal firstPayment = schedule.Count > 0 ? schedule[0].PaymentNok : 0m;
        return new OffshoreBuildPreview(
            PlotId: plotId ?? "",
            Type: type,
            Contract: contract,
            EquityShare: equity,
            ContractCapexNok: budget,
            CompanyCapexNok: total,
            TurnkeyPremium: common.Construction.TurnkeyPremium,
            OverrunMedianNok: schedule.Sum(s => s.OverrunMedianNok),
            OverrunP90Nok: schedule.Sum(s => s.OverrunP90Nok),
            Payments: schedule,
            ConsortiumEquityShare: reference.Of1.ConsortiumEquityShare,
            ConsortiumCompanyCapexNok: budget * (decimal)reference.Of1.ConsortiumEquityShare * (contract == ContractForm.Turnkey ? 1m + (decimal)common.Construction.TurnkeyPremium : 1m),
            BuyoutMultiple: cal.Costs.BuyoutMultiple,
            Licences: licences,
            LicenceTonnesMtb: licenceTonnes,
            LicenceMarketValueNok: licenceMarketValue,
            LicencePriceNokPerTonne: reference.OpenPen.LicencePriceNokPerTonneMtb,
            ConversionFeeNok: conversionFee,
            ResourceRentBeforeConversion: false,
            TeethingDefectsMin: first ? teething.FirstUnitDefectsMin : teething.LaterUnitDefectsMin,
            TeethingDefectsMax: first ? teething.FirstUnitDefectsMax : teething.LaterUnitDefectsMax,
            TeethingBudgetNok: unitClass?.TeethingBudgetNok ?? 0m,
            ShakedownOptionsDays: teething.ShakedownOptions(),
            PlannedBuildDays: buildDays,
            FirstStockingDay: State.Day + buildDays,
            Gates: gates,
            FirstPaymentNok: firstPayment,
            Affordable: (FinanceManager?.Balance ?? 0m) >= firstPayment,
            RefusalKey: refusal.Length > 0 ? refusal : (FinanceManager?.Balance ?? 0m) >= firstPayment ? "" : OffshoreReasons.InsufficientFunds);
    }

    /// <summary>
    /// Build an offshore unit (spec §3.4 <c>BuildOffshoreUnit(plotId, OffshoreType,
    /// ContractForm, equityShare)</c>): an offshore sea site of the class's
    /// zones on <paramref name="plotId"/>, built by a construction project
    /// whose phases are paid at their start (the yard contract is paid now),
    /// with the tow waiting for the player (<see cref="StartTow"/>). A
    /// consortium unit (equity = the catalogue's consortium share, 35 %) pays
    /// and books its share. The unit gets the Driftsplan's offshore defaults
    /// and a teething deck drawn at commissioning. Fails before any change:
    /// engine_not_ready, invalid_type (the legacy submersible), out_of_range
    /// (an equity other than 1 or the consortium share), plot_not_found,
    /// not_an_offshore_slot, plot_taken, exposure_exceeds_design, not_unlocked
    /// (a gate other than the development licence or money), licence_missing
    /// (no development-licence award for this plot and concept, F2-09),
    /// insufficient_funds (the signing payment). The award's licences are bound
    /// to the unit.
    /// </summary>
    public CommandResult BuildOffshoreUnit(string plotId, OffshoreType type, ContractForm contract, double equityShare = 1.0)
    {
        if (State == null || FinanceManager == null)
            return CommandResult.Fail("engine_not_ready", "Engine not initialized");
        string refusal = OffshoreBuildRefusal(plotId, type, equityShare, out var plot);
        if (refusal.Length > 0)
            return CommandResult.Fail(refusal, $"Cannot build a {type} unit on '{plotId}': {refusal}");
        return ContractOffshoreUnit(plot!, type, contract, equityShare);
    }

    /// <summary>
    /// The contract of an offshore unit whose plot, class, gates and award
    /// have been checked (<see cref="BuildOffshoreUnit"/>; F2-20's QA staging
    /// seam with the unlock gates waived): the signing payment, the site, the
    /// construction project, the award's licences bound to it.
    /// </summary>
    private CommandResult ContractOffshoreUnit(SitePlot plot, OffshoreType type, ContractForm contract, double equityShare)
    {
        string plotId = plot.Id;
        var preview = PreviewOffshoreBuild(plotId, type, contract, equityShare);
        if (!preview.Affordable)
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds,
                $"Insufficient funds: the signing payment is {preview.FirstPaymentNok:N0} NOK", (double)preview.FirstPaymentNok);

        var unitClass = OffshoreProfiles.ClassOf(type, OffshoreCal)!;
        string id = NextFacilityId("offshore",
            State.SeaSites.Where(s => s.Kind == SiteKind.Offshore).Select(s => s.Id).Concat(OffshoreFarms.Select(f => f.Id)));
        string name = FacilityName("", type == OffshoreType.ShipShaped ? "Skipsmerd" : "Havmerd", OffshoreUnits.Count);
        var site = NewOffshoreSite(id, name, type, plot!);
        var unit = site.Offshore!;
        unit.CAPEX = unitClass.ContractCapexNok;
        unit.EquityShare = equityShare;
        unit.HasTeethingDeck = true;
        unit.SiloCapacityTonnes = unitClass.SiloTonnes;
        unit.SilageCapacityTonnes = unitClass.SilageTonnes;
        unit.EmployeeCount = unitClass.CrewInRotation;
        unit.ConstructionDays = preview.PlannedBuildDays;
        unit.ConstructionStartDay = State.Day;
        var phases = OffshoreProfiles.PhasesFor(unitClass, OffshoreCal.Units.Payments, OffshoreCal.Operability);

        State.SeaSites.Add(site);
        ClaimPlot(plot!, site.Id);
        var started = StartConstructionProject(site.Id, FacilityAssetKind.OffshoreUnit, unitClass.ContractCapexNok, phases, contract, equityShare);
        if (!started.Ok)
        {
            // Cannot happen with the class's own phases; undo the site so the refusal changes nothing.
            State.SeaSites.Remove(site);
            FindPlot(plot!.Id)!.OwnerUnitId = "";
            return started;
        }
        unit.ConstructionProjectId = started.Id;
        BindAwardToUnit(BuildableAward(plot!.Id, OffshoreProfiles.VariantOf(type))!, site); // F2-09: the award's licences behind the unit
        EnsureOffshoreSea();
        TakeTodaysSea(site); // its gauges read the sea the window board shows from its first day
        ApplyDriftsplanDefaults(site.Id);
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Havmerd kontrahert",
            Category = EventCategory.Equipment,
            Description = equityShare < 1.0
                ? SimText.Nb($"{site.Label} er kontrahert for {plot!.Name}: {unitClass.ContractCapexNok:N0} kr i konsortium ({equityShare:P0} eierandel), første utsett tidligst dag {preview.FirstStockingDay}")
                : SimText.Nb($"{site.Label} er kontrahert for {plot!.Name}: {unitClass.ContractCapexNok:N0} kr, første utsett tidligst dag {preview.FirstStockingDay}"),
            BalanceChange = -preview.FirstPaymentNok,
        });
        State.TrimEventLog();
        return CommandResult.Success($"Offshore unit '{site.Label}' contracted on {plot.Id} ({started.Id})",
            (double)preview.CompanyCapexNok, preview.PlannedBuildDays, site.Id);
    }

    /// <summary>"" when a build would go through, else its refusal key (the plot it would stand on in <paramref name="plot"/>).</summary>
    private string OffshoreBuildRefusal(string plotId, OffshoreType type, double equityShare, out SitePlot? plot)
    {
        plot = null;
        if (State == null) return "engine_not_ready";
        var unitClass = OffshoreProfiles.ClassOf(type, OffshoreCal);
        if (unitClass == null || !Enum.IsDefined(type)) return OffshoreReasons.InvalidType;
        double consortium = _calibration.Facilities.Common.Catalog.Of1.ConsortiumEquityShare;
        if (!(equityShare == 1.0 || Math.Abs(equityShare - consortium) < 1e-12)) return OffshoreReasons.OutOfRange;
        plot = SitePlots.FirstOrDefault(p => p.Id == plotId && !p.IsOverflow);
        if (plot == null) return OffshoreReasons.PlotNotFound;
        if (plot.Kind != PlotKind.OffshoreSlot) return OffshoreReasons.NotAnOffshoreSlot;
        if (!plot.IsFree) return OffshoreReasons.PlotTaken;
        if (!plot.AcceptsDesignHs(OffshoreUnit.DefaultDesignHsM(type))) return OffshoreReasons.ExposureExceedsDesign;
        var status = FarmTypeStatus(OffshoreProfiles.VariantOf(type));
        // Every gate but money (paid phase by phase) and the development licence,
        // which F2-09 checks on its own: an award for this plot and concept.
        if (status == null || status.Gates.Any(g => !g.Met
                && g.Gate.Kind != FarmTypeGateKind.Funds && g.Gate.Kind != FarmTypeGateKind.DevelopmentLicence))
            return OffshoreReasons.NotUnlocked;
        if (BuildableAward(plot.Id, OffshoreProfiles.VariantOf(type)) == null)
            return OffshoreReasons.LicenceMissing;
        return "";
    }

    // ─── Construction ──────────────────────────────────────────────────────

    /// <summary>The construction project building an offshore unit, or null (a legacy-built or migrated unit has none).</summary>
    public ConstructionProject? OffshoreProjectOf(SeaSite site)
    {
        string id = site?.Offshore?.ConstructionProjectId ?? "";
        return id.Length == 0 ? null : State?.ConstructionProjects.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// The construction of an offshore unit as the board reads it (spec §6.3):
    /// each phase with its planned and actual days, the current phase and its
    /// progress, the overall progress, and why it waits (the player's tow, a
    /// shelter after a failed tow, the money for a payment). Null for an
    /// unknown site; a legacy unit reports its day-count progress alone.
    /// </summary>
    public OffshoreConstructionView? GetOffshoreConstruction(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null) return null;
        var project = OffshoreProjectOf(site);
        if (project == null)
            return new OffshoreConstructionView(site.Id, "", unit.IsOperational, "", -1, unit.ConstructionProgress,
                unit.IsOperational ? 1.0 : unit.ConstructionProgress, "", -1, Array.Empty<OffshorePhaseView>(), 0m);
        int plannedTotal = project.Phases.Sum(p => p.PlannedDays);
        double doneDays = 0.0;
        var views = new List<OffshorePhaseView>(project.Phases.Count);
        int plannedStart = project.PhaseCompletedDays.Count > 0 ? project.PhaseCompletedDays[^1] : State.Day;
        for (int i = 0; i < project.Phases.Count; i++)
        {
            var phase = project.Phases[i];
            string status;
            int start = -1, end = -1;
            double progress = 0.0;
            if (i < project.CurrentPhase)
            {
                status = "done";
                end = project.PhaseCompletedDays[i];
                start = i == 0 ? (unit.ConstructionStartDay >= 0 ? unit.ConstructionStartDay : end - phase.PlannedDays) : project.PhaseCompletedDays[i - 1];
                progress = 1.0;
                doneDays += phase.PlannedDays;
            }
            else if (i == project.CurrentPhase && project.Status == ConstructionStatus.Building)
            {
                status = "building";
                start = project.PhaseStartDay;
                end = project.PhaseEndDay;
                progress = Math.Clamp((State.Day - project.PhaseStartDay) / (double)Math.Max(1, project.PhaseDays), 0.0, 1.0);
                doneDays += progress * phase.PlannedDays;
                plannedStart = end;
            }
            else if (i == project.CurrentPhase)
            {
                status = "waiting";
                start = Math.Max(State.Day, plannedStart);
                end = start + phase.PlannedDays;
                plannedStart = end;
            }
            else
            {
                status = "pending";
                start = plannedStart;
                end = start + phase.PlannedDays;
                plannedStart = end;
            }
            views.Add(new OffshorePhaseView(phase.Key, phase.PlannedDays, status, start, end, progress, phase.PlayerStarted,
                ConstructionSchedule.StartPayment(_calibration.Facilities.Common.Construction, project, phase)));
        }
        string current = project.Current?.Key ?? "";
        string waiting = WaitingReasonOf(site, project);
        double overall = project.Status == ConstructionStatus.Complete ? 1.0 : doneDays / Math.Max(1, plannedTotal);
        double phaseProgress = views.Count > project.CurrentPhase && project.CurrentPhase >= 0 ? views[Math.Min(project.CurrentPhase, views.Count - 1)].Progress : 1.0;
        return new OffshoreConstructionView(site.Id, project.Id, project.Status == ConstructionStatus.Complete, current,
            project.CurrentPhase, phaseProgress, overall, waiting, unit.TowShelterUntilDay, views, project.PaidNOK);
    }

    private string WaitingReasonOf(SeaSite site, ConstructionProject project)
    {
        if (project.Status != ConstructionStatus.Waiting) return "";
        var unit = site.Offshore!;
        if (project.Current?.Key == OffshoreProfiles.TowPhase)
            return unit.TowShelterUntilDay >= State.Day ? "sheltering" : "tow";
        return "funds";
    }

    /// <summary>
    /// Start the tow to the plot (spec §6.3 "Start slep nå"): the unit's
    /// project must wait at its tow phase and the unit not shelter after a
    /// failed window. The tugs are chartered from today; the tow needs its
    /// sea within the tow limit on each of its days, else it shelters 5-15
    /// days at charter cost (no structural damage) and waits for the player
    /// again. The result's value is P(the window holds), from the preview.
    /// Fails: not_found, not_ready (no tow waiting), sheltering.
    /// </summary>
    public CommandResult StartTow(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        var project = site == null ? null : OffshoreProjectOf(site);
        if (site == null || unit == null || project == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit under construction '{siteId}'");
        if (project.Status != ConstructionStatus.Waiting || project.Current?.Key != OffshoreProfiles.TowPhase)
            return CommandResult.Fail(OffshoreReasons.NotReady, $"{site.Label} has no tow waiting");
        if (unit.TowShelterUntilDay >= State.Day)
            return CommandResult.Fail(OffshoreReasons.Sheltering, $"{site.Label} shelters until day {unit.TowShelterUntilDay}");
        var preview = PreviewTow(siteId);
        var started = StartConstructionPhase(project.Id);
        if (!started.Ok) return started;
        unit.TowStartedDay = State.Day;
        unit.TowAttempts++;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Slep startet",
            Category = EventCategory.Equipment,
            Description = SimText.Nb($"Slepet av {site.Label} til feltet er startet: {OffshoreCal.Costs.TowTugs} slepebåter, sannsynlighet for at vinduet holder {preview?.HoldProbability ?? 0.0:P0}"),
        });
        State.TrimEventLog();
        return CommandResult.Success(preview != null && preview.HoldProbability < 0.5
                ? $"Tow of {site.Label} started (window_unlikely: P = {preview.HoldProbability:F2})"
                : $"Tow of {site.Label} started", preview?.HoldProbability ?? 0.0, preview?.ExpectedCostNok is { } c ? (double)c : 0.0, site.Id);
    }

    /// <summary>
    /// The tow's preview (spec §6.3 "P(vinduet holder 3 døgn)"): the chance the
    /// tow sea state holds on each of the tow's days from tomorrow, the tugs'
    /// cost of a tow that holds, and the expected cost with the shelter of a
    /// failed window. Null when the unit has no open sea yet. Pure (its own
    /// sample of the model's futures).
    /// </summary>
    public OffshoreTowPreview? PreviewTow(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var sea = State?.Metocean;
        if (State == null || site == null || sea == null || sea.LastTickDay < 0 || Weather.SimulationDay != sea.LastTickDay) return null;
        var operability = OffshoreCal.Operability;
        var costs = OffshoreCal.Costs;
        int days = operability.TowHoldDays;
        var futures = OffshoreSeaSampler.Sample(sea, Weather.CurrentState, Weather.StormFrequencyMultiplier, Weather.MaxState,
            PlotHsScaleOf(site), days, OffshoreSeaSampler.DefaultPaths,
            OffshoreSeaSampler.QuestionKey(site.PlotId, OffshoreOp.Tow, VesselClass.Standard, State.Day + 1), operability);
        int holds = 0;
        double failDays = 0.0;
        foreach (var path in futures)
        {
            int fail = Array.FindIndex(path, d => d.HsM > operability.TowHsM);
            if (fail < 0) holds++;
            else failDays += fail + 1;
        }
        double p = holds / (double)futures.Length;
        double equity = site.Offshore?.EquityShare ?? 1.0;
        decimal tugDay = costs.AnchorTugNokPerDay * costs.TowTugs * (decimal)equity;
        double meanShelter = 0.5 * (costs.TowShelterDaysMin + costs.TowShelterDaysMax);
        decimal success = tugDay * days;
        decimal expected = tugDay * (decimal)(p * days + failDays / futures.Length + (1.0 - p) * meanShelter);
        return new OffshoreTowPreview(site.Id, p, days, operability.TowHsM, tugDay, success, expected,
            costs.TowShelterDaysMin, costs.TowShelterDaysMax);
    }

    // ─── Shakedown and teething ────────────────────────────────────────────

    /// <summary>
    /// Choose a shakedown ("Prøvedrift", spec §3.3): first stocking waits
    /// <paramref name="days"/> after commissioning (one of 0 / 60 / 90 / 120),
    /// and the crew's trials reveal each hidden teething defect at 0.5 per 30
    /// days meanwhile, so it can be fixed before there are fish. Chosen during
    /// construction or after commissioning, before the first stocking. Fails:
    /// not_found, out_of_range, already_stocked.
    /// </summary>
    public CommandResult ChooseShakedown(string siteId, int days)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!OffshoreCal.Teething.ShakedownOptions().Contains(days))
            return CommandResult.Fail(OffshoreReasons.OutOfRange, $"A shakedown is one of {string.Join("/", OffshoreCal.Teething.ShakedownOptions())} days, not {days}");
        if (unit.FirstStockedDay >= 0 || site.Pens.Any(p => p.TotalCount > 0))
            return CommandResult.Fail(OffshoreReasons.AlreadyStocked, $"{site.Label} already holds fish");
        unit.ShakedownDays = days;
        if (unit.IsCommissioned)
            unit.ShakedownEndDay = days == 0 ? -1 : Math.Max(State.Day, unit.CommissionedDay) + days;
        return CommandResult.Success($"{site.Label}: shakedown of {days} days", days, unit.ShakedownEndDay, site.Id);
    }

    /// <summary>
    /// Fix a revealed teething defect (spec §3.4 <c>FixTeethingDefect</c>): a
    /// share of the class's teething budget (the budget over the deck's mean
    /// size, at the company's equity), done after the calibrated days; until
    /// then a defect with fish in the unit fires. Fails: not_found,
    /// not_revealed, already_fixing, insufficient_funds.
    /// </summary>
    public CommandResult FixTeethingDefect(string siteId, string defectId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        var defect = unit?.TeethingDefects.FirstOrDefault(d => d.Id == defectId);
        if (site == null || unit == null || defect == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No defect '{defectId}' at '{siteId}'");
        if (!defect.IsRevealed)
            return CommandResult.Fail(OffshoreReasons.NotRevealed, $"{defectId} is not known yet");
        if (defect.FixStartedDay >= 0)
            return CommandResult.Fail(OffshoreReasons.AlreadyFixing, $"{defectId} is fixed or being fixed");
        decimal cost = TeethingFixCostNok(unit);
        if (!TryPayCost(cost, CostBooking.Opex, "teething_fixes"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the fix costs {cost:N0} NOK", (double)cost);
        RingFenceOwnLine(site, cost);
        defect.FixStartedDay = State.Day;
        defect.FixedDay = State.Day + OffshoreCal.Teething.FixDays;
        return CommandResult.Success($"{site.Label}: fixing {defect.Kind} until day {defect.FixedDay}", (double)cost, defect.FixedDay, defect.Id);
    }

    /// <summary>The price of one teething fix (NOK, the company's share): the class's budget over the first unit's mean deck size.</summary>
    internal decimal TeethingFixCostNok(OffshoreUnit unit)
    {
        var unitClass = OffshoreProfiles.ClassOf(unit.FarmType, OffshoreCal);
        var t = OffshoreCal.Teething;
        double meanDeck = Math.Max(1.0, 0.5 * (t.FirstUnitDefectsMin + t.FirstUnitDefectsMax));
        return (unitClass?.TeethingBudgetNok ?? 0m) / (decimal)meanDeck * (decimal)unit.EquityShare;
    }

    // ─── Sensors, inspection and repair ───────────────────────────────────

    /// <summary>The calibration record of a sensor package.</summary>
    internal OffshoreSensorPackageCalibration SensorCalibration(SensorPackageKind kind) => kind switch
    {
        SensorPackageKind.Mru => OffshoreCal.Sensors.Mru,
        SensorPackageKind.SubseaCam => OffshoreCal.Sensors.SubseaCam,
        SensorPackageKind.Echo => OffshoreCal.Sensors.Echo,
        _ => OffshoreCal.Sensors.AisBroadband,
    };

    /// <summary>
    /// Install a sensor package (spec §3.4 <c>InstallSensorPackage</c>; §3.6):
    /// the motion reference unit keeps the structure known, the subsea cameras
    /// the nets, the echo sounders the biomass; each needs a service every
    /// calibrated interval or it adds to the alarm load. Fails: not_found,
    /// already_installed, insufficient_funds.
    /// </summary>
    public CommandResult InstallSensorPackage(string siteId, SensorPackageKind kind)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null || !Enum.IsDefined(kind))
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}' or package '{kind}'");
        if (unit.Sensors.Any(s => s.Kind == kind))
            return CommandResult.Fail(OffshoreReasons.AlreadyInstalled, $"{site.Label} has {kind}");
        decimal cost = SensorCalibration(kind).InstallNok * (decimal)unit.EquityShare;
        if (!TryPayCost(cost, CostBooking.Opex, "offshore_sensors"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: {cost:N0} NOK", (double)cost);
        RingFenceOwnLine(site, cost);
        unit.Sensors.Add(new SensorPackage { Kind = kind, InstalledDay = State.Day, LastServiceDay = State.Day });
        unit.Sensors.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return CommandResult.Success($"{site.Label}: {kind} installed", (double)cost, id: site.Id);
    }

    /// <summary>
    /// Service every installed package due within <paramref name="withinDays"/>
    /// days (the crew on board; the Driftsplan's sensor policy issues it).
    /// Fails: not_found, nothing_to_repair (nothing due).
    /// </summary>
    public CommandResult ServiceSensors(string siteId, int withinDays = 0)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        int interval = OffshoreCal.Sensors.ServiceIntervalDays;
        var due = unit.Sensors.Where(s => State.Day - s.LastServiceDay >= interval - Math.Max(0, withinDays)).ToList();
        if (due.Count == 0)
            return CommandResult.Fail(OffshoreReasons.NothingToRepair, $"{site.Label}: no package due for service");
        decimal cost = due.Sum(s => SensorCalibration(s.Kind).ServiceNok) * (decimal)unit.EquityShare;
        PayCost(cost, CostBooking.Opex, "offshore_sensors");
        RingFenceOwnLine(site, cost);
        foreach (var s in due) s.LastServiceDay = State.Day;
        return CommandResult.Success($"{site.Label}: {due.Count} package(s) serviced", (double)cost, due.Count, site.Id);
    }

    /// <summary>
    /// Repair the unit (spec §3.4 <c>RepairOffshore(scope)</c>): "net" restores
    /// the nets, "structure" the structure, "all" both, priced per point
    /// restored at the company's equity; the work needs today's sea within the
    /// ROV and crane limit (3.0 m), and leaves both known as of today. Fails:
    /// not_found, out_of_range (an unknown scope), no_window,
    /// nothing_to_repair, insufficient_funds.
    /// </summary>
    public CommandResult RepairOffshore(string siteId, string scope)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        bool net = scope is "net" or "all", structure = scope is "structure" or "all";
        if (!net && !structure)
            return CommandResult.Fail(OffshoreReasons.OutOfRange, $"Repair scope is net, structure or all, not '{scope}'");
        if (unit.IsOperational && !OffshoreOperationPossibleToday(site, OffshoreOp.NetInspection, VesselClass.Standard))
            return CommandResult.Fail(OffshoreReasons.NoWindow, $"{site.Label}: the sea ({unit.CurrentWaveHeight:F1} m) is above the repair limit");
        var s = OffshoreCal.Structure;
        decimal cost = 0m;
        if (structure) cost += s.StructureRepairFullNok * (decimal)Math.Max(0.0, 1.0 - unit.StructuralIntegrity);
        if (net) cost += s.NetRepairNokPerPct * (decimal)Math.Max(0.0, 100.0 - site.NetIntegrityPct);
        cost *= (decimal)unit.EquityShare;
        if (cost <= 0m)
            return CommandResult.Fail(OffshoreReasons.NothingToRepair, $"{site.Label} needs no {scope} repair");
        if (!TryPayCost(cost, CostBooking.Opex, "offshore_repairs"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the repair costs {cost:N0} NOK", (double)cost);
        RingFenceOwnLine(site, cost);
        if (structure) unit.StructuralIntegrity = 1.0;
        if (net)
        {
            site.NetIntegrityPct = 100.0;
            MendTearAfterRepair(site); // F2-08: an open tear stops leaking
        }
        unit.StructureKnown = unit.StructuralIntegrity;
        unit.NetKnownPct = site.NetIntegrityPct;
        unit.StructureKnownAsOfDay = State.Day;
        unit.NetKnownAsOfDay = State.Day;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Reparasjon av havmerd",
            Category = EventCategory.Equipment,
            Description = SimText.Nb($"{site.Label} er reparert ({scope}) for {cost:N0} kr"),
            BalanceChange = -cost,
        });
        State.TrimEventLog();
        return CommandResult.Success($"{site.Label} repaired ({scope})", (double)cost, id: site.Id);
    }

    // ─── Consortium ────────────────────────────────────────────────────────

    /// <summary>
    /// Buy the consortium partner out (spec §6.1 "buy-out later at book × 1.2"):
    /// the partner's share of the unit's book value (the company's own book
    /// value scaled by the shares) times the multiple, paid as capex and
    /// depreciated from today; the unit is then the company's alone. The open
    /// settlement is settled first. Fails: not_found, not_consortium,
    /// not_operational, insufficient_funds.
    /// </summary>
    public CommandResult BuyOutPartner(string siteId)
    {
        var site = FindOffshoreUnit(siteId);
        var unit = site?.Offshore;
        if (site == null || unit == null)
            return CommandResult.Fail(OffshoreReasons.NotFound, $"No offshore unit '{siteId}'");
        if (!unit.IsConsortium)
            return CommandResult.Fail(OffshoreReasons.NotConsortium, $"{site.Label} is the company's alone");
        if (!unit.IsCommissioned)
            return CommandResult.Fail(OffshoreReasons.NotOperational, $"{site.Label} is not commissioned yet");
        decimal price = BuyOutPriceNok(site);
        if (!TryPayCost(price, CostBooking.Capex, "facilities"))
            return CommandResult.Fail(OffshoreReasons.InsufficientFunds, $"Insufficient funds: the buy-out is {price:N0} NOK", (double)price);
        SettlePartner(site, Weather.SimulationDay);
        RegisterFacilityAsset(site.Id, FacilityAssetKind.OffshoreUnit, price, State.Day);
        unit.EquityShare = 1.0;
        State.EventLog.Add(new EventRecord
        {
            Day = State.Day,
            Name = "Partner kjøpt ut",
            Category = EventCategory.Equipment,
            Description = SimText.Nb($"Konsortiepartneren i {site.Label} er kjøpt ut for {price:N0} kr"),
            BalanceChange = -price,
        });
        State.TrimEventLog();
        return CommandResult.Success($"{site.Label}: partner bought out", (double)price, id: site.Id);
    }

    /// <summary>The partner's share of the unit's book value times the buy-out multiple (NOK).</summary>
    public decimal BuyOutPriceNok(SeaSite site)
    {
        var unit = site.Offshore;
        if (unit == null || !unit.IsConsortium) return 0m;
        decimal own = State.FacilityAssets.Where(a => a.UnitId == site.Id).Sum(a => a.BookValueNok);
        decimal partner = own * (decimal)((1.0 - unit.EquityShare) / unit.EquityShare);
        return partner * (decimal)OffshoreCal.Costs.BuyoutMultiple;
    }
}

/// <summary>One phase's payment in a build preview (spec §6.2 "payment schedule per phase").</summary>
/// <param name="Phase">The phase key.</param>
/// <param name="PlannedStartDay">The day it starts on the plan.</param>
/// <param name="PlannedDays">Its planned days.</param>
/// <param name="PaymentNok">Paid at its start (the company's share, with a turnkey premium).</param>
/// <param name="OverrunMedianNok">A managed contract's median overrun, paid at its end (0 turnkey).</param>
/// <param name="OverrunP90Nok">The overrun's 90th percentile.</param>
/// <param name="PlayerStarted">True for the tow: it waits for the player.</param>
public sealed record OffshorePhasePayment(string Phase, int PlannedStartDay, int PlannedDays, decimal PaymentNok,
    decimal OverrunMedianNok, decimal OverrunP90Nok, bool PlayerStarted);

/// <summary>The build preview of an offshore unit (spec §6.2 step 4; F2-07).</summary>
public sealed record OffshoreBuildPreview(string PlotId, OffshoreType Type, ContractForm Contract, double EquityShare,
    decimal ContractCapexNok, decimal CompanyCapexNok, double TurnkeyPremium, decimal OverrunMedianNok, decimal OverrunP90Nok,
    IReadOnlyList<OffshorePhasePayment> Payments, double ConsortiumEquityShare, decimal ConsortiumCompanyCapexNok,
    double BuyoutMultiple, int Licences, double LicenceTonnesMtb, decimal LicenceMarketValueNok, decimal LicencePriceNokPerTonne,
    decimal ConversionFeeNok, bool ResourceRentBeforeConversion, int TeethingDefectsMin, int TeethingDefectsMax,
    decimal TeethingBudgetNok, IReadOnlyList<int> ShakedownOptionsDays, int PlannedBuildDays, int FirstStockingDay,
    IReadOnlyList<FarmTypeGateStatus> Gates, decimal FirstPaymentNok, bool Affordable, string RefusalKey);

/// <summary>One construction phase as the board reads it.</summary>
public sealed record OffshorePhaseView(string Phase, int PlannedDays, string Status, int StartDay, int EndDay, double Progress,
    bool PlayerStarted, decimal PaymentNok);

/// <summary>An offshore unit's construction (spec §6.3).</summary>
public sealed record OffshoreConstructionView(string SiteId, string ProjectId, bool Complete, string CurrentPhase,
    int CurrentPhaseIndex, double PhaseProgress, double OverallProgress, string WaitingReason, int ShelterUntilDay,
    IReadOnlyList<OffshorePhaseView> Phases, decimal PaidNok);

/// <summary>The tow preview (spec §6.3 "Start slep nå: P(vinduet holder 3 døgn)").</summary>
public sealed record OffshoreTowPreview(string SiteId, double HoldProbability, int HoldDays, double TowLimitHsM,
    decimal TugsNokPerDay, decimal CostIfHoldsNok, decimal ExpectedCostNok, int ShelterDaysMin, int ShelterDaysMax);
