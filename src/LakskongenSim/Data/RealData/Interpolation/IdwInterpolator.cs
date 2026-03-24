using System;
using System.Collections.Generic;
using System.Linq;

namespace Lakskongen.Data.RealData.Interpolation
{
    /// <summary>
    /// Inverse Distance Weighting (IDW) interpolator.
    /// Good balance of quality and performance.
    /// </summary>
    public class IdwInterpolator : ISpatialInterpolator
    {
        private readonly int _maxPoints;
        private readonly double _power;
        private readonly double _maxDistanceKm;

        /// <summary>
        /// Create IDW interpolator.
        /// </summary>
        /// <param name="maxPoints">Maximum neighbors to use (default 12)</param>
        /// <param name="power">Distance weighting power (default 2.0)</param>
        /// <param name="maxDistanceKm">Maximum distance to consider (default 100km)</param>
        public IdwInterpolator(int maxPoints = 12, double power = 2.0, double maxDistanceKm = 100.0)
        {
            _maxPoints = maxPoints;
            _power = power;
            _maxDistanceKm = maxDistanceKm;
        }

        public string MethodName => $"IDW(p={_power:F1})";

        public float Interpolate(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints)
        {
            var result = InterpolateWithUncertainty(target, dataPoints);
            return result.Value;
        }

        public InterpolationResult InterpolateWithUncertainty(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints)
        {
            var allPoints = dataPoints.ToList();
            
            if (allPoints.Count == 0)
            {
                return new InterpolationResult 
                { 
                    Value = float.NaN, 
                    QualityLevel = 3,
                    MethodUsed = MethodName
                };
            }

            // Get points within max distance, sorted by distance
            var nearbyPoints = allPoints
                .Select(p => new 
                { 
                    Point = p, 
                    Distance = target.DistanceTo(p.Location)
                })
                .Where(x => x.Distance <= _maxDistanceKm)
                .OrderBy(x => x.Distance)
                .Take(_maxPoints)
                .ToList();

            if (nearbyPoints.Count == 0)
            {
                // Fall back to nearest neighbor if no points within range
                var nearest = allPoints
                    .Select(p => new { Point = p, Distance = target.DistanceTo(p.Location) })
                    .OrderBy(x => x.Distance)
                    .First();

                return new InterpolationResult
                {
                    Value = nearest.Point.Value,
                    Uncertainty = (float)(nearest.Distance * 0.1),
                    PointsUsed = 1,
                    AverageDistanceKm = (float)nearest.Distance,
                    QualityLevel = 3, // Extrapolated
                    MethodUsed = MethodName + "(fallback)"
                };
            }

            // Handle exact match
            var exactMatch = nearbyPoints.FirstOrDefault(x => x.Distance < 0.001);
            if (exactMatch != null)
            {
                return new InterpolationResult
                {
                    Value = exactMatch.Point.Value,
                    Uncertainty = exactMatch.Point.Uncertainty ?? 0f,
                    PointsUsed = 1,
                    AverageDistanceKm = 0f,
                    QualityLevel = 0,
                    MethodUsed = MethodName
                };
            }

            // IDW calculation
            double weightedSum = 0;
            double weightSum = 0;
            double maxWeight = 0;

            foreach (var item in nearbyPoints)
            {
                double weight = 1.0 / Math.Pow(item.Distance, _power);
                weightedSum += item.Point.Value * weight;
                weightSum += weight;
                maxWeight = Math.Max(maxWeight, weight);
            }

            float interpolatedValue = (float)(weightedSum / weightSum);
            float avgDistance = (float)nearbyPoints.Average(x => x.Distance);

            // Estimate uncertainty based on variance and distance
            float variance = 0;
            if (nearbyPoints.Count > 1)
            {
                var values = nearbyPoints.Select(x => x.Point.Value).ToList();
                float mean = values.Average();
                variance = values.Select(v => (v - mean) * (v - mean)).Average();
            }

            // Quality based on number of points and average distance
            int qualityLevel;
            if (nearbyPoints.Count >= 4 && avgDistance < 20) qualityLevel = 0;
            else if (nearbyPoints.Count >= 2 && avgDistance < 50) qualityLevel = 1;
            else qualityLevel = 2;

            return new InterpolationResult
            {
                Value = interpolatedValue,
                Uncertainty = (float)(Math.Sqrt(variance) + avgDistance * 0.05),
                PointsUsed = nearbyPoints.Count,
                AverageDistanceKm = avgDistance,
                QualityLevel = qualityLevel,
                MethodUsed = MethodName
            };
        }
    }
}
