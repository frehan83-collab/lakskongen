#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using LakskongenSim.Environment;
using LakskongenSim.Environment.Geo;
using Xunit;

namespace LakskongenSim.Tests.Geo;

/// <summary>
/// The sim's sea-temperature table against the geodata (STAGE23_PLAN track A,
/// A1, stronger data tests): ocean_climatology.json's sim_sea_temp_c and the
/// production areas' sea_temp_monthly_c equal ProductionAreas' table exactly,
/// the Copernicus SST lies within 2.0 °C of it every month, and every open-pen
/// candidate's temperature component is the published trapezoid over that table.
/// </summary>
public class GeoClimateParityTests
{
    private static JsonElement Monthly(int po) =>
        GeoTestData.Climatology.GetProperty("areas").GetProperty(po.ToString()).GetProperty("monthly");

    [Fact]
    public void SimSeaTemp_InTheClimatology_EqualsTheSimTableExactly()
    {
        for (int po = 1; po <= ProductionAreas.Count; po++)
        {
            var sim = Monthly(po).GetProperty("sim_sea_temp_c").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            Assert.Equal(ProductionAreas.Get(po).SeaTemperatureMonthlyC, sim);
        }
    }

    [Fact]
    public void TheMapsAreaTemperatures_EqualTheSimTableExactly()
    {
        using var doc = JsonDocument.Parse(GeoResources.ReadText(GeoResources.ProductionAreasGeoJson));
        foreach (var f in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var p = f.GetProperty("properties");
            int po = p.GetProperty("id").GetInt32();
            var map = p.GetProperty("sea_temp_monthly_c").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            Assert.Equal(ProductionAreas.Get(po).SeaTemperatureMonthlyC, map);
        }
    }

    [Fact]
    public void CopernicusSst_LiesWithinTwoDegreesOfTheSimTable_EveryMonth()
    {
        double worst = 0;
        for (int po = 1; po <= ProductionAreas.Count; po++)
        {
            var sst = Monthly(po).GetProperty("sst_c").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            var sim = ProductionAreas.Get(po).SeaTemperatureMonthlyC;
            Assert.Equal(12, sst.Length);
            for (int m = 0; m < 12; m++)
            {
                double gap = Math.Abs(sst[m] - sim[m]);
                worst = Math.Max(worst, gap);
                Assert.True(gap <= 2.0, $"PO{po} month {m + 1}: SST {sst[m]} vs sim {sim[m]}");
            }
        }
        Assert.True(worst > 0.5, "the two sources are independent; identical values would mean a copy");
    }

    [Fact]
    public void EveryOpenPensTemperatureComponent_IsThePublishedTrapezoidOverTheSimTable()
    {
        var rules = GeoSuitabilityRules.Default;
        foreach (var c in GeoCandidates.Default.All.Where(c => c.IsOpenPen))
        {
            double expected = rules.TemperatureComponent(ProductionAreas.Get(c.Po).SeaTemperatureMonthlyC);
            Assert.True(c.TryGetComponent("temperature", out double actual), c.Id);
            Assert.True(Math.Abs(expected - actual) <= 0.0005, $"{c.Id}: {actual} vs {expected:F4}");
        }
        // The colder areas are the ones the temperature ramp holds back (PO1's summer peak too).
        Assert.True(rules.TemperatureComponent(ProductionAreas.Get(13).SeaTemperatureMonthlyC)
                    < rules.TemperatureComponent(ProductionAreas.Get(6).SeaTemperatureMonthlyC));
    }
}
