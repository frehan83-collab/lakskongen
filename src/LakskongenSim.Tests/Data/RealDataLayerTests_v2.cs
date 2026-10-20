using System;
using System.Linq;
using Lakskongen.Data.RealData;
using Lakskongen.Data.RealData.Cache;
using Lakskongen.Data.RealData.Layers;
using Xunit;

namespace LakskongenSim.Tests.DataLayers;

/// <summary>
/// Tests for the new real data layers (S4 data layer v2).
/// Verifies that offline snapshots shift realism bands as expected.
/// </summary>
public class RealDataLayerTests_v2
{
    private readonly SstLayer _sst;
    private readonly ChlorophyllLayer _chl;
    private readonly WavesLayer _waves;
    private readonly WindLayer _wind;
    private readonly CurrentLayer _current;
    private readonly BiomassLayer _biomass;
    private readonly SeaLiceLayer _lice;

    public RealDataLayerTests_v2()
    {
        var cache = new DataCache("test_cache");
        var loaders = new System.Collections.Generic.List<IDataLoader>();
        
        _sst = new SstLayer(cache, loaders);
        _chl = new ChlorophyllLayer(cache, loaders);
        _waves = new WavesLayer(cache, loaders);
        _wind = new WindLayer(cache, loaders);
        _current = new CurrentLayer(cache, loaders);
        _biomass = new BiomassLayer(cache, loaders);
        _lice = new SeaLiceLayer(cache, loaders);
    }

    [Fact]
    public void Sst_SeasonalCycle_MatchesNorwegianClimatology()
    {
        // Winter cold, summer warm
        var winter = _sst.GetValue(63.5f, 10f, new DateTime(2024, 1, 15));
        var summer = _sst.GetValue(63.5f, 10f, new DateTime(2024, 7, 15));
        
        Assert.InRange(winter, 3f, 8f);
        Assert.InRange(summer, 10f, 16f);
        Assert.True(summer > winter + 2f, "Summer should be at least 2°C warmer than winter");
    }

    [Fact]
    public void Sst_LatitudeGradient_ColderInNorth()
    {
        var south = _sst.GetValue(58f, 10f, new DateTime(2024, 7, 15));
        var north = _sst.GetValue(70f, 10f, new DateTime(2024, 7, 15));
        
        Assert.True(north < south - 1f, "North should be at least 1°C colder than south in summer");
    }

    [Fact]
    public void Chlorophyll_SpringBloom_PeakInMay()
    {
        var spring = _chl.GetValue(63.5f, 10f, new DateTime(2024, 5, 15));
        var winter = _chl.GetValue(63.5f, 10f, new DateTime(2024, 1, 15));
        
        Assert.True(spring > winter * 3, "Spring bloom should be at least 3x winter chlorophyll");
    }

    [Fact]
    public void Waves_WinterStormSeason_HigherThanSummer()
    {
        var winter = _waves.GetValue(63.5f, 5f, new DateTime(2024, 1, 15));
        var summer = _waves.GetValue(63.5f, 5f, new DateTime(2024, 7, 15));
        
        Assert.True(winter > summer * 2, "Winter waves should be at least 2x summer");
    }

    [Fact]
    public void Wind_WinterStronger_ThanSummer()
    {
        var winter = _wind.GetValue(63.5f, 10f, new DateTime(2024, 1, 15));
        var summer = _wind.GetValue(63.5f, 10f, new DateTime(2024, 7, 15));
        
        Assert.True(winter > summer * 1.5, "Winter winds should be at least 1.5x summer");
    }

    [Fact]
    public void Current_SummerWeaker_ThanWinter()
    {
        var winter = _current.GetValue(63.5f, 10f, new DateTime(2024, 1, 15));
        var summer = _current.GetValue(63.5f, 10f, new DateTime(2024, 7, 15));
        
        Assert.True(winter > summer * 1.2, "Winter currents should be stronger");
    }

    [Fact]
    public void Biomass_AutumnPeak_HigherThanSpring()
    {
        var autumn = _biomass.GetValue(65f, 12f, new DateTime(2024, 9, 15));
        var spring = _biomass.GetValue(65f, 12f, new DateTime(2024, 4, 15));
        
        Assert.True(autumn > spring * 1.3, "Autumn biomass should be at least 30% higher than spring");
    }

    [Fact]
    public void Lice_SpringAndAutumnPeaks_HigherThanSummer()
    {
        var spring = _lice.GetValue(65f, 12f, new DateTime(2024, 5, 15));
        var autumn = _lice.GetValue(65f, 12f, new DateTime(2024, 10, 15));
        var summer = _lice.GetValue(65f, 12f, new DateTime(2024, 7, 15));
        
        Assert.True(spring > summer * 1.5, "Spring lice should be higher than summer");
        Assert.True(autumn > summer * 1.2, "Autumn lice should be higher than summer");
    }

    [Fact]
    public void Lice_NorthernRegions_HigherPressure()
    {
        var north = _lice.GetValue(69f, 20f, new DateTime(2024, 5, 15));
        var south = _lice.GetValue(58f, 10f, new DateTime(2024, 5, 15));
        
        Assert.True(north >= south * 0.9, "Northern regions should have comparable or higher lice pressure");
    }

    [Fact]
    public void Provenance_Metadata_Complete_ForAllLayers()
    {
        var config = new RealDataConfig();
        var orchestrator = new RealDataOrchestrator(config);
        
        foreach (var layerName in new[] { "sst", "chl", "waves", "wind", "currents", "biomass", "lice" })
        {
            var prov = orchestrator.GetProvenance(layerName);
            Assert.True(prov.Available, $"{layerName} should be available");
            Assert.False(string.IsNullOrEmpty(prov.License), $"{layerName} should have license info");
            Assert.False(string.IsNullOrEmpty(prov.SourceDescription), $"{layerName} should have source description");
            Assert.True(prov.TemporalCoverage.Length > 0, $"{layerName} should have temporal coverage");
            Assert.True(prov.SpatialResolutionKm > 0, $"{layerName} should have spatial resolution");
        }
    }

    [Fact]
    public void Provenance_AndAvailability_ReportSnapshotDate_NotWallClock()
    {
        var snapshot = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var orchestrator = new RealDataOrchestrator(new RealDataConfig { SnapshotDate = snapshot });

        Assert.Equal(snapshot, orchestrator.GetProvenance("sst").RetrievalDate);
        var availability = orchestrator.GetLayer("sst")
            .GetAvailabilityAsync(new BoundingBox(60f, 61f, 5f, 6f))
            .GetAwaiter().GetResult();
        Assert.Equal(snapshot.AddDays(-1), availability.LatestData);
    }
}
