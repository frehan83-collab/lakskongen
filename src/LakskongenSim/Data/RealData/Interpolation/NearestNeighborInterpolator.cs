using System;
using System.Collections.Generic;
using System.Linq;

namespace Lakskongen.Data.RealData.Interpolation
{
    /// <summary>
    /// Simple nearest neighbor interpolator - fast but low quality.
    /// </summary>
    public class NearestNeighborInterpolator : ISpatialInterpolator
    {
        public string MethodName => "NearestNeighbor";

        public float Interpolate(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints)
        {
            var result = InterpolateWithUncertainty(target, dataPoints);
            return result.Value;
        }

        public InterpolationResult InterpolateWithUncertainty(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints)
        {
            var points = dataPoints.ToList();
            
            if (points.Count == 0)
            {
                return new InterpolationResult 
                { 
                    Value = float.NaN, 
                    QualityLevel = 3,
                    MethodUsed = MethodName
                };
            }

            // Find nearest point
            var nearest = points
                .Select(p => new { Point = p, Distance = target.DistanceTo(p.Location) })
                .OrderBy(x => x.Distance)
                .First();

            // Quality based on distance
            int quality = nearest.Distance < 10 ? 0 : nearest.Distance < 50 ? 1 : 2;

            return new InterpolationResult
            {
                Value = nearest.Point.Value,
                Uncertainty = (float)nearest.Distance * 0.1f, // Rough uncertainty estimate
                PointsUsed = 1,
                AverageDistanceKm = (float)nearest.Distance,
                QualityLevel = quality,
                MethodUsed = MethodName
            };
        }
    }
}
