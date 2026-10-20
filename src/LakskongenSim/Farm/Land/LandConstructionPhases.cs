using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Calibration;
using LakskongenSim.Core;

namespace LakskongenSim.Farm.Land
{
    /// <summary>What a land construction project builds (F2-14).</summary>
    public enum LandBuildKind
    {
        /// <summary>A new lot or module of the template.</summary>
        NewLot = 0,
        /// <summary>A pilot lot grown into a full HFS lot (spec §2A: +1.25 bn).</summary>
        PilotExpansion = 1,
    }

    /// <summary>One phase of a land build on the calendar: when it starts and ends.</summary>
    /// <param name="Key">The phase key ("site_works" …).</param>
    /// <param name="StartDay">Sim day the phase starts.</param>
    /// <param name="EndDay">Sim day the phase ends.</param>
    /// <param name="WaitedDays">Days it waited for an open month before it started.</param>
    /// <param name="SuspendedDays">Closed-season days it stood still once started.</param>
    public readonly record struct LandPhaseSpan(string Key, int StartDay, int EndDay, int WaitedDays, int SuspendedDays);

    /// <summary>A land build on the calendar: every phase, the first tanks and the whole unit.</summary>
    /// <param name="Phases">The phases in order.</param>
    /// <param name="FirstTanksDay">Day the first tanks take fish.</param>
    /// <param name="CompleteDay">Day the whole unit is built.</param>
    public sealed record LandSchedule(IReadOnlyList<LandPhaseSpan> Phases, int FirstTanksDay, int CompleteDay);

    /// <summary>One phase's payments in a build preview (NOK, the company's share).</summary>
    /// <param name="Key">The phase key.</param>
    /// <param name="PlannedDays">Its planned days.</param>
    /// <param name="StartDay">Its start on the planned schedule.</param>
    /// <param name="EndDay">Its end on the planned schedule.</param>
    /// <param name="PaymentNok">Paid when it starts (premium included for a turnkey contract).</param>
    /// <param name="OverrunP50Nok">Median overrun paid when it ends (0 for turnkey).</param>
    /// <param name="OverrunP90Nok">90th-percentile overrun paid when it ends (0 for turnkey).</param>
    public sealed record LandPhasePayment(string Key, int PlannedDays, int StartDay, int EndDay, decimal PaymentNok,
        decimal OverrunP50Nok, decimal OverrunP90Nok);

    /// <summary>
    /// The phases, the schedule and the payments of a land build (roadmap
    /// F2-14, docs/FARM_TYPES_SPEC.md §6.3): the template's phase plan from the
    /// calibration as the shared construction project's phases (F2-04), the
    /// pad's blasting in the site works of the first build on a pad, the sea
    /// works closed and stood still in April-June ([L:R12]), and the planned
    /// calendar and payment schedule a preview shows. Pure: nothing here draws a
    /// random number or reads engine state; the p50/p90 bands are quantiles of
    /// the engine's own cost and duration draws, computed on a deterministic
    /// quasi-random grid.
    /// </summary>
    public static class LandConstructionPhases
    {
        /// <summary>Phase keys, in build order.</summary>
        public const string SiteWorks = "site_works", TankRings = "tank_rings", TreatmentHall = "treatment_hall",
            SeaWorks = "sea_works", FirstTanks = "first_tanks", Completion = "completion";

        /// <summary>Points of the quasi-random grid the preview's p50/p90 are read from.</summary>
        public const int PreviewSamples = 4_096;

        /// <summary>The phase plan of <paramref name="template"/> (or its pilot's expansion).</summary>
        public static LandPhasePlanCalibration PlanOf(LandBuildCalibration build, LandTemplate template, LandBuildKind kind)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (kind == LandBuildKind.PilotExpansion) return build.HfsExpansionPhases;
            return template switch
            {
                LandTemplate.HfsLot => build.HfsLotPhases,
                LandTemplate.HfsPilotLot => build.HfsPilotPhases,
                LandTemplate.RasHall => build.RasHallPhases,
                LandTemplate.PostSmoltModule => build.PostSmoltPhases,
                _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown land template"),
            };
        }

        /// <summary>The unit's own budget (NOK) before blasting, the denitrification commitment and the contract's premium.</summary>
        public static decimal UnitCapexNok(LandBuildCalibration build, LandTemplate template, LandBuildKind kind)
        {
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (kind == LandBuildKind.PilotExpansion) return build.HfsPilotExpansionCapexNok;
            return template switch
            {
                LandTemplate.HfsLot => build.HfsLotCapexNok,
                LandTemplate.HfsPilotLot => build.HfsPilotCapexNok,
                LandTemplate.RasHall => build.RasHallCapexNok,
                LandTemplate.PostSmoltModule => build.PostSmoltModuleCapexNok,
                _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown land template"),
            };
        }

        /// <summary>
        /// The project's budget (NOK): the unit's capex, plus the denitrification
        /// stage's share when committed, plus the pad's blasting when this build
        /// blasts the pad (the first build on it, with site works).
        /// </summary>
        public static decimal BudgetNok(LandBuildCalibration build, LandTemplate template, LandBuildKind kind,
            bool denitrification, decimal padBlastingNok)
        {
            decimal unit = UnitCapexNok(build, template, kind);
            decimal denitrify = denitrification ? unit * (decimal)build.DenitrificationCapexShare : 0m;
            bool blasts = PlanOf(build, template, kind).SiteWorksDays > 0;
            return unit + denitrify + (blasts ? Math.Max(0m, padBlastingNok) : 0m);
        }

        /// <summary>
        /// The phases of the build as construction-project phases: each phase of
        /// the plan with days, in order; the shares are the plan's on the unit's
        /// budget, with the pad's blasting paid in the site works. The sea works
        /// neither start nor run in the closed season.
        /// </summary>
        public static IReadOnlyList<ConstructionPhase> Phases(LandPhasePlanCalibration plan, LandPermitCalibration permits,
            decimal unitBudgetNok, decimal padBlastingNok)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            decimal blasting = plan.SiteWorksDays > 0 ? Math.Max(0m, padBlastingNok) : 0m;
            decimal total = unitBudgetNok + blasting;
            if (total <= 0m) throw new ArgumentOutOfRangeException(nameof(unitBudgetNok), unitBudgetNok, "A build needs a budget");
            int[] closed = ClosedMonths(permits);
            var steps = new (string Key, int Days, double Share, bool Sea)[]
            {
                (SiteWorks, plan.SiteWorksDays, plan.SiteWorksShare, false),
                (TankRings, plan.TankRingsDays, plan.TankRingsShare, false),
                (TreatmentHall, plan.TreatmentHallDays, plan.TreatmentHallShare, false),
                (SeaWorks, plan.SeaWorksDays, plan.SeaWorksShare, true),
                (FirstTanks, plan.FirstTanksDays, plan.FirstTanksShare, false),
                (Completion, plan.CompletionDays, plan.CompletionShare, false),
            };
            double planShares = steps.Where(s => s.Days > 0).Sum(s => s.Share);
            var phases = new List<ConstructionPhase>();
            double assigned = 0.0;
            var built = steps.Where(s => s.Days > 0).ToList();
            for (int i = 0; i < built.Count; i++)
            {
                var step = built[i];
                decimal money = unitBudgetNok * (decimal)(planShares > 0.0 ? step.Share / planShares : 0.0)
                    + (step.Key == SiteWorks ? blasting : 0m);
                // The last phase takes the rest, so the shares add up to exactly 1.
                double share = i == built.Count - 1 ? 1.0 - assigned : (double)(money / total);
                assigned += share;
                phases.Add(new ConstructionPhase
                {
                    Key = step.Key,
                    PlannedDays = step.Days,
                    CapexShare = share,
                    ClosedMonths = step.Sea ? closed : Array.Empty<int>(),
                    SuspendedInClosedMonths = step.Sea,
                });
            }
            return phases;
        }

        /// <summary>The months closed to sea works (spec §3.9: April-June, [L:R12]).</summary>
        public static int[] ClosedMonths(LandPermitCalibration permits)
        {
            if (permits == null) throw new ArgumentNullException(nameof(permits));
            int from = Math.Clamp(permits.SeaWorksClosedFromMonth, 1, 12), to = Math.Clamp(permits.SeaWorksClosedToMonth, 1, 12);
            var months = new List<int>();
            for (int m = from; ; m = m % 12 + 1)
            {
                months.Add(m);
                if (m == to || months.Count >= 11) break;
            }
            return months.ToArray();
        }

        /// <summary>
        /// The calendar of <paramref name="phases"/> started on
        /// <paramref name="startDay"/> as the engine runs them: a phase starts
        /// the day the previous one ends (or the first day of an open month), and
        /// a phase stopped by its closed months stands still in them; each phase
        /// lasts its planned days × <paramref name="durationFactors"/> (1 when none).
        /// </summary>
        public static LandSchedule Schedule(IReadOnlyList<ConstructionPhase> phases, int startDay,
            IReadOnlyList<double>? durationFactors = null)
        {
            if (phases == null) throw new ArgumentNullException(nameof(phases));
            var spans = new List<LandPhaseSpan>(phases.Count);
            int day = startDay;
            int firstTanks = -1;
            for (int i = 0; i < phases.Count; i++)
            {
                var phase = phases[i];
                int waited = 0;
                while (IsClosed(phase, day) && waited < SimCalendar.DaysPerYear) { day++; waited++; }
                int start = day;
                double factor = durationFactors != null && i < durationFactors.Count ? durationFactors[i] : 1.0;
                int days = ConstructionSchedule.PhaseDays(phase, factor);
                int worked = 0, suspended = 0, d = start;
                while (worked < days)
                {
                    d++;
                    if (phase.SuspendedInClosedMonths && IsClosed(phase, d)) suspended++;
                    else worked++;
                }
                spans.Add(new LandPhaseSpan(phase.Key, start, d, waited, suspended));
                if (phase.Key == FirstTanks) firstTanks = d;
                day = d;
            }
            int complete = spans.Count > 0 ? spans[^1].EndDay : startDay;
            return new LandSchedule(spans, firstTanks >= 0 ? firstTanks : complete, complete);
        }

        private static bool IsClosed(ConstructionPhase phase, int day)
            => phase.ClosedMonths.Length > 0 && phase.ClosedMonths.Contains(SimCalendar.Month(day));

        /// <summary>
        /// The payment schedule of a build (spec §6.2 "payment schedule per
        /// phase: managed with its p50/p90 overrun band, turnkey +12 % with no
        /// overrun"), on the planned calendar from <paramref name="startDay"/>.
        /// </summary>
        public static IReadOnlyList<LandPhasePayment> Payments(IReadOnlyList<ConstructionPhase> phases, decimal budgetNok,
            ContractForm contract, FacilityConstructionCalibration risk, int startDay)
        {
            if (risk == null) throw new ArgumentNullException(nameof(risk));
            var project = new ConstructionProject { BudgetNok = budgetNok, Contract = contract, EquityShare = 1.0 };
            var schedule = Schedule(phases, startDay);
            var rows = new List<LandPhasePayment>(phases.Count);
            for (int i = 0; i < phases.Count; i++)
            {
                var phase = phases[i];
                decimal pay = ConstructionSchedule.StartPayment(risk, project, phase);
                decimal p50 = 0m, p90 = 0m;
                if (contract == ContractForm.Managed)
                {
                    decimal planned = budgetNok * (decimal)phase.CapexShare;
                    p50 = planned * (decimal)ConstructionSchedule.ExcessFraction(risk.OverrunMedian, risk.OverrunP90, 0.5 / ConstructionSchedule.TruncationQuantile);
                    p90 = planned * (decimal)ConstructionSchedule.ExcessFraction(risk.OverrunMedian, risk.OverrunP90, 0.9 / ConstructionSchedule.TruncationQuantile);
                }
                rows.Add(new LandPhasePayment(phase.Key, phase.PlannedDays, schedule.Phases[i].StartDay, schedule.Phases[i].EndDay,
                    pay, p50, p90));
            }
            return rows;
        }

        /// <summary>The distribution of a build's outcome over the engine's draws: the median and the 90th percentile.</summary>
        /// <param name="OverrunP50">Median total overrun (fraction of the budget; 0 for turnkey).</param>
        /// <param name="OverrunP90">90th-percentile total overrun (fraction of the budget).</param>
        /// <param name="FirstTanksP50Day">Median day of the first tanks.</param>
        /// <param name="FirstTanksP90Day">90th-percentile day of the first tanks.</param>
        /// <param name="CompleteP50Day">Median day the unit is complete.</param>
        /// <param name="CompleteP90Day">90th-percentile day the unit is complete.</param>
        public readonly record struct LandBuildRisk(double OverrunP50, double OverrunP90, int FirstTanksP50Day,
            int FirstTanksP90Day, int CompleteP50Day, int CompleteP90Day);

        /// <summary>
        /// The median and 90th percentile of the build's total overrun and of its
        /// first-tanks and completion days, over the engine's two uniforms a
        /// phase (cost, duration) read by the same inverse CDFs, on a
        /// deterministic Halton grid of <see cref="PreviewSamples"/> points.
        /// </summary>
        public static LandBuildRisk Risk(IReadOnlyList<ConstructionPhase> phases, ContractForm contract,
            FacilityConstructionCalibration risk, int startDay)
        {
            if (phases == null) throw new ArgumentNullException(nameof(phases));
            if (risk == null) throw new ArgumentNullException(nameof(risk));
            int n = phases.Count;
            var overruns = new double[PreviewSamples];
            var firsts = new int[PreviewSamples];
            var completes = new int[PreviewSamples];
            var factors = new double[n];
            for (int k = 0; k < PreviewSamples; k++)
            {
                double overrun = 0.0;
                for (int i = 0; i < n; i++)
                {
                    double uCost = Halton(k + 1, Primes[(2 * i) % Primes.Length]);
                    double uDuration = Halton(k + 1, Primes[(2 * i + 1) % Primes.Length]);
                    overrun += phases[i].CapexShare * (ConstructionSchedule.OverrunFactor(risk, contract, uCost) - 1.0);
                    factors[i] = ConstructionSchedule.DurationFactor(risk, uDuration);
                }
                var schedule = Schedule(phases, startDay, factors);
                overruns[k] = overrun;
                firsts[k] = schedule.FirstTanksDay;
                completes[k] = schedule.CompleteDay;
            }
            Array.Sort(overruns);
            Array.Sort(firsts);
            Array.Sort(completes);
            return new LandBuildRisk(Quantile(overruns, 0.5), Quantile(overruns, 0.9), Quantile(firsts, 0.5),
                Quantile(firsts, 0.9), Quantile(completes, 0.5), Quantile(completes, 0.9));
        }

        private static readonly int[] Primes = { 2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37 };

        /// <summary>The <paramref name="index"/>-th point of the van der Corput sequence in <paramref name="radix"/> (0-1).</summary>
        private static double Halton(int index, int radix)
        {
            double result = 0.0, f = 1.0;
            for (int i = index; i > 0; i /= radix)
            {
                f /= radix;
                result += f * (i % radix);
            }
            return result;
        }

        private static double Quantile(double[] sorted, double q) => sorted[Math.Clamp((int)Math.Floor(q * sorted.Length), 0, sorted.Length - 1)];

        private static int Quantile(int[] sorted, double q) => sorted[Math.Clamp((int)Math.Floor(q * sorted.Length), 0, sorted.Length - 1)];

        /// <summary>
        /// The electrical design load of one lot (MW): its heat pumps at full
        /// input, the intake pumps at the plot's capacity, the loop pumping,
        /// treatment and UV at the design loop flow of a fully stocked lot at the
        /// template's reuse, and the base load. What the pad's grid must carry
        /// (F2-14 <c>grid_capacity</c>).
        /// </summary>
        public static double DesignPowerMW(LandTemplate template, LandCalibration calibration, in LandIntakeSite site)
        {
            if (calibration == null) throw new ArgumentNullException(nameof(calibration));
            var t = calibration.For(template);
            double volume = t.GrowOutTanks * LandTank.VolumeOf(t.GrowOutDiameterM, t.WaterDepthM)
                + t.PreGrowTanks * LandTank.VolumeOf(t.PreGrowDiameterM, t.WaterDepthM);
            var flows = LandWaterModel.Flows(volume, t, t.DefaultReuse, site.PumpCapacityM3sPerLot, calibration.Intake);
            // The intake pumps at the design new-water flow (capped by the plot's pumps), half from each line.
            double intake = LandEnergyModel.IntakePumpingKWh(flows.NewM3, 0.5, site, calibration.Energy);
            double kwhPerDay = intake
                + LandEnergyModel.LoopPumpingKWh(flows.ReusedM3, calibration.Energy)
                + LandEnergyModel.TreatmentKWh(flows.ReusedM3, t)
                + LandEnergyModel.UvKWh(flows.NewM3, site, calibration.Energy)
                + t.BaseKWhPerDay;
            return t.HeatPumpMWe + kwhPerDay / 24.0 / 1000.0;
        }
    }
}
