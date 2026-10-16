using System;
using System.Collections.Generic;
using LakskongenSim.Calibration;

namespace LakskongenSim.Core;

/// <summary>How a facility is contracted (docs/FARM_TYPES_SPEC.md §3.2).</summary>
public enum ContractForm
{
    /// <summary>The company manages the build and carries the cost overrun.</summary>
    Managed,
    /// <summary>A contractor delivers at a fixed price with a premium (no cost overrun; delays still happen).</summary>
    Turnkey,
}

/// <summary>Where a construction project stands.</summary>
public enum ConstructionStatus
{
    /// <summary>The current phase waits to start: for the player (a player-started phase), a closed month or the money for its payment.</summary>
    Waiting,
    /// <summary>The current phase is under way.</summary>
    Building,
    /// <summary>Every phase is done; the unit is commissioned and on the asset register.</summary>
    Complete,
}

/// <summary>
/// One phase of a construction project (spec §3.2). The lanes define their
/// phase lists in their own files (a land lot's site works, tanks and
/// commissioning; an offshore unit's yard, tow and mooring).
/// </summary>
public sealed class ConstructionPhase
{
    /// <summary>Phase key ("yard", "tow", "tanks" …), unique within its project.</summary>
    public string Key { get; init; } = "";

    /// <summary>Planned length (days, at least 1).</summary>
    public int PlannedDays { get; init; }

    /// <summary>Share of the project's budget paid at this phase (the shares of a project add up to 1).</summary>
    public double CapexShare { get; init; }

    /// <summary>Highest sea state the phase may start in (m Hs); evaluated by the sea lane's operability (F2-06/F2-07).</summary>
    public double? MaxHsM { get; init; }

    /// <summary>Calendar months (1-12) the phase may not start in (sea works closed in spring, spec §3.9).</summary>
    public int[] ClosedMonths { get; init; } = Array.Empty<int>();

    /// <summary>True when the player starts the phase (e.g. "tow"); otherwise it starts when the previous one ends.</summary>
    public bool PlayerStarted { get; init; }
}

/// <summary>
/// A facility under construction (roadmap F2-04, spec §3.2, §3.6, §3.7). Its
/// phases are paid one at a time as capex (<c>CostBooking.Capex</c>): the
/// planned payment when a phase starts and, for a managed build, the cost
/// overrun when it ends. At a phase's start the <c>facility_construction</c>
/// stream (suffix project id and phase key) gives exactly two uniforms: the
/// cost factor and the duration factor, read by inverse CDF ("draw, then
/// decide": a turnkey phase draws both and ignores the cost factor). When the
/// last phase ends, the project is complete and the money paid enters the
/// asset register from that day. Saved in <c>GameState.ConstructionProjects</c>.
/// </summary>
public sealed class ConstructionProject
{
    /// <summary>Deterministic id "cp_N".</summary>
    public string Id { get; set; } = "";

    /// <summary>The unit being built (a land site's or an offshore sea site's id).</summary>
    public string UnitId { get; set; } = "";

    /// <summary>Land site or offshore unit (sets the asset's life).</summary>
    public Finance.FacilityAssetKind AssetKind { get; set; }

    /// <summary>Managed or turnkey.</summary>
    public ContractForm Contract { get; set; }

    /// <summary>The project's budget (NOK) before overrun, premium and equity share.</summary>
    public decimal BudgetNok { get; set; }

    /// <summary>The company's share of the unit (1 = all of it; 0.35 for a consortium unit).</summary>
    public double EquityShare { get; set; } = 1.0;

    /// <summary>The phases, in order.</summary>
    public List<ConstructionPhase> Phases { get; set; } = new();

    /// <summary>Index of the current phase (= Phases.Count once complete).</summary>
    public int CurrentPhase { get; set; }

    /// <summary>Waiting, building or complete.</summary>
    public ConstructionStatus Status { get; set; } = ConstructionStatus.Waiting;

    /// <summary>True once the player has started the current player-started phase.</summary>
    public bool PlayerStartRequested { get; set; }

    /// <summary>Day the current phase started (-1 before it starts).</summary>
    public int PhaseStartDay { get; set; } = -1;

    /// <summary>Length of the current phase (days), its planned days times its duration factor.</summary>
    public int PhaseDays { get; set; }

    /// <summary>Cost factor of the current phase (1 + the overrun fraction; 1 for turnkey), drawn at its start.</summary>
    public double OverrunFactor { get; set; } = 1.0;

    /// <summary>Duration factor of the current phase (1 + the slip fraction), drawn at its start.</summary>
    public double DurationFactor { get; set; } = 1.0;

    /// <summary>Money paid so far (NOK, the company's share, premium and overruns included).</summary>
    public decimal PaidNOK { get; set; }

    /// <summary>The day each finished phase ended (drives the First Sight timelapse).</summary>
    public List<int> PhaseCompletedDays { get; set; } = new();

    /// <summary>Set by a lane's acceleration verb (spec §3.4); recorded, not yet read.</summary>
    public bool Accelerated { get; set; }

    /// <summary>The phase under way or waiting, or null once complete.</summary>
    public ConstructionPhase? Current => CurrentPhase >= 0 && CurrentPhase < Phases.Count ? Phases[CurrentPhase] : null;

    /// <summary>Day the current phase ends (meaningful while building).</summary>
    public int PhaseEndDay => PhaseStartDay + PhaseDays;
}

/// <summary>
/// The arithmetic of a construction phase (spec §3.2, calibration
/// <c>facilities.common.construction</c>): what it costs at its start and its
/// end, and how long it takes, from its two pre-drawn uniforms. Pure.
/// </summary>
public static class ConstructionSchedule
{
    /// <summary>Quantile at which the cost and duration lognormals are truncated (a draw maps onto [0, this)).</summary>
    public const double TruncationQuantile = 0.99;

    /// <summary>Exactly this many uniforms are drawn at each phase's start.</summary>
    public const int DrawsPerPhase = 2;

    /// <summary>
    /// The overrun or slip fraction at uniform <paramref name="u"/>: a lognormal
    /// with the given median and 90th percentile, truncated at its 99th
    /// percentile (u in [0, 1) maps onto quantiles [0, 0.99)). Never negative.
    /// </summary>
    public static double ExcessFraction(double median, double p90, double u)
    {
        if (!(u >= 0.0 && u < 1.0)) throw new ArgumentOutOfRangeException(nameof(u), u, "a uniform lies in [0, 1)");
        if (median <= 0.0) return 0.0;
        double p = Math.Max(u * TruncationQuantile, double.Epsilon);
        return StandardNormal.LognormalQuantile(median, Math.Max(p90, median * (1.0 + 1e-9)), p);
    }

    /// <summary>Cost factor of a phase (1 for turnkey).</summary>
    public static double OverrunFactor(FacilityConstructionCalibration c, ContractForm contract, double u)
        => contract == ContractForm.Turnkey ? 1.0 : 1.0 + ExcessFraction(c.OverrunMedian, c.OverrunP90, u);

    /// <summary>Duration factor of a phase.</summary>
    public static double DurationFactor(FacilityConstructionCalibration c, double u)
        => 1.0 + ExcessFraction(c.DurationSlipMedian, c.DurationSlipP90, u);

    /// <summary>Days of a phase: its planned days times its duration factor, rounded, at least 1.</summary>
    public static int PhaseDays(ConstructionPhase phase, double durationFactor)
        => Math.Max(1, (int)Math.Round(Math.Max(1, phase.PlannedDays) * durationFactor, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Payment at a phase's start (NOK): budget × share × equity share, plus the
    /// premium of a turnkey contract.
    /// </summary>
    public static decimal StartPayment(FacilityConstructionCalibration c, ConstructionProject project, ConstructionPhase phase)
    {
        decimal premium = project.Contract == ContractForm.Turnkey ? 1m + (decimal)c.TurnkeyPremium : 1m;
        return project.BudgetNok * (decimal)phase.CapexShare * (decimal)project.EquityShare * premium;
    }

    /// <summary>Payment at a managed phase's end (NOK): the overrun on its planned share (zero for turnkey).</summary>
    public static decimal OverrunPayment(ConstructionProject project, ConstructionPhase phase)
        => project.Contract == ContractForm.Turnkey || project.OverrunFactor <= 1.0
            ? 0m
            : project.BudgetNok * (decimal)phase.CapexShare * (decimal)project.EquityShare * (decimal)(project.OverrunFactor - 1.0);
}
