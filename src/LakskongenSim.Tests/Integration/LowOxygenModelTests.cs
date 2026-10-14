using System;
using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Calibration;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Wave 2 sim-a-1, the known red of S2-05: in PO3's real water every
/// low-oxygen death of the po3 careers came from the bloom deck's
/// <c>hypoxia</c> card, which killed 3 % x severity of a site per weekly tick
/// whatever oxygen the water held (zone oxygen 7.6-8.2 mg/L on every sag;
/// 65,225 of 525,255 deaths over 20 seeds). The model now: the card is an
/// in-cage oxygen sag that lowers the pens' water through the holding seam
/// (Solstorm et al. 2018), and every source of low oxygen kills through one
/// hazard at the fish's own oxygen, below the acute limit of 3 mg/L (US EPA
/// 1986); a plankton bloom's crash takes the oxygen its carbon needs
/// (Redfield 1963), not 75 % of the zone's.
/// </summary>
public class LowOxygenModelTests
{
    private readonly ITestOutputHelper _output;

    public LowOxygenModelTests(ITestOutputHelper output) => _output = output;

    private static readonly MortalityCalibration Mortality = CalibrationRegistry.Default.Mortality;
    private static readonly WaterOxygenCalibration WaterOxygen = CalibrationRegistry.Default.WaterOxygen;

    private static CohortEnvironment AtOxygen(double o2)
        => new(HoldingKind.SeaPen, "s", 15.0, o2, double.NaN, 0.0);

    /// <summary>A stocked open-net sea site in its own zone, mid-April (outside the jellyfish and algae seasons), events off.</summary>
    private static (SimulationEngine Engine, Zone Zone, SeaSite Site) StockedSite(int seed, double waterC,
        ContainmentLevel containment = ContainmentLevel.OpenNet)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        var zone = new Zone("sag-zone") { ProductionArea = 3 };
        engine.State.Zones.Add(zone);
        var site = new SeaSite("sag-site", zone.Id) { MABTonnes = 5_000, Containment = containment };
        engine.State.SeaSites.Add(site);
        var stocked = engine.StockSmolts(site.Id, 100_000, "standard");
        Assert.True(stocked.Ok, stocked.Message);
        engine.Advance(100);
        zone.SetTemperature(waterC);
        zone.WaterChemistry.OxygenO2 = WaterQuality.CalculateOxygenSaturation(waterC);
        return (engine, zone, site);
    }

    private static BloomEvent Sag(SimulationEngine engine, SeaSite site, double severity)
    {
        var bloom = new BloomEvent { Kind = "hypoxia", SiteId = site.Id, Severity01 = severity, StartDay = engine.State.Day, DurationDays = 14 };
        engine.State.ActiveBlooms.Add(bloom);
        return bloom;
    }

    private static long LowOxygenDeaths(SimulationEngine engine, string siteId)
        => engine.MortalityLedger.ForSite(siteId).Deaths(MortalityCause.LowOxygen).Fish;

    [Fact]
    public void TheLowOxygenHazard_KillsOnlyBelowTheAcuteLimit_OnTheRegistrysLadder()
    {
        Assert.Equal(3.0, Mortality.AcuteLowOxygenMgL);          // US EPA 1986, salmonid other life stages
        Assert.Equal(0.0, MortalityHazards.LowOxygenFraction(AtOxygen(9.0), Mortality));
        Assert.Equal(0.0, MortalityHazards.LowOxygenFraction(AtOxygen(3.0), Mortality));
        Assert.Equal(0.10, MortalityHazards.LowOxygenFraction(AtOxygen(2.999), Mortality));
        Assert.Equal(0.25, MortalityHazards.LowOxygenFraction(AtOxygen(1.999), Mortality));
        Assert.Equal(0.50, MortalityHazards.LowOxygenFraction(AtOxygen(0.999), Mortality));
        Assert.Equal(0.0, MortalityHazards.LowOxygenFraction(AtOxygen(double.NaN), Mortality));
        // The limit is data: a registry with another limit moves the threshold.
        var stricter = Mortality with { AcuteLowOxygenMgL = 4.0 };
        Assert.Equal(0.10, MortalityHazards.LowOxygenFraction(AtOxygen(3.5), stricter));
    }

    /// <summary>
    /// The sag reaches the fish through the holding seam: the site's pens'
    /// water (and so the hazards' environment) holds severity x 0.42 less
    /// oxygen than the zone; an O2 diffuser cuts the depth to 30 %, a
    /// semi-closed pen's oxygen demand raises it 1.4-fold; a land tank in the
    /// same zone, another site and an expired sag are untouched.
    /// </summary>
    [Fact]
    public void AnOxygenSag_LowersThePensWater_ThroughTheSeam_AndNothingElse()
    {
        var (engine, zone, site) = StockedSite(seed: 81, waterC: 15.0);
        var other = new SeaSite("other-site", zone.Id) { MABTonnes = 5_000 };
        engine.State.SeaSites.Add(other);
        Assert.True(engine.StockSmolts(other.Id, 10_000, "standard").Ok);
        var pen = site.Pens.Single();
        double ambient = zone.WaterChemistry.OxygenO2;
        Assert.Equal(ambient, engine.HoldingWaterOf(pen).OxygenMgPerL, 12);

        var sag = Sag(engine, site, severity: 0.8);
        double depth = 0.8 * WaterOxygen.HypoxiaSagMaxDepth;
        Assert.Equal(ambient * (1.0 - depth), engine.HoldingWaterOf(pen).OxygenMgPerL, 12);
        Assert.Equal(engine.HoldingWaterOf(pen).OxygenMgPerL, engine.ResolveCohortEnvironment(pen).OxygenMgL, 12);
        Assert.Equal(ambient, engine.HoldingWaterOf(other.Pens.Single()).OxygenMgPerL, 12);

        site.HasO2Diffuser = true;
        Assert.Equal(ambient * (1.0 - depth * 0.3), engine.HoldingWaterOf(pen).OxygenMgPerL, 12);
        site.HasO2Diffuser = false;
        site.Containment = ContainmentLevel.SemiClosed;
        Assert.Equal(ambient * (1.0 - depth * 1.4), engine.HoldingWaterOf(pen).OxygenMgPerL, 12);
        site.Containment = ContainmentLevel.OpenNet;

        sag.DurationDays = 0; // over
        Assert.Equal(ambient, engine.HoldingWaterOf(pen).OxygenMgPerL, 12);
    }

    /// <summary>
    /// The defect, pinned: the worst sag at an open-net site in saturated
    /// 16 °C water costs gills (and appetite through the water) but kills
    /// nothing, because the cage keeps 58 % of its oxygen, above the 3 mg/L
    /// acute limit. The old card killed 3 % of the site on this tick.
    /// </summary>
    [Fact]
    public void TheWorstSag_InSaturatedWater_AtAnOpenNetSite_DamagesGillsButKillsNoFish()
    {
        var (engine, _, site) = StockedSite(seed: 82, waterC: 16.0);
        int before = site.Pens.Sum(p => p.TotalCount);
        Sag(engine, site, severity: 1.0);

        double cageO2 = engine.HoldingWaterOf(site.Pens.Single()).OxygenMgPerL;
        Assert.InRange(cageO2, Mortality.AcuteLowOxygenMgL, 6.0);
        engine.RollBloomWeek();

        Assert.Equal(0, LowOxygenDeaths(engine, site.Id));
        Assert.Equal(before, site.Pens.Sum(p => p.TotalCount));
        Assert.True(site.GillScore < 100.0, "the sag must still cost gills");
    }

    /// <summary>
    /// A sag kills when the water at the fish falls below the acute limit:
    /// a zone already low (5 mg/L) under the worst open-net sag leaves the
    /// cage at 2.9 mg/L, and a tenth of the pen dies, booked as low oxygen;
    /// an O2 diffuser keeps the same pen above the limit.
    /// </summary>
    [Fact]
    public void ASag_OverWaterAlreadyLow_KillsATenth_AndADiffuserPreventsIt()
    {
        var (engine, zone, site) = StockedSite(seed: 83, waterC: 16.0);
        zone.WaterChemistry.OxygenO2 = 5.0;
        Sag(engine, site, severity: 1.0);
        var pen = site.Pens.Single();
        Assert.True(engine.HoldingWaterOf(pen).OxygenMgPerL < Mortality.AcuteLowOxygenMgL);
        int before = pen.TotalCount;

        engine.RollBloomWeek();

        long died = LowOxygenDeaths(engine, site.Id);
        Assert.Equal(before - pen.TotalCount, died);
        Assert.InRange(died, (long)(before * 0.099), (long)(before * 0.1));

        var (engine2, zone2, site2) = StockedSite(seed: 83, waterC: 16.0);
        zone2.WaterChemistry.OxygenO2 = 5.0;
        site2.HasO2Diffuser = true;
        Sag(engine2, site2, severity: 1.0);
        engine2.RollBloomWeek();
        Assert.Equal(0, LowOxygenDeaths(engine2, site2.Id));
    }

    /// <summary>
    /// The oxygen the sag leaves follows the water's temperature-dependent
    /// solubility (Benson &amp; Krause 1984): in a closed floating pen (1.6x
    /// oxygen demand) the worst sag in saturated 7 °C water stays above the
    /// acute limit, the same sag in saturated 18 °C water falls below it.
    /// </summary>
    [Fact]
    public void TheSameSag_KillsInWarmWater_ButNotInColdWater_BecauseWarmWaterHoldsLessOxygen()
    {
        double depth = Math.Min(1.0, WaterOxygen.HypoxiaSagMaxDepth * ContainmentTech.ModifiersFor(ContainmentLevel.ClosedFloat).OxygenDemandMult);
        Assert.True(WaterQuality.CalculateOxygenSaturation(7.0) * (1.0 - depth) >= Mortality.AcuteLowOxygenMgL);
        Assert.True(WaterQuality.CalculateOxygenSaturation(18.0) * (1.0 - depth) < Mortality.AcuteLowOxygenMgL);

        var (cold, _, coldSite) = StockedSite(seed: 84, waterC: 7.0, ContainmentLevel.ClosedFloat);
        Sag(cold, coldSite, severity: 1.0);
        cold.RollBloomWeek();
        Assert.Equal(0, LowOxygenDeaths(cold, coldSite.Id));

        var (warm, _, warmSite) = StockedSite(seed: 84, waterC: 18.0, ContainmentLevel.ClosedFloat);
        Sag(warm, warmSite, severity: 1.0);
        warm.RollBloomWeek();
        Assert.True(LowOxygenDeaths(warm, warmSite.Id) > 0, "the warm-water sag must kill");
    }

    /// <summary>
    /// A plankton bloom's crash takes the oxygen its dying carbon needs:
    /// chlorophyll at capacity (10 µg/L) x C:Chl (50) x Redfield O2:C (3.47
    /// g/g) = 1.735 mg/L for a whole bloom, so a crash never takes more than
    /// that from the zone. The old crash cut 75 % x the peak (8 to about 3 mg/L).
    /// </summary>
    [Fact]
    public void APlanktonCrash_TakesTheOxygenItsCarbonNeeds_NotThreeQuartersOfTheZones()
    {
        Assert.Equal(10.0 * 50.0 * 3.47 / 1000.0, WaterOxygen.BloomCrashOxygenDemandAtCapacityMgL, 12);
        var engine = new SimulationEngine();
        engine.Initialize(enableEvents: false, seed: 85);
        Assert.Equal(WaterOxygen.BloomCrashOxygenDemandAtCapacityMgL, engine.PlanktonBloom.Config.CrashOxygenDemandAtCapacityMgL, 12);

        var zone = new Zone("crash-zone", 15.0) { CurrentSpeed = 0.05, NutrientLevel = 3.0 };
        engine.State.Zones.Add(zone);
        engine.PlanktonBloom.RestoreBloomStates(new Dictionary<string, PlanktonBloom.BloomState>
        {
            [zone.Id] = new PlanktonBloom.BloomState { Intensity = 0.99, IsActive = true, PeakIntensity = 0.99, StartDay = 0 },
        });
        double before = zone.WaterChemistry.OxygenO2;
        engine.PlanktonBloom.AdvanceDay(engine.State, engine.Weather);
        double taken = before - zone.WaterChemistry.OxygenO2;

        Assert.True(engine.PlanktonBloom.GetBloomIntensity(zone.Id) < 0.2, "the bloom must have crashed");
        double dying = 0.9 * WaterOxygen.BloomCrashOxygenDemandAtCapacityMgL; // all but the 10 % retained
        Assert.InRange(taken, 0.98 * dying, dying + 1e-9);
        Assert.True(zone.WaterChemistry.OxygenO2 > Mortality.AcuteLowOxygenMgL);
    }

    /// <summary>
    /// The po3 careers, 20 seeds: low oxygen is a rare compound event, not a
    /// weekly certainty. Pooled low-oxygen deaths stay under 2 % of all deaths
    /// (NVI's whole environment class, jellyfish and algae included, was 1 %
    /// of deaths in PO1-PO4 in 2024 and 0.2-2.7 % nationally in 2021-2023;
    /// Fiskehelserapporten 2024 table 2.4.2 and p. 38), every career keeps
    /// D4's band (at most 35 % lost) and ends above the -1M floor. Before:
    /// 12.4 % of the pooled deaths and 14 of 20 careers hit.
    /// </summary>
    [Fact]
    public void Po3Careers_TwentySeeds_LowOxygenIsARareEvent_AndEveryCareerHoldsTheD4Band()
    {
        long deaths = 0, lowOxygen = 0;
        int hit = 0;
        for (int seed = 1; seed <= 20; seed++)
        {
            var r = PlayerScriptProbe.RunCareer(seed, roundTripSaveLoad: false);
            long low = r.Mortality.Deaths(MortalityCause.LowOxygen).Fish;
            deaths += r.Mortality.Total.Fish;
            lowOxygen += low;
            if (low > 0) hit++;
            Assert.InRange(r.SeaMortalityPct, 0.0, 35.0);
            Assert.True(r.EndingBalance > -1_000_000m, $"seed {seed} ends at {r.EndingBalance:N0} NOK");
        }
        _output.WriteLine($"20 po3 careers: {deaths:N0} deaths, {lowOxygen:N0} low oxygen ({100.0 * lowOxygen / deaths:F3} %), {hit} careers hit");
        Assert.True(lowOxygen < deaths * 0.02, $"{lowOxygen} of {deaths} deaths from low oxygen");
        Assert.True(hit <= 2, $"{hit} of 20 careers lost fish to low oxygen");
    }
}
