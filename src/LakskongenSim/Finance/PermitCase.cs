using System;
using System.Collections.Generic;

namespace LakskongenSim.Finance
{
    /// <summary>The kinds of permit case a farm type goes through (docs/FARM_TYPES_SPEC.md §2C, §3.2).</summary>
    public enum PermitKind
    {
        /// <summary>A zoning plan for a land pad (1-2 years unless pre-zoned).</summary>
        Zoning,
        /// <summary>The fee-free land aquaculture permit capping standing biomass and production.</summary>
        LandAquaculture,
        /// <summary>The discharge permit (nitrogen, phosphorus, particles).</summary>
        Discharge,
        /// <summary>The sea-works permit for an intake or outfall.</summary>
        SeaWorks,
        /// <summary>Intake disinfection and biosecurity approval.</summary>
        Biosecurity,
        /// <summary>A grid connection from the queue.</summary>
        GridConnection,
        /// <summary>A development licence for an offshore concept.</summary>
        DevelopmentLicence,
        /// <summary>Conversion of development licences to commercial licences.</summary>
        Conversion,
        /// <summary>A dispensation (density above 25 kg/m³, fish per pen).</summary>
        Dispensation,
        /// <summary>The offshore-area competition (stage 3).</summary>
        OffshoreArea,
    }

    /// <summary>Where a permit case stands.</summary>
    public enum PermitStatus
    {
        /// <summary>Submitted; the decision falls on <see cref="PermitCase.DecisionDay"/>.</summary>
        Submitted,
        /// <summary>Granted.</summary>
        Approved,
        /// <summary>Refused (it may be appealed).</summary>
        Refused,
        /// <summary>A refusal under appeal; the new decision falls on <see cref="PermitCase.DecisionDay"/>.</summary>
        Appealed,
        /// <summary>Granted, but its conditions or payment lapsed.</summary>
        Lapsed,
        /// <summary>Withdrawn by the company.</summary>
        Withdrawn,
    }

    /// <summary>
    /// The terms a permit case is decided on, supplied by the lane that files
    /// it from its own calibration: the earliest and latest decision day after
    /// submission, the refusal probability (shown to the player before
    /// submission) and the fee paid at submission.
    /// </summary>
    public sealed record PermitTerms(int MinDecisionDays, int MaxDecisionDays, double RefusalProbability, decimal FeeNok = 0m)
    {
        /// <summary>The first problem with these terms, or null when they are usable.</summary>
        public string? Problem()
        {
            if (MinDecisionDays < 0 || MaxDecisionDays < MinDecisionDays) return "decision days must satisfy 0 <= min <= max";
            if (!(RefusalProbability >= 0.0 && RefusalProbability <= 1.0)) return "refusal probability must lie in [0, 1]";
            if (FeeNok < 0m) return "the fee cannot be negative";
            return null;
        }
    }

    /// <summary>
    /// One permit case (roadmap F2-04, spec §3.2, §3.7). At submission (and at
    /// an appeal) the <c>facility_permits</c> stream (suffix: the case id and
    /// its round) gives exactly two uniforms: the first sets the decision day
    /// inside the terms' window, the second is compared with the refusal
    /// probability on that day ("draw, then decide": the outcome is fixed at
    /// submission and nothing the player does later moves a stream). Saved in
    /// <c>GameState.PermitCases</c>.
    /// </summary>
    public sealed class PermitCase
    {
        /// <summary>Deterministic id "pc_N".</summary>
        public string Id { get; set; } = "";

        /// <summary>The kind of permit.</summary>
        public PermitKind Kind { get; set; }

        /// <summary>The plot the case is for ("" when it is not tied to a plot).</summary>
        public string PlotId { get; set; } = "";

        /// <summary>The unit the case is for ("" before the unit exists).</summary>
        public string UnitId { get; set; } = "";

        /// <summary>Day the case was submitted.</summary>
        public int SubmittedDay { get; set; }

        /// <summary>Day of the (next) decision.</summary>
        public int DecisionDay { get; set; }

        /// <summary>Where the case stands.</summary>
        public PermitStatus Status { get; set; } = PermitStatus.Submitted;

        /// <summary>Probability of refusal in the current round, shown to the player before submission.</summary>
        public double RefusalProbability { get; set; }

        /// <summary>The uniform drawn for the current round's outcome: refused when below <see cref="RefusalProbability"/>.</summary>
        public double OutcomeDraw { get; set; }

        /// <summary>Decision rounds so far (1 = the first decision; each appeal adds one).</summary>
        public int Round { get; set; } = 1;

        /// <summary>Day of the last decision (-1 before any).</summary>
        public int DecidedDay { get; set; } = -1;

        /// <summary>Mitigations the application offered (keys; the lanes set what each cuts).</summary>
        public List<string> Mitigations { get; set; } = new();

        /// <summary>Conditions of the permit (maxStandingT, maxProductionT, nitrogenTPerYear, maxDensityKgM3 …).</summary>
        public Dictionary<string, double> Conditions { get; set; } = new(StringComparer.Ordinal);

        /// <summary>True while a decision is pending.</summary>
        public bool IsPending => Status is PermitStatus.Submitted or PermitStatus.Appealed;

        /// <summary>The decision the pre-drawn uniform gives: refused when it lies below the refusal probability.</summary>
        public bool WillBeRefused => OutcomeDraw < RefusalProbability;

        /// <summary>The decision day a uniform gives inside the terms' window (submission day + min … max, inclusive).</summary>
        public static int DecisionDayFor(int submittedDay, PermitTerms terms, double u)
        {
            ArgumentNullException.ThrowIfNull(terms);
            int span = terms.MaxDecisionDays - terms.MinDecisionDays + 1;
            int offset = Math.Min(span - 1, (int)Math.Floor(u * span));
            return submittedDay + terms.MinDecisionDays + offset;
        }
    }
}
