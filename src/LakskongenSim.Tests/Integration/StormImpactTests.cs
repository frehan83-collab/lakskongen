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

    /// <summary>
    /// U1-05: a multi-day Advance (the day-skip buttons, the screenshot run)
    /// drains every day's notes at once. Each storm note must carry the day it
    /// struck — the dashboard listed four storms from days 1-9 as "Nå" on day
    /// 12 — and that day is the one its "Storm Damage" record carries.
    /// </summary>
    [Fact]
    public void MultiDayAdvance_DatesEachStormNoteToItsOwnDay()
    {
        const int days = 60;
        for (int seed = 1; seed <= 40; seed++)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: true, seed: seed);
            var zone = new Zone("zone-1");
            engine.State.Zones.Add(zone);
            engine.State.SeaSites.Add(new SeaSite("site-1", zone.Id));
            Assert.True(engine.StockSmolts("site-1", 5_000, "standard").Ok);
            int start = engine.State.Day;
            engine.Advance(days);
            var notes = engine.DrainDayNotes();
            var storms = notes.Where(n => n.Kind == "storm_impact").ToList();
            if (storms.Count < 2) continue;

            Assert.All(notes, n => Assert.InRange(n.Day, start, start + days));
            var damageDays = engine.State.EventLog.Where(e => e.Name == "Storm Damage").Select(e => e.Day).ToList();
            foreach (var storm in storms)
                Assert.Contains(storm.Day, damageDays);
            Assert.True(storms.Select(n => n.Day).Distinct().Count() == storms.Count,
                $"seed {seed}: storms on separate days must carry separate days ({string.Join(",", storms.Select(n => n.Day))})");
            Assert.True(storms[0].Day < engine.State.Day, "the first storm is dated before the last day of the advance");
            return;
        }

        Assert.Fail($"No seed in 1-40 had two storm episodes within {days} days");
    }
}