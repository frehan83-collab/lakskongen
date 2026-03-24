using System.Collections.Generic;

namespace Lakskongen.Data.RealData.Interpolation
{
    /// <summary>
    /// Interface for spatial interpolation of scattered data points.
    /// </summary>
    public interface ISpatialInterpolator
    {
        /// <summary>
        /// Name of the interpolation method.
        /// </summary>
        string MethodName { get; }

        /// <summary>
        /// Interpolate a value at a target location from known data points.
        /// </summary>
        /// <param name="target">Target location for interpolation</param>
        /// <param name="dataPoints">Known data points with values</param>
        /// <returns>Interpolated value</returns>
        float Interpolate(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints);

        /// <summary>
        /// Interpolate with uncertainty estimate.
        /// </summary>
        InterpolationResult InterpolateWithUncertainty(GeoCoordinate target, IEnumerable<InterpolatablePoint> dataPoints);
    }

    /// <summary>
    /// A point that can be used for interpolation.
    /// </summary>
    public class InterpolatablePoint
    {
        public GeoCoordinate Location { get; set; }
        public float Value { get; set; }
        public float? Weight { get; set; }
        public float? Uncertainty { get; set; }

        public InterpolatablePoint(GeoCoordinate location, float value, float? uncertainty = null)
        {
            Location = location;
            Value = value;
            Uncertainty = uncertainty;
        }
    }

    /// <summary>
    /// Result of interpolation with metadata.
    /// </summary>
    public class InterpolationResult
    {
        /// <summary>
        /// The interpolated value.
        /// </summary>
        public float Value { get; set; }

        /// <summary>
        /// Estimated uncertainty of the interpolation.
        /// </summary>
        public float Uncertainty { get; set; }

        /// <summary>
        /// Number of points used in interpolation.
        /// </summary>
        public int PointsUsed { get; set; }

        /// <summary>
        /// Average distance to points used (in km).
        /// </summary>
        public float AverageDistanceKm { get; set; }

        /// <summary>
        /// Quality indicator: 0=high confidence, 1=medium, 2=low, 3=extrapolated
        /// </summary>
        public int QualityLevel { get; set; }

        /// <summary>
        /// Interpolation method used.
        /// </summary>
        public string MethodUsed { get; set; } = "";

        public bool IsValid => !float.IsNaN(Value) && QualityLevel <= 2;
    }
}
