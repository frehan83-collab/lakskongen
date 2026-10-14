using System;
using System.Collections.Generic;
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
    /// Roadmap S2-02: each production area's sea temperature reaches its zones,
    /// and growth, lice, oxygen, treatments and temperature-stress deaths read
    /// the water of whatever holds the fish (the <see cref="HoldingWater"/> seam).
    /// </summary>
    public class SeaTemperatureCouplingTests
    {
        private static SimulationEngine NewEngine(int seed, bool events = false)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(500_000_000m) };
            engine.Initialize(enableEvents: events, seed: seed);
            return engine;
        }

        private static (Zone Zone, SeaSite Site) AddSite(SimulationEngine engine, string id, int productionArea)
        {
            var zone = new Zone(id + "-zone") { ProductionArea = productionArea };
            engine.State.Zones.Add(zone);
            var site = new SeaSite(id, zone.Id) { MABTonnes = 5_000 };
            engine.State.SeaSites.Add(site);
            return (zone, site);
        }

        private static double MeanWeightKg(SeaSite site)
        {
            int fish = site.Pens.Sum(p => p.TotalCount);
            return fish > 0 ? site.Pens.Sum(p => p.GetTotalBiomassKg()) / fish : 0.0;
        }

        [Fact]
        public void ZonesInDifferentAreas_DifferByTheirClimatologies_AndShareOneAnomaly()
        {
            var engine = NewEngine(seed: 11);
            var (po4, _) = AddSite(engine, "vest", 4);
            var (po13, _) = AddSite(engine, "aust", 13);
            engine.State.HomeProductionArea = 4;

            var augustGap = new List<double>();
            for (int i = 0; i < SimCalendar.DaysPerYear; i++)
            {
                engine.Advance(1);
                int day = engine.Weather.SimulationDay;
                double gap = po4.WaterChemistry.Temperature - po13.WaterChemistry.Temperature;
                Assert.Equal(ProductionAreas.ClimatologyC(4, day) - ProductionAreas.ClimatologyC(13, day), gap, 9);
                // The HUD temperature is the home area's water.
                Assert.Equal(po4.WaterChemistry.Temperature, engine.Weather.CurrentTemperatureC, 12);
                Assert.Equal(engine.SeaTemperatureOfZone(po13.Id), po13.WaterChemistry.Temperature, 12);
                if (SimCalendar.Month(day) == 8) augustGap.Add(gap);
            }
            // PO4 is about 4.5 °C warmer than Øst-Finnmark in August (15.5 vs 11.0 normal).
            Assert.InRange(augustGap.Average(), 4.2, 4.8);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        public void TheSameSmolts_GrowMoreThroughPO4sMilderWinter_ThanInFinnmark(int seed)
        {
            var engine = NewEngine(seed);
            var (_, vest) = AddSite(engine, "vest", 4);
            var (_, aust) = AddSite(engine, "aust", 13);
            engine.Advance(304);   // 1 November
            Assert.True(engine.StockSmolts(vest.Id, 10_000, "standard").Ok);
            Assert.True(engine.StockSmolts(aust.Id, 10_000, "standard").Ok);

            engine.Advance(151);   // to 1 April: PO4 water 5.5-8 °C, PO13 4-6 °C

            // (In summer the growth law's narrow 11 °C optimum lets PO13 keep pace
            // with PO4; S2-03 owns that law, see docs/BALANCE.md §1f.)
            double warm = MeanWeightKg(vest), cold = MeanWeightKg(aust);
            Assert.True(warm > cold * 1.15, $"PO4 {warm:F3} kg vs PO13 {cold:F3} kg on 1 April");
            // Each population grew in its own zone's water (the S2-02 seam).
            var vestWater = engine.HoldingWaterOf(vest.Pens[0]);
            var austWater = engine.HoldingWaterOf(aust.Pens[0]);
            Assert.Equal(HoldingKind.SeaPen, vestWater.Kind);
            Assert.Equal(4, vestWater.ProductionArea);
            Assert.Equal(13, austWater.ProductionArea);
            Assert.Equal(engine.SeaTemperatureAtSite(vest), vestWater.TemperatureC, 12);
            Assert.Equal(engine.State.Zones.Single(z => z.Id == aust.ZoneId).WaterChemistry.OxygenO2, austWater.OxygenMgPerL, 12);
        }

        [Fact]
        public void Growth_ReadsEachPopulationsOwnWater_NotOneSeaTemperature()
        {
            var engine = NewEngine(seed: 12);
            var (optimumZone, optimum) = AddSite(engine, "optimum", 4);
            var (coldZone, cold) = AddSite(engine, "cold", 4);
            optimumZone.SetTemperature(11.0);  // the growth law's optimum
            coldZone.SetTemperature(4.0);
            Assert.True(engine.StockSmolts(optimum.Id, 10_000, "standard").Ok);
            Assert.True(engine.StockSmolts(cold.Id, 10_000, "standard").Ok);

            engine.Advance(60);

            // Same area, same day, same anomaly: only the water differs.
            Assert.True(MeanWeightKg(optimum) > MeanWeightKg(cold) * 1.5,
                $"11 °C {MeanWeightKg(optimum):F3} kg vs 4 °C {MeanWeightKg(cold):F3} kg");
            Assert.Equal(11.0, engine.HoldingWaterOf(optimum.Pens[0]).TemperatureC, 12);
            Assert.Equal(4.0, engine.HoldingWaterOf(cold.Pens[0]).TemperatureC, 12);
        }

        [Fact]
        public void SiteLice_DevelopAtTheirSitesOwnWaterTemperature()
        {
            var engine = NewEngine(seed: 5);
            var (_, vest) = AddSite(engine, "vest", 1);
            var (_, aust) = AddSite(engine, "aust", 13);
            Assert.True(engine.StockSmolts(vest.Id, 5_000, "standard").Ok);
            Assert.True(engine.StockSmolts(aust.Id, 5_000, "standard").Ok);
            for (int i = 0; i < 240; i++)
            {
                engine.Advance(1);
                Assert.Equal(Math.Clamp(engine.SeaTemperatureAtSite(vest), 0.0, 20.0), vest.Lice.TemperatureC, 12);
                Assert.Equal(Math.Clamp(engine.SeaTemperatureAtSite(aust), 0.0, 20.0), aust.Lice.TemperatureC, 12);
            }
            Assert.True(vest.Lice.TemperatureC > aust.Lice.TemperatureC + 3.0, "late August: PO1 is far warmer than PO13");
        }

        [Fact]
        public void ZoneOxygen_FollowsItsAreasSolubility()
        {
            var engine = NewEngine(seed: 8);
            var (warm, _) = AddSite(engine, "sor", 1);
            var (cold, _) = AddSite(engine, "nord", 13);
            engine.Advance(225);   // mid-August, no fish: the water sits near saturation
            Assert.True(warm.WaterChemistry.Temperature > cold.WaterChemistry.Temperature + 4.0);
            Assert.True(warm.WaterChemistry.OxygenO2 < cold.WaterChemistry.OxygenO2 - 0.5,
                $"O2 PO1 {warm.WaterChemistry.OxygenO2:F2} vs PO13 {cold.WaterChemistry.OxygenO2:F2} mg/L");
        }

        [Fact]
        public void ATreatment_RunsInTheSitesWater_SoCold_Finnmark_RefusesTheThermolicerInMarch()
        {
            var engine = NewEngine(seed: 2);
            var (_, aust) = AddSite(engine, "aust", 13);
            var (_, vest) = AddSite(engine, "vest", 4);
            Assert.True(engine.StockSmolts(aust.Id, 2_000, "standard").Ok);
            Assert.True(engine.StockSmolts(vest.Id, 2_000, "standard").Ok);
            engine.Advance(75);    // mid-March
            Assert.True(engine.SeaTemperatureAtSite(aust) < 6.0, $"PO13 March {engine.SeaTemperatureAtSite(aust):F1} °C");
            Assert.True(engine.SeaTemperatureAtSite(vest) >= 4.0);

            var cold = engine.DelouseSite(aust.Id, "thermal");      // Thermolicer: 6-18 °C
            Assert.False(cold.Ok);
            Assert.Equal("cancelled", cold.ErrorKey);
            var mechanical = engine.DelouseSite(vest.Id, "mechanical");
            Assert.True(mechanical.Ok, mechanical.Message);

            engine.Advance(150);   // August
            Assert.True(engine.SeaTemperatureAtSite(aust) >= 6.0);
            Assert.True(engine.DelouseSite(aust.Id, "thermal").Ok);
        }

        [Fact]
        public void TemperatureStress_KillsOnlyThePensInColdWater()
        {
            var engine = NewEngine(seed: 4);
            var (iceZone, ice) = AddSite(engine, "ice", 13);
            var (mildZone, mild) = AddSite(engine, "mild", 4);
            Assert.True(engine.StockSmolts(ice.Id, 100_000, "standard").Ok);
            Assert.True(engine.StockSmolts(mild.Id, 100_000, "standard").Ok);
            iceZone.SetTemperature(1.0);
            mildZone.SetTemperature(10.0);

            engine.ApplyHoldingWaterTemperatureStress();

            // 3 °C below the 4 °C band: (int)(n x 0.03 x 0.005) a day, as the rule says.
            Assert.Equal(100_000 - (int)(100_000 * 0.03 * 0.005), ice.Pens.Sum(p => p.TotalCount));
            Assert.Equal(100_000, mild.Pens.Sum(p => p.TotalCount));
        }

        [Fact]
        public void HoldingWater_ComesFromWhateverHoldsTheFish()
        {
            var engine = NewEngine(seed: 6);
            var (zone, site) = AddSite(engine, "sjo", 9);
            engine.State.HomeProductionArea = 9;
            Assert.True(engine.StockSmolts(site.Id, 1_000, "standard").Ok);
            var tank = new FishPopulation(engine.Growth, engine.Mortality) { ZoneId = "", SiteId = "ras-1" };
            var ras = new RASFacility { Id = "ras-1", CurrentWaterTemp = 12.5, OxygenLevel = 8.2 };
            ras.Tanks.Add(tank);
            engine.State.RASFacilities.Add(ras);
            var hullPen = new FishPopulation(engine.Growth, engine.Mortality) { ZoneId = zone.Id, SiteId = "hav-1" };
            var offshore = new OffshoreFarm { Id = "hav-1", ZoneId = zone.Id };
            offshore.Pens.Add(hullPen);
            engine.State.OffshoreFarms.Add(offshore);
            engine.Advance(40);

            var land = engine.HoldingWaterOf(tank);
            Assert.Equal(HoldingKind.LandTank, land.Kind);
            Assert.False(land.OpenToSea);
            Assert.Equal(ras.CurrentWaterTemp, land.TemperatureC, 12);
            Assert.Equal(ras.OxygenLevel, land.OxygenMgPerL, 12);

            var sea = engine.HoldingWaterOf(site.Pens[0]);
            Assert.Equal(HoldingKind.SeaPen, sea.Kind);
            Assert.True(sea.OpenToSea);
            Assert.Equal(zone.WaterChemistry.Temperature, sea.TemperatureC, 12);
            Assert.Equal(9, sea.ProductionArea);

            var hull = engine.HoldingWaterOf(hullPen);
            Assert.Equal(HoldingKind.Offshore, hull.Kind);
            Assert.Equal(engine.Weather.SeaTemperatureAt(9), hull.TemperatureC, 12);
            // February in Vestfjorden is far colder than the tank.
            Assert.True(land.TemperatureC > sea.TemperatureC + 5.0);
        }

        /// <summary>
        /// Roadmap S2-02 acceptance 4, on the engine's weekly lice census: a site at
        /// 0.3 adult female lice breaches the 0.2 spring limit in ISO week 18 only in
        /// PO1-PO7, in week 24 only in PO8-PO13, and the 0.5 limit nowhere in week 30.
        /// </summary>
        [Theory]
        [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
        [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(12)] [InlineData(13)]
        public void TheSpringLiceLimit_BitesInEachRegionsOwnWeeks(int productionArea)
        {
            var engine = NewEngine(seed: 1);
            var (zone, site) = AddSite(engine, "lus", productionArea);
            zone.SetTemperature(10.0);   // warm enough to count (luseforskriften § 6: weekly at >= 4 °C)
            Assert.True(engine.StockSmolts(site.Id, 2_000, "standard").Ok);
            bool south = productionArea <= 7;

            int Breaches(int isoWeek)
            {
                engine.State.Day = Enumerable.Range(0, SimCalendar.DaysPerYear).First(d => SimCalendar.IsoWeek(d) == isoWeek) + 2;
                site.ConsecutiveLiceBreaches = 0;
                site.Lice.AdultFemalePerFish = 0.3;
                engine.UpdateSiteLiceCounts();
                Assert.Equal(engine.State.Day, site.LastLiceCountDay);
                return site.ConsecutiveLiceBreaches;
            }

            Assert.Equal(south ? 1 : 0, Breaches(18));
            Assert.Equal(south ? 0 : 1, Breaches(24));
            Assert.Equal(1, Breaches(21));   // the week both rules share
            Assert.Equal(0, Breaches(30));
        }

        [Fact]
        public void TheSharedAnomaly_HasItsCalibratedSpreadAndMemory()
        {
            const int seeds = 40, days = 3 * SimCalendar.DaysPerYear;
            double sum = 0, sumSq = 0, lagProducts = 0;
            long n = 0, lagN = 0;
            for (int seed = 1; seed <= seeds; seed++)
            {
                var weather = new WeatherSystem(new SimRng(seed).Stream(SimRngStreams.Weather));
                var series = new double[days];
                for (int d = 0; d < days; d++)
                {
                    weather.AdvanceDays(1);
                    series[d] = weather.SeaAnomalyC;
                }
                // Skip the first 180 days: the process starts at zero.
                for (int d = 180; d < days; d++)
                {
                    sum += series[d]; sumSq += series[d] * series[d]; n++;
                    if (d + 30 < days) { lagProducts += series[d] * series[d + 30]; lagN++; }
                }
            }
            double mean = sum / n;
            double variance = sumSq / n - mean * mean;
            double sd = Math.Sqrt(variance);
            double lag30 = (lagProducts / lagN - mean * mean) / variance;
            // Calibration: SD 1.0 °C, e-folding 90 days (lag-30 autocorrelation exp(-1/3) = 0.72).
            Assert.InRange(sd, 0.85, 1.15);
            Assert.InRange(lag30, 0.62, 0.82);
            Assert.InRange(mean, -0.3, 0.3);
        }

        [Fact]
        public void AStorytellerHeatwave_ReachesEveryArea_AndFadesOverThreeWeeks()
        {
            var engine = NewEngine(seed: 9);
            var (vest, _) = AddSite(engine, "vest", 3);
            var (nord, _) = AddSite(engine, "nord", 12);
            engine.Advance(190);
            double before = engine.Weather.EventAnomalyC;
            engine.Weather.ApplyTemperatureAnomaly(+4.0);
            Assert.Equal(before + 4.0, engine.Weather.EventAnomalyC, 12);
            engine.Advance(1);
            Assert.Equal(engine.Weather.SeaTemperatureAt(3), vest.WaterChemistry.Temperature, 12);
            Assert.Equal(engine.Weather.SeaTemperatureAt(12), nord.WaterChemistry.Temperature, 12);
            engine.Advance(20);
            // 21 days at an e-folding of 21 days: 4 x e^-1 = 1.47 °C left.
            Assert.Equal(4.0 * Math.Exp(-1.0), engine.Weather.EventAnomalyC - before * Math.Exp(-1.0), 2);
        }

        [Fact]
        public void TheWeatherChain_DrawsFourNumbersADay_AsBeforeTheAnomaly()
        {
            // The anomaly reuses the two uniforms the old daily noise drew, so the
            // storm chain that shares the stream sees the same draws as before S2-02.
            var weather = new WeatherSystem(new SimRng(17).Stream(SimRngStreams.Weather));
            var mirror = new SimRng(17).Stream(SimRngStreams.Weather);
            for (int d = 0; d < 400; d++)
            {
                weather.AdvanceDays(1);
                for (int k = 0; k < 4; k++) mirror.NextDouble();
                Assert.Equal(mirror.ExportState(), weather.RngForSnapshot.ExportState());
            }
        }

        [Fact]
        public void EveryStartingSite_LiesInItsScenariosProductionArea_AndTheHudReportsTheHomeArea()
        {
            var roster = new ScenarioSystem(new GameState());
            foreach (var scenario in roster.All.Values)
            {
                var engine = NewEngine(seed: 21);
                Assert.True(engine.StartScenario(scenario.Id).Ok, scenario.Id);
                foreach (var def in scenario.StartingSites)
                {
                    Assert.True(ProductionAreas.IsValid(def.ProductionArea), $"{scenario.Id}/{def.Id} names no area");
                    var zone = engine.State.Zones.Single(z => z.Id == def.Zone);
                    Assert.Equal(def.ProductionArea, zone.ProductionArea);
                }
                int home = scenario.StartingSites[0].ProductionArea;
                Assert.Equal(home, engine.State.HomeProductionArea);
                Assert.Equal(home, engine.Weather.ReferenceProductionArea);
                Assert.Equal(engine.Weather.SeaTemperatureAt(home), engine.Weather.CurrentTemperatureC, 12);
            }

            int AreaOf(string id) => roster.GetScenario(id)!.StartingSites[0].ProductionArea;
            Assert.Equal(3, AreaOf("forste_sesong"));        // Hardanger
            Assert.Equal(3, AreaOf("po3_red_sone"));
            Assert.Equal(9, AreaOf("lofoten_eventyr"));      // Lofoten
            Assert.Equal(12, AreaOf("krise_manager"));       // Finnmark
            Assert.Equal(Data.ProductionRegion.Vestland, roster.GetScenario("po3_red_sone")!.StartingRegion);
        }
    }
}
