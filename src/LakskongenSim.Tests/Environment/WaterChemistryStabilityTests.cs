using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Tests.Integration;
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
        // pH used to climb 0.04/day into the 8.5 clamp. It settles at the
        // coastal seawater equilibrium (Norwegian coastal surface pH 8.0-8.1;
        // S1-01 moved the baseline from 7.8, the low edge of seawater).
        Assert.Equal(8.05, chem.pH, 2);
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

    /// <summary>The sim calendar's summer half-year ("sommerhalvåret", April-September):
    /// month index = day of year / 30, as WeatherSystem counts months.</summary>
    private static bool InSummerHalfYear(int simulationDay) => (simulationDay % 365) / 30 is >= 3 and <= 8;

    private static SimulationEngine Po3CareerScript(int seed)
    {
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: true, seed: seed);
        var scenario = engine.Scenario.GetScenario("po3_red_sone");
        Assert.NotNull(scenario);
        scenario.ApplyStartingState(engine.State, engine);
        Assert.True(engine.StockSmolts("po3_nord", 12_000, "robust").Ok);
        Assert.True(engine.StockSmolts("po3_sor", 10_000, "standard").Ok);
        Assert.True(engine.RequestWellboat("po3_nord", "rov").Ok);
        Assert.True(engine.RequestWellboat("po3_sor", "delouse", "hydrolicer").Ok);
        return engine;
    }

    /// <summary>
    /// S1-01 acceptance 1, on the 600-day po3 probe (the career script, events
    /// on, never harvested, so the biomass load only grows). Before F1024: O2 at
    /// 0 mg/L on 293 days, DO below 70% saturation on 306, a bloom on 597, pH on
    /// its 8.5 clamp. Now: no state variable touches a clamp, DO dips below 70%
    /// saturation on at most 10% of days and only in the summer half-year
    /// (plankton-bloom crashes in warm water), a bloom runs on at most 15% of
    /// days, and pH stays in the seawater band 7.8-8.2.
    /// </summary>
    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    public void Po3Probe600Days_StaysOffTheClamps_WithRareSummerOnlyOxygenDips(int seed)
    {
        const int days = 600;
        var engine = Po3CareerScript(seed);
        var lowSatDays = new Dictionary<string, int>();
        var bloomDays = new Dictionary<string, int>();

        for (int day = 0; day < days; day++)
        {
            engine.Advance(1);
            double seaTemperature = engine.Weather.CurrentTemperatureC;
            foreach (var zone in engine.State.Zones)
            {
                var c = zone.WaterChemistry;
                lowSatDays.TryAdd(zone.Id, 0);
                bloomDays.TryAdd(zone.Id, 0);
                string where = $"{zone.Id} day {engine.State.Day}";

                Assert.InRange(c.OxygenO2, 0.01, 13.99);
                Assert.InRange(c.pH, 7.8, 8.2);
                Assert.InRange(c.AmmoniaNH4, 1e-6, 9.99);
                Assert.InRange(c.NitriteNO2, 1e-6, 1.99);
                Assert.InRange(c.NitrateNO3, 1e-6, 19.99);
                Assert.InRange(c.PhosphorusPO4, 1e-6, 4.99);

                if (WaterQuality.CalculateDOSaturation(c.OxygenO2, seaTemperature) < 0.7)
                {
                    lowSatDays[zone.Id]++;
                    Assert.True(InSummerHalfYear(engine.Weather.SimulationDay),
                        $"{where}: DO {c.OxygenO2:F2} mg/L below 70% saturation outside April-September");
                }
                if (zone.AlgaeBloomActive) bloomDays[zone.Id]++;
            }
        }

        Assert.Equal(2, lowSatDays.Count);
        foreach (var zoneId in lowSatDays.Keys)
        {
            Assert.True(lowSatDays[zoneId] <= days * 0.10,
                $"{zoneId}: {lowSatDays[zoneId]} of {days} days below 70% DO saturation");
            Assert.True(bloomDays[zoneId] <= days * 0.15,
                $"{zoneId}: algal bloom active {bloomDays[zoneId]} of {days} days");
        }
    }

    /// <summary>
    /// S1-01: the zone water takes the sea temperature every day. Nothing set
    /// it before, so every zone sat at 10 °C all year: oxygen solubility, the
    /// chemistry's Q10 rates and the plankton bloom's temperature factor never
    /// saw a season while growth and feeding did.
    /// </summary>
    [Fact]
    public void ZoneWater_FollowsTheSeaTemperature_AndItsOxygenTheSeasonalSolubility()
    {
        // Storyteller events off: a temperature-anomaly event changes the sea
        // temperature after the zones took it, which is the right order but
        // would blur this check.
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 7);
        engine.Scenario.GetScenario("po3_red_sone")!.ApplyStartingState(engine.State, engine);
        var pinned = new Zone("own-temperature");
        pinned.SetTemperature(12.0);
        engine.State.Zones.Add(pinned);
        Assert.Equal(3, engine.State.Zones.Count);

        double coldest = double.MaxValue, warmest = double.MinValue;
        for (int day = 0; day < 365; day++)
        {
            engine.Advance(1);
            double sea = engine.Weather.CurrentTemperatureC;
            foreach (var zone in engine.State.Zones.Where(z => z.FollowsSeaTemperature))
                Assert.Equal(sea, zone.WaterChemistry.Temperature, 9);
            Assert.Equal(12.0, pinned.WaterChemistry.Temperature, 9);
            coldest = Math.Min(coldest, sea);
            warmest = Math.Max(warmest, sea);
        }
        Assert.True(warmest - coldest > 6.0, $"no season: {coldest:F1}-{warmest:F1} °C");
        Assert.Equal(2, engine.State.Zones.Count(z => z.FollowsSeaTemperature));

        // A zone's temperature and its "follows the sea" flag survive a save.
        var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(engine), events: false);
        Assert.False(resumed.State.Zones.Single(z => z.Id == pinned.Id).FollowsSeaTemperature);
        Assert.True(resumed.State.Zones.Where(z => z.Id != pinned.Id).All(z => z.FollowsSeaTemperature));

        // A fish-free zone in winter and summer water holds saturation at its
        // own temperature (more oxygen in cold water).
        var winter = new Zone("winter-zone");
        var summer = new Zone("summer-zone");
        winter.SetTemperature(4.0);
        summer.SetTemperature(14.0);
        winter.AdvanceDays(30);
        summer.AdvanceDays(30);
        Assert.Equal(WaterQuality.CalculateOxygenSaturation(4.0), winter.WaterChemistry.OxygenO2, 2);
        Assert.Equal(WaterQuality.CalculateOxygenSaturation(14.0), summer.WaterChemistry.OxygenO2, 2);
        Assert.True(winter.WaterChemistry.OxygenO2 - summer.WaterChemistry.OxygenO2 > 1.5);
    }
}
