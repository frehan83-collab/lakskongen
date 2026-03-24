using System;
using System.Collections.Generic;
using System.Linq;

namespace Lakskongen.Data.RealData.Interpolation
{
    /// <summary>
    /// Temporal interpolator for time-series data.
    /// </summary>
    public class TemporalInterpolator
    {
        /// <summary>
        /// Interpolate a value at a target time from known time points.
        /// </summary>
        public float Interpolate(DateTime target, IEnumerable<TimeDataPoint> dataPoints)
        {
            var points = dataPoints.OrderBy(p => p.Timestamp).ToList();
            
            if (points.Count == 0) return float.NaN;
            if (points.Count == 1) return points[0].Value;

            // Check for exact match
            var exact = points.FirstOrDefault(p => p.Timestamp == target);
            if (exact != null) return exact.Value;

            // Find surrounding points
            var before = points.LastOrDefault(p => p.Timestamp < target);
            var after = points.FirstOrDefault(p => p.Timestamp > target);

            if (before == null) return after?.Value ?? points[0].Value;
            if (after == null) return before.Value;

            // Linear interpolation
            double totalTicks = (after.Timestamp - before.Timestamp).Ticks;
            double targetTicks = (target - before.Timestamp).Ticks;
            double t = totalTicks > 0 ? targetTicks / totalTicks : 0;

            return (float)(before.Value + t * (after.Value - before.Value));
        }

        /// <summary>
        /// Interpolate with seasonal smoothing.
        /// </summary>
        public float InterpolateWithSeasonality(DateTime target, IEnumerable<TimeDataPoint> dataPoints, int dayOfYearPeriod = 365)
        {
            var points = dataPoints.ToList();
            
            // Build seasonal pattern
            var seasonalMeans = new Dictionary<int, List<float>>();
            foreach (var point in points)
            {
                int doy = point.Timestamp.DayOfYear;
                if (!seasonalMeans.ContainsKey(doy))
                    seasonalMeans[doy] = new List<float>();
                seasonalMeans[doy].Add(point.Value);
            }

            // Simple linear interpolation for now
            // TODO: Implement proper seasonal decomposition
            return Interpolate(target, points);
        }

        /// <summary>
        /// Apply moving average smoothing.
        /// </summary>
        public List<TimeDataPoint> SmoothMovingAverage(IEnumerable<TimeDataPoint> dataPoints, int windowDays)
        {
            var points = dataPoints.OrderBy(p => p.Timestamp).ToList();
            var result = new List<TimeDataPoint>();

            for (int i = 0; i < points.Count; i++)
            {
                var window = points
                    .Where(p => Math.Abs((p.Timestamp - points[i].Timestamp).TotalDays) <= windowDays / 2.0)
                    .ToList();

                if (window.Count > 0)
                {
                    result.Add(new TimeDataPoint
                    {
                        Timestamp = points[i].Timestamp,
                        Value = window.Average(p => p.Value),
                        Uncertainty = (float?)window.Select(p => p.Uncertainty ?? 0).Average()
                    });
                }
            }

            return result;
        }
    }

    /// <summary>
    /// A data point in time.
    /// </summary>
    public class TimeDataPoint
    {
        public DateTime Timestamp { get; set; }
        public float Value { get; set; }
        public float? Uncertainty { get; set; }
        public int? QualityFlag { get; set; }
    }
}
