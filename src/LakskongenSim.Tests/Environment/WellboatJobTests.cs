using System.Linq;
using System.Text.Json;
using LakskongenSim.Core;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>Phase 2.4: wellboat job DTO sanity (engine flow covered in-game).</summary>
    public class WellboatJobTests
    {
        /// <summary>
        /// Roadmap D8 (P0-07): the default id is deterministic, composed of the
        /// booking day, site, kind and booking sequence. It replaces the old
        /// assertion that the id was merely non-empty (a fresh Guid.NewGuid(),
        /// different in every run).
        /// </summary>
        [Fact]
        public void Job_Defaults()
        {
            var job = new Logistics.WellboatJob { SiteId = "s1", Kind = "harvest", BookedDay = 5, Sequence = 3, DueDay = 10 };
            Assert.Equal("s1", job.SiteId);
            Assert.Equal(10, job.DueDay);
            Assert.Equal("wellboat-d5-s1-harvest-3", job.Id);
            Assert.Equal(Logistics.WellboatJob.ComposeId(5, "s1", "harvest", 3), job.Id);

            var twin = new Logistics.WellboatJob { SiteId = "s1", Kind = "harvest", BookedDay = 5, Sequence = 3, DueDay = 10 };
            Assert.Equal(job.Id, twin.Id);
        }

        [Fact]
        public void Job_KeepsASavedId_IncludingALegacyGuid()
        {
            const string legacy = "0f8fad5b-d9cb-469f-a165-70867728950e";
            var job = JsonSerializer.Deserialize<Logistics.WellboatJob>(
                $"{{\"Id\":\"{legacy}\",\"SiteId\":\"s1\",\"Kind\":\"rov\",\"DueDay\":4}}", SaveJson.Options)!;
            Assert.Equal(legacy, job.Id);

            var booked = new Logistics.WellboatJob { SiteId = "s2", Kind = "delouse", BookedDay = 7, Sequence = 1 };
            var roundTripped = JsonSerializer.Deserialize<Logistics.WellboatJob>(
                JsonSerializer.Serialize(booked, SaveJson.Options), SaveJson.Options)!;
            Assert.Equal("wellboat-d7-s2-delouse-1", roundTripped.Id);
        }

        private static SimulationEngine Po3(int seed)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            return engine;
        }

        /// <summary>
        /// Two runs of the same seeded game book the same ids; the sequence
        /// numbers the game's bookings and continues across a save and load.
        /// </summary>
        [Fact]
        public void RequestWellboat_BooksDeterministicIds_ThatContinueAcrossALoad()
        {
            string[] Book(SimulationEngine engine)
            {
                Assert.True(engine.RequestWellboat("po3_nord", "rov").Ok);
                string rov = engine.State.WellboatJobs.Last().Id;
                engine.Advance(3);
                Assert.True(engine.RequestWellboat("po3_sor", "delouse", "hydrolicer").Ok);
                return new[] { rov, engine.State.WellboatJobs.Last().Id };
            }

            var first = Po3(11);
            var second = Po3(11);
            string[] ids = Book(first);
            Assert.Equal(ids, Book(second));
            Assert.Equal(new[] { "wellboat-d0-po3_nord-rov-1", "wellboat-d3-po3_sor-delouse-2" }, ids);
            Assert.Equal(2, first.State.WellboatJobsBooked);

            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(first), events: false);
            Assert.Equal(2, resumed.State.WellboatJobsBooked);
            Assert.True(resumed.RequestWellboat("po3_nord", "freshwater").Ok);
            Assert.Contains($"wellboat-d{resumed.State.Day}-po3_nord-freshwater-3",
                resumed.State.WellboatJobs.Select(j => j.Id));
        }
    }
}
