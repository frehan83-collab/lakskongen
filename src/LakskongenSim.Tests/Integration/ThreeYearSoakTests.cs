using System.Linq;
using System.Text.Json;
using Xunit;
using LakskongenSim.Core;
using LakskongenSim.Environment;

namespace LakskongenSim.Tests.Integration
{
    /// <summary>
    /// 3-year stability soak with events ON: no exceptions, sane economy,
    /// save/load mid-run preserves continuity.
    /// </summary>
    public class ThreeYearSoakTests
    {
        [Fact]
        public void Soak_ThreeYearsWithEvents_NoCrashSaneEconomy()
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true);
            if (!engine.State.Zones.Any())
            {
                var zone = new Zone("SoakZone");
                zone.SetTemperature(11.0);
                engine.State.Zones.Add(zone);
            }
            engine.Hatchery.ReleaseSmolt(50_000);
            engine.State.Populations.First().ZoneId = engine.State.Zones.First().Id;

            // Year 1-1.5 with events firing (NorwegianEvents now hit FinanceManager)
            engine.Advance(500);
            Assert.Equal(500, engine.State.Day);

            // Mid-run save/load: JSON round-trip like SaveLoadManager does
            var json = JsonSerializer.Serialize(engine.State);
            engine.PrepareForSave();
            var snapshot = JsonSerializer.Deserialize<GameState>(json)!;
            var loaded = new SimulationEngine();
            loaded.Initialize(enableEvents: true);
            loaded.State.Day = snapshot.Day; // mirrors GameBridge.LoadGame (day is set, not restored)
            loaded.RestoreFromLoad(snapshot);

            // Continue to year 3 on the loaded engine
            loaded.Advance(595);
            Assert.Equal(1095, loaded.State.Day);

            // Economy sane: finite balance, no runaway debt spiral
            Assert.True(loaded.FinanceManager.Balance > -100_000_000m,
                $"Runaway debt: {loaded.FinanceManager.Balance:N0} NOK");
            foreach (var pop in loaded.State.Populations)
                foreach (var cohort in pop.Cohorts)
                {
                    Assert.True(cohort.Count >= 0, "Negative fish count");
                    Assert.True(cohort.MeanWeightKg >= 0, "Negative weight");
                }
        }
    }
}
