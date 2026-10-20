using System;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Delousing treatment methods ordered by mechanical/thermal intensity.
    /// </summary>
    public enum DelousingMethod
    {
        /// <summary>Chemical / medicinal bath (least injurious).</summary>
        Medicinal,
        /// <summary>Freshwater bath.</summary>
        Freshwater,
        /// <summary>Mechanical / brushing systems.</summary>
        Mechanical,
        /// <summary>Thermal treatment (warm-water bath).</summary>
        Thermal,
        /// <summary>Hydrolicer (high-pressure water jets).</summary>
        Hydrolicer
    }

    /// <summary>
    /// Per-method harm characteristics for a delousing transaction.
    /// Grounded in NVI Fish Health Report 2025: thermal/hydrolicer treatments
    /// are a leading driver of handling injury and post-treatment mortality.
    /// </summary>
    public readonly struct DelousingHarmProfile
    {
        /// <summary>Treatment method.</summary>
        public DelousingMethod Method { get; }
        /// <summary>Per-treatment physical injury rate (0-1).</summary>
        public double InjuryRate { get; }
        /// <summary>Additional handling mortality applied at treatment time (0-1).</summary>
        public double HandlingMortalityDelta { get; }
        /// <summary>Contribution to cumulative gill damage per treatment (0-1).</summary>
        public double GillDamageContribution { get; }
        /// <summary>Abstract welfare cost scalar per treatment (>=0).</summary>
        public double WelfareCost { get; }

        /// <summary>Create a harm profile.</summary>
        public DelousingHarmProfile(
            DelousingMethod method,
            double injuryRate,
            double handlingMortalityDelta,
            double gillDamageContribution,
            double welfareCost)
        {
            Method = method;
            InjuryRate = injuryRate;
            HandlingMortalityDelta = handlingMortalityDelta;
            GillDamageContribution = gillDamageContribution;
            WelfareCost = welfareCost;
        }
    }

    /// <summary>
    /// Outcome of evaluating a single delousing transaction.
    /// </summary>
    public readonly struct DelousingHarmResult
    {
        /// <summary>Treatment method.</summary>
        public DelousingMethod Method { get; }
        /// <summary>Mortality arising from the treatment procedure itself (0-1).</summary>
        public double HandlingMortalityDelta { get; }
        /// <summary>Injury load injected by this treatment (0-1, before compounding).</summary>
        public double InjuryRate { get; }
        /// <summary>Gill damage added by this treatment (0-1).</summary>
        public double GillDamageContribution { get; }
        /// <summary>Welfare cost scalar contributed by this treatment.</summary>
        public double WelfareCost { get; }
        /// <summary>Total immediate mortality contribution of the event (handling + injury-linked).</summary>
        public double TotalMortalityContribution { get; }

        /// <summary>Create a result.</summary>
        public DelousingHarmResult(
            DelousingMethod method,
            double handlingMortalityDelta,
            double injuryRate,
            double gillDamageContribution,
            double welfareCost,
            double totalMortalityContribution)
        {
            Method = method;
            HandlingMortalityDelta = handlingMortalityDelta;
            InjuryRate = injuryRate;
            GillDamageContribution = gillDamageContribution;
            WelfareCost = welfareCost;
            TotalMortalityContribution = totalMortalityContribution;
        }
    }

    /// <summary>
    /// Accumulated injury/welfare state across repeated delousing events.
    /// </summary>
    public readonly struct DelousingInjuryState
    {
        /// <summary>Accumulated injury load (0..InjuryLoadCap).</summary>
        public double InjuryLoad { get; init; }
        /// <summary>Cumulative gill damage (0-1).</summary>
        public double GillDamage { get; init; }
        /// <summary>Running total of welfare cost across all treatments.</summary>
        public double CumulativeWelfareCost { get; init; }
        /// <summary>Days elapsed since the last treatment.</summary>
        public int DaysSinceLastTreatment { get; init; }
        /// <summary>Number of treatments applied to date.</summary>
        public int TreatmentsToDate { get; init; }
    }

    /// <summary>
    /// Deterministic model of delousing as a welfare/mortality transaction.
    /// Repeated treatments compound: recent injury raises a baseline mortality
    /// floor that decays slowly over the following days.
    /// </summary>
    public static class DelousingHarmModel
    {
        /// <summary>Days over which a fresh treatment amplifies subsequent injury load.</summary>
        public const int RecoveryWindowDays = 14;

        /// <summary>Upper clamp on injury load.</summary>
        public const double InjuryLoadCap = 3.0;

        /// <summary>Mortality contribution per unit of injury load.</summary>
        public const double MortalityPerInjuryUnit = 0.01;

        /// <summary>Hard ceiling on injury-derived baseline mortality.</summary>
        public const double MaxInjuryMortality = 0.05;

        /// <summary>Daily recovery fraction of injury load (≈14-day half-life).</summary>
        public const double DailyRecoveryFraction = 0.05;

        /// <summary>Return the calibrated harm profile for a method.</summary>
        public static DelousingHarmProfile ProfileFor(DelousingMethod method)
        {
            return method switch
            {
                DelousingMethod.Medicinal => new DelousingHarmProfile(method, 0.02, 0.005, 0.05, 0.10),
                DelousingMethod.Freshwater => new DelousingHarmProfile(method, 0.08, 0.015, 0.20, 0.35),
                DelousingMethod.Mechanical => new DelousingHarmProfile(method, 0.12, 0.020, 0.30, 0.50),
                DelousingMethod.Thermal => new DelousingHarmProfile(method, 0.35, 0.045, 0.70, 0.90),
                DelousingMethod.Hydrolicer => new DelousingHarmProfile(method, 0.40, 0.050, 0.85, 1.00),
                _ => new DelousingHarmProfile(method, 0.02, 0.005, 0.05, 0.10)
            };
        }

        /// <summary>
        /// Evaluate a treatment named by the game-wide method enum
        /// (LakskongenSim.Data.DelousingMethod) without taking a dependency
        /// on it: chemical/feed baths are low-harm, thermal/mechanical are
        /// injurious, precision/preventive methods count as low-harm.
        /// </summary>
        /// <summary>
        /// Map a game-wide method name (LakskongenSim.Data.DelousingMethod) to
        /// the closest harm method. Shared by EvaluateFor and callers that
        /// need the enum (e.g. injury-state accumulation).
        /// </summary>
        public static DelousingMethod MethodFor(string dataMethodName)
        {
            return dataMethodName switch
            {
                nameof(Data.DelousingMethod.HydrogenPeroxide) => DelousingMethod.Medicinal,
                nameof(Data.DelousingMethod.MedicatedFeed) => DelousingMethod.Medicinal,
                nameof(Data.DelousingMethod.FreshwaterBath) => DelousingMethod.Freshwater,
                nameof(Data.DelousingMethod.Thermolicer) => DelousingMethod.Thermal,
                nameof(Data.DelousingMethod.Hydrolicer) => DelousingMethod.Hydrolicer,
                _ => DelousingMethod.Medicinal, // CleanerFish, Laser, LiceSkirts: precise/gentle
            };
        }

        public static DelousingHarmResult EvaluateFor(string dataMethodName, double doseFraction = 1.0)
        {
            return Evaluate(MethodFor(dataMethodName), doseFraction);
        }

        /// <summary>
        /// Evaluate a single treatment event at the given dose fraction (0-1).
        /// Pure function; no state mutation.
        /// </summary>
        public static DelousingHarmResult Evaluate(DelousingMethod method, double doseFraction = 1.0)
        {
            double dose = Math.Clamp(doseFraction, 0.0, 1.0);
            var p = ProfileFor(method);

            double injury = p.InjuryRate * dose;
            double handling = p.HandlingMortalityDelta * dose;
            double gill = p.GillDamageContribution * dose;
            double welfare = p.WelfareCost * dose;

            double immediate = Math.Clamp(handling + injury * MortalityPerInjuryUnit, 0.0, 1.0);

            return new DelousingHarmResult(method, handling, injury, gill, welfare, immediate);
        }

        /// <summary>
        /// Amplification applied to a new injury when the previous treatment was recent.
        /// Returns 1.0 once <see cref="RecoveryWindowDays"/> have elapsed, up to 2.0 immediately after.
        /// </summary>
        public static double CompoundingMultiplier(int daysSinceLastTreatment)
        {
            if (daysSinceLastTreatment >= RecoveryWindowDays)
                return 1.0;

            int elapsed = Math.Max(0, daysSinceLastTreatment);
            double closeness = (RecoveryWindowDays - elapsed) / (double)RecoveryWindowDays;
            return 1.0 + closeness;
        }

        /// <summary>
        /// Pure compounding function: decay prior load, then add new injury amplified by recency.
        /// </summary>
        public static double CompoundInjury(double priorLoad, double newInjury, int daysSinceLastTreatment)
        {
            // exponential decay at DailyRecoveryFraction per day
            double decay = Math.Exp(-DailyRecoveryFraction * Math.Max(0, daysSinceLastTreatment));
            double amplified = Math.Max(0.0, newInjury) * CompoundingMultiplier(daysSinceLastTreatment);
            return Math.Clamp(Math.Max(0.0, priorLoad) * decay + amplified, 0.0, InjuryLoadCap);
        }

        /// <summary>
        /// Baseline mortality floor (0-1) implied by the current injury load.
        /// </summary>
        public static double ElevatedBaselineMortality(double injuryLoad)
        {
            return Math.Clamp(Math.Max(0.0, injuryLoad) * MortalityPerInjuryUnit, 0.0, MaxInjuryMortality);
        }

        /// <summary>
        /// Register a treatment event, folding its harm into the accumulated state.
        /// </summary>
        public static DelousingInjuryState ApplyTreatment(
            DelousingInjuryState state,
            DelousingMethod method,
            double doseFraction = 1.0)
        {
            var r = Evaluate(method, doseFraction);
            return new DelousingInjuryState
            {
                InjuryLoad = CompoundInjury(state.InjuryLoad, r.InjuryRate, state.DaysSinceLastTreatment),
                GillDamage = Math.Clamp(state.GillDamage + r.GillDamageContribution, 0.0, 1.0),
                CumulativeWelfareCost = state.CumulativeWelfareCost + r.WelfareCost,
                DaysSinceLastTreatment = 0,
                TreatmentsToDate = state.TreatmentsToDate + 1
            };
        }

        /// <summary>
        /// Advance the accumulated state one day: loads decay, the clock advances.
        /// </summary>
        public static DelousingInjuryState AdvanceDay(DelousingInjuryState state)
        {
            double decay = Math.Exp(-DailyRecoveryFraction);
            return new DelousingInjuryState
            {
                InjuryLoad = Math.Clamp(state.InjuryLoad * decay, 0.0, InjuryLoadCap),
                GillDamage = Math.Clamp(state.GillDamage * 0.98, 0.0, 1.0),
                CumulativeWelfareCost = state.CumulativeWelfareCost,
                DaysSinceLastTreatment = state.DaysSinceLastTreatment + 1,
                TreatmentsToDate = state.TreatmentsToDate
            };
        }

        /// <summary>
        /// Current excess daily mortality attributable to accumulated injury and gill damage.
        /// </summary>
        public static double CurrentMortalityRate(DelousingInjuryState state)
        {
            return ElevatedBaselineMortality(state.InjuryLoad) + 0.02 * Math.Clamp(state.GillDamage, 0.0, 1.0);
        }
    }
}