using System;

namespace Lakskongen.Data.RealData.Models
{
    /// <summary>
    /// Provenance metadata for a data layer.
    /// </summary>
    public class DataProvenance
    {
        public string LayerName { get; set; } = "";
        public string VariableName { get; set; } = "";
        public string Unit { get; set; } = "";
        public bool Available { get; set; }
        public string License { get; set; } = "";
        public string SourceDescription { get; set; } = "";
        public DateTime RetrievalDate { get; set; }
        public string TemporalCoverage { get; set; } = "";
        public float SpatialResolutionKm { get; set; }
    }
}
