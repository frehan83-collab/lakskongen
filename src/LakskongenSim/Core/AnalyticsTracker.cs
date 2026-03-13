using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace LakskongenSim.Core
{
    /// <summary>
    /// Metric keys used throughout the analytics system.
    /// </summary>
    public static class AnalyticsMetric
    {
        public const string FishCount        = "fish_count";
        public const string TotalBiomass     = "total_biomass_kg";
        public const string AvgWeight        = "avg_weight_kg";
        public const string Temperature      = "temperature_c";
        public const string MarketPrice      = "market_price_nok";
        public const string Balance          = "balance_nok";
        public const string FCR              = "fcr";
        public const string LiceCount       = "lice_per_fish";
        public const string SustainabilityScore = "sustainability_score";
        public const string MortalityRate   = "mortality_rate";
    }

    /// <summary>
    /// One snapshot of key performance metrics captured at the end of each simulated day.
    /// Fully JSON-serializable for save/load.
    /// </summary>
    public class DailySnapshot
    {
        /// <summary>Simulation day this snapshot was captured on.</summary>
        public int Day { get; set; }

        /// <summary>Total live fish count across all populations.</summary>
        public int FishCount { get; set; }

        /// <summary>Total fish biomass in kg.</summary>
        public double TotalBiomassKg { get; set; }

        /// <summary>Average individual fish weight in kg.</summary>
        public double AvgWeightKg { get; set; }

        /// <summary>Sea temperature in °C.</summary>
        public double TemperatureC { get; set; }

        /// <summary>Market price in NOK/kg.</summary>
        public double MarketPriceNok { get; set; }

        /// <summary>Farm balance in NOK.</summary>
        public double BalanceNok { get; set; }

        /// <summary>Current Feed Conversion Ratio.</summary>
        public double FCR { get; set; }

        /// <summary>Average sea lice per fish.</summary>
        public double LicePerFish { get; set; }

        /// <summary>Sustainability score (0–100).</summary>
        public int SustainabilityScore { get; set; }

        /// <summary>Daily mortality rate as a fraction (0.0–1.0).</summary>
        public double MortalityRate { get; set; }

        /// <summary>
        /// Return the value of a named metric. Returns NaN if unknown.
        /// </summary>
        public double GetMetric(string metric) => metric switch
        {
            AnalyticsMetric.FishCount           => FishCount,
            AnalyticsMetric.TotalBiomass        => TotalBiomassKg,
            AnalyticsMetric.AvgWeight           => AvgWeightKg,
            AnalyticsMetric.Temperature         => TemperatureC,
            AnalyticsMetric.MarketPrice         => MarketPriceNok,
            AnalyticsMetric.Balance             => BalanceNok,
            AnalyticsMetric.FCR                 => FCR,
            AnalyticsMetric.LiceCount           => LicePerFish,
            AnalyticsMetric.SustainabilityScore => SustainabilityScore,
            AnalyticsMetric.MortalityRate       => MortalityRate,
            _                                   => double.NaN
        };
    }

    /// <summary>
    /// Direction of a metric trend.
    /// </summary>
    public enum TrendDirection
    {
        Up,
        Down,
        Stable
    }

    /// <summary>
    /// Captures daily metric snapshots and provides time-series analytics:
    /// moving averages, trend detection, and all-time highs/lows.
    /// Attached to GameState and fed by SimulationEngine each day.
    /// </summary>
    public class AnalyticsTracker
    {
        // ── Tuning ────────────────────────────────────────────────────────────

        /// <summary>Minimum slope magnitude (as fraction of mean) to count as a trend.</summary>
        private const double TrendThreshold = 0.005; // 0.5% change per day

        // ── Storage ───────────────────────────────────────────────────────────

        /// <summary>The snapshot list stored in GameState (injected, not owned here).</summary>
        private readonly List<DailySnapshot> _snapshots;

        // ── Constructor ───────────────────────────────────────────────────────

        /// <param name="snapshotList">Reference to GameState.DailySnapshots.</param>
        public AnalyticsTracker(List<DailySnapshot> snapshotList)
        {
            _snapshots = snapshotList ?? throw new ArgumentNullException(nameof(snapshotList));
        }

        // ── Snapshot capture ──────────────────────────────────────────────────

        /// <summary>
        /// Record a snapshot from the current simulation state.
        /// Called at the end of each AdvanceOneDay().
        /// </summary>
        public void RecordSnapshot(SimulationEngine engine)
        {
            if (engine == null) return;

            var state  = engine.State;
            var allCohorts = state.Populations.SelectMany(p => p.Cohorts).ToList();

            int    fishCount   = allCohorts.Sum(c => c.Count);
            double biomass     = allCohorts.Sum(c => c.Count * c.MeanWeightKg);
            double avgWeight   = fishCount > 0 ? biomass / fishCount : 0.0;

            // Mortality rate: compare to previous day's fish count
            double mortalityRate = 0.0;
            if (_snapshots.Count > 0)
            {
                int prevCount = _snapshots[^1].FishCount;
                if (prevCount > 0 && fishCount < prevCount)
                    mortalityRate = (double)(prevCount - fishCount) / prevCount;
            }

            var snap = new DailySnapshot
            {
                Day                = state.Day,
                FishCount          = fishCount,
                TotalBiomassKg     = biomass,
                AvgWeightKg        = avgWeight,
                TemperatureC       = engine.Weather?.CurrentTemperatureC ?? 0.0,
                MarketPriceNok     = (double)(engine.MarketSimulation?.PricePerKg ?? 70m),
                BalanceNok         = (double)(engine.FinanceManager?.Balance ?? 0m),
                FCR                = engine.Feed?.CurrentFCR ?? 1.15,
                LicePerFish        = engine.SeaLice?.AverageLicePerFish ?? 0.0,
                SustainabilityScore = engine.SustainabilityManager?.CurrentScore ?? 0,
                MortalityRate      = mortalityRate
            };

            _snapshots.Add(snap);
        }

        // ── Query API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the values of <paramref name="metric"/> for the last <paramref name="lastNDays"/> days,
        /// oldest-first. Returns all available data if fewer snapshots exist.
        /// </summary>
        public List<double> GetTimeSeries(string metric, int lastNDays)
        {
            var window = GetWindow(lastNDays);
            return window.Select(s => s.GetMetric(metric)).ToList();
        }

        /// <summary>
        /// Returns a simple moving average of <paramref name="metric"/> over the last
        /// <paramref name="lastNDays"/> days, using a rolling window of <paramref name="windowDays"/>.
        /// The output list has the same length as the input window (NaN for early positions).
        /// </summary>
        public List<double> GetMovingAverage(string metric, int lastNDays, int windowDays)
        {
            if (windowDays < 1) windowDays = 1;
            var series = GetTimeSeries(metric, lastNDays);
            var result = new List<double>(series.Count);

            for (int i = 0; i < series.Count; i++)
            {
                int start = Math.Max(0, i - windowDays + 1);
                var slice = series.GetRange(start, i - start + 1);
                result.Add(slice.Average());
            }
            return result;
        }

        /// <summary>
        /// Returns the trend of <paramref name="metric"/> over the last <paramref name="lastNDays"/> days
        /// using linear regression slope. Up / Down / Stable.
        /// </summary>
        public TrendDirection GetTrend(string metric, int lastNDays = 14)
        {
            var series = GetTimeSeries(metric, lastNDays);
            if (series.Count < 2) return TrendDirection.Stable;

            double slope = LinearRegressionSlope(series);
            double mean  = series.Average();
            if (mean == 0.0) return TrendDirection.Stable;

            double relativeSope = slope / Math.Abs(mean);
            if (relativeSope > TrendThreshold)  return TrendDirection.Up;
            if (relativeSope < -TrendThreshold) return TrendDirection.Down;
            return TrendDirection.Stable;
        }

        /// <summary>Returns the all-time highest recorded value for <paramref name="metric"/>.</summary>
        public double GetAllTimeHigh(string metric)
        {
            if (_snapshots.Count == 0) return double.NaN;
            return _snapshots.Max(s => s.GetMetric(metric));
        }

        /// <summary>Returns the all-time lowest recorded value for <paramref name="metric"/>.</summary>
        public double GetAllTimeLow(string metric)
        {
            if (_snapshots.Count == 0) return double.NaN;
            return _snapshots.Min(s => s.GetMetric(metric));
        }

        /// <summary>Number of snapshots recorded so far.</summary>
        public int SnapshotCount => _snapshots.Count;

        // ── Helpers ───────────────────────────────────────────────────────────

        private List<DailySnapshot> GetWindow(int days)
        {
            if (days <= 0 || _snapshots.Count == 0) return new List<DailySnapshot>();
            int skip = Math.Max(0, _snapshots.Count - days);
            return _snapshots.GetRange(skip, _snapshots.Count - skip);
        }

        /// <summary>
        /// Ordinary least-squares slope for a 0-indexed series (x = index, y = value).
        /// </summary>
        private static double LinearRegressionSlope(List<double> values)
        {
            int n = values.Count;
            if (n < 2) return 0.0;

            double sumX  = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            for (int i = 0; i < n; i++)
            {
                sumX  += i;
                sumY  += values[i];
                sumXY += i * values[i];
                sumX2 += i * i;
            }

            double denom = n * sumX2 - sumX * sumX;
            if (Math.Abs(denom) < 1e-10) return 0.0;
            return (n * sumXY - sumX * sumY) / denom;
        }
    }
}
