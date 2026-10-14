using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using Xunit;

namespace LakskongenSim.Tests.Environment;

/// <summary>
/// F1024: the zone water chemistry is integrated exactly per day. The old
/// explicit step applied a 6.5-20/day exchange rate as a fraction (gain about
/// −9); the [0,14] clamp hid the divergence and turned dissolved oxygen into a
/// daily 0/14 mg/L square wave that cut growth and inflated FCR on alternate
/// days. The zone nutrient balance had the same unit defect and kept an algal
/// bloom running almost permanently.
/// </summary>
public class WaterChemistryStabilityTests
{
    private const double ZoneVolumeM3 = 500_000.0;

    [Theory]
    [InlineData(650.0)]    // slack current, 0.05 m/s
    [InlineData(1_000.0)]
    [InlineData(2_000.0)]  // strong current, 0.5 m/s
    public void Chemistry_StaysOffItsClamps_AndOxygenMovesUnderOneMgPerDay_Over400Days(double flow)
    {
        var chem = new WaterChemistry(10.0);
        double previousO2 = chem.OxygenO2;

        for (int day = 0; day < 400; day++)
        {
            double biomassKg = 2_000_000.0 * Math.Min(1.0, day / 300.0); // ramp to 2,000 t
            chem.UpdateOneDay(biomassKg, 1.2, flow, ZoneVolumeM3);

            Assert.InRange(chem.OxygenO2, 0.5, 13.5);
            Assert.True(Math.Abs(chem.OxygenO2 - previousO2) < 1.0,
                $"day {day}: O2 jumped {previousO2:F2} -> {chem.OxygenO2:F2} mg/L");
            Assert.InRange(chem.pH, 6.6, 8.4);
            Assert.InRange(chem.AmmoniaNH4, 0.0, 1.0);
            Assert.InRange(chem.NitrateNO3, 0.0, 5.0);
            Assert.InRange(chem.PhosphorusPO4, 0.0, 1.0);
            previousO2 = chem.OxygenO2;
        }
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(10.0)]
    [InlineData(16.0)]
    public void EmptyZone_SettlesAtSaturation_AndTheInflowBaseline(double temperatureC)
    {
        var chem = new WaterChemistry(temperatureC);
        for (int day = 0; day < 60; day++)
            chem.UpdateOneDay(0.0, 1.2, 1_000.0, ZoneVolumeM3);

        double saturation = WaterQuality.CalculateOxygenSaturation(temperatureC);
        Assert.Equal(saturation, chem.OxygenO2, 2);
        // Flushing brings in fjord water; it used to drain every species to 0.
        Assert.Equal(0.5, chem.NitrateNO3, 2);
        Assert.Equal(0.1, chem.PhosphorusPO4, 2);
        Assert.Equal(0.05, chem.AmmoniaNH4, 2);
        // pH used to climb 0.04/day into the 8.5 clamp.
        Assert.Equal(7.8, chem.pH, 2);
    }

    [Fact]
    public void OxygenEquilibrium_FallsWithBiomass_AndRecoversWithStrongerFlow()
    {
        static double Settle(double biomassKg, double flow)
        {
            var chem = new WaterChemistry(12.0);
            for (int day = 0; day < 60; day++)
                chem.UpdateOneDay(biomassKg, 1.2, flow, ZoneVolumeM3);
            return chem.OxygenO2;
        }

        double saturation = WaterQuality.CalculateOxygenSaturation(12.0);
        double light = Settle(1_000_000.0, 650.0);
        double heavySlack = Settle(50_000_000.0, 650.0);
        double heavyStrong = Settle(50_000_000.0, 2_000.0);

        Assert.True(light < saturation);
        Assert.True(heavySlack < light, $"more fish must lower O2: {heavySlack} vs {light}");
        Assert.True(heavyStrong > heavySlack, $"stronger flow must restore O2: {heavyStrong} vs {heavySlack}");
        Assert.True(heavyStrong <= saturation);
    }

    [Theory]
    [InlineData(0.0, 6.0)]
    [InlineData(2.0, 6.5)]
    [InlineData(1e9, 10.0)] // even a huge (rate × dt ≫ 2) step lands on the equilibrium
    public void RelaxOneDay_NeverOvershootsTheEquilibrium(double start, double lossRate)
    {
        const double source = 30.0;
        double equilibrium = source / lossRate;
        double next = WaterChemistry.RelaxOneDay(start, source, lossRate);

        double lo = Math.Min(start, equilibrium), hi = Math.Max(start, equilibrium);
        Assert.InRange(next, lo, hi);
    }

    [Fact]
    public void PenDoUpdate_NormalizesDemandByVolume_InsteadOfZeroingAnyStockedPen()
    {
        // 10 t in a 10,000 m³ pen (1 kg/m³): the old update applied 1e6 mg/L/day
        // of demand and pinned oxygen at 0.
        var chem = new WaterChemistry(10.0);
        double saturation = WaterQuality.CalculateOxygenSaturation(10.0);
        for (int day = 0; day < 30; day++)
            WaterQuality.UpdateDailyDO(chem, 10.0, biomassKg: 10_000.0, feedRateKgPerDay: 100.0,
                fcr: 1.2, penVolumeM3: 10_000.0, currentSpeedMs: 0.3);

        Assert.InRange(chem.OxygenO2, saturation - 0.5, saturation);
    }

    [Fact]
    public void NutrientLevel_StaysNearBackground_AndAFishFreeBloomRunsItsCourse()
    {
        var zone = new Zone("zone-nutrients") { CurrentFishBiomassKg = 400_000.0 };
        for (int day = 0; day < 365; day++)
            zone.AdvanceDays(1);

        // 400 t used to push NutrientLevel past 1,000 mg/L and keep a bloom running.
        Assert.InRange(zone.NutrientLevel, 0.3, 1.0);
        Assert.False(zone.AlgaeBloomActive);

        zone.TriggerAlgalBloom();
        zone.NutrientLevel = 3.0;
        int bloomDays = 0;
        while (zone.AlgaeBloomActive && bloomDays < 100)
        {
            zone.AdvanceDays(1);
            bloomDays++;
        }
        Assert.InRange(bloomDays, Zone.DefaultAlgalBloomDays - 1, Zone.DefaultAlgalBloomDays + 1);
    }

    [Fact]
    public void CareerZones_KeepAStableOxygenSignal_NotASquareWave()
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: 7);
        var scenario = engine.Scenario.GetScenario("po3_red_sone");
        Assert.NotNull(scenario);
        scenario.ApplyStartingState(engine.State, engine);
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "robust").Ok);
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);

        var o2 = new Dictionary<string, List<double>>();
        var lowSatDays = new Dictionary<string, int>();
        var bloomDays = new Dictionary<string, int>();
        const int days = 400;
        for (int day = 0; day < days; day++)
        {
            engine.Advance(1);
            double temperature = engine.Weather.CurrentTemperatureC;
            foreach (var zone in engine.State.Zones)
            {
                var chem = zone.WaterChemistry;
                if (!o2.ContainsKey(zone.Id)) { o2[zone.Id] = new(); lowSatDays[zone.Id] = 0; bloomDays[zone.Id] = 0; }
                o2[zone.Id].Add(chem.OxygenO2);
                if (WaterQuality.CalculateDOSaturation(chem.OxygenO2, temperature) < 0.7) lowSatDays[zone.Id]++;
                if (zone.AlgaeBloomActive) bloomDays[zone.Id]++;

                Assert.InRange(chem.pH, 6.51, 8.49); // used to sit on the 8.5 clamp
                Assert.True(zone.NutrientLevel < 5.0, $"{zone.Id} nutrients {zone.NutrientLevel:F1} mg/L");
            }
        }

        Assert.NotEmpty(o2);
        foreach (var (zoneId, series) in o2)
        {
            // Before: O2 was 0.00 on ~49% of days and DO saturation below 70% on
            // as many; a bloom was active on ~99% of days.
            Assert.DoesNotContain(series, v => v < 0.01 || v > 13.99);
            Assert.True(lowSatDays[zoneId] < days * 0.05,
                $"{zoneId}: {lowSatDays[zoneId]} of {days} days below 70% DO saturation");
            Assert.True(bloomDays[zoneId] < days * 0.2,
                $"{zoneId}: algal bloom active {bloomDays[zoneId]} of {days} days");
        }
    }
}
