using System;
using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;
using Xunit.Abstractions;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Wave-2 integration of S2-03 (TGC growth and appetite) with the sim-a-1
/// farm-type seam: the growth law reads the water of whatever holds the fish
/// (<see cref="SimulationEngine.HoldingWaterOf(FishPopulation)"/>, the holding
/// resolved once per step), so an in-cage oxygen sag that exists only in a
/// pen's holding water slows that pen's appetite and growth and nobody else's.
/// </summary>
public class WaveTwoSeamIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public WaveTwoSeamIntegrationTests(ITestOutputHelper output) => _output = output;

    private const double WaterC = 15.0;
    private const int SagDays = 14;

    /// <summary>Two stocked open-net sites in one PO3 zone at 15 °C, saturated water, events off.</summary>
    private static (SimulationEngine Engine, Zone Zone, SeaSite Sagged, SeaSite Neighbour) TwoSites(int seed)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        var zone = new Zone("seam-zone") { ProductionArea = 3 };
        engine.State.Zones.Add(zone);
        var sagged = new SeaSite("sagged-site", zone.Id) { MABTonnes = 5_000 };
        var neighbour = new SeaSite("neighbour-site", zone.Id) { MABTonnes = 5_000 };
        engine.State.SeaSites.Add(sagged);
        engine.State.SeaSites.Add(neighbour);
        Assert.True(engine.StockSmolts(sagged.Id, 50_000, "standard").Ok);
        Assert.True(engine.StockSmolts(neighbour.Id, 50_000, "standard").Ok);
        engine.Advance(100);
        return (engine, zone, sagged, neighbour);
    }

    private static double MeanKg(SeaSite site)
    {
        int fish = site.Pens.Sum(p => p.TotalCount);
        return fish > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish : 0.0;
    }

    /// <summary>Hold the zone at 15 °C and saturation for <paramref name="days"/> days; returns each site's weight gain (kg per fish).</summary>
    private static (double Sagged, double Neighbour) Grow(SimulationEngine engine, Zone zone, SeaSite sagged,
        SeaSite neighbour, int days)
    {
        double sagged0 = MeanKg(sagged), neighbour0 = MeanKg(neighbour);
        for (int d = 0; d < days; d++)
        {
            zone.SetTemperature(WaterC);
            zone.WaterChemistry.OxygenO2 = WaterQuality.CalculateOxygenSaturation(WaterC);
            engine.Advance(1);
        }
        return (MeanKg(sagged) - sagged0, MeanKg(neighbour) - neighbour0);
    }

    /// <summary>
    /// The worst sag (severity 1: the cage keeps 58 % of the zone's oxygen,
    /// Solstorm et al. 2018) below the 60 % DO-saturation appetite threshold:
    /// on the S2-03 law the sagged site's fish grow clearly less over two
    /// weeks than the same fish without the sag (the appetite modifier
    /// 1 - e^(-0.58/0.3) = 0.855, plus the sag's gill damage), while the
    /// neighbouring site in the same zone grows exactly as without it.
    /// </summary>
    [Fact]
    public void AnInCageOxygenSag_SlowsTheGrowthLaw_AtItsOwnSiteOnly()
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            var (plain, plainZone, plainSagged, plainNeighbour) = TwoSites(seed);
            var (sag, sagZone, sagSagged, sagNeighbour) = TwoSites(seed);
            sag.State.ActiveBlooms.Add(new BloomEvent
            {
                Kind = "hypoxia", SiteId = sagSagged.Id, Severity01 = 1.0,
                StartDay = sag.State.Day, DurationDays = SagDays + 1
            });
            var pen = sagSagged.Pens.First();
            Assert.True(sag.HoldingWaterOf(pen).OxygenMgPerL < plain.HoldingWaterOf(plainSagged.Pens.First()).OxygenMgPerL * 0.6,
                "the sag must reach the pen's holding water");

            var without = Grow(plain, plainZone, plainSagged, plainNeighbour, SagDays);
            var with = Grow(sag, sagZone, sagSagged, sagNeighbour, SagDays);
            _output.WriteLine($"seed {seed}: sagged site +{with.Sagged * 1000:F1} g vs +{without.Sagged * 1000:F1} g without " +
                $"({with.Sagged / without.Sagged:P1}); neighbour +{with.Neighbour * 1000:F3} g vs +{without.Neighbour * 1000:F3} g");

            Assert.True(without.Sagged > 0.0, "the fish must grow in the plain run");
            Assert.True(with.Sagged < 0.95 * without.Sagged,
                $"seed {seed}: the sag cut growth only to {with.Sagged / without.Sagged:P1}");
            Assert.Equal(without.Neighbour, with.Neighbour, 9);
        }
    }

    /// <summary>A site in its own PO4 zone stocked with <paramref name="smolts"/> standard S1 smolts mid-April, events off.</summary>
    private static (SimulationEngine Engine, SeaSite Site) Stocked(int seed, int smolts)
    {
        var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
        engine.Initialize(enableEvents: false, seed: seed);
        var zone = new Zone("density-zone") { ProductionArea = 4 };
        engine.State.Zones.Add(zone);
        var site = new SeaSite("density-site", zone.Id) { MABTonnes = 50_000 };
        engine.State.SeaSites.Add(site);
        engine.Advance(105);
        Assert.True(engine.StockSmolts(site.Id, smolts, "standard").Ok);
        return (engine, site);
    }

    /// <summary>
    /// S2-01a lets a site stock its plan room in one go (173,555 smolts per po3
    /// site, 561,600 at a barekraft_mester site), and a stocking is one
    /// population. The density stress on growth and FCR (S2-03) read that
    /// population against one 10,000 m³ proxy pen, so 200,000 smolts stood at
    /// 4x the density of S2-03's 50,000-fish commercial pen (90-280 kg/m³ at
    /// harvest size) and grew at the Oppedal floor. A stocking now fills as
    /// many proxy pens as its fish need at the § 25 density at 5 kg: per fish
    /// it grows like the commercial pen (within 1 % after 400 days), while a
    /// commercial pen keeps its one proxy pen exactly.
    /// </summary>
    [Fact]
    public void AStockingOfThePlansRoom_GrowsLikeACommercialPen_NotAtFourTimesItsDensity()
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            var (commercialEngine, commercial) = Stocked(seed, 50_000);
            var (roomEngine, room) = Stocked(seed, 200_000);
            var pen = commercial.Pens.Single();
            Assert.Equal(SimulationEngine.PenVolumeProxyM3, commercialEngine.StockingVolumeM3(pen), 9);
            Assert.Equal(4.0 * SimulationEngine.PenVolumeProxyM3, roomEngine.StockingVolumeM3(room.Pens.Single()), 9);

            commercialEngine.Advance(400);
            roomEngine.Advance(400);
            double commercialKg = MeanKg(commercial), roomKg = MeanKg(room);
            _output.WriteLine($"seed {seed}: 50,000 smolts {commercialKg:F3} kg, 200,000 smolts {roomKg:F3} kg after 400 days");
            Assert.True(commercialKg > 3.0, $"seed {seed}: the commercial pen only reached {commercialKg:F2} kg");
            Assert.InRange(roomKg / commercialKg, 0.99, 1.01);
        }
    }
}
