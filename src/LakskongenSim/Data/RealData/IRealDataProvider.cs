using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lakskongen.Data.RealData.Layers;
using Lakskongen.Data.RealData.Models;

namespace Lakskongen.Data.RealData
{
    /// <summary>
    /// Interface for accessing real-world environmental and aquaculture data.
    /// </summary>
    public interface IRealDataProvider
    {
        /// <summary>
        /// Gets a data layer by name.
        /// </summary>
        ITemporalDataLayer GetLayer(string layerName);

        /// <summary>
        /// Gets sea surface temperature at a location.
        /// </summary>
        Task<float> GetSstAsync(float latitude, float longitude, DateTime timestamp, float? depth = null);

        /// <summary>
        /// Gets chlorophyll-a concentration (for bloom detection).
        /// </summary>
        Task<float> GetChlorophyllAsync(float latitude, float longitude, DateTime timestamp, float? depth = null);

        /// <summary>
        /// Gets wave height.
        /// </summary>
        Task<float> GetWaveHeightAsync(float latitude, float longitude, DateTime timestamp);

        /// <summary>
        /// Gets 10m wind speed.
        /// </summary>
        Task<float> GetWindSpeedAsync(float latitude, float longitude, DateTime timestamp);

        /// <summary>
        /// Gets ocean current speed.
        /// </summary>
        Task<float> GetCurrentSpeedAsync(float latitude, float longitude, DateTime timestamp, float? depth = null);

        /// <summary>
        /// Gets aquaculture biomass at a site.
        /// </summary>
        Task<float> GetBiomassAsync(float latitude, float longitude, DateTime timestamp);

        /// <summary>
        /// Gets sea lice count per fish.
        /// </summary>
        Task<float> GetLiceAsync(float latitude, float longitude, DateTime timestamp);

        /// <summary>
        /// Gets all environmental data for a location as a snapshot.
        /// </summary>
        Task<EnvironmentalSnapshot> GetSnapshotAsync(float latitude, float longitude, DateTime timestamp);

        /// <summary>
        /// Gets data provenance information for a layer.
        /// </summary>
        DataProvenance GetProvenance(string layerName);
    }
}
