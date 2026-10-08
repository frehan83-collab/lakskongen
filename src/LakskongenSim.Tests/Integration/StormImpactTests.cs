using System.Collections.Generic;
using System.Linq;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Farm;
using Xunit;

namespace LakskongenSim.Tests.Integration;

/// <summary>
/// Storm/temperature impacts moved from the GameBridge into the engine
/// (Phase 1 wave 2). The mechanism is edge-triggered on the transition into
/// Storm/Severe, so the test scans a spread of seeds instead of pinning one
/// seed's weather calendar — it tracks the weather model, not a date table.
/// </summary>
public class StormImpactTests
{
    [Fact]
    public void StormEpisode_FiresMortalityRepairAndInsurance()
    {
        const int horizon = 120;

        for (int seed = 1; seed <= 40; seed++)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: seed);
            var zone = new Zone("zone-1");
            engine.State.Zones.Add(zone);
            engine.State.SeaSites.Add(new SeaSite("site-1", zone.Id));
            Assert.True(engine.StockSmolts("site-1", 5_000, "standard").Ok);
            Assert.True(engine.SetInsurancePolicy("storm", true, 1.0, 0).Ok);

            var notes = new List<DayNote>();
            bool stormed = false;
            for (int day = 0; day < horizon && !stormed; day++)
            {
                engine.Advance(1);
                notes.AddRange(engine.DrainDayNotes());
                stormed = notes.Any(n => n.Kind == "storm_impact");
            }

            if (!stormed) continue;

            Assert.Contains(engine.State.EventLog, e => e.Name == "Storm Damage");
            Assert.Contains(engine.State.EventLog, e => e.Name == "Forsikringsoppgjør");
            var impact = Assert.Single(notes, n => n.Kind == "storm_impact");
            Assert.True(impact.Success);
            Assert.InRange(impact.Severity, 0.5, 1.0);
            Assert.False(string.IsNullOrEmpty(impact.EventType));
            // One claim per episode — a second consecutive storm day must not
            // resettle insurance (edge-triggered, like the old bridge).
            int settlementsBefore = engine.State.EventLog.Count(e => e.Name == "Forsikringsoppgjør");
            engine.Advance(1);
            _ = engine.DrainDayNotes();
            Assert.Equal(settlementsBefore,
                engine.State.EventLog.Count(e => e.Name == "Forsikringsoppgjør"));
            return;
        }

        Assert.Fail($"No storm episode observed across seeds 1-40 within {horizon} days");
    }
}