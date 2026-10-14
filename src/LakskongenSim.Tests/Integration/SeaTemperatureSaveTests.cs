using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Finance;
using Xunit;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// S2-02 save format: zones keep their production area, the game its home
    /// area and the weather its shared sea-temperature anomaly; a save from
    /// before S2-02 loads with the active scenario's areas.
    /// </summary>
    public class SeaTemperatureSaveTests
    {
        private static SimulationEngine StartedPo3(int seed)
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(12_000_000m) };
            engine.Initialize(enableEvents: false, seed: seed);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            return engine;
        }

        [Fact]
        public void AreasHomeAndAnomaly_SurviveASave_AndTheGameContinuesExactly()
        {
            var engine = StartedPo3(seed: 13);
            engine.Weather.ApplyTemperatureAnomaly(+2.0);
            engine.Advance(150);
            string saved = SaveStateComparer.Serialize(engine);
            var resumed = SaveStateComparer.Resume(saved, events: false);

            Assert.Equal(3, resumed.State.HomeProductionArea);
            Assert.All(resumed.State.Zones, z => Assert.Equal(3, z.ProductionArea));
            Assert.Equal(engine.Weather.SeaAnomalyC, resumed.Weather.SeaAnomalyC, 15);
            Assert.Equal(engine.Weather.EventAnomalyC, resumed.Weather.EventAnomalyC, 15);
            Assert.Equal(engine.Weather.CurrentTemperatureC, resumed.Weather.CurrentTemperatureC, 15);
            Assert.Equal(3, resumed.Weather.ReferenceProductionArea);

            for (int day = 0; day < 60; day++)
            {
                engine.Advance(1);
                resumed.Advance(1);
                Assert.Equal(engine.Weather.CurrentTemperatureC, resumed.Weather.CurrentTemperatureC, 15);
                foreach (var zone in engine.State.Zones)
                    Assert.Equal(zone.WaterChemistry.Temperature,
                        resumed.State.Zones.Single(z => z.Id == zone.Id).WaterChemistry.Temperature, 15);
            }
            Assert.Equal("identical", SaveStateComparer.FirstDifference(
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(engine)),
                SaveStateComparer.Normalize(SaveStateComparer.Serialize(resumed))));
        }

        [Fact]
        public void AnOldSave_WithoutAreas_LoadsIntoItsScenariosAreas_WithAZeroAnomaly()
        {
            var engine = StartedPo3(seed: 7);
            engine.Advance(40);
            var json = JsonNode.Parse(SaveStateComparer.Serialize(engine))!.AsObject();
            // Strip what S2-02 added: the save a v3 game wrote.
            json.Remove("HomeProductionArea");
            foreach (var zone in json["Zones"]!.AsArray())
                zone!.AsObject().Remove("ProductionArea");
            var runtime = json["Runtime"]!.AsObject();
            foreach (var key in new[] { "WeatherSeaAnomalyC", "WeatherEventAnomalyC", "WeatherDailyFluctuationC", "WeatherStormCoolingC" })
                runtime.Remove(key);
            string legacy = json.ToJsonString(SaveJson.Options);
            Assert.DoesNotContain("ProductionArea", legacy);

            var resumed = SaveStateComparer.Resume(legacy, events: false);

            Assert.Equal(3, resumed.State.HomeProductionArea);
            Assert.All(resumed.State.Zones, z => Assert.Equal(3, z.ProductionArea));
            Assert.Equal(0.0, resumed.Weather.SeaAnomalyC);
            resumed.Advance(1);
            Assert.Equal(resumed.Weather.SeaTemperatureAt(3), resumed.State.Zones[0].WaterChemistry.Temperature, 12);
        }

        [Fact]
        public void AGameWithoutAScenario_RunsOnTheNationalReference()
        {
            var engine = new SimulationEngine { FinanceManager = new FinanceManager(5_000_000m) };
            engine.Initialize(enableEvents: false, seed: 2);
            var zone = new Zone("ingen");
            engine.State.Zones.Add(zone);
            engine.State.SeaSites.Add(new LakskongenSim.Farm.SeaSite("site", zone.Id));
            engine.Advance(30);

            Assert.Equal(ProductionAreas.NationalReference, engine.HomeProductionArea);
            Assert.Equal(engine.Weather.SeaTemperatureAt(ProductionAreas.NationalReference), zone.WaterChemistry.Temperature, 12);
            Assert.Equal(engine.Weather.CurrentTemperatureC, zone.WaterChemistry.Temperature, 12);
        }
    }
}
