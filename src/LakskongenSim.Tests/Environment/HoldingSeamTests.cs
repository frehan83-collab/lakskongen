using System.Linq;
using LakskongenSim.Biology;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Environment
{
    /// <summary>
    /// Wave 2 sim-a-1: one farm-type seam. S2-02's <see cref="HoldingWater"/>
    /// and S2-05's <see cref="CohortEnvironment"/> describe the same water:
    /// the holding is resolved once per simulated step
    /// (<see cref="SimulationEngine.HoldingOf"/>), the water at the fish is
    /// <see cref="SimulationEngine.HoldingWaterOf(FishPopulation)"/>, and the
    /// hazards' environment is built from that water plus only the holding's
    /// id, CO2 and wave height. One enum (<see cref="HoldingKind"/>) names the
    /// holding; <see cref="FishHolding"/> keeps S2-05's spellings of it.
    /// </summary>
    public class HoldingSeamTests
    {
        /// <summary>A sea site in PO9, a land tank and an offshore structure, each stocked, 40 days in (February).</summary>
        private static (SimulationEngine Engine, Zone Zone, SeaSite Site, RASFacility Ras, OffshoreFarm Farm) ThreeHoldings(bool events = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000_000m) };
            engine.Initialize(enableEvents: events, seed: 61);
            var zone = new Zone("sjo-zone") { ProductionArea = 9 };
            engine.State.Zones.Add(zone);
            engine.State.HomeProductionArea = 9;
            var site = new SeaSite("sjo", zone.Id) { MABTonnes = 5_000 };
            engine.State.SeaSites.Add(site);
            var stocked = engine.StockSmolts(site.Id, 20_000, "standard");
            Assert.True(stocked.Ok, stocked.Message);

            Assert.True(engine.BuildRASFacility("Landtank", RASScale.Small).Ok);
            Assert.True(engine.BuildOffshoreFarm("Havfarm", OffshoreType.SemiSubmersible).Ok);
            var ras = engine.RASFacilities.Single();
            var farm = engine.OffshoreFarms.Single();
            farm.ZoneId = zone.Id;
            ras.ConstructionDays = 1;
            farm.ConstructionDays = 1;
            engine.Advance(2);
            Assert.True(engine.StockRASFacility(ras.Id, 20_000).Ok);
            Assert.True(engine.StockOffshoreFarm(farm.Id, 20_000).Ok);
            engine.Advance(38);
            return (engine, zone, site, ras, farm);
        }

        [Fact]
        public void TheOneHoldingEnum_KeepsTheS205Spellings()
        {
            Assert.Equal(HoldingKind.SeaPen, FishHolding.SeaPen);
            Assert.Equal(HoldingKind.LandTank, FishHolding.LandBasedTank);
            Assert.Equal(HoldingKind.Offshore, FishHolding.OffshoreStructure);
            // Three holdings, three distinct values: no S2-05 name collapses onto another.
            Assert.Equal(3, new[] { FishHolding.SeaPen, FishHolding.LandBasedTank, FishHolding.OffshoreStructure }.Distinct().Count());
        }

        [Fact]
        public void SeaPen_ResolvesToItsSiteAndZone_AndTheHazardsReadTheSameWater()
        {
            var (engine, zone, site, _, _) = ThreeHoldings();
            var pen = site.Pens.Single();

            var holding = engine.HoldingOf(pen);
            Assert.Equal(HoldingKind.SeaPen, holding.Kind);
            Assert.Equal(site.Id, holding.Id);
            Assert.Same(site, holding.Site);
            Assert.Same(zone, holding.Zone);

            var water = engine.HoldingWaterOf(pen);
            Assert.Equal(HoldingKind.SeaPen, water.Kind);
            Assert.Equal(zone.WaterChemistry.Temperature, water.TemperatureC, 12);
            Assert.Equal(zone.WaterChemistry.OxygenO2, water.OxygenMgPerL, 12);
            Assert.Equal(9, water.ProductionArea);

            var env = engine.ResolveCohortEnvironment(pen);
            Assert.Equal(water.Kind, env.Holding);
            Assert.Equal(site.Id, env.HoldingId);
            Assert.Equal(water.TemperatureC, env.WaterTemperatureC, 12);
            Assert.Equal(water.OxygenMgPerL, env.OxygenMgL, 12);
            Assert.True(double.IsNaN(env.Co2MgL), "sea water carries no modelled CO2");
            Assert.Equal(engine.Weather.CurrentWaveHeight, env.WaveHeightM, 12);
        }

        [Fact]
        public void LandTank_ResolvesToItsFacility_WithItsOwnWaterAndCo2_AndNoWaves()
        {
            var (engine, _, site, ras, _) = ThreeHoldings();
            var tank = ras.Tanks.Single();
            ras.CurrentWaterTemp = 12.5;
            ras.OxygenLevel = 8.4;
            ras.CO2Level = 9.5;

            var holding = engine.HoldingOf(tank);
            Assert.Equal(HoldingKind.LandTank, holding.Kind);
            Assert.Equal(ras.Id, holding.Id);
            Assert.Same(ras, holding.LandFacility);
            Assert.Null(holding.Site);

            var water = engine.HoldingWaterOf(tank);
            Assert.Equal(HoldingKind.LandTank, water.Kind);
            Assert.False(water.OpenToSea);
            Assert.Equal(12.5, water.TemperatureC, 12);
            Assert.Equal(8.4, water.OxygenMgPerL, 12);

            var env = engine.ResolveCohortEnvironment(tank);
            Assert.Equal(HoldingKind.LandTank, env.Holding);
            Assert.Equal(ras.Id, env.HoldingId);
            Assert.Equal(12.5, env.WaterTemperatureC, 12);
            Assert.Equal(8.4, env.OxygenMgL, 12);
            Assert.Equal(9.5, env.Co2MgL, 12);
            Assert.Equal(0.0, env.WaveHeightM);
            // The tank's 12.5 °C is not February's PO9 sea.
            Assert.True(water.TemperatureC > engine.HoldingWaterOf(site.Pens.Single()).TemperatureC + 5.0);
        }

        [Fact]
        public void OffshorePen_TakesItsAreasSea_WithTheFarmsCurrentFedOxygen_InBothViews()
        {
            var (engine, zone, _, _, farm) = ThreeHoldings();
            var pen = farm.Pens.Single();
            farm.CurrentWaveHeight = 3.25;

            var holding = engine.HoldingOf(pen);
            Assert.Equal(HoldingKind.Offshore, holding.Kind);
            Assert.Equal(farm.Id, holding.Id);
            Assert.Same(farm, holding.OffshoreFarm);
            Assert.Same(zone, holding.Zone);

            var water = engine.HoldingWaterOf(pen);
            Assert.Equal(HoldingKind.Offshore, water.Kind);
            Assert.True(water.OpenToSea);
            Assert.Equal(engine.Weather.SeaTemperatureAt(9), water.TemperatureC, 12);
            // S2-02 gave the offshore pen the zone's oxygen, S2-05 the zone's plus
            // the farm's boost; the one water now carries the boost everywhere.
            Assert.Equal(zone.WaterChemistry.OxygenO2 + farm.OxygenBoost, water.OxygenMgPerL, 12);

            var env = engine.ResolveCohortEnvironment(pen);
            Assert.Equal(HoldingKind.Offshore, env.Holding);
            Assert.Equal(farm.Id, env.HoldingId);
            Assert.Equal(water.OxygenMgPerL, env.OxygenMgL, 12);
            Assert.Equal(water.TemperatureC, env.WaterTemperatureC, 12);
            Assert.Equal(3.25, env.WaveHeightM, 12);
        }

        [Fact]
        public void APinnedSeaTemperature_ReachesSeaAndOffshorePens_ButNeverALandTank()
        {
            var (engine, _, site, ras, farm) = ThreeHoldings();
            ras.CurrentWaterTemp = 12.0;
            var seaPen = site.Pens.Single();
            var hullPen = farm.Pens.Single();
            var tank = ras.Tanks.Single();

            foreach (var pinned in new[] { -1.0, 3.0, 19.5 })
            {
                var sea = engine.ResolveCohortEnvironment(seaPen, pinned);
                var hull = engine.ResolveCohortEnvironment(hullPen, pinned);
                var land = engine.ResolveCohortEnvironment(tank, pinned);
                Assert.Equal(pinned, sea.WaterTemperatureC, 12);
                Assert.Equal(pinned, hull.WaterTemperatureC, 12);
                Assert.Equal(12.0, land.WaterTemperatureC, 12);
                // Pinning the temperature changes nothing else.
                Assert.Equal(engine.ResolveCohortEnvironment(seaPen) with { WaterTemperatureC = pinned }, sea);
                Assert.Equal(engine.ResolveCohortEnvironment(hullPen) with { WaterTemperatureC = pinned }, hull);
                Assert.Equal(engine.ResolveCohortEnvironment(tank), land);
            }
        }

        /// <summary>
        /// The per-step index and a fresh scan of the facility, site and
        /// population lists give every population the same holding, on every
        /// day of a 60-day run with events on (stocking, delousing, blooms).
        /// </summary>
        [Fact]
        public void TheStepIndex_AgreesWithAFreshScan_ForEveryPopulationEveryDay()
        {
            var (engine, _, _, _, _) = ThreeHoldings(events: true);
            for (int d = 0; d < 60; d++)
            {
                engine.Advance(1);
                var index = HoldingIndex.Build(engine.State);
                Assert.Equal(engine.State.Populations.Count, index.Count);
                foreach (var population in engine.State.Populations)
                {
                    Assert.True(index.TryGet(population, out var indexed));
                    Assert.Equal(engine.HoldingOf(population), indexed); // outside a step: a fresh scan
                }
            }
        }

        /// <summary>
        /// The per-tick cost rule: each simulated step resolves every
        /// population's holding once (one index per step), and the hazards,
        /// growth, feeding and the disease tick all answer from it: no fresh
        /// scan of the facility lists happens inside a step.
        /// </summary>
        [Fact]
        public void EachStep_ResolvesTheHoldingsOnce_AndNoHazardScansTheFacilities()
        {
            var (engine, _, _, _, _) = ThreeHoldings(events: true);
            int buildsBefore = engine.HoldingIndexBuilds;
            long scansBefore = engine.HoldingScans;
            int populationsBefore = engine.State.Populations.Count;

            engine.Advance(120);

            Assert.Equal(buildsBefore + 120, engine.HoldingIndexBuilds);
            Assert.Equal(populationsBefore, engine.State.Populations.Count);
            Assert.Equal(scansBefore, engine.HoldingScans);
        }

        /// <summary>
        /// Between steps the player moves fish (stocking, harvest); a lookup
        /// then resolves afresh, so a tank stocked after a step is a land tank
        /// at once, not a sea pen from a stale index.
        /// </summary>
        [Fact]
        public void BetweenSteps_ANewTankResolvesAsALandTank_NotFromAStaleIndex()
        {
            var (engine, _, _, ras, _) = ThreeHoldings();
            engine.Advance(1);
            Assert.True(engine.StockRASFacility(ras.Id, 5_000).Ok);
            var newTank = ras.Tanks.Last();
            Assert.Equal(HoldingKind.LandTank, engine.HoldingOf(newTank).Kind);
            Assert.Equal(HoldingKind.LandTank, engine.ResolveCohortEnvironment(newTank).Holding);

            long scans = engine.HoldingScans;
            engine.Advance(1);
            Assert.Equal(scans, engine.HoldingScans); // the next step's index already holds it
        }
    }
}
