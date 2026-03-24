using System;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Represents a geographic coordinate with utilities for distance calculation.
    /// </summary>
    public readonly struct GeoCoordinate : IEquatable<GeoCoordinate>
    {
        public float Latitude { get; }
        public float Longitude { get; }
        public float? Depth { get; }

        public GeoCoordinate(float latitude, float longitude, float? depth = null)
        {
            Latitude = Math.Clamp(latitude, -90f, 90f);
            Longitude = Math.Clamp(longitude, -180f, 180f);
            Depth = depth;
        }

        /// <summary>
        /// Calculate great-circle distance to another point using Haversine formula (in km).
        /// </summary>
        public double DistanceTo(GeoCoordinate other)
        {
            const double EarthRadiusKm = 6371.0;
            const double DegToRad = Math.PI / 180.0;

            double lat1 = Latitude * DegToRad;
            double lat2 = other.Latitude * DegToRad;
            double dLat = (other.Latitude - Latitude) * DegToRad;
            double dLon = (other.Longitude - Longitude) * DegToRad;

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1) * Math.Cos(lat2) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return EarthRadiusKm * c;
        }

        /// <summary>
        /// Calculate distance with depth consideration (3D distance in km).
        /// </summary>
        public double DistanceTo3D(GeoCoordinate other)
        {
            double horizontalDistance = DistanceTo(other);
            
            if (!Depth.HasValue || !other.Depth.HasValue)
                return horizontalDistance;

            // Depth is in meters, convert to km
            double depthDiffKm = Math.Abs(Depth.Value - other.Depth.Value) / 1000.0;
            
            return Math.Sqrt(horizontalDistance * horizontalDistance + depthDiffKm * depthDiffKm);
        }

        /// <summary>
        /// Check if this coordinate is within Norwegian territorial waters (approximate).
        /// </summary>
        public bool IsInNorwegianWaters()
        {
            // Approximate bounding box for Norway's EEZ and coastal waters
            return Latitude >= 57.0f && Latitude <= 80.0f &&
                   Longitude >= 4.0f && Longitude <= 35.0f;
        }

        /// <summary>
        /// Get the production zone (PO) this coordinate falls into.
        /// </summary>
        public string GetProductionZone()
        {
            // Simplified zone detection - would use actual zone polygons in production
            if (Latitude >= 70.0f) return "PO13"; // Finnmark
            if (Latitude >= 68.5f) return "PO12"; // Troms
            if (Latitude >= 66.5f) return "PO11"; // Nordland Nord
            if (Latitude >= 65.0f) return "PO10"; // Nordland Midt
            if (Latitude >= 64.0f) return "PO9";  // Nordland Sør
            if (Latitude >= 63.0f) return "PO8";  // Trøndelag Nord
            if (Latitude >= 62.0f) return "PO7";  // Trøndelag Sør
            if (Latitude >= 61.0f) return "PO6";  // Møre og Romsdal
            if (Latitude >= 60.5f) return "PO5";  // Sogn og Fjordane
            if (Latitude >= 59.5f) return "PO4";  // Hordaland
            if (Latitude >= 58.5f) return "PO3";  // Rogaland
            if (Latitude >= 58.0f) return "PO2";  // Agder
            return "PO1"; // Østfold
        }

        public bool Equals(GeoCoordinate other)
        {
            return Math.Abs(Latitude - other.Latitude) < 0.0001f &&
                   Math.Abs(Longitude - other.Longitude) < 0.0001f;
        }

        public override bool Equals(object? obj) => obj is GeoCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Latitude, Longitude);
        public override string ToString() => $"({Latitude:F4}, {Longitude:F4})";
        public static bool operator ==(GeoCoordinate a, GeoCoordinate b) => a.Equals(b);
        public static bool operator !=(GeoCoordinate a, GeoCoordinate b) => !a.Equals(b);
    }
}
