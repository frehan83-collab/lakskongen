using System;

namespace LakskongenSim.Health
{
    /// <summary>
    /// Environmental and treatment drivers used by the gill-health early-warning score.
    /// </summary>
    public readonly struct GillRiskInput
    {
        /// <summary>Water temperature (°C).</summary>
        public double TemperatureC { get; init; }
        /// <summary>Dissolved oxygen (mg/L).</summary>
        public double OxygenMgL { get; init; }
        /// <summary>Turbidity (NTU) — suspended solids/plankton load.</summary>
        public double TurbidityNTU { get; init; }
        /// <summary>Current speed at cage depth (m/s).</summary>
        public double CurrentSpeedMs { get; init; }
        /// <summary>Recent accumulated delousing injury load (0-1+).</summary>
        public double RecentTreatmentLoad { get; init; }
    }

    /// <summary>
    /// Gill-health risk assessment result.
    /// </summary>
    public readonly struct GillRiskResult
    {
        /// <summary>Composite risk in [0,1].</summary>
        public double Risk01 { get; }
        /// <summary>Dominant driver description.</summary>
        public string Reason { get; }

        /// <summary>Create a result.</summary>
        public GillRiskResult(double risk01, string reason)
        {
            Risk01 = Math.Clamp(risk01, 0.0, 1.0);
            Reason = reason;
        }

        /// <summary>True when risk meets or exceeds the warning threshold.</summary>
        public bool IsWarning => Risk01 >= GillHealthWarning.WarningThreshold;

        /// <summary>True when risk meets or exceeds the critical threshold.</summary>
        public bool IsCritical => Risk01 >= GillHealthWarning.CriticalThreshold;
    }

    /// <summary>
    /// Early-warning score for gill health from temperature, oxygen, turbidity,
    /// currents and recent delousing treatment load. Deterministic, 0-1 output
    /// with a human-readable dominant reason.
    /// </summary>
    public static class GillHealthWarning
    {
        /// <summary>Risk at/above which a warning is issued.</summary>
        public const double WarningThreshold = 0.50;

        /// <summary>Risk at/above which the situation is critical.</summary>
        public const double CriticalThreshold = 0.75;

        // Sub-score weights (sum to 1.0).
        private const double WeightOxygen = 0.30;
        private const double WeightThermal = 0.25;
        private const double WeightTreatment = 0.20;
        private const double WeightTurbidity = 0.15;
        private const double WeightCurrent = 0.10;

        private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

        private static double ThermalScore(double temperatureC) => Clamp01((temperatureC - 14.0) / 8.0);
        private static double OxygenScore(double oxygenMgL) => Clamp01((7.0 - oxygenMgL) / 4.0);
        private static double TurbidityScore(double turbidityNtu) => Clamp01((turbidityNtu - 10.0) / 30.0);
        private static double CurrentScore(double currentSpeedMs) => Clamp01((0.10 - currentSpeedMs) / 0.10);
        private static double TreatmentScore(double recentLoad) => Clamp01(recentLoad);

        /// <summary>
        /// Evaluate the gill-health risk for the supplied drivers.
        /// </summary>
        public static GillRiskResult Evaluate(GillRiskInput input)
        {
            double thermal = ThermalScore(input.TemperatureC);
            double oxygen = OxygenScore(input.OxygenMgL);
            double turbidity = TurbidityScore(input.TurbidityNTU);
            double current = CurrentScore(input.CurrentSpeedMs);
            double treatment = TreatmentScore(input.RecentTreatmentLoad);

            double risk = Clamp01(
                WeightOxygen * oxygen +
                WeightThermal * thermal +
                WeightTreatment * treatment +
                WeightTurbidity * turbidity +
                WeightCurrent * current);

            string reason = "No significant gill-health risk";
            double best = 0.2; // reason only reported when a driver is materially elevated
            if (WeightOxygen * oxygen > best) { best = WeightOxygen * oxygen; reason = "Low dissolved oxygen"; }
            if (WeightThermal * thermal > best) { best = WeightThermal * thermal; reason = "Temperature stress"; }
            if (WeightTreatment * treatment > best) { best = WeightTreatment * treatment; reason = "Recent delousing treatment load"; }
            if (WeightTurbidity * turbidity > best) { best = WeightTurbidity * turbidity; reason = "High turbidity / suspended solids"; }
            if (WeightCurrent * current > best) { reason = "Stagnant currents limiting gill clearance"; }

            return new GillRiskResult(risk, reason);
        }

        /// <summary>
        /// Convenience overload for scalar drivers.
        /// </summary>
        public static GillRiskResult Evaluate(
            double temperatureC,
            double oxygenMgL,
            double turbidityNtu,
            double currentSpeedMs,
            double recentTreatmentLoad)
        {
            return Evaluate(new GillRiskInput
            {
                TemperatureC = temperatureC,
                OxygenMgL = oxygenMgL,
                TurbidityNTU = turbidityNtu,
                CurrentSpeedMs = currentSpeedMs,
                RecentTreatmentLoad = recentTreatmentLoad
            });
        }
    }
}