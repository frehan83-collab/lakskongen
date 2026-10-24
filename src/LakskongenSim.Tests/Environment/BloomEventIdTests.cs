using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using LakskongenSim.Core;
using LakskongenSim.Environment;
using LakskongenSim.Tests.Integration;
using Xunit;

namespace LakskongenSim.Tests.StormPhase12
{
    /// <summary>
    /// SHIP_PLAN §10 / RUN_LOG §8 cycle-1 follow-up (a): <see cref="BloomEvent.Id"/>
    /// was <c>Guid.NewGuid()</c>, so two runs of one seeded game wrote different
    /// saves. The id is now start day, site and kind (the WellboatJob pattern, D8).
    /// </summary>
    public class BloomEventIdTests
    {
        private static readonly Regex GuidShape = new(
            @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");

        [Fact]
        public void DefaultId_IsStartDaySiteAndKind()
        {
            var bloom = new BloomEvent { Kind = "algae", SiteId = "po3_nord", StartDay = 140, DurationDays = 9 };
            Assert.Equal("bloom-d140-po3_nord-algae", bloom.Id);
            Assert.Equal(BloomEvent.ComposeId(140, "po3_nord", "algae"), bloom.Id);

            var twin = new BloomEvent { Kind = "algae", SiteId = "po3_nord", StartDay = 140, DurationDays = 9 };
            Assert.Equal(bloom.Id, twin.Id);
            Assert.NotEqual(bloom.Id, new BloomEvent { Kind = "jellyfish", SiteId = "po3_nord", StartDay = 140 }.Id);
            Assert.NotEqual(bloom.Id, new BloomEvent { Kind = "algae", SiteId = "po3_nord", StartDay = 147 }.Id);
        }

        [Fact]
        public void ASetId_IsKept_IncludingALegacyGuid()
        {
            Assert.Equal("scenario_x_1", new BloomEvent { Id = "scenario_x_1", SiteId = "s", StartDay = 3 }.Id);

            const string legacy = "0f8fad5b-d9cb-469f-a165-70867728950e";
            var loaded = JsonSerializer.Deserialize<BloomEvent>(
                $"{{\"Id\":\"{legacy}\",\"Kind\":\"hypoxia\",\"SiteId\":\"s1\",\"StartDay\":4}}", SaveJson.Options)!;
            Assert.Equal(legacy, loaded.Id);

            var spawned = new BloomEvent { Kind = "jellyfish", SiteId = "s2", StartDay = 12 };
            var roundTripped = JsonSerializer.Deserialize<BloomEvent>(
                JsonSerializer.Serialize(spawned, SaveJson.Options), SaveJson.Options)!;
            Assert.Equal("bloom-d12-s2-jellyfish", roundTripped.Id);
        }

        private static SimulationEngine Po3(int seed)
        {
            var engine = new SimulationEngine();
            engine.Initialize(enableEvents: false, seed: seed);
            Assert.True(engine.StartScenario("po3_red_sone").Ok);
            return engine;
        }

        /// <summary>Every bloom id the game holds on any of <paramref name="days"/> days, in order of appearance.</summary>
        private static List<string> BloomIdsOver(SimulationEngine engine, int days)
        {
            var seen = new List<string>();
            for (int d = 0; d < days; d++)
            {
                engine.Advance(1);
                foreach (var bloom in engine.State.ActiveBlooms)
                    if (!seen.Contains(bloom.Id)) seen.Add(bloom.Id);
            }
            return seen;
        }

        /// <summary>
        /// The game's own blooms (TrySpawnBloom): two engines on one seed hold the
        /// same ids, each is the composed id (not a Guid), and a save carries them
        /// unchanged into the loaded game.
        /// </summary>
        [Fact]
        public void SpawnedBlooms_HaveTheSameIdsInTwoRunsOfOneSeed_AndKeepThemAcrossALoad()
        {
            var first = Po3(7);
            var second = Po3(7);
            var ids = BloomIdsOver(first, 365);
            Assert.Equal(ids, BloomIdsOver(second, 365));

            Assert.True(ids.Count >= 3, $"expected the deck to spawn blooms in a year, saw {ids.Count}");
            Assert.All(ids, id => Assert.DoesNotMatch(GuidShape, id));
            Assert.All(ids, id => Assert.StartsWith("bloom-d", id));

            // Advance until a bloom is live, then save and load mid-bloom.
            for (int d = 0; d < 365 && first.State.ActiveBlooms.Count == 0; d++) first.Advance(1);
            Assert.NotEmpty(first.State.ActiveBlooms);
            var before = first.State.ActiveBlooms.Select(b => b.Id).ToList();
            Assert.All(before, id => Assert.Equal(id, BloomEvent.ComposeId(
                first.State.ActiveBlooms.First(b => b.Id == id).StartDay,
                first.State.ActiveBlooms.First(b => b.Id == id).SiteId,
                first.State.ActiveBlooms.First(b => b.Id == id).Kind)));

            var resumed = SaveStateComparer.Resume(SaveStateComparer.Serialize(first), events: false);
            Assert.Equal(before, resumed.State.ActiveBlooms.Select(b => b.Id).ToList());
        }
    }
}
