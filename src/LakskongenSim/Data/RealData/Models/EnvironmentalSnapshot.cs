using System;
using System.Collections.Generic;

namespace Lakskongen.Data.RealData.Models
{
    /// <summary>
    /// Complete environmental snapshot for a location and time.
    /// </summary>
    public class EnvironmentalSnapshot
    {
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public DateTime Timestamp { get; set; }

        public float SeaSurfaceTemperature { get; set; }
        public float ChlorophyllA { get; set; }
        public float WaveHeight { get; set; }
        public float WindSpeed { get; set; }
        public float CurrentSpeed { get; set; }
        public float Biomass { get; set; }
        public float LicePerFish { get; set; }

        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }
}
