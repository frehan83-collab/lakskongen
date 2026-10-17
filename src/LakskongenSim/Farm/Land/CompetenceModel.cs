using System;
using LakskongenSim.Calibration;

namespace LakskongenSim.Farm.Land
{
    /// <summary>
    /// What a land grow-out site has done for its competence (roadmap F2-15,
    /// docs/FARM_TYPES_SPEC.md §3.3 "Growth and competence"): the levers it
    /// pulled and when, and its completed cycles. Saved on the site; the
    /// competence itself is derived (<see cref="CompetenceModel.Evaluate"/>).
    /// </summary>
    public sealed class LandCompetence
    {
        /// <summary>Day the operations manager (driftsleder) was hired, -1 when none is employed.</summary>
        public int DriftslederSinceDay { get; set; } = -1;

        /// <summary>Day the training programme started, -1 when none ran.</summary>
        public int TrainingStartDay { get; set; } = -1;

        /// <summary>Day the external consultancy started, -1 when none ran.</summary>
        public int ConsultancyStartDay { get; set; } = -1;

        /// <summary>Day the operating staff were hired ahead of commissioning, -1 when they were not.</summary>
        public int StaffHiredDay { get; set; } = -1;

        /// <summary>Day the site took its first fish, -1 before.</summary>
        public int FirstFishDay { get; set; } = -1;

        /// <summary>Production cycles the site has completed (each a full rotation of its grow-out tanks harvested).</summary>
        public int CompletedCycles { get; set; }

        /// <summary>Fish the site has harvested since it was built (the cycle count's measure).</summary>
        public long HarvestedFish { get; set; }

        /// <summary>Day of the last change of the site's feeding plan, -1 when it never changed.</summary>
        public int FeedPlanChangedDay { get; set; } = -1;

        /// <summary>Last day the feed-plan change raises the FCR (inclusive), -1 when no penalty runs.</summary>
        public int FcrPenaltyUntilDay { get; set; } = -1;
    }

    /// <summary>A site's competence and what it is made of (spec §3.3).</summary>
    /// <param name="Start">The start (0.4).</param>
    /// <param name="Research">From the research <c>land-based-ras</c>.</param>
    /// <param name="Driftsleder">From an employed operations manager.</param>
    /// <param name="Training">From the training programme's completed steps.</param>
    /// <param name="Consultancy">From a running consultancy.</param>
    /// <param name="EarlyHiring">From staff hired well before the first fish.</param>
    /// <param name="Cycles">From completed cycles.</param>
    /// <param name="Total">The competence: the sum, at most 1.</param>
    public readonly record struct CompetenceBreakdown(double Start, double Research, double Driftsleder, double Training,
        double Consultancy, double EarlyHiring, double Cycles, double Total);

    /// <summary>
    /// The competence model of a land grow-out site (roadmap F2-15, spec §3.3):
    /// competence starts at 0.4 and rises with research, a driftsleder, a
    /// training programme, a consultancy, early hiring and completed cycles; it
    /// sets the utilisation cap (0.55 at 0.4, rising linearly to 1 at 1.0, the
    /// Oppkjøring) and the growth multiplier on the growth law (the achieved
    /// result at 0.4, the plan at 1.0, [L:R1], [V:17]). Pure.
    /// </summary>
    public static class CompetenceModel
    {
        /// <summary>
        /// The competence of <paramref name="state"/> on <paramref name="day"/>,
        /// with <paramref name="researchDone"/> the research <c>land-based-ras</c>.
        /// </summary>
        public static CompetenceBreakdown Evaluate(LandCompetence state, int day, bool researchDone,
            LandCompetenceCalibration calibration)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double research = researchDone ? calibration.Research : 0.0;
            double driftsleder = state.DriftslederSinceDay >= 0 && day >= state.DriftslederSinceDay ? calibration.Driftsleder : 0.0;
            double training = 0.0;
            if (state.TrainingStartDay >= 0 && day >= state.TrainingStartDay && calibration.TrainingStepDays > 0)
            {
                int steps = (day - state.TrainingStartDay) / calibration.TrainingStepDays;
                training = Math.Min(calibration.TrainingMax, steps * calibration.TrainingPerStep);
            }
            double consultancy = state.ConsultancyStartDay >= 0 && day >= state.ConsultancyStartDay
                && day < state.ConsultancyStartDay + calibration.ConsultancyDays ? calibration.Consultancy : 0.0;
            double early = HiredEarly(state, calibration) ? calibration.EarlyHiring : 0.0;
            double cycles = Math.Max(0, state.CompletedCycles) * calibration.PerCycle;
            double total = Math.Clamp(calibration.Start + research + driftsleder + training + consultancy + early + cycles, 0.0, 1.0);
            return new CompetenceBreakdown(calibration.Start, research, driftsleder, training, consultancy, early, cycles, total);
        }

        /// <summary>True when the staff were hired at least the calibration's lead days before the first fish.</summary>
        public static bool HiredEarly(LandCompetence state, LandCompetenceCalibration calibration)
            => state.StaffHiredDay >= 0 && state.FirstFishDay >= 0
               && state.FirstFishDay - state.StaffHiredDay >= calibration.EarlyHiringLeadDays;

        /// <summary>
        /// Share of nameplate production a year the site can run at with
        /// <paramref name="competence"/>: the calibration's 0.55 at the start
        /// competence, rising linearly to 1 at competence 1 (spec §3.3).
        /// </summary>
        public static double UtilisationCap(double competence, LandCompetenceCalibration calibration)
            => Interpolate(competence, calibration, calibration.UtilisationCapAtStart);

        /// <summary>
        /// Multiplier on the growth law at <paramref name="competence"/>: the
        /// achieved result's at the start competence, the plan's 1.0 at
        /// competence 1, linear between (spec §3.3).
        /// </summary>
        public static double GrowthMultiplier(double competence, LandCompetenceCalibration calibration)
            => Interpolate(competence, calibration, calibration.GrowthAtStart);

        /// <summary>
        /// The competence at which the utilisation cap reaches <paramref name="cap"/>
        /// (the Oppkjøring milestones 55 → 70 → 85 → 100 %).
        /// </summary>
        public static double CompetenceForCap(double cap, LandCompetenceCalibration calibration)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double span = 1.0 - calibration.UtilisationCapAtStart;
            if (span <= 0.0) return calibration.Start;
            double t = Math.Clamp((cap - calibration.UtilisationCapAtStart) / span, 0.0, 1.0);
            return calibration.Start + t * (1.0 - calibration.Start);
        }

        private static double Interpolate(double competence, LandCompetenceCalibration calibration, double atStart)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            double span = 1.0 - calibration.Start;
            if (span <= 0.0) return 1.0;
            double t = Math.Clamp((competence - calibration.Start) / span, 0.0, 1.0);
            return atStart + t * (1.0 - atStart);
        }
    }
}
